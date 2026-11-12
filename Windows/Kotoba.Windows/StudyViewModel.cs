using System.ComponentModel;
using System.IO;
using Kotoba.Core;

namespace Kotoba.Windows;
public sealed class StudyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public DictionaryStore Dictionary { get; }
    public LearningStore? Learning { get; private set; }
    public Settings Preferences { get; }
    public string SettingsPath { get; }
    public string LearningPath { get; }
    public string Page { get; set; } = "Today";
    public string Query { get; set; } = "";
    public string SavedQuery { get; set; } = "";
    public string SavedKind { get; set; } = "Mixed";
    public string? Scope { get; set; } = "inbox";
    public bool Exploring { get; set; }
    public bool Detail { get; set; }
    public bool Reviewing { get; set; }
    public bool TranslationVisible { get; set; } = true;
    public DictionaryEntry? Entry { get; set; }
    public ReviewSession? Session { get; private set; }
    public List<SavedEntry> Saved { get; private set; } = [];
    public List<LearningList> Lists { get; private set; } = [];
    public LearningSummary Summary { get; private set; } = new(0,20,0);
    private string? operationError, dictionaryError, settingsError;
    public string? Error { get => operationError??dictionaryError??settingsError; private set => operationError=value; }
    public (int Kanji,int Words) Counts { get; private set; }
    public StudyViewModel(string? directory=null)
    {
        directory ??= Settings.DataDirectory;
        SettingsPath=Path.Combine(directory,"settings.json"); LearningPath=Path.Combine(directory,"learning.sqlite");
        try { Preferences=Settings.Load(SettingsPath); } catch(Exception ex) { Preferences=new(); settingsError="Could not read settings: "+ex.Message; }
        Dictionary=new(Path.Combine(AppContext.BaseDirectory,"Data","dictionary.sqlite"));
        ReloadDictionary();
        Refresh();
    }
    public string? Kind => Preferences.Content=="Kanji"?"kanji":Preferences.Content=="Words"?"word":null;
    public void ReloadDictionary()
    {try {Counts=Dictionary.Counts();Entry=Dictionary.Random(Kind,null);dictionaryError=null;}catch(Exception ex){dictionaryError="Could not load dictionary: "+ex.Message;}}
    public Card? CurrentCard => Saved.FirstOrDefault(r=>r.Id==Session?.CurrentId)?.Card;
    public DictionaryEntry? ReviewEntry => Session?.CurrentId is string id ? Dictionary.Entry(id) : null;
    public void Notify() => PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(""));
    public void Act(Action action,bool refresh=true)
    {
        try { action(); Error=null; if(refresh) Refresh(); } catch(Exception ex) { Error=ex.Message; }
        Notify();
    }
    public void Refresh()
    {
        try { Learning??=new(LearningPath,Dictionary); Saved=Learning.Records(); Lists=Learning.Lists(); Summary=Learning.Summary(Preferences.DailyNewLimit); Session=Learning.Session(); Error=null; }
        catch(Exception ex) { Learning=null; Error="Learning storage is unavailable. Check access to " + Path.GetDirectoryName(LearningPath)+" and Retry. "+ex.Message; }
    }
    public LearningStore Store => Learning??throw new InvalidOperationException("Learning storage is unavailable. Use Retry.");
    public void PersistSettings() {Preferences.Save(SettingsPath);settingsError=null;}
    public void Next() { Entry=Dictionary.Random(Kind,Entry?.Id); TranslationVisible=true; }
    public void Start(bool resume=false,string? scope=null)
    { Session=resume?Store.Resume(Preferences.DailyNewLimit):Store.Start(scope,Preferences.DailyNewLimit); Reviewing=true; Page="Today"; }
    public void Rate(ReviewGrade grade) { Session=Store.Rate(Session?.CurrentId??"",grade,Preferences.DailyNewLimit); }
    public void SelectEntry(DictionaryEntry entry) { Entry=entry; Page="Dictionary"; Detail=true; Exploring=false; TranslationVisible=true; }
}
