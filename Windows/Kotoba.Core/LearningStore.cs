using System.Text.Json;
using Kotoba.Storage;

namespace Kotoba.Core;
public sealed record LearningList(string Id, string Name) { public override string ToString() => Name; }
public sealed record SavedEntry(string Id, Card? Card, HashSet<string> Lists);
public sealed record ReviewSession(string? Scope = null, string? CurrentId = null, double? RevealedAt = null, string? ExpectedCard = null);
public sealed record LearningSummary(int Due, int RemainingNew, int AvailableNew);

public sealed class LearningStore
{
    public string Path { get; }
    private readonly DictionaryStore dictionary;
    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)!;
    public LearningStore(string path, DictionaryStore dictionary)
    {
        Path = path; this.dictionary = dictionary;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        using var db = Open(); Run(db, "PRAGMA journal_mode=WAL");
        Tx(db, () => {
            int version = int.Parse(Rows(db, "PRAGMA user_version")[0][0]!);
            if (version > 2) throw new InvalidOperationException("This learning database requires a newer version of Kotoba.");
            if (version == 0)
                foreach (var sql in new[] {
                    "CREATE TABLE saved(id TEXT PRIMARY KEY, active INTEGER NOT NULL DEFAULT 1,saved_at REAL NOT NULL,card TEXT,due REAL,first_review REAL)",
                    "CREATE TABLE lists(id TEXT PRIMARY KEY,name TEXT NOT NULL)",
                    "CREATE TABLE memberships(entry_id TEXT NOT NULL REFERENCES saved(id),list_id TEXT NOT NULL REFERENCES lists(id) ON DELETE CASCADE,PRIMARY KEY(entry_id,list_id))",
                    "CREATE TABLE history(id INTEGER PRIMARY KEY AUTOINCREMENT,entry_id TEXT NOT NULL REFERENCES saved(id),rating INTEGER NOT NULL,reviewed REAL NOT NULL,was_new INTEGER NOT NULL,before_card TEXT,after_card TEXT NOT NULL,undone INTEGER NOT NULL DEFAULT 0)",
                    "CREATE INDEX due_index ON saved(active,due)", "CREATE INDEX history_date ON history(reviewed,was_new,undone)",
                    "CREATE TABLE state(key TEXT PRIMARY KEY,value TEXT NOT NULL)" }) Run(db, sql);
            if (version < 2) { Run(db, "ALTER TABLE history ADD COLUMN fsrs_log TEXT"); Run(db, "PRAGMA user_version=2"); }
            return true;
        });
    }
    public SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        try { db.Open(); Run(db, "PRAGMA foreign_keys=ON"); Run(db, "PRAGMA synchronous=FULL"); return db; }
        catch { db.Dispose(); throw; }
    }
    public static List<string?[]> Rows(SqliteConnection db, string sql, params object?[] args)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = sql;
        for (int i=0;i<args.Length;i++) cmd.Parameters.AddWithValue("@p"+i, args[i] ?? DBNull.Value);
        using var reader = cmd.ExecuteReader(); var result = new List<string?[]>();
        while (reader.Read()) result.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetString(i)).ToArray());
        return result;
    }
    public static void Run(SqliteConnection db, string sql, params object?[] args) => Rows(db, sql, args);
    private static T Tx<T>(SqliteConnection db, Func<T> action)
    {
        Run(db, "BEGIN IMMEDIATE");
        try { var result=action(); Run(db,"COMMIT"); return result; }
        catch { Run(db,"ROLLBACK"); throw; }
    }
    private T Write<T>(Func<SqliteConnection,T> action) { using var db=Open(); return Tx(db,()=>action(db)); }
    private static string? Value(SqliteConnection db,string key) => Rows(db,"SELECT value FROM state WHERE key=@p0",key).FirstOrDefault()?[0];
    private static void Set(SqliteConnection db,string key,string value) => Run(db,"INSERT INTO state VALUES(@p0,@p1) ON CONFLICT(key) DO UPDATE SET value=excluded.value",key,value);
    public void Save(string id) => Write(db => {
        if(dictionary.Entry(id)==null) throw new InvalidOperationException("This entry is not available in the dictionary.");
        Run(db,"INSERT INTO saved(id,saved_at) VALUES(@p0,@p1) ON CONFLICT(id) DO UPDATE SET active=1",id,Now); return true;
    });
    public void Remove(string id) => Write(db => { Run(db,"DELETE FROM memberships WHERE entry_id=@p0",id); Run(db,"UPDATE saved SET active=0 WHERE id=@p0",id); return true; });
    public List<LearningList> Lists() { using var db=Open(); return Rows(db,"SELECT id,name FROM lists ORDER BY name COLLATE NOCASE,id").Select(r=>new LearningList(r[0]!,r[1]!)).ToList(); }
    private static string Name(string name) => name.Trim().Length>0 ? name.Trim() : throw new InvalidOperationException("Enter a list name.");
    public string CreateList(string name) => Write(db=> { var id=Guid.NewGuid().ToString(); Run(db,"INSERT INTO lists VALUES(@p0,@p1)",id,Name(name)); return id; });
    public void RenameList(string id,string name) => Write(db=>{ Run(db,"UPDATE lists SET name=@p0 WHERE id=@p1",Name(name),id); return true; });
    public void DeleteList(string id) => Write(db=>{ Run(db,"DELETE FROM lists WHERE id=@p0",id); return true; });
    public void Membership(string entry,string list,bool included) => Write(db=>{
        if(Rows(db,"SELECT id FROM saved WHERE id=@p0 AND active=1",entry).Count==0) throw new InvalidOperationException("Save this entry first.");
        Run(db,included ? "INSERT OR IGNORE INTO memberships VALUES(@p0,@p1)" : "DELETE FROM memberships WHERE entry_id=@p0 AND list_id=@p1",entry,list); return true;
    });
    private static List<SavedEntry> Records(SqliteConnection db,string? scope=null)
    {
        var map=Rows(db,"SELECT entry_id,list_id FROM memberships").GroupBy(r=>r[0]!).ToDictionary(g=>g.Key,g=>g.Select(r=>r[1]!).ToHashSet());
        return Rows(db,"SELECT id,card FROM saved WHERE active=1 ORDER BY saved_at,id").Select(r=>new SavedEntry(r[0]!,r[1]==null?null:Decode<Card>(r[1]!),map.GetValueOrDefault(r[0]!)??[]))
            .Where(r=>scope==null || (scope=="inbox" ? r.Lists.Count==0 : r.Lists.Contains(scope))).ToList();
    }
    public List<SavedEntry> Records(string? scope=null) => Write(db=>Records(db,scope));
    public static (double Start,double End) DayBounds(double now,TimeZoneInfo? zone=null)
    {
        zone ??= TimeZoneInfo.Local;
        var date=TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds((long)(now*1000)),zone).Date;
        double Utc(DateTime local) { while(zone.IsInvalidTime(local)) local=local.AddMinutes(1); return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),zone)).ToUnixTimeSeconds(); }
        return (Utc(date),Utc(date.AddDays(1)));
    }
    private static LearningSummary Summary(SqliteConnection db,double now,int limit,string? scope=null,TimeZoneInfo? zone=null)
    {
        var bounds=DayBounds(now,zone);
        int used=int.Parse(Rows(db,"SELECT count(*) FROM history WHERE was_new=1 AND undone=0 AND reviewed>=@p0 AND reviewed<@p1",bounds.Start,bounds.End)[0][0]!);
        var records=Records(db,scope); return new(records.Count(r=>r.Card?.Due<=now),Math.Max(0,limit-used),records.Count(r=>r.Card==null));
    }
    public LearningSummary Summary(int limit=20,double? now=null,string? scope=null,TimeZoneInfo? zone=null) => Write(db=>Summary(db,now??Now,limit,scope,zone));
    private static string? NextId(SqliteConnection db,string? scope,double now,int limit)
    {
        var records=Records(db,scope); return records.Where(r=>r.Card?.Due<=now).OrderBy(r=>r.Card!.Due).FirstOrDefault()?.Id
            ?? (Summary(db,now,limit).RemainingNew>0 ? records.FirstOrDefault(r=>r.Card==null)?.Id : null);
    }
    private static string? CardJson(SqliteConnection db,string? id) => id==null?null:Rows(db,"SELECT card FROM saved WHERE id=@p0",id).FirstOrDefault()?[0];
    private static ReviewSession Persist(SqliteConnection db,ReviewSession session) { Set(db,"session",Encode(session)); return session; }
    public ReviewSession? Session() { using var db=Open(); var raw=Value(db,"session"); return raw==null?null:Decode<ReviewSession>(raw); }
    public ReviewSession Start(string? scope=null,int limit=20,double? now=null) => Write(db=> { var id=NextId(db,scope,now??Now,limit); return Persist(db,new(scope,id,ExpectedCard:CardJson(db,id))); });
    public ReviewSession Resume(int limit=20,double? now=null) => Write(db=>{
        double time=now??Now; var raw=Value(db,"session"); var s=raw==null?new():Decode<ReviewSession>(raw);
        if(s.Scope!=null && s.Scope!="inbox" && Rows(db,"SELECT id FROM lists WHERE id=@p0",s.Scope).Count==0) s=s with {Scope=null};
        var current=CardJson(db,s.CurrentId);
        if(!Records(db,s.Scope).Any(r=>r.Id==s.CurrentId&&(r.Card==null||r.Card.Due<=time)) || current!=s.ExpectedCard || (current==null&&Summary(db,time,limit).RemainingNew==0))
        { var id=NextId(db,s.Scope,time,limit); s=s with {CurrentId=id,ExpectedCard=CardJson(db,id),RevealedAt=null}; }
        return Persist(db,s);
    });
    public ReviewSession Reveal(double? now=null) => Write(db=>{
        var raw=Value(db,"session")??throw new InvalidOperationException("Start a review first."); var s=Decode<ReviewSession>(raw);
        return Persist(db,s with {RevealedAt=s.RevealedAt??now??Now});
    });
    public ReviewSession Rate(string id,ReviewGrade grade,int limit=20,double? now=null) => Write(db=>{
        double time=now??Now; var raw=Value(db,"session")??throw new InvalidOperationException("Start a review first."); var s=Decode<ReviewSession>(raw);
        if(s.CurrentId!=id || s.RevealedAt==null || !Records(db,s.Scope).Any(r=>r.Id==id)) throw new InvalidOperationException("Reveal the current card before rating it.");
        string? before=CardJson(db,id);
        if(before!=s.ExpectedCard) throw new InvalidOperationException("This card changed. Continue the review to reload it.");
        var card=before==null?null:Decode<Card>(before);
        if(card==null && Summary(db,time,limit).RemainingNew==0) throw new InvalidOperationException("Today's new-entry allowance is used up.");
        var result=ReviewScheduler.Next(card,grade,s.RevealedAt.Value); string after=Encode(result.Card);
        Run(db,"INSERT INTO history(entry_id,rating,reviewed,was_new,before_card,after_card,fsrs_log) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",id,(int)grade,time,card==null?1:0,before,after,Encode(result.Log));
        Set(db,"undo",Rows(db,"SELECT last_insert_rowid()")[0][0]!); Set(db,"undo.session",raw);
        Set(db,"scheduler","swift-fsrs/v5.0.0; retention=0.9; fuzz=false; shortTerm=true; csharp-port");
        Run(db,"UPDATE saved SET card=@p0,due=@p1,first_review=coalesce(first_review,@p2) WHERE id=@p3",after,result.Card.Due,time,id);
        var next=NextId(db,s.Scope,time,limit); return Persist(db,new(s.Scope,next,ExpectedCard:CardJson(db,next)));
    });
    public bool CanUndo() { using var db=Open(); return !string.IsNullOrEmpty(Value(db,"undo")); }
    public ReviewSession Undo(double? now=null) => Write(db=>{
        var undo=Value(db,"undo"); var row=Rows(db,"SELECT entry_id,before_card,after_card,was_new FROM history WHERE id=@p0 AND undone=0",undo).FirstOrDefault()??throw new InvalidOperationException("There is no rating to undo.");
        if(CardJson(db,row[0])!=row[2]) throw new InvalidOperationException("A later review changed this entry.");
        var card=row[1]==null?null:Decode<Card>(row[1]!);
        Run(db,"UPDATE saved SET card=@p0,due=@p1,first_review=CASE WHEN @p2='1' THEN NULL ELSE first_review END WHERE id=@p3",row[1],card?.Due,row[3],row[0]);
        Run(db,"UPDATE history SET undone=1 WHERE id=@p0",undo); Set(db,"undo","");
        var raw=Value(db,"undo.session"); var s=raw==null?new():Decode<ReviewSession>(raw);
        return Persist(db,s with {CurrentId=row[0],RevealedAt=now??Now,ExpectedCard=row[1]});
    });
}
