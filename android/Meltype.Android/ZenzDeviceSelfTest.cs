// SPDX-License-Identifier: GPL-3.0-or-later
using Android.Content;
using Meltype.AndroidCore;
using Meltype.Config;
using System.Diagnostics;
using System.Text.Json;

namespace Meltype.Mobile;

// Fixed test inputs only; no access to the current editor, personal dictionary or history.
internal static class ZenzDeviceSelfTest
{
    private sealed class ObservedEvaluator(Meltype.Composition.IContextEvaluator inner) : Meltype.Composition.IContextEvaluator
    {
        public List<object> Requests { get; } = [];
        public List<string?> Results { get; } = [];
        public async Task<string?> EvaluateAsync(Meltype.Composition.ContextRequest request, CancellationToken cancellation)
        {
            var result = await inner.EvaluateAsync(request, cancellation);
            Requests.Add(new { request.Reading, request.Candidates, request.RomanTypo, result });
            Results.Add(result);
            return result;
        }
    }
    public static void Start(Context context) => _ = Task.Run(async () =>
    {
        var path = System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "zenz-selftest.json");
        var rows = new List<object>();
        try
        {
            using var evaluator = LocalZenz.Create(context);
            var clock = Stopwatch.StartNew();
            if (!await evaluator.WarmUpAsync())
                throw new InvalidOperationException("Native model preparation failed: " + string.Join("; ",
                    Meltype.Diagnostics.Log.Snapshot().Where(e => e.Message.StartsWith("端末内AI", StringComparison.Ordinal)).Select(e => e.Message)));
            var prepareMs = clock.ElapsedMilliseconds;
            var probe = await evaluator.EvaluateAsync(new Meltype.Composition.ContextRequest("にほんご", "", "", ["日本語", "ニホンゴ"]), CancellationToken.None);
            if (probe != "日本語") throw new InvalidOperationException("Managed native probe returned: " + (probe ?? "null"));
            var converter = MozcJniBridge.Create(context, learning: false);
            foreach (var (input, expected) in new[] { ("onegaishimsu", "お願いします"),
                ("sumimasne", "すみません"), ("arigatpu", "ありがとう"), ("github", "github"), ("camera", "camera") })
            {
                var committed = "";
                var observed = new ObservedEvaluator(evaluator);
                var session = new AndroidInputSession(converter, converter.Candidates,
                    (operation, text, _) => { if (operation == 1) committed += text; },
                    () => new InputPreferences { LearningEnabled = false, LocalZenz = true, EnterCommitsAsShown = false },
                    contextEvaluator: observed);
                foreach (var c in input) session.Character(c);
                var baseline = session.View?.Text;
                clock.Restart();
                session.Character('\n');
                while (session.ContextEvaluationPending && clock.ElapsedMilliseconds < 4000)
                {
                    session.PollContextEvaluation(System.Environment.TickCount64);
                    await Task.Delay(10);
                }
                rows.Add(new { input, baseline, committed, expected, elapsedMs = clock.ElapsedMilliseconds, observed.Requests });
                if (committed != expected || session.ContextEvaluationPending)
                    throw new InvalidOperationException("Fixed input did not commit its expected correction: " + input);
                session.PollContextEvaluation(System.Environment.TickCount64);
                if (committed != expected) throw new InvalidOperationException("Committed twice");
            }
            foreach (var input in new[] { "n78", "a1", "a7f1s", "ab12cd34", "konoa7f1swotsukau", "katabanhaa7f1sdesu" })
            {
                var committed = "";
                var session = new AndroidInputSession(converter, converter.Candidates,
                    (operation, text, _) => { if (operation == 1) committed += text; },
                    () => new InputPreferences { LearningEnabled = false, EnterCommitsAsShown = true });
                foreach (var c in input) session.Character(c);
                var baseline = session.View?.Text ?? "";
                var model = input.Contains("a7f1s") ? "a7f1s" : input;
                if (!baseline.Contains(model, StringComparison.Ordinal)) throw new InvalidOperationException("Model changed in preview: " + input + " / " + baseline);
                session.Key(0x0D);
                rows.Add(new { input, baseline, committed, kind = "model-number" });
                if (!committed.Contains(model, StringComparison.Ordinal)) throw new InvalidOperationException("Model changed on commit: " + input);
            }
            foreach (var (left, right, expected) in new[]
            {
                ("型番「", "」の製品を購入しました。", "a7f1ka"),
                ("製品の型番は", "です。", "a7f1ka"),
                ("使えるのは", "、b7f1ですか？", "a7f1か")
            })
            {
                var committed = "";
                var observed = new ObservedEvaluator(evaluator);
                var session = new AndroidInputSession(converter, converter.Candidates,
                    (operation, text, _) => { if (operation == 1) committed += text; },
                    () => new InputPreferences { LearningEnabled = false, LocalZenz = true, EnterCommitsAsShown = false },
                    surrounding: () => (left, right), contextEvaluator: observed);
                foreach (var c in "a7f1ka") session.Character(c);
                var baseline = session.View?.Text ?? "";
                clock.Restart();
                while (session.ContextEvaluationPending && clock.ElapsedMilliseconds < 4000)
                { session.PollContextEvaluation(System.Environment.TickCount64); await Task.Delay(10); }
                var candidates = session.View?.Candidates ?? [];
                rows.Add(new { input = "a7f1ka", left, right, baseline, expected,
                    shown = session.View?.Text, candidates, elapsedMs = clock.ElapsedMilliseconds, observed.Requests, kind = "model-boundary" });
                if (session.View?.Text != baseline) throw new InvalidOperationException("Boundary suggestion replaced text automatically");
                if (observed.Results.Count != 1 || observed.Results[0] != expected)
                    throw new InvalidOperationException("Native boundary evaluation differs: " + string.Join(",", observed.Results));
                if (expected != baseline)
                {
                    var index = candidates.ToList().IndexOf(expected);
                    if (index < 0) throw new InvalidOperationException("Boundary suggestion missing: " + baseline);
                    session.Select(index);
                }
                else session.Key(0x0D);
                if (committed != expected) throw new InvalidOperationException("Boundary choice failed to commit: " + committed);
            }
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = "PASS", prepareMs,
                processPssKb = global::Android.OS.Debug.Pss, rows }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = "FAIL", error = error.ToString(), rows },
                new JsonSerializerOptions { WriteIndented = true }));
        }
    });
}
