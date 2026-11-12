using System.Diagnostics;
using Kotoba.Core;

string dictionaryPath=args.Length>0?Path.GetFullPath(args[0]):Path.GetFullPath("Data/dictionary.sqlite");
var dict=new DictionaryStore(dictionaryPath);
if(args.Length>1&&args[1]=="--child-save") {new LearningStore(args[2],dict).Save("kanji-76");return;}
if(args.Length>1&&args[1]=="--child-link") {try{InstanceChannel.Send(args[2],"yarxi-study://entry/word-100").GetAwaiter().GetResult();}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}return;}
int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
void Throws(Action action,string message){bool threw=false;try{action();}catch{threw=true;}Check(threw,message);}
var root=Path.Combine(Path.GetTempPath(),"kotoba-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);
try
{
    Check(dict.Counts()==(6286,55915),"Dictionary counts");
    Check(dict.Search("あいず").Any(e=>e.Id=="word-100"),"Hiragana query");
    Check(!dict.Search("あいす").Any(e=>e.Id=="word-100"),"Voicing preserved");
    Check(DictionaryStore.Fold("GAKKŌ")=="gakko","Latin macrons");
    Check(DictionaryStore.Fold("ЁЛКА")=="елка","Cyrillic normalization matches export");
    Check(dict.Search("трех").Count>0&&dict.Search("трёх").Select(e=>e.Id).SequenceEqual(dict.Search("трех").Select(e=>e.Id)),"Russian accents match index");
    Check(dict.Search("сигнал").Count>0,"Russian query");
    Check(dict.Search("' OR 1=1 --").Count==0,"Bound search");
    Check(dict.Search("%").Count<100,"Literal wildcard");
    Check(dict.Search("", "kanji").All(e=>e.IsKanji),"Kind filter");
    Check(dict.Random(null,"kanji-76")?.Id!="kanji-76","Exclude previous");
    Check(dict.Entry("invalid")==null,"Invalid ID");
    Check(dict.KanjiIn("合図").Select(e=>e.Text).SequenceEqual(new[]{"合","図"}),"Compound kanji order");
    Check(dict.KanjiIn("食べ物").Select(e=>e.Text).SequenceEqual(new[]{"食","物"}),"Kana have no lookup links");
    Check(dict.KanjiIn("時々時").Select(e=>e.Text).SequenceEqual(new[]{"時"}),"Repeated kanji resolved once; repetition marks excluded");
    Check(dict.KanjiIn("あいず・ー").Count==0&&dict.KanjiIn("").Count==0,"Kana/empty headwords");
    var diagramWords=dict.WordsContainingKanji("図");Check(diagramWords.Count==84&&diagramWords.Any(e=>e.Id=="word-100"),"All 84 words containing 図");
    var oneWords=dict.WordsContainingKanji("一");Check(oneWords.Count==901&&oneWords.Select(e=>e.Id).Distinct().Count()==901,"Uncapped complete 901-word list");
    Check(oneWords.All(e=>!e.IsKanji&&e.Text.Contains("一")),"Related words match actual spelling only");
    Check(dict.WordsContainingKanji("' OR 1=1 --").Count==0&&dict.WordsContainingKanji("%").Count==0,"Related-word literal/bound query");
    double now=1800000000;
    var again=ReviewScheduler.Next(null,ReviewGrade.Again,now).Card;
    var good=ReviewScheduler.Next(null,ReviewGrade.Good,now).Card;
    var easy=ReviewScheduler.Next(null,ReviewGrade.Easy,now).Card;
    Check(again.Due==now+60&&again.Stability==.4072&&again.Difficulty==7.2102&&again.State==1,"FSRS new Again");
    Check(good.Due==now+600&&good.Stability==3.1262&&good.State==1,"FSRS new Good");
    Check(easy.Due==now+15*86400&&easy.Stability==15.4722&&easy.State==2,"FSRS new Easy");
    var graduating=ReviewScheduler.Next(good,ReviewGrade.Good,good.Due).Card;
    Check(graduating.State==2&&graduating.Due==good.Due+4*86400&&graduating.Reps==2,"FSRS learning graduation");
    var lapse=ReviewScheduler.Next(easy,ReviewGrade.Again,easy.Due).Card;
    Check(lapse.State==3&&lapse.Lapses==1&&lapse.Due==easy.Due+300,"FSRS review lapse");
    Check(ReviewScheduler.Next(easy,ReviewGrade.Easy,easy.Due)==ReviewScheduler.Next(easy,ReviewGrade.Easy,easy.Due),"Deterministic scheduling");
    Check(ReviewScheduler.Next(null,ReviewGrade.Hard,now).Card.Due==now+300,"FSRS new Hard");
    var reviewGood=ReviewScheduler.Next(easy,ReviewGrade.Good,easy.Due).Card;
    var reviewHard=ReviewScheduler.Next(easy,ReviewGrade.Hard,easy.Due).Card;
    var reviewEasy=ReviewScheduler.Next(easy,ReviewGrade.Easy,easy.Due).Card;
    Check(reviewHard.Due<reviewGood.Due&&reviewGood.Due<reviewEasy.Due,"Review interval ordering");
    Check(ReviewScheduler.Next(lapse,ReviewGrade.Good,lapse.Due).Card.State==2,"Relearning graduation");
    string dbPath=Path.Combine(root,"learning.sqlite");var store=new LearningStore(dbPath,dict);
    var child=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};child.ArgumentList.Add(dictionaryPath);child.ArgumentList.Add("--child-save");child.ArgumentList.Add(dbPath);
    using(var process=Process.Start(child)!){process.WaitForExit();Check(process.ExitCode==0,"Separate writer");}
    var pipe="kotoba-test-"+Guid.NewGuid();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var received=InstanceChannel.Receive(pipe,timeout.Token);
    var sender=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};foreach(var arg in new[]{dictionaryPath,"--child-link",pipe})sender.ArgumentList.Add(arg);
    using(var process=Process.Start(sender)!)
    {
        process.WaitForExit();var error=process.StandardError.ReadToEnd();
        if(process.ExitCode!=0&&error.Contains("UnauthorizedAccessException")&&Environment.GetEnvironmentVariable("KOTOBA_ALLOW_IPC_SKIP")=="1")
        {Console.WriteLine("SKIP: cross-process IPC: sandbox denied named-pipe client access.");timeout.Cancel();try{received.GetAwaiter().GetResult();}catch(OperationCanceledException){}}
        else {Check(process.ExitCode==0,$"Link sender exited {process.ExitCode}: {error}");var delivered=received.GetAwaiter().GetResult();Check(delivered=="yarxi-study://entry/word-100",$"Cross-process entry delivery: message=[{delivered}]");}
    }
    Parallel.For(0,24,_=>new LearningStore(dbPath,dict).Save("kanji-76"));Check(store.Records().Count==1,"Concurrent duplicate saves");
    Throws(()=>store.Save("invalid"),"Invalid save");
    var a=store.CreateList("A");var b=store.CreateList("B");store.Membership("kanji-76",a,true);store.Membership("kanji-76",b,true);
    Check(store.Records("inbox").Count==0&&store.Records(a).Count==1,"Membership/Inbox");
    store.Start(a,now:now);store.Reveal(now);Check(new LearningStore(dbPath,dict).Session()?.RevealedAt==now,"Durable reveal");
    store.Rate("kanji-76",ReviewGrade.Easy,now:now);Check(store.Summary(now:now).RemainingNew==19,"First rating consumes allowance");
    var progress=store.Records(b)[0].Card;store.RenameList(a,"Renamed");store.DeleteList(a);store.DeleteList(b);Check(store.Records("inbox")[0].Card==progress,"List deletion preserves progress");
    store.Remove("kanji-76");store.Save("kanji-76");Check(store.Records()[0].Card==progress,"Restore progress");
    store.Undo(now);Check(store.Records()[0].Card==null&&store.Summary(now:now).RemainingNew==20&&!store.CanUndo(),"Atomic undo");
    store.Resume(now:now);store.Reveal(now);
    using(var db=store.Open())LearningStore.Run(db,"CREATE TRIGGER fail_rating BEFORE UPDATE OF card ON saved BEGIN SELECT RAISE(ABORT,'injected failure'); END");
    Throws(()=>store.Rate("kanji-76",ReviewGrade.Good,now:now),"Failed rating");Check(store.Records()[0].Card==null&&!store.CanUndo()&&store.Summary(now:now).RemainingNew==20,"Rollback");
    using(var db=store.Open())LearningStore.Run(db,"DROP TRIGGER fail_rating");
    store.Rate("kanji-76",ReviewGrade.Good,now:now);Check(store.Resume(now:now).CurrentId==null,"Future card not due");
    Check(store.Resume(now:now+600).CurrentId=="kanji-76","Due card resumes");
    store.Reveal(now+600);store.Rate("kanji-76",ReviewGrade.Good,now:now+600);Check(store.Summary(now:now+600).RemainingNew==19,"Repeat rating consumes no new allowance");
    store.Save("word-100");store.Save("word-53797");store.Start(limit:1,now:now);Check(store.Session()?.CurrentId==null,"Global quota");
    store.Start(limit:20,now:now);store.Reveal(now);var stale=store.Session()!;
    store.Rate(stale.CurrentId!,ReviewGrade.Good,now:now);Throws(()=>store.Rate(stale.CurrentId!,ReviewGrade.Good,now:now),"Duplicate grade rejected");
    var zone=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    double Stamp(int month,int day)=>new DateTimeOffset(2026,month,day,12,0,0,TimeSpan.Zero).ToUnixTimeSeconds();
    var spring=LearningStore.DayBounds(Stamp(3,8),zone);var autumn=LearningStore.DayBounds(Stamp(11,1),zone);
    Check(spring.End-spring.Start==23*3600&&autumn.End-autumn.Start==25*3600,"DST calendar bounds");
    var bounds=LearningStore.DayBounds(now);Check(bounds.Start<=now&&bounds.End>now,"Local bounds");
    Check(store.Summary(now:bounds.End+1).RemainingNew==20,"Quota reset after midnight");
    using(var db=store.Open()){Check(LearningStore.Rows(db,"PRAGMA integrity_check")[0][0]=="ok","SQLite integrity");Check(LearningStore.Rows(db,"PRAGMA journal_mode")[0][0]=="wal","WAL mode");}
    Directory.CreateDirectory(Path.Combine(root,"bad"));Throws(()=>new LearningStore(Path.Combine(root,"bad"),dict),"Unavailable path");
    string settingsPath=Path.Combine(root,"settings.json");var settings=new Settings{HeadwordScale=2,Romaji=true};settings.Save(settingsPath);Check(Settings.Load(settingsPath).HeadwordScale==2&&Settings.Load(settingsPath).Romaji,"Settings persistence");
    File.WriteAllText(settingsPath,"{\"HeadwordScale\":20,\"ReadingScale\":0,\"DailyNewLimit\":500,\"RefreshSeconds\":2}");var clamped=Settings.Load(settingsPath);
    Check(clamped.HeadwordScale==2&&clamped.ReadingScale==.5&&clamped.DailyNewLimit==200&&clamped.RefreshSeconds==3600,"Settings bounds");
    var migrationPath=Path.Combine(root,"migration.sqlite");var migration=new LearningStore(migrationPath,dict);migration.Save("word-100");
    using(var db=migration.Open()){LearningStore.Run(db,"ALTER TABLE history DROP COLUMN fsrs_log");LearningStore.Run(db,"PRAGMA user_version=1");}
    migration=new LearningStore(migrationPath,dict);Check(migration.Records().Single().Id=="word-100","Schema v1 migration preserves entries");
    using(var db=migration.Open())Check(LearningStore.Rows(db,"PRAGMA user_version")[0][0]=="2","Schema migrated to v2");
    migration.Start(now:bounds.End-2);migration.Reveal(bounds.End-1);migration.Rate("word-100",ReviewGrade.Good,now:bounds.End+1);
    Check(migration.Summary(now:bounds.End-1).RemainingNew==20&&migration.Summary(now:bounds.End+1).RemainingNew==19,"Rating day, not reveal day, consumes quota");
    using(var db=migration.Open())LearningStore.Run(db,"PRAGMA user_version=3");
    Throws(()=>new LearningStore(migrationPath,dict),"Reject newer schema");
    var widgetLearning=new LearningStore(Path.Combine(root,"widget-learning.sqlite"),dict);
    var widgetPath=Path.Combine(root,"widgets.json");var widgets=new DesktopCardStore(widgetPath,dict,()=>widgetLearning);
    var card=widgets.Create("Medium",now);Check(card.EntryId!=null&&card.NextAt==now+3600,"New widget initial selection/timer");
    var restored=new DesktopCardStore(widgetPath,dict,()=>widgetLearning).Get(card.Id);Check(restored==card,"Widget current entry survives relaunch");
    widgets.Save(card.EntryId!);widgets.Save(card.EntryId!);
    Check(widgets.Get(card.Id)==card&&widgetLearning.Records().Count==1&&widgetLearning.Summary(now:now).RemainingNew==20,"Save is committed, idempotent, preserves widget and quota");
    widgets.Update(card with{Size="Large",Romaji=true,HeadwordPercent=150,ReadingPercent=50,TranslationPercent=150,Topmost=true},now);
    var styled=widgets.Get(card.Id);Check(styled.EntryId==card.EntryId&&styled.Token==card.Token&&styled.NextAt==card.NextAt,"Display preferences preserve selection and schedule");
    var other=widgets.Create("Small",now);Check(other==styled with{Size="Small"}&&widgets.Cards.Count==1,"Selecting a size reuses the sole card and preserves its settings/selection");
    Parallel.For(0,12,_=>widgets.Create("Medium",now));Check(widgets.Cards.Count==1&&widgets.Get(card.Id).Id==card.Id,"Concurrent show/resize cannot create duplicates");
    int wins=0;Parallel.For(0,12,_=>{if(widgets.Advance(card.Id,card.EntryId,card.Token,now))Interlocked.Increment(ref wins);});
    var advanced=widgets.Get(card.Id);Check(wins==1&&advanced.EntryId!=card.EntryId,"Concurrent Next advances exactly once");
    Check(!widgets.Advance(card.Id,card.EntryId,card.Token,now),"Stale Next rejected");
    widgets.Move(card.Id,-200,120);Check(new DesktopCardStore(widgetPath,dict,()=>widgetLearning).Get(card.Id).Left==-200,"Position persists including negative monitor coordinates");
    widgets.Update(widgets.Get(card.Id) with{RefreshSeconds=0},now);Check(!widgets.Tick(card.Id,now+999999)&&widgets.Get(card.Id).NextAt==null,"Manual-only widget");
    widgets.Update(widgets.Get(card.Id) with{RefreshSeconds=300},now);
    Check(!widgets.Tick(card.Id,now+299)&&widgets.Tick(card.Id,now+600)&&!widgets.Tick(card.Id,now+600),"Overdue timer advances once then reschedules");
    var inbox=widgets.Update(widgets.Get(card.Id) with{Feed="Learning list",ListId="inbox"},now);Check(widgets.Display(inbox.Id)?.Id==card.EntryId,"Inbox feed");
    Check(widgets.Advance(inbox.Id,inbox.EntryId,inbox.Token,now)&&widgets.Get(inbox.Id).EntryId==inbox.EntryId,"One-entry list remains usable");
    string empty=widgetLearning.CreateList("Empty");widgets.Update(widgets.Get(card.Id) with{ListId=empty},now);Check(widgets.Display(card.Id)==null,"Empty learning-list state");
    var emptyBefore=widgets.Get(card.Id);widgets.Display(card.Id);Check(widgets.Get(card.Id)==emptyBefore,"Empty-list refresh does not churn storage");
    widgetLearning.DeleteList(empty);Throws(()=>widgets.Display(card.Id),"Deleted-list error");
    widgets.Update(widgets.Get(card.Id) with{ListId="inbox"},now);widgetLearning.Remove(card.EntryId!);Check(widgets.Display(card.Id)==null,"Removed entry no longer displayed in learning list");
    widgetLearning.Save(card.EntryId!);Check(widgets.Display(card.Id)?.Id==card.EntryId,"Restored list entry becomes available");
    var beforeFailure=widgets.Get(other.Id);Directory.CreateDirectory(widgetPath+".tmp");
    Throws(()=>widgets.Advance(other.Id,beforeFailure.EntryId,beforeFailure.Token,now),"Widget write failure");Check(widgets.Get(other.Id)==beforeFailure,"Failed Next preserves in-memory entry/token");Directory.Delete(widgetPath+".tmp");
    Check(new DesktopCardStore(widgetPath,dict,()=>widgetLearning).Get(other.Id)==beforeFailure,"Failed Next preserves persisted entry/token");
    widgets.Remove(other.Id);Check(widgets.Cards.Count==0&&new DesktopCardStore(widgetPath,dict,()=>widgetLearning).Cards.Count==0,"Widget removal persists");
    var replacement=widgets.Create("Large",now);Check(widgets.Cards.Count==1&&replacement.Id!=card.Id,"Recreate after removal");
    var legacyPath=Path.Combine(root,"legacy-widgets.json");string legacy=System.Text.Json.JsonSerializer.Serialize(new DesktopCardDocument(1,[replacement,replacement with{Id="legacy-second",Size="Small"}]));
    File.WriteAllText(legacyPath,legacy);var migratedWidgets=new DesktopCardStore(legacyPath,dict,()=>widgetLearning);
    Check(migratedWidgets.Cards.Count==1&&migratedWidgets.Cards[0]==replacement,"Migration retains the original widget unchanged");
    Check(File.ReadAllText(legacyPath+".multiple-backup")==legacy&&new DesktopCardStore(legacyPath,dict,()=>widgetLearning).Cards.Count==1,"Migration preserves backup and survives relaunch");
    var blockedPath=Path.Combine(root,"blocked-widgets.json");File.WriteAllText(blockedPath,legacy);Directory.CreateDirectory(blockedPath+".tmp");
    Throws(()=>new DesktopCardStore(blockedPath,dict,()=>widgetLearning),"Failed singleton migration reports error");Check(File.ReadAllText(blockedPath)==legacy,"Failed migration preserves original settings");
    File.WriteAllText(Path.Combine(root,"broken-widgets.json"),"broken");Throws(()=>new DesktopCardStore(Path.Combine(root,"broken-widgets.json"),dict,()=>widgetLearning),"Corrupt widget configuration is not overwritten");
    Console.WriteLine($"PASS: {checks} checks (dictionary, scheduler, learning, transactions, calendar, settings).");
}
catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
finally {Directory.Delete(root,true);}
