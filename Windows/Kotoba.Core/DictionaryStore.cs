using System.Globalization;
using System.Text;
using Kotoba.Storage;

namespace Kotoba.Core;

public sealed record DictionaryEntry(string Id, string Kind, string Text, string Reading, string Meaning, string Detail, int Strokes, string Hiragana)
{
    public bool IsKanji => Kind == "kanji";
    public string Read(bool romaji = false) => !romaji && Hiragana.Length > 0 ? Hiragana : Reading;
}

public sealed class DictionaryStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        db.Open(); return db;
    }
    private List<DictionaryEntry> Query(string predicate, string order, params object?[] values)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT id,kind,text,reading,meaning,detail,strokes,hiragana FROM entries WHERE eligible=1 {predicate} {order}";
        for (int i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("@p" + i, values[i] ?? DBNull.Value);
        using var r = cmd.ExecuteReader(); var result = new List<DictionaryEntry>();
        while (r.Read()) result.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6), r.GetString(7)));
        return result;
    }
    // Match extract_database.py: fold accents, preserving Japanese voicing marks.
    public static string Fold(string value)
    {
        var b = new StringBuilder();
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            var category=CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            { if (ch is '\u3099' or '\u309a') b.Append(ch); }
            else b.Append(char.ToLowerInvariant(ch));
        }
        return b.ToString().Normalize(NormalizationForm.FormC);
    }
    public List<DictionaryEntry> Search(string text, string? kind = null)
    {
        var term = Fold(text.Trim()).Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return Query("AND search_text LIKE @p0 ESCAPE char(92) AND (@p1 IS NULL OR kind=@p1)", "ORDER BY length(text),source_id LIMIT 100", "%" + term + "%", kind);
    }
    public DictionaryEntry? Entry(string id) => Query("AND id=@p0", "", id).FirstOrDefault();
    public List<DictionaryEntry> KanjiIn(string text)
    {
        var characters=StringInfo.GetTextElementEnumerator(text);var unique=new List<string>();
        while(characters.MoveNext()) {string value=characters.GetTextElement();if(!unique.Contains(value))unique.Add(value);}
        if(unique.Count==0)return [];
        var matches=Query("AND kind='kanji' AND text IN ("+string.Join(",",unique.Select((_,i)=>"@p"+i))+")","",unique.Cast<object?>().ToArray());
        var map=matches.GroupBy(e=>e.Text).ToDictionary(g=>g.Key,g=>g.First());
        return unique.Where(map.ContainsKey).Select(c=>map[c]).ToList();
    }
    public List<DictionaryEntry> WordsContainingKanji(string character)
    {
        if(new StringInfo(character).LengthInTextElements!=1)return [];
        return Query("AND kind='word' AND instr(text,@p0)>0","ORDER BY length(text),source_id",character);
    }
    public DictionaryEntry? Random(string? kind, string? excluding) => Query("AND id!=@p0 AND (@p1 IS NULL OR kind=@p1)", "ORDER BY random() LIMIT 1", excluding ?? "", kind).FirstOrDefault();
    public (int Kanji, int Words) Counts()
    {
        using var db = Open(); using var c = db.CreateCommand(); c.CommandText = "SELECT kind,count(*) FROM entries WHERE eligible=1 GROUP BY kind";
        using var r = c.ExecuteReader(); int k = 0, w = 0;
        while (r.Read()) { if (r.GetString(0) == "kanji") k = r.GetInt32(1); else w = r.GetInt32(1); }
        return (k, w);
    }
}
