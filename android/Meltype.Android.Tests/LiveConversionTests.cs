// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
internal static class LiveConversionTests
{
    sealed class Converter : IKanjiConverter
    {
        public string? Convert(string text) => text == "なつ" ? "夏" : text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text, string? context = null) => [new(text, Convert(text)!)];
    }
    public static void Run()
    {
        foreach (var predictions in new[] { false, true })
        {
            var committed = "";
            var session = new AndroidInputSession(new Converter(), _ => [], (op,text,_) => { if (op == 1) committed += text; }, () => new InputPreferences { Predictions = predictions });
            foreach (var c in "natu") session.Character(c);
            if (session.View?.Text != "夏") throw new Exception("Short Japanese must show the best conversion before choosing a candidate: " + session.View?.Text);
            session.Key(0x0D);
            if (committed != "夏") throw new Exception("Enter must commit the displayed best conversion: " + committed);
        }
        foreach (var raw in new[] { "github", "camera", "open", "a7f1s" })
        {
            var session = new AndroidInputSession(new Converter(), _ => [], (_,_,_) => { });
            foreach (var c in raw) session.Character(c);
            if (session.View?.Text != raw) throw new Exception("English/model token changed by short live conversion: " + raw + " / " + session.View?.Text);
        }
        var manual = new AndroidInputSession(new Converter(), _ => [], (_,_,_) => { });
        foreach (var c in "natu") manual.Character(c);
        manual.Key(0x75);
        if (manual.View?.Text != "なつ") throw new Exception("Explicit hiragana was automatically converted");
        Console.WriteLine("PASS: short Japanese shows and commits best conversion; English/model tokens and explicit hiragana preserved");
    }
}
