// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
using System.Net.Http.Json;
using System.Text.Json;

internal static class ModelBoundaryTests
{
    private sealed class Plain : IKanjiConverter
    {
        public string? Convert(string text) => text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text, string? context = null) => [new(text, text)];
    }

    private sealed class Choice(bool delayed = false) : IContextEvaluator
    {
        public bool Fail { get; init; }
        public bool Original { get; init; }
        public List<ContextRequest> Requests { get; } = [];
        public TaskCompletionSource<string?> Completion { get; } = new();
        public Task<string?> EvaluateAsync(ContextRequest request, CancellationToken cancellation)
        {
            Requests.Add(request);
            if (Fail) throw new IOException("Fixed test failure");
            return delayed ? Completion.Task : Task.FromResult<string?>(Original ? request.Candidates.First() : request.Candidates.Last());
        }
    }

    private sealed class Editor(IContextEvaluator? model, string left = "型番「", string right = "」です。", bool asShown = false)
    {
        public string Document = "";
        public AndroidInputSession Session = null!;
        public void Init() => Session = new(new Plain(), _ => [],
            (op, text, _) => { if (op == 1) Document += text; },
            () => new InputPreferences { LearningEnabled = false, EnterCommitsAsShown = asShown },
            surrounding: () => (left, right), contextEvaluator: model);
        public void Type(string raw) { foreach (var c in raw) Session.Character(c); }
        public void Poll() => Session.PollContextEvaluation(Environment.TickCount64 + 400);
    }

    private static void Equal(string expected, string? actual)
    { if (expected != actual) throw new Exception($"Model boundary expected '{expected}', got '{actual}'"); }

    public static void Run()
    {
        var model = new Choice(); var editor = new Editor(model); editor.Init();
        editor.Type("a7f1ka"); editor.Poll();
        Equal("a7f1か", editor.Session.View?.Text);
        if (model.Requests.Count != 1) throw new Exception("Ambiguous model suffix was not evaluated");
        var request = model.Requests[0];
        if (request.Reading != "" || !request.Left.Contains("型番") || !request.Right.Contains("です"))
            throw new Exception("Boundary evaluation lacks neutral reading or surrounding context");
        foreach (var candidate in request.Candidates)
            if (!ZenzCandidateRules.AllowsCandidate(request, candidate)) throw new Exception("Legal boundary candidate rejected");
        foreach (var candidate in new[] { "a7f2ka", "a7f1kaです", "お願いします", "a7f1", "a7f1に" })
            if (ZenzCandidateRules.AllowsCandidate(request, candidate)) throw new Exception("Boundary changed protected text");
        if (ZenzCandidateRules.AllowsCandidate(request with { Candidates = ["a7f1か", "a7f2ka"] }, "a7f2ka") ||
            ZenzCandidateRules.AllowsCandidate(request with { Reading = "カ" }, "a7f1ka") ||
            ZenzCandidateRules.AllowsCandidate(request with { RomanTypo = true }, "a7f1ka"))
            throw new Exception("Malformed boundary request accepted");
        if (!editor.Session.View!.Candidates.Contains("a7f1ka")) throw new Exception("No model boundary suggestion");
        editor.Session.Select(editor.Session.View.Candidates.ToList().IndexOf("a7f1ka"));
        Equal("a7f1ka", editor.Document);

        foreach (var asShown in new[] { false, true })
        {
            var unchanged = new Editor(new Choice(), asShown: asShown); unchanged.Init();
            unchanged.Type("a7f1ka"); unchanged.Poll(); unchanged.Session.Key(0x0D);
            Equal("a7f1か", unchanged.Document);
            var pending = new Editor(new Choice(true), asShown: asShown); pending.Init();
            pending.Type("a7f1ka"); pending.Session.Key(0x0D);
            Equal("a7f1か", pending.Document);
        }
        foreach (var edit in new[] { "back", "reset", "append", "manual", "space" })
        {
            var delayed = new Choice(true); var stale = new Editor(delayed); stale.Init();
            stale.Type("a7f1ka"); stale.Poll();
            if (edit == "back") stale.Session.Key(0x08);
            else if (edit == "reset") stale.Session.Reset();
            else if (edit == "append") stale.Type("desu");
            else stale.Session.Key(edit == "space" ? 0x20 : 0x76);
            var before = stale.Session.View?.Text;
            delayed.Completion.SetResult("a7f1ka"); stale.Poll();
            if (before != stale.Session.View?.Text) throw new Exception("Stale boundary result changed edited input");
        }
        var embedded = new Editor(new Choice()); embedded.Init(); embedded.Type("konoa7f1kadesu"); embedded.Poll();
        Equal("このa7f1かです", embedded.Session.View?.Text);
        if (!embedded.Session.View!.Candidates.Contains("このa7f1kaです")) throw new Exception("Missing embedded boundary suggestion");
        var disabled = new Editor(null); disabled.Init(); disabled.Type("a7f1ka"); disabled.Poll();
        Equal("a7f1か", disabled.Session.View?.Text);
        foreach (var evaluation in new[] { new Choice { Fail = true }, new Choice { Original = true } })
        {
            var unchanged = new Editor(evaluation); unchanged.Init(); unchanged.Type("a7f1ka"); unchanged.Poll();
            Equal("a7f1か", unchanged.Session.View?.Text);
            unchanged.Session.Key(0x0D); Equal("a7f1か", unchanged.Document);
        }
        var deadlineModel = new Choice(true); var deadline = new Editor(deadlineModel); deadline.Init();
        deadline.Type("a7f1ka"); deadline.Poll();
        deadline.Session.PollContextEvaluation(Environment.TickCount64 + 3000);
        deadlineModel.Completion.SetResult("a7f1ka"); deadline.Poll();
        Equal("a7f1か", deadline.Session.View?.Text);
        deadline.Session.Key(0x0D); Equal("a7f1か", deadline.Document);
        var englishModel = new Choice(); var english = new Editor(englishModel); english.Init();
        english.Session.SetEnglish(true); english.Type("a7f1ka"); english.Poll();
        Equal("a7f1ka", english.Document);
        if (englishModel.Requests.Count != 0) throw new Exception("Explicit English was evaluated");
        foreach (var raw in new[] { "n78", "a1", "a7f1s", "https://a7f1ka.example", "a7f1katob7f1ka" })
        {
            var protectedModel = new Choice(); var literal = new Editor(protectedModel); literal.Init();
            literal.Type(raw); literal.Poll();
            if (protectedModel.Requests.Count != 0) throw new Exception("Unambiguous/protected/multiple target was evaluated: " + raw);
        }
        Console.WriteLine("PASS: zenz model boundary suggestions preserve baseline, explicit selection, Enter, edits and disabled mode");
    }

    private sealed class LocalModel(HttpClient client) : IContextEvaluator
    {
        public List<string> Results { get; } = [];
        public async Task<string?> EvaluateAsync(ContextRequest request, CancellationToken cancellation)
        {
            if (request.Candidates.Any(c => !ZenzCandidateRules.AllowsCandidate(request, c))) return null;
            using var response = await client.PostAsJsonAsync("completion", new
            {
                prompt = ZenzCandidateRules.Prompt(request), grammar = ZenzCandidateRules.Grammar(request),
                n_predict = 128, temperature = 0, seed = 1, cache_prompt = false
            }, cancellation);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellation);
            var result = ZenzCandidateRules.LiteralPrefix(request) + json.GetProperty("content").GetString();
            Results.Add(result);
            return result;
        }
    }

    public static void RunLocalModel(string endpoint)
    {
        var uri = new Uri(endpoint);
        if (!uri.IsLoopback || uri.Scheme != "http") throw new Exception("Use a local fixed-input test server");
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
            { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(5) };
        // HTTP/JIT startup is a property of this desktop test bridge, not the Android native adapter.
        var warm = new LocalModel(client);
        var warmRequest = new ContextRequest("", "型番「", "」です。", ["a7f1か", "a7f1ka"],
            ModelBoundary: new("a7f1", "ka", "か"));
        warm.EvaluateAsync(warmRequest, CancellationToken.None).GetAwaiter().GetResult();
        foreach (var (left, right) in new[]
        {
            ("型番「", "」の製品を購入しました。"),
            ("製品の型番は", "です。"),
            ("型番一覧にはa7f1kaと登録されています。注文する型番は", "です。"),
            ("使えるのは", "、b7f1ですか？"),
            ("どちらを使いますか、", "、b7f1か。"),
            ("対応している型番は", "どうか確認してください。")
        })
        {
            var model = new LocalModel(client); var editor = new Editor(model, left, right); editor.Init();
            editor.Type("a7f1ka");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            editor.Poll();
            while (editor.Session.ContextEvaluationPending && timer.ElapsedMilliseconds < 4000)
            { Thread.Sleep(10); editor.Poll(); }
            Equal("a7f1か", editor.Session.View?.Text);
            if (model.Results.Count != 1) throw new Exception("Local model did not return a boundary choice");
            var chosen = model.Results[0];
            if (chosen == "a7f1ka")
            {
                var index = editor.Session.View!.Candidates.ToList().IndexOf(chosen);
                if (index < 0) throw new Exception("Real-model suggestion did not reach candidate UI");
                editor.Session.Select(index); Equal(chosen, editor.Document);
            }
            else
            {
                Equal("a7f1か", chosen);
                editor.Session.Key(0x0D); Equal("a7f1か", editor.Document);
            }
            Console.WriteLine(JsonSerializer.Serialize(new { left, right, chosen, timer.ElapsedMilliseconds, automaticReplacement = false }));
        }
        Console.WriteLine("PASS: real local zenz model to Android session boundary suggestions and explicit commit (6 contexts)");
    }
}
