// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;

static class TypoTrainingTests
{
    public static void Run()
    {
        void Check(bool value) { if (!value) throw new Exception("Typo training regression failed"); }
        var edits = new List<(string Before, string After)>();
        var capture = new TypoEditCapture((a, b) => edits.Add((a, b)));
        capture.BeforeDelete("helo"); capture.Typed(); capture.Finish("hello");
        Check(edits.Count == 1 && edits[0] == ("helo", "hello"));
        capture.BeforeDelete("delete only"); capture.Finish("delete onl");
        capture.BeforeDelete("same"); capture.Typed(); capture.Finish("same");
        capture.BeforeDelete("private"); capture.Typed(); capture.Cancel(); capture.Finish("other field");
        Check(edits.Count == 1);
        capture.BeforeDelete("we are"); capture.BeforeDelete("we ar"); capture.Typed(); capture.Finish("they are");
        Check(edits[^1] == ("we are", "they are")); // Rewording remains reviewable.
        capture.BeforeDelete(new string('a', 80)); capture.Typed(); capture.Finish(new string('b', 80));
        Check(edits[^1].Before.Length == 64 && edits[^1].After.Length == 64);
        foreach (var input in new[] { 0, 2, 3, 4, 0x81, 0x91, 0xe1, 0x21, 0x11, 0xd1 })
            Check(!TypoTrainingPolicy.Allows(input, 0));
        Check(TypoTrainingPolicy.Allows(1, 0));
        Check(!TypoTrainingPolicy.Allows(1, 0x1000000));
        var folder = Path.Combine(Path.GetTempPath(), "meltype-training-" + Guid.NewGuid());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var store = new TypoTrainingStore(folder, () => now);
            Check(!store.Active); store.Append("a", "b"); Check(store.Count == 0);
            store.Start(); store.Append("a", "b"); Check(store.Count == 1);
            store.Stop(); store.Append("b", "c"); Check(store.Count == 1);
            store.Start(); now = now.AddDays(8); Check(!store.Active);
            store.Append("c", "d"); Check(store.Count == 1);
            store.Clear(); Check(store.Count == 0 && !store.Active);
            store.Start();
            File.WriteAllText(Path.Combine(folder, "events.jsonl"), string.Concat(Enumerable.Repeat("{}\n", 5000)));
            store.Append("limit", "limited"); Check(store.Count == 5000);
            var unicode = TypoEditCapture.Bound("🙂" + new string('x', 63));
            Check(unicode.Length <= 64 && !char.IsLowSurrogate(unicode[0]));
        }
        finally { foreach (var file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
        Console.WriteLine("PASS: editing pairs, deletion-only, cancellation, bounds, private fields, opt-in, stop, expiry, erase");
    }
}
