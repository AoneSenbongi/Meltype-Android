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
            Requests.Add(new { request.Reading, request.Candidates, request.RomanTypo, request.KanaConversion, result });
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
            var learningDirectory = Path.Combine(context.CacheDir!.AbsolutePath, "zenz-learning-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(learningDirectory);
            AndroidInputSession? learningSession = null;
            try
            {
                var learnedCommit = "";
                learningSession = new AndroidInputSession(converter, converter.Candidates,
                    (operation,text,_) => { if (operation == 1) learnedCommit += text; },
                    () => new InputPreferences { LocalZenz = true, LearningEnabled = true },
                    stateDirectory: learningDirectory, contextEvaluator: evaluator);
                foreach (var c in "chinamoni") learningSession.Character(c);
                learningSession.Key(0x08); learningSession.Key(0x08);
                foreach (var c in "mini") learningSession.Character(c);
                learningSession.Key(0x0D);
                await learningSession.PendingTypoLearning;
                var learned = new PersonalTypoLearning(Path.Combine(learningDirectory, "zenz-personal-typos.json"), evaluator, () => true);
                if (learned.Find("chinamoni")?.Kana != "ちなみに")
                    throw new InvalidOperationException("Native zenz did not approve the first personal edit");
                learnedCommit = "";
                foreach (var c in "chinamoni") learningSession.Character(c);
                learningSession.Character(',');
                var normalCandidates=learningSession.View!.Candidates.ToArray();
                var normalClauses=learningSession.View.Clauses!.ToArray();
                clock.Restart();
                while (clock.ElapsedMilliseconds < 4000 && learningSession.ContextEvaluationPending)
                {
                    learningSession.PollContextEvaluation(System.Environment.TickCount64);
                    await Task.Delay(20);
                }
                if (learningSession.View?.Text is not ("ちなみに，" or "因みに，")) throw new InvalidOperationException("Native personal correction did not appear: " + learningSession.View?.Text);
                if(!learningSession.View!.Candidates.SequenceEqual(normalCandidates) || !learningSession.View.Clauses!.SequenceEqual(normalClauses))
                    throw new InvalidOperationException("Native correction changed normal candidate presentation");
                var correctedShown=learningSession.View!.Text;
                learningSession.Key(0x0D); await learningSession.PendingTypoLearning;
                if (learnedCommit != correctedShown) throw new InvalidOperationException("Native personal correction changed at commit");
                rows.Add(new { input = "chinamoni", committed = learnedCommit, kind = "personal-typo-learning", acceptedEdits = 1 });
                learnedCommit = "";
                foreach(var c in "chinamonikyouha") learningSession.Character(c);
                clock.Restart();
                while(learningSession.ContextEvaluationPending && clock.ElapsedMilliseconds<4000)
                { learningSession.PollContextEvaluation(System.Environment.TickCount64); await Task.Delay(16); }
                if(learningSession.View?.Text is not ("ちなみに今日は" or "因みに今日は")) throw new InvalidOperationException("Native clause transition did not correct the completed prefix: "+learningSession.View?.Text);
                rows.Add(new { input="chinamonikyouha", shown=learningSession.View.Text, kind="automatic-clause-transition", elapsedMs=clock.ElapsedMilliseconds });
                var correctedSentence=learningSession.View!.Text;
                learningSession.Key(0x0D); await learningSession.PendingTypoLearning;
                if(learnedCommit!=correctedSentence) throw new InvalidOperationException("Native clause prefix changed at commit");
            }
            finally
            {
                learningSession?.Reset();
                foreach (var file in Directory.GetFiles(learningDirectory)) File.Delete(file);
                Directory.Delete(learningDirectory);
            }
            foreach(var (left,expected) in new[]{("川の向こうへ行くために","橋，"),("ごはんを食べるための","箸，")})
            {
                var observed=new ObservedEvaluator(evaluator);
                var session=new AndroidInputSession(converter,converter.Candidates,(_,_,_)=>{},
                    ()=>new InputPreferences{LocalZenz=true,LearningEnabled=false},surrounding:()=>(left,""),contextEvaluator:observed);
                foreach(var c in "hashi")session.Character(c);
                session.Character(',');var candidates=session.View!.Candidates.ToArray();
                clock.Restart();
                while(session.ContextEvaluationPending && clock.ElapsedMilliseconds<4000)
                { session.PollContextEvaluation(System.Environment.TickCount64);await Task.Delay(16); }
                rows.Add(new{input="hashi",left,expected,shown=session.View?.Text,kind="neural-kana-context",elapsedMs=clock.ElapsedMilliseconds,observed.Requests});
                if(session.View?.Text!=expected || observed.Results.Count!=1 || observed.Results[0]!=expected[..^1])
                    throw new InvalidOperationException("Native contextual kana conversion differed: "+session.View?.Text);
                if(!session.View.Candidates.SequenceEqual(candidates))throw new InvalidOperationException("Native kana conversion changed candidate UI");
                session.Reset();
            }
            foreach (var (input, expected) in new[] { ("onegaishimsu", "お願いします"), ("sumimasne", "すみません"), ("arigatpu", "ありがとう") })
            {
                var observed=new ObservedEvaluator(evaluator);
                var session = new AndroidInputSession(converter, converter.Candidates, (_,_,_) => { },
                    () => new InputPreferences { LearningEnabled = false, LocalZenz = true }, contextEvaluator: observed);
                foreach (var c in input) session.Character(c);
                var original=session.View?.Text;
                clock.Restart();
                session.Character(',');
                while (session.ContextEvaluationPending && clock.ElapsedMilliseconds < 4000)
                {
                    session.PollContextEvaluation(System.Environment.TickCount64);
                    await Task.Delay(16);
                }
                rows.Add(new { input, expected, shown = session.View?.Text, elapsedMs = clock.ElapsedMilliseconds, kind = "clause-boundary-latency", original, observed.Requests });
                if (session.View?.Text != expected+"，") throw new InvalidOperationException("Live latency sample did not correct: " + input);
                session.Reset();
            }
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
            foreach(var (left,right) in new[]{("型番「","」の製品を購入しました。"),("製品の型番は","です。"),("使えるのは","、b7f1ですか？")})
            {
                var committed="";var observed=new ObservedEvaluator(evaluator);
                var session=new AndroidInputSession(converter,converter.Candidates,(op,text,_)=>{if(op==1)committed+=text;},
                    ()=>new InputPreferences{LearningEnabled=false,LocalZenz=true,EnterCommitsAsShown=false},
                    surrounding:()=>(left,right),contextEvaluator:observed);
                foreach(var c in "a7f1ka")session.Character(c);
                var before=session.View!;var candidates=before.Candidates.ToArray();
                session.PollContextEvaluation(System.Environment.TickCount64+500);
                if(observed.Requests.Count!=0 || session.View?.Text!=before.Text || !session.View.Candidates.SequenceEqual(candidates))
                    throw new InvalidOperationException("Android injected zenz model-boundary candidates");
                session.Key(0x0D);
                if(committed!=before.Text)throw new InvalidOperationException("Model changed at commit");
                rows.Add(new{input="a7f1ka",left,right,committed,kind="normal-candidates-preserved"});
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
