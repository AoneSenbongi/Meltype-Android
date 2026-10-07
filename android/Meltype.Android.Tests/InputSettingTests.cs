// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Config;
using Meltype.Composition;

static class InputSettingTests
{
    public static void Run()
    {
        foreach (var comma in new[] { "、", "，", "," })
        foreach (var period in new[] { "。", "．", "." })
        {
            var preferences = new InputPreferences { Comma = comma, Period = period };
            var document = "";
            var session = new AndroidInputSession(new Passthrough(), _ => [], (op, text, _) => { if (op == 1) document += text; }, () => preferences);
            foreach (var c in "nihongo,nihongo.") session.Character(c);
            session.Commit();
            if (document != "にほんご" + comma + "にほんご" + period) throw new Exception("Japanese punctuation setting was not applied: " + document);
            document = "";
            session.SetEnglish(true); foreach (var c in "hello,.") session.Character(c);
            if (document != "hello,.") throw new Exception("English punctuation changed");
        }
        var calls = 0;
        var mozc = new AndroidMozcConverter(_ => { calls++; throw new Exception("must not learn"); }, true) { LearningEnabled = () => false };
        mozc.Learn(null, [new("こうせい", "校正")]);
        if (calls != 0) throw new Exception("Stopped learner contacted Mozc");
        Console.WriteLine("PASS: all Japanese punctuation pairs through live session, English mode, stopped Mozc learning");
    }
    sealed class Passthrough : IKanjiConverter
    {
        public string? Convert(string text) => text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text, string? context = null) => [new(text, text)];
    }
}
