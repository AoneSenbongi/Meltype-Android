// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
internal static class PersonalRomajiTests
{
    public static void Audit(string modelPath, string analysisPath, string reportPath)
    {
        var model = PersonalRomajiTypos.Load(modelPath);
        using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(analysisPath));
        var results = data.RootElement.EnumerateArray().Select(row =>
        {
            var before = row.GetProperty("roman_before").GetString()!;
            var after = row.GetProperty("roman_after").GetString()!;
            var fix = model.Fix(before, true);
            return new { pair = row.GetProperty("pair").GetString(), label = row.GetProperty("label").GetString(),
                before, after, corrected = fix?.Right, matches = fix?.Right == after };
        }).ToArray();
        File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(results));
        Console.WriteLine($"Audit: {results.Length} pairs, {results.Count(r=>r.corrected!=null)} corrected, " +
            $"{results.Count(r=>r.matches)} matched, {results.Count(r=>r.label=="not_typo" && r.corrected!=null)} non-typos changed");
    }
    public static void Run()
    {
        var model = new PersonalRomajiTypos([new("o", "i", 3)]);
        var fix = model.Fix("chinamoni", false);
        if (fix?.Right != "chinamini" || fix?.Kana != "ちなみに") throw new Exception("Repeated roman typo was not corrected");
        foreach (var word in new[] {"chinamini", "open", "time", "data", "code", "nihongo", "Chinamoni", "user@example.com"})
            if (model.Fix(word, true) != null) throw new Exception("Normal word or explicit input was rewritten: " + word);
        if (new PersonalRomajiTypos([new("o", "i", 2)]).Fix("chinamoni", true) != null)
            throw new Exception("Unrepeated pattern was used");
        var bounded = new PersonalRomajiTypos([new("o", "i", 3, [new("nam", "ni", false, true)])]);
        if (bounded.Fix("chinamoni", true)?.Kana != "ちなみに" || bounded.Fix("gokou", true) != null ||
            new PersonalRomajiTypos([new("o", "i", 3)], ["chinamoni"]).Fix("chinamoni",true) != null)
            throw new Exception("Observed context or negative reading was ignored");
        var roman = new RomajiDetector();
        if (roman.Canonicalize("simasu", true) != roman.Canonicalize("shimasu", true) ||
            roman.Canonicalize("dalarA",true) == "dakara") throw new Exception("Canonicalization changed meaning");
        // Compare disabled/protected input with the current upstream detector without a personal model.
        var baselineSession = new AndroidInputSession(new Plain(), _ => [], (_,_,_) => {});
        foreach (var c in "chinamoni") baselineSession.Character(c);
        var uncorrected = baselineSession.View!.Text;
        var deletedBaseline = new AndroidInputSession(new Plain(), _ => [], (_,_,_) => {});
        foreach (var c in "chinamo") deletedBaseline.Character(c);
        foreach (var allowed in new[] {true,false})
        foreach (var learning in new[] {true,false})
        {
            var committed = "";
            var session = new AndroidInputSession(new Plain(), _ => [], (op,text,_) => {if(op==1)committed+=text;},
                () => new InputPreferences {LearningEnabled=learning},allowed,personalTypos:model);
            foreach(var c in "chinamoni")session.Character(c);
            var expected = allowed && learning ? "ちなみに" : uncorrected;
            if (session.View?.Text != expected || committed.Length != 0) throw new Exception("Live correction/policy failed (allowed=" + allowed + ", learning=" + learning + ", expected=" + expected + "): " + session.View?.Text);
            session.Key(0x0D);
            if(committed!=expected)throw new Exception("Commit differs from corrected preview: " + committed);
        }
        var editable = new AndroidInputSession(new Plain(),_=>[],(_,_,_)=>{},personalTypos:model);
        foreach(var c in "chinamoni")editable.Character(c);
        editable.Key(0x08);
        if (editable.View?.Text != deletedBaseline.View?.Text) throw new Exception("Backspace did not remove the original kana unit: " + editable.View?.Text);
        editable.Character('n'); editable.Character('i');
        if(editable.View?.Text!="ちなみに")throw new Exception("Editing lost original input or reapplied a correction twice");
        var enabled = true;
        var dynamicCommit = "";
        var dynamicSession = new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)dynamicCommit+=text;},
            () => new InputPreferences { LearningEnabled=enabled },personalTypos:model);
        foreach(var c in "chinamoni")dynamicSession.Character(c);
        enabled = false;
        dynamicSession.Key(0x0D);
        if(dynamicCommit!=uncorrected)throw new Exception("Disabling learning reused the corrected segment cache");
        // Explicit kana mode must render the original keys, independent of the personal projection.
        editable.ToggleKana();
        if(editable.View?.Text!="チナモニ")throw new Exception("Explicit kana mode was corrected");
        var dictionaryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json");
        try
        {
            var dictionary = new AndroidUserDictionary(dictionaryPath);
            dictionary.Add("ちなもに", "固有名");
            var registered = new AndroidInputSession(new Plain(), _=>[], (_,_,_)=>{},
                userDictionary:dictionary,personalTypos:model);
            foreach(var c in "chinamoni")registered.Character(c);
            if(registered.View?.Text!=uncorrected)throw new Exception("Registered reading was corrected");
        }
        finally { File.Delete(dictionaryPath); }
        Console.WriteLine("PASS: repeated raw roman correction, live/commit agreement, protection and original-key editing");
    }
    sealed class Plain : IKanjiConverter
    {
        public string? Convert(string text)=>text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text,string? context=null)=>[new(text,text)];
    }
}
