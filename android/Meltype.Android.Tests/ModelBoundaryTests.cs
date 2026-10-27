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

    private sealed class Choice : IContextEvaluator
    {
        public List<ContextRequest> Requests { get; } = [];
        public Task<string?> EvaluateAsync(ContextRequest request, CancellationToken cancellation)
        {
            Requests.Add(request);
            return Task.FromResult<string?>(request.Candidates.Last());
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
        var request=new ContextRequest("","型番「","」です。",["a7f1か","a7f1ka"],ModelBoundary:new("a7f1","ka","か"));
        foreach(var candidate in request.Candidates)
            if(!ZenzCandidateRules.AllowsCandidate(request,candidate))throw new Exception("Legal boundary candidate rejected");
        foreach(var candidate in new[]{"a7f2ka","a7f1kaです","お願いします","a7f1","a7f1に"})
            if(ZenzCandidateRules.AllowsCandidate(request,candidate))throw new Exception("Boundary changed protected text");
        if(ZenzCandidateRules.AllowsCandidate(request with{Reading="カ"},"a7f1ka") ||
            ZenzCandidateRules.AllowsCandidate(request with{RomanTypo=true},"a7f1ka"))throw new Exception("Malformed boundary accepted");
        foreach(var raw in new[]{"a7f1ka","konoa7f1kadesu","a7f1s","a1","n78","a7f1katob7f1ka"})
        foreach(var shown in new[]{false,true})
        {
            var model=new Choice();var editor=new Editor(model,asShown:shown);editor.Init();editor.Type(raw);
            var before=editor.Session.View!;var candidates=before.Candidates.ToArray();var clauses=before.Clauses?.ToArray();
            editor.Poll();
            if(model.Requests.Count!=0 || !editor.Session.View!.Candidates.SequenceEqual(candidates) ||
                !editor.Session.View.Clauses!.SequenceEqual(clauses!))throw new Exception("Android injected model-boundary candidates");
            Equal(before.Text,editor.Session.View.Text);
            editor.Session.Key(0x0D);Equal(before.Text,editor.Document);
        }
        var english=new Editor(new Choice());english.Init();english.Session.SetEnglish(true);english.Type("a7f1ka");Equal("a7f1ka",english.Document);
        Console.WriteLine("PASS: model boundary rules protect literals; Android retains normal candidates and commit without injected suggestions");
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
        var uri=new Uri(endpoint);
        if(!uri.IsLoopback || uri.Scheme!="http")throw new Exception("Use a local fixed-input test server");
        using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=uri,Timeout=TimeSpan.FromSeconds(5)};
        foreach(var left in new[]{"型番「","製品の型番は","使えるのは"})
        {
            var request=new ContextRequest("",left,"です。",["a7f1か","a7f1ka"],ModelBoundary:new("a7f1","ka","か"));
            var result=new LocalModel(client).EvaluateAsync(request,CancellationToken.None).GetAwaiter().GetResult();
            if(result is null || !request.Candidates.Contains(result))throw new Exception("Invalid bounded model result");
            Console.WriteLine(JsonSerializer.Serialize(new{left,result,androidCandidateInjection=false}));
        }
    }
}
