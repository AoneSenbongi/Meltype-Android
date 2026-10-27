// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class AndroidZenzTests
{
    private sealed class Model(bool delayed = false) : IContextEvaluator
    {
        public string? Forced { get; init; }
        public List<ContextRequest> Requests { get; } = [];
        public TaskCompletionSource<string?> Completion { get; } = new();
        public Task<string?> EvaluateAsync(ContextRequest request, CancellationToken cancellation)
        {
            Requests.Add(request);
            return delayed ? Completion.Task : Task.FromResult<string?>(Forced ?? request.Candidates.FirstOrDefault(c => c == "お願いします"));
        }
    }
    private sealed class Converter : IKanjiConverter
    {
        public string? Convert(string reading) => reading.Replace("おねがいし", "お願いし");
        public IReadOnlyList<ConversionClause>? ConvertClauses(string reading, string? context = null) => [new(reading, Convert(reading)!)];
    }
    private sealed class Editor(IContextEvaluator model, bool asShown = false, string? stateDirectory = null, bool learning = false)
    {
        public string Document = "";
        public AndroidInputSession Session = null!;
        public Editor Init()
        {
            Session = new(new Converter(), _ => [], (op, text, _) => { if (op == 1) Document += text; },
                () => new InputPreferences { LearningEnabled = learning, EnterCommitsAsShown = asShown },
                stateDirectory: stateDirectory, contextEvaluator: model);
            return this;
        }
        public void Type(string text) { foreach (var c in text) Session.Character(c); }
        public void Poll(int delay = 400) => Session.PollContextEvaluation(Environment.TickCount64 + delay);
    }
    private static void Equal(string? expected, string? actual)
    { if (expected != actual) throw new Exception($"zenz: expected {expected}, got {actual}"); }
    public static void Run()
    {
        var promptModel = new Model(); var promptEditor = new Editor(promptModel).Init();
        promptEditor.Type("onegaishimsu"); promptEditor.Poll(65);
        if(promptModel.Requests.Count != 0) throw new Exception("Android zenz reviewed an unfinished clause after an idle delay");
        promptEditor.Session.Character(','); promptEditor.Poll(0);
        Equal("お願いします，",promptEditor.Session.View?.Text);
        var model = new Model(); var editor = new Editor(model).Init();
        editor.Type("onegaishimsu"); editor.Session.Character(','); editor.Poll();
        Equal("お願いします，", editor.Session.View?.Text);
        editor.Session.Key(0x0D); Equal("お願いします，", editor.Document);
        if (!model.Requests.Any(r => r.RomanTypo && r.Candidates.Contains("お願いします"))) throw new Exception("No bounded typo request");

        foreach (var literal in new[] { "meeting", "github", "camera", "https://example.com", "ps5", "ABC" })
        {
            var protectedModel = new Model(); var protectedEditor = new Editor(protectedModel).Init();
            protectedEditor.Type(literal); var before = protectedEditor.Session.View?.Text; protectedEditor.Poll();
            Equal(before, protectedEditor.Session.View?.Text);
            if (protectedModel.Requests.Count != 0) throw new Exception("Protected input was evaluated: " + literal);
        }
        foreach (var (literal, expected) in new[]
        {
            ("n78", "n78"), ("N78", "N78"), ("a1", "a1"), ("m1234", "m1234"),
            ("a7f1s", "a7f1s"), ("a7f1sni", "a7f1sに"),
            ("konoa7f1swotsukau", "このa7f1sをつかう"),
            ("katabanhaa7f1sdesu", "かたばんはa7f1sです"),
            ("konoab12cd34wotsukau", "このab12cd34をつかう")
        })
        {
            var protectedModel = new Model { Forced = "お願いします" };
            var protectedEditor = new Editor(protectedModel).Init();
            protectedEditor.Type(literal); protectedEditor.Poll();
            Equal(expected, protectedEditor.Session.View?.Text);
            protectedEditor.Session.Key(0x0D); protectedEditor.Poll();
            Equal(expected, protectedEditor.Document);
            if (protectedModel.Requests.Any(r => r.ModelBoundary is null))
                throw new Exception("Model number was sent to unrestricted review: " + literal);
        }
        foreach (var edit in new[] { "reset", "back", "escape", "manual" })
        {
            var delayed = new Model(true); var stale = new Editor(delayed).Init();
            stale.Type("onegaishimsu"); stale.Session.Character(','); stale.Poll();
            if (edit == "reset") stale.Session.Reset();
            else { stale.Session.Key(edit == "back" ? 0x08 : edit == "escape" ? 0x1B : 0x79); if(edit=="back")stale.Session.Key(0x08); }
            var before = stale.Session.View?.Text;
            delayed.Completion.SetResult("お願いします"); stale.Poll(800);
            Equal(before, stale.Session.View?.Text); Equal("", stale.Document);
        }
        var waiting = new Model(true); var enter = new Editor(waiting).Init();
        enter.Type("onegaishimsu"); enter.Session.Key(0x0D); Equal("", enter.Document);
        waiting.Completion.SetResult("お願いします"); enter.Poll(); Equal("お願いします", enter.Document);
        enter.Poll(); Equal("お願いします", enter.Document);

        var timingOut = new Model(true); var timeout = new Editor(timingOut).Init();
        timeout.Type("onegaishimsu"); var baseline = timeout.Session.View!.Text;
        timeout.Session.Key(0x0D); timeout.Poll(2500); Equal(baseline, timeout.Document);
        timingOut.Completion.SetResult("お願いします"); timeout.Poll(3000); Equal(baseline, timeout.Document);

        var shown = new Editor(new Model(true), true).Init(); shown.Type("onegaishimsu");
        baseline = shown.Session.View!.Text; shown.Session.Key(0x0D); Equal(baseline, shown.Document);
        var space = new Editor(new Model()).Init(); space.Type("onegaishimsu"); space.Session.SpaceKey(false); space.Poll(); Equal("お願いします ", space.Document);

        var editing = new Editor(new Model()).Init(); editing.Type("onegaishimsu"); editing.Poll();
        var reference = new Editor(new Model()).Init(); reference.Type("onegaishimsu");
        reference.Session.Key(0x08); reference.Session.Key(0x79);
        editing.Session.Key(0x08); editing.Session.Key(0x79); Equal(reference.Session.View?.Text, editing.Session.View?.Text);
        var choosing = new Editor(new Model()).Init(); choosing.Type("onegaishimsu");
        var original = choosing.Session.View!.Text; choosing.Poll();
        choosing.Session.Select(choosing.Session.View!.Candidates.ToList().IndexOf(choosing.Session.View.Clauses!.Last())); Equal(original, choosing.Document);
        var invalid = new Editor(new Model { Forced = "候補にない文章" }).Init(); invalid.Type("onegaishimsu");
        original = invalid.Session.View!.Text; invalid.Poll(); Equal(original, invalid.Session.View?.Text);

        var continuingModel = new Model(true); var continuing = new Editor(continuingModel).Init();
        continuing.Type("onegaishimsu"); original = continuing.Session.View!.Text;
        continuing.Session.Key(0x0D); continuing.Type("k"); Equal(original, continuing.Document);
        var next = continuing.Session.View?.Text; continuingModel.Completion.SetResult("お願いします");
        continuing.Poll(800); Equal(next, continuing.Session.View?.Text); Equal(original, continuing.Document);

        var testDirectory = Path.Combine(AppContext.BaseDirectory, "zenz-test-state", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            var memoryPath = Path.Combine(testDirectory, "language-memory.json");
            var memory = new LanguageMemory(memoryPath); memory.Remember("onegaishimsu", true); memory.Remember("onegaishimsu", true);
            var saved = File.ReadAllText(memoryPath);
            var learned = new Editor(new Model(), stateDirectory: testDirectory, learning: true).Init();
            learned.Type("onegaishimsu"); learned.Session.Character(','); learned.Poll(); Equal("お願いします,", learned.Session.View?.Text);
            learned.Session.Key(0x0D); Equal("お願いします,", learned.Document); Equal(saved, File.ReadAllText(memoryPath));
            memory.Remember("onegaishimsu", true, true);
            var explicitModel = new Model(); var explicitEnglish = new Editor(explicitModel, stateDirectory: testDirectory, learning: true).Init();
            explicitEnglish.Type("onegaishimsu"); original = explicitEnglish.Session.View!.Text;
            explicitEnglish.Poll(); Equal(original, explicitEnglish.Session.View?.Text);
            if (explicitModel.Requests.Count != 0) throw new Exception("Explicit English memory was evaluated");
        }
        finally
        {
            var resolved = Path.GetFullPath(testDirectory);
            var testRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "zenz-test-state")) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected test cleanup path");
            Directory.Delete(resolved, recursive: true);
        }
        Console.WriteLine("PASS: Android zenz bounded correction, protected literals, stale results, Enter/Space, deadline and as-shown commit");
    }

    public static void RunNative(string model, string runtime)
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeZenzEvaluator).Assembly, (name, _, _) =>
            name == "meltype_zenz" ? NativeLibrary.Load(Path.GetFullPath(runtime)) : IntPtr.Zero);
        using var evaluator = new NativeZenzEvaluator(Path.GetFullPath(model));
        var elapsed = Stopwatch.StartNew();
        evaluator.WarmUpAsync().GetAwaiter().GetResult();
        Console.WriteLine($"PASS: asynchronous native model warmup ({elapsed.ElapsedMilliseconds}ms)");
        elapsed.Restart();
        var japanese = evaluator.EvaluateAsync(new("にほんご", "", "", ["日本語", "ニホンゴ"]), CancellationToken.None).GetAwaiter().GetResult();
        Equal("日本語", japanese);
        Console.WriteLine($"PASS: native managed Japanese generation ({elapsed.ElapsedMilliseconds}ms after warmup)");
        elapsed.Restart();
        var correction = evaluator.EvaluateAsync(new("おねがいします", "", "", ["onegaishimsu", "お願いします", "おねがいします"], RomanTypo: true), CancellationToken.None).GetAwaiter().GetResult();
        if (correction is not ("onegaishimsu" or "お願いします" or "おねがいします")) throw new Exception("Native candidate constraint failed");
        Console.WriteLine($"PASS: native bounded typo choice = {correction} ({elapsed.ElapsedMilliseconds}ms)");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Equal(null, evaluator.EvaluateAsync(new("にほんご", "", "", ["日本語", "ニホンゴ"]), cancelled.Token).GetAwaiter().GetResult());
    }
}
