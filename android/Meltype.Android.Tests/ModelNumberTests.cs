// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;

internal static class ModelNumberTests
{
    private sealed class Plain : IKanjiConverter
    {
        public string? Convert(string text) => text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text, string? context = null) => [new(text, text)];
    }

    public static void Run()
    {
        foreach (var (raw, expected) in new[]
        {
            ("n78", "n78"), ("N78", "N78"), ("n7", "n7"), ("m1234", "m1234"),
            ("a1", "a1"), ("a1ni", "a1に"), ("i2de", "i2で"),
            ("u3no", "u3の"), ("e4ni", "e4に"), ("o5de", "o5で"),
            ("n78ni", "n78に"), ("n78no", "n78の"),
            ("n78de", "n78で"), ("n78nitaiou", "n78にたいおう"),
            ("nihon78", "にほん78"), ("pan78", "ぱん78"), ("nn78", "ん78"),
            ("i18n", "i18n"), ("l10n", "l10n"), ("h2o", "h2o")
        }.Concat("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => ($"{c}78", $"{c}78"))))
        {
            var committed = new List<string>();
            var session = new AndroidInputSession(new Plain(), _ => [],
                (op, text, _) => { if (op == 1) committed.Add(text); }, () => new InputPreferences { FullWidthParentheses = false }, learningAllowed: false);
            foreach (var c in raw) session.Character(c);
            if (session.View?.Text != expected)
                throw new Exception($"Model preview {raw}: expected '{expected}', got '{session.View?.Text}'");
            session.Commit();
            if (string.Concat(committed) != expected)
                throw new Exception($"Model confirmation {raw}: expected '{expected}', got '{string.Concat(committed)}'");
            foreach (var scored in new[] { false, true })
            {
                var detector = CompositionDetector.CreateDefault();
                detector.UseScoredSegmentation = () => scored;
                var composition = new CompositionText(detector);
                foreach (var c in raw) composition.Append(c);
                foreach (var final in new[] { false, true })
                    if (composition.Display(final) != expected)
                        throw new Exception($"Model segmentation {raw}, scored={scored}, final={final}: '{composition.Display(final)}'");
            }
        }
        var editing = new AndroidInputSession(new Plain(), _ => [], (_, _, _) => { }, learningAllowed: false);
        foreach (var c in "n78") editing.Character(c);
        editing.Key(0x08);
        if (editing.View?.Text != "n7") throw new Exception("Deleting model digit lost literal prefix");
        editing.Key(0x08);
        if (editing.View?.Text != "ん") throw new Exception("Deleting model digits did not restore Japanese n");
        editing.Character('7'); editing.Character('8');
        if (editing.View?.Text != "n78") throw new Exception("Retyping model digits lost original n");
        foreach (var mode in new[] { DisplayMode.Auto, DisplayMode.Hiragana })
        {
            var manual = new CompositionText(CompositionDetector.CreateDefault()) { Level = () => DetectionLevel.Manual, Mode = mode };
            foreach (var c in "n78") manual.Append(c);
            if (manual.Display(true) != "ん78") throw new Exception("Model detection overrode manual Japanese input");
        }
        Console.WriteLine("PASS: model numbers stay literal in Android preview and confirmation; Japanese readings and numeronyms preserved");
        RunEmbedded();
    }

    private static void RunEmbedded()
    {
        var failures = new List<string>();
        foreach (var (raw, expected) in new[]
        {
            ("a7f1s", "a7f1s"), ("A7F1S", "A7F1S"), ("a7f1s2", "a7f1s2"),
            ("a7f1sni", "a7f1sに"), ("a7f1sdesu", "a7f1sです"),
            ("a7f1swotsukau", "a7f1sをつかう"),
            ("konoa7f1swotsukau", "このa7f1sをつかう"),
            ("katabanhaa7f1sdesu", "かたばんはa7f1sです"),
            ("konoa7f1sdesu", "このa7f1sです"),
            ("a7f1swoa7f2snikoukansuru", "a7f1sをa7f2sにこうかんする"),
            ("(a7f1s)wotsukau", "(a7f1s)をつかう"),
            ("ab12cd34", "ab12cd34"), ("ab12cd34desu", "ab12cd34です"),
            ("konoab12cd34wotsukau", "このab12cd34をつかう"),
            ("kaigiha3ji", "かいぎは3じ"), ("nihon78", "にほん78"),
            ("pan78", "ぱん78"), ("n78ni", "n78に")
        }.Concat("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c =>
            ($"kono{c}7f1swotsukau", $"この{c}7f1sをつかう"))))
        {
            void Check(string stage, string? actual)
            {
                if (actual != expected) failures.Add($"{raw} {stage}: expected '{expected}', got '{actual}'");
            }
            var committed = new List<string>();
            var session = new AndroidInputSession(new Plain(), _ => [],
                (op, text, _) => { if (op == 1) committed.Add(text); }, () => new InputPreferences { FullWidthParentheses = false }, learningAllowed: false);
            foreach (var c in raw) session.Character(c);
            Check("preview", session.View?.Text);
            session.Commit(); Check("commit", string.Concat(committed));
            foreach (var scored in new[] { false, true })
            {
                var detector = CompositionDetector.CreateDefault();
                detector.UseScoredSegmentation = () => scored;
                var composition = new CompositionText(detector);
                foreach (var c in raw) composition.Append(c);
                Check($"scored={scored} live", composition.Display(false));
                Check($"scored={scored} final", composition.Display(true));
            }
        }
        if (failures.Count > 0) throw new Exception("Embedded model numbers:\n" + string.Join("\n", failures));
        Console.WriteLine("PASS: alternating model numbers inside Japanese sentences, repeated codes and ordinary Japanese numbers");
    }
}
