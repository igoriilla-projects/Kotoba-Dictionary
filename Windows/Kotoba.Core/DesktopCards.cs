using System.Text.Json;

namespace Kotoba.Core;

public sealed record DesktopCard
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Size { get; init; } = "Medium";
    public string Content { get; init; } = "Mixed";
    public string Feed { get; init; } = "Dictionary";
    public string ListId { get; init; } = "inbox";
    public bool Romaji { get; init; }
    public bool ShowReading { get; init; } = true;
    public bool ShowTranslation { get; init; } = true;
    public int HeadwordPercent { get; init; } = 100;
    public int ReadingPercent { get; init; } = 100;
    public int TranslationPercent { get; init; } = 100;
    public int RefreshSeconds { get; init; } = 3600;
    public bool Topmost { get; init; }
    public double? Left { get; init; }
    public double? Top { get; init; }
    public string? EntryId { get; init; }
    public string Token { get; init; } = Guid.NewGuid().ToString("N");
    public double? NextAt { get; init; }
    public static (double Width,double Height) Dimensions(string size) => size switch {"Small"=>(170,170),"Large"=>(360,360),_=>(360,170)};
    public DesktopCard Validated() => this with {
        Size=new[]{"Small","Medium","Large"}.Contains(Size)?Size:"Medium",
        Content=new[]{"Mixed","Kanji","Words"}.Contains(Content)?Content:"Mixed",
        Feed=Feed=="Learning list"?Feed:"Dictionary",ListId=string.IsNullOrWhiteSpace(ListId)?"inbox":ListId,
        HeadwordPercent=Math.Clamp(HeadwordPercent,50,150),ReadingPercent=Math.Clamp(ReadingPercent,50,150),TranslationPercent=Math.Clamp(TranslationPercent,50,150),
        RefreshSeconds=new[]{0,300,900,1800,3600,21600,86400}.Contains(RefreshSeconds)?RefreshSeconds:3600,
        Left=Left.HasValue&&double.IsFinite(Left.Value)?Left:null,Top=Top.HasValue&&double.IsFinite(Top.Value)?Top:null,
        NextAt=NextAt.HasValue&&double.IsFinite(NextAt.Value)?NextAt:null
    };
}
public sealed record DesktopCardDocument(int Version, List<DesktopCard> Cards);

