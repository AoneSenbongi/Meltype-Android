// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Config;
using Meltype.Composition;
internal static class Feature130Tests
{
    sealed class Plain : IKanjiConverter
    {
        public string? Convert(string text) => text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text, string? context = null) => [new(text,text)];
    }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static AndroidInputSession Session(InputPreferences prefs, Action<int,string,CompositionView?>? output = null) =>
        new(new Plain(), _ => [], output ?? ((_,_,_) => {}), () => prefs);
    static void Type(AndroidInputSession s, string raw) { foreach (var c in raw) s.Character(c); }
    public static void Run()
    {
        Require(!InputFeatureSettings.All.Any(f => f.Key == "LogKeyIntervals"), "Unrequested key timing setting remains visible");
        Require(typeof(InputPreferences).GetProperty("LogKeyIntervals") == null, "Removed key timing preference remains");
        Require(typeof(CompositionOptions).GetProperty("LogKeyIntervals") == null, "Removed key timing recorder remains");
        Require(!System.Text.Json.JsonSerializer.Deserialize<InputPreferences>("{\"LogKeyIntervals\":true,\"LearningEnabled\":false}")!.LearningEnabled, "Legacy timing flag broke unrelated preferences");
        Require(!InputFeatureSettings.All.Any(f => f.Key.StartsWith("Esc")), "Removed cancel settings remain visible");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<InputPreferences>("{\"EscStepsBack\":true,\"EscRevertsSelectedClause\":true,\"LearningEnabled\":false,\"ShowTypedKeys\":true}")!;
        Require(!legacy.LearningEnabled && legacy.ShowTypedKeys, "Old cancel fields broke remaining settings");
        Require(!System.Text.Json.JsonSerializer.Serialize(legacy).Contains("Esc"), "Removed cancel fields remain serialized");
        var defaults = new InputPreferences();
        Require(defaults.LocalZenz, "Selected release profile must enable the model by default");
        Require(System.Text.Json.JsonSerializer.Deserialize<InputPreferences>("{}")!.LocalZenz, "Default record must match the release profile");
        foreach (var feature in InputFeatureSettings.All)
        {
            var changed = feature.Write(defaults, !feature.Read(defaults));
            Require(feature.Read(changed) != feature.Read(defaults), "Setting is not connected: " + feature.Key);
            Require(changed.Comma == defaults.Comma && changed.LearningEnabled == defaults.LearningEnabled, "Setting overwrote other preferences");
        }
        var calc=Session(new() {CalculatorCandidates=true});Type(calc,"1+2*3");calc.Key(0x20);
        Require(calc.View?.Candidates.Contains("7")==true,"Android calculator switch does not produce result candidate");
        var abbr=Session(new() {ConsonantAbbreviations=true});Type(abbr,"ast");abbr.Key(0x20);
        Require(abbr.View?.Candidates.Contains("明日")==true,"Android abbreviation switch is disconnected");
        var reads=0; var clipboard="copied-value";
        var clip=new AndroidInputSession(new Plain(), _=>[], (_,_,_)=>{},()=>new InputPreferences {ClipboardCandidates=true},
            clipboard:()=>{reads++;return clipboard;});
        Type(clip,"pe-suto");Require(reads==0,"Clipboard read before conversion");clip.Key(0x20);
        Require(reads==1 && clip.View?.Candidates.Contains(clipboard)==true,"Clipboard candidate did not read once on conversion");
        reads=0;
        var hidden=new AndroidInputSession(new Plain(), _=>[], (_,_,_)=>{},()=>new InputPreferences {ClipboardCandidates=true},false,
            clipboard:()=>{reads++;return clipboard;});Type(hidden,"pe-suto");hidden.Key(0x20);
        Require(reads==0,"Private editor read clipboard");
        var names=Session(new(){Predictions=true,EnglishNamePredictions=true});Type(names,"shaomi");
        Require(names.View?.Predictions?.Contains("Xiaomi")==true,"English-name prediction is not connected");
        var predicted="";
        names=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)predicted+=text;},()=>new InputPreferences {Predictions=true,EnglishNamePredictions=true});
        Type(names,"shaomi");names.SelectPrediction(names.View!.Predictions!.ToList().IndexOf("Xiaomi"));
        Require(predicted=="Xiaomi","Prediction click changed text");
        var literal=Session(new(){EnglishFirstInput=true});Type(literal,"nihongo");
        Require(literal.View?.Text=="nihongo","English-first input did not remain literal");literal.Key(0x75);
        Require(literal.View?.Text!="nihongo","English-first hiragana mode did not switch to Japanese");
        var normal=Session(new(){ShowCandidatesWhileTyping=false});Type(normal,"nihongo");
        Require(normal.View?.Candidates.Count==0,"Candidate-display switch was ignored");
        var keys=Session(new(){ShowTypedKeys=true});Type(keys,"nihongo");Require(keys.View?.Typed=="nihongo","Typed-key display switch was ignored");
        var spaces="";
        var wide=Session(new(){FullWidthSpaceByDefault=true},(op,text,_)=>{if(op==1)spaces+=text;});wide.SpaceKey(false);wide.SpaceKey(true);
        Require(spaces=="　 ","Default full-width and shifted half-width spaces failed");
        spaces="";wide=Session(new(){ShiftSpaceHalfWidth=true},(op,text,_)=>{if(op==1)spaces+=text;});wide.SpaceKey(true);
        Require(spaces==" ","Explicit Shift half-width space failed");
        foreach(var inside in new[]{false,true})
        {
            var ops=new List<(int,string)>();var brackets=Session(new(){FullWidthParentheses=true,CursorIntoBrackets=inside},(op,text,_)=>ops.Add((op,text)));
            brackets.BeginBracketConversion(false);Require(brackets.View?.Text=="（）","Full-width bracket default ignored");brackets.Key(0x0D);
            Require(ops.Last()==(inside?7:1,"（）"),"Bracket cursor setting ignored");
        }
        var slash=Session(new(){FullWidthSlash=true});Type(slash,"nihongo/");Require(slash.View?.Text.Contains('／')==true,"Full-width slash switch ignored");
        var square=Session(new(){SquareBrackets=true});Type(square,"[");Require(square.View?.Text=="［","Square-bracket switch ignored");
        var laugh=Session(new(){LaughterAfterKana=true});Type(laugh,"new");Require(laugh.View?.Text=="ねw","Laughter switch ignored");
        var folder=Path.Combine(Path.GetTempPath(),"meltype-features-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            var file=Path.Combine(folder,"preferences.json");var store=new InputPreferenceStore(file);
            foreach(var f in InputFeatureSettings.All)store.Save(f.Write(store.Current,true));
            Require(InputFeatureSettings.All.All(f=>f.Read(new InputPreferenceStore(file).Current)),"Feature settings do not round trip");
            var dictionary=new AndroidUserDictionary(Path.Combine(folder,"user-dictionary.json"));dictionary.Add("x","一文字");dictionary.Add("abc","登録語");
            foreach(var reading in new[]{"x","abc"})
            {
                var registered=new AndroidInputSession(new Plain(),_=>[],(_,_,_)=>{},userDictionary:dictionary);
                Type(registered,reading);Require(registered.View!.Candidates.Contains(reading=="x"?"一文字":"登録語"),"ASCII user dictionary reading not usable: "+reading);
            }
            var contextual=new AndroidInputSession(new Plain(),_=>[],(_,_,_)=>{},()=>new InputPreferences{ContextualLearning=true},
                surrounding:()=> ("I want ",null),stateDirectory:folder);
            Type(contextual,"nihongo");contextual.Key(0x79);contextual.Commit();
            Require(new LanguageMemory(Path.Combine(folder,"language-memory.json")).Entries().Any(e=>e.Context=="英語" || e.Key?.EndsWith("@e")==true),"Context learning was not saved separately");
            var phrases=new PhraseHistory(Path.Combine(folder,"phrase-history.tsv"));phrases.Remember("にほんごのぶんしょう","日本語の文章");
            var history=new AndroidInputSession(new Plain(),_=>[],(_,_,_)=>{},()=>new InputPreferences{Predictions=true},stateDirectory:folder);
            Type(history,"nihongo");var index=history.View!.Predictions!.ToList().IndexOf("日本語の文章");
            Require(index>=0,"Phrase prediction was not loaded");history.ForgetPrediction(index);
            Require(new PhraseHistory(Path.Combine(folder,"phrase-history.tsv")).Count==0,"Prediction long-press did not forget stored phrase");
        }
        finally{Directory.Delete(folder,true);}
        Console.WriteLine("PASS: 1.3.0 settings, calculator, abbreviation, clipboard protection, predictions/forget, symbols, spaces, English-first and persistence");
    }
}
