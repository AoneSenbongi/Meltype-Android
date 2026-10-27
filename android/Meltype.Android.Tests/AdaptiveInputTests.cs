// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
internal static class AdaptiveInputTests
{
    public static void Run()
    {
        var builtIn = NextKeyPrediction.BuiltIn();
        if (builtIn.Probability("arigat", 'o') < .55)
            throw new Exception("Built-in prediction missed arigat -> o: " + builtIn.Probability("arigat", 'o'));
        var ops = new List<int>();
        var session = new AndroidInputSession(new Plain(), _ => [], (op, _, _) => ops.Add(op));
        session.Character('a');
        if (ops.FirstOrDefault() != 2 || session.View == null)
            throw new Exception("Initial Hide/Show prediction lifecycle fixture changed");
        var prediction = new NextKeyPrediction(["time", "timer", "tiny", "nihongo"]);
        if (prediction.Probability("tim", 'e') != 1 || prediction.Probability("", 'e') != 0 ||
            prediction.Probability("unknown", 'e') != 0) throw new Exception("Next-key prefix prediction failed");
        var keys = new[] { new TouchKey('w', 0, 0, 40, 54), new TouchKey('e', 40, 0, 40, 54) };
        foreach (var (x, ambiguous) in new[] { (38f, true), (28f, true), (27f, false), (20f, false) })
        {
            var touch = AdaptiveKeyTarget.Capture(keys, x, 27, c => prediction.Probability("tim", c));
            if (touch?.Original.Letter != 'w' || touch.Alternatives.Any() != ambiguous)
                throw new Exception("Touch evidence changed original key or boundary at " + x);
        }
        if (AdaptiveKeyTarget.Capture(keys, 90, 27, _ => 1) != null ||
            AdaptiveKeyTarget.Capture([new('w',0,0,40,54),new('e',40,54,40,54)],38,27,_=>1)!.Alternatives.Count != 0)
            throw new Exception("Touch evidence included a gap or a different row");
        Console.WriteLine("PASS: prefix prediction and bounded deferred touch evidence");
    }
    sealed class Plain : IKanjiConverter
    {
        public string? Convert(string text) => text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text, string? context = null) => [new(text, text)];
    }
}
