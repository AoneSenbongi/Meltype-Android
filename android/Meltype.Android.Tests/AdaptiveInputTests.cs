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
        if (AdaptiveKeyTarget.Choose(keys, 38, 27, c => prediction.Probability("tim", c)) != 'e' ||
            AdaptiveKeyTarget.Choose(keys, 20, 27, c => prediction.Probability("tim", c)) != 'w' ||
            AdaptiveKeyTarget.Choose(keys, 38, 27, _ => 0) != 'w' ||
            AdaptiveKeyTarget.Choose(keys, 90, 27, _ => 1) != null)
            throw new Exception("Adaptive hit bounds stole a center, empty space, or ignored prediction");
        foreach (var (x, expected) in new[] { (30f, 'e'), (28f, 'e'), (27f, 'w'), (20f, 'w') })
            if (AdaptiveKeyTarget.Choose(keys, x, 27, c => prediction.Probability("tim", c)) != expected)
                throw new Exception("Expanded prediction boundary failed at " + x);
        if (AdaptiveKeyTarget.Choose(keys, 30, 27, _ => .5) != 'w' ||
            AdaptiveKeyTarget.Choose(keys, 30, 27, _ => .8) != 'w' ||
            AdaptiveKeyTarget.Choose([new('w', 0, 0, 40, 54), new('e', 40, 54, 40, 54)], 38, 27, c => c == 'e' ? 1 : 0) != 'w')
            throw new Exception("Expanded boundary ignored probability, ties or row separation");
        Console.WriteLine("PASS: prefix prediction and bounded touch adjustment");
    }
    sealed class Plain : IKanjiConverter
    {
        public string? Convert(string text) => text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text, string? context = null) => [new(text, text)];
    }
}