/// One private desktop selection, deliberately independent of main-app preferences.
/// All changes commit to an atomic replacement before replacing in-memory state.
public sealed class DesktopCardStore
{
    private readonly object gate=new();
    private readonly string path;
    private readonly DictionaryStore dictionary;
    private readonly Func<LearningStore> learning;
    private List<DesktopCard> cards;
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,WriteIndented=true};
    public DesktopCardStore(string path,DictionaryStore dictionary,Func<LearningStore> learning)
    {
        this.path=path;this.dictionary=dictionary;this.learning=learning;
        var doc=File.Exists(path)?JsonSerializer.Deserialize<DesktopCardDocument>(File.ReadAllText(path),Json)??throw new InvalidDataException("Desktop widget settings are empty."):new(1,[]);
        if(doc.Version!=1)throw new InvalidDataException("Unsupported desktop widget settings version.");
        cards=doc.Cards?.Select(c=>c.Validated()).ToList()??throw new InvalidDataException("Desktop widget settings are missing.");
        if(cards.Any(c=>string.IsNullOrWhiteSpace(c.Id))||cards.Select(c=>c.Id).Distinct().Count()!=cards.Count)throw new InvalidDataException("Desktop widget identities are invalid.");
        if(cards.Count>1)
        {
            // Keep the original card unchanged, and retain legacy settings for recovery.
            if(!File.Exists(path+".multiple-backup"))File.Copy(path,path+".multiple-backup");
            Commit([cards[0]]);
        }
    }
    public List<DesktopCard> Cards {get {lock(gate)return [..cards];}}
    public DesktopCard Get(string id) {lock(gate)return cards.First(c=>c.Id==id);}
    private void Commit(List<DesktopCard> updated)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary=path+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(new DesktopCardDocument(1,updated),Json));
        File.Move(temporary,path,true);cards=updated;
    }
    private void Replace(DesktopCard card)=>Commit(cards.Select(c=>c.Id==card.Id?card:c).ToList());
    private List<string>? Pool(DesktopCard card)
    {
        if(card.Feed!="Learning list")return null;
        var store=learning();
        if(card.ListId!="inbox"&&!store.Lists().Any(l=>l.Id==card.ListId))throw new InvalidOperationException("This learning list was deleted. Right-click → Edit widget to choose another list.");
        return store.Records(card.ListId).Select(r=>dictionary.Entry(r.Id)).OfType<DictionaryEntry>()
            .Where(e=>card.Content=="Mixed"||(card.Content=="Kanji"?e.IsKanji:!e.IsKanji)).Select(e=>e.Id).ToList();
    }
    private string? Pick(DesktopCard card)
    {
        var pool=Pool(card);
        if(pool!=null) {var choices=pool.Where(id=>id!=card.EntryId).ToList();return choices.Count>0?choices[Random.Shared.Next(choices.Count)]:pool.FirstOrDefault();}
        return dictionary.Random(card.Content=="Kanji"?"kanji":card.Content=="Words"?"word":null,card.EntryId)?.Id;
    }
    private DesktopCard Selected(DesktopCard card,double now)=>card with {EntryId=Pick(card),Token=Guid.NewGuid().ToString("N"),NextAt=card.RefreshSeconds>0?now+card.RefreshSeconds:null};
    public DesktopCard Create(string size,double? now=null)
    {lock(gate){if(cards.Count>0)return Update(cards[0] with{Size=size},now);var card=Selected(new DesktopCard{Size=size}.Validated(),now??LearningStore.Now);Commit([card]);return card;}}
    public void Remove(string id) {lock(gate)Commit(cards.Where(c=>c.Id!=id).ToList());}
    public DesktopCard Update(DesktopCard options,double? now=null)
    {
        lock(gate)
        {
            var old=Get(options.Id);var c=options.Validated() with {EntryId=old.EntryId,Token=old.Token,NextAt=old.NextAt};
            if(c.Content!=old.Content||c.Feed!=old.Feed||c.ListId!=old.ListId)c=Selected(c,now??LearningStore.Now);
            else if(c.RefreshSeconds!=old.RefreshSeconds)c=c with {NextAt=c.RefreshSeconds>0?(now??LearningStore.Now)+c.RefreshSeconds:null};
            Replace(c);return c;
        }
    }
    public void Move(string id,double left,double top) {lock(gate){var c=Get(id);Replace((c with {Left=left,Top=top}).Validated());}}
    public bool Advance(string id,string? displayedId,string token,double? now=null)
    {
        lock(gate)
        {
            var c=Get(id);if(c.Token!=token||c.EntryId!=displayedId)return false;
            Replace(Selected(c,now??LearningStore.Now));return true;
        }
    }
    public bool Tick(string id,double? now=null)
    {lock(gate){var c=Get(id);double time=now??LearningStore.Now;return c.RefreshSeconds>0&&c.NextAt<=time&&Advance(id,c.EntryId,c.Token,time);}}
    public DictionaryEntry? Display(string id)
    {
        lock(gate)
        {
            var c=Get(id);var pool=Pool(c);
            if(c.EntryId!=null&&(pool==null||pool.Contains(c.EntryId))&&dictionary.Entry(c.EntryId) is DictionaryEntry entry)return entry;
            if(c.EntryId==null&&pool?.Count==0)return null;
            var refreshed=Selected(c,LearningStore.Now);Replace(refreshed);
            return refreshed.EntryId==null?null:dictionary.Entry(refreshed.EntryId);
        }
    }
    public bool IsSaved(string entryId)=>learning().Records().Any(r=>r.Id==entryId);
    public void Save(string entryId)=>learning().Save(entryId);
    public List<LearningList> Lists()=>learning().Lists();
}
