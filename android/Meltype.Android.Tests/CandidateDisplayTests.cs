// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
internal static class CandidateDisplayTests
{
    sealed class Model : IContextEvaluator
    {
        public TaskCompletionSource<string?>? Delayed;
        public Task<string?> EvaluateAsync(ContextRequest r,CancellationToken c)=>Delayed?.Task??Task.FromResult<string?>(r.Candidates.Last());
    }
    sealed class Plain : IKanjiConverter
    {
        public string? Convert(string r)=>r;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string r,string? context=null)=>[new(r,r)];
    }
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    static async Task RunAsync()
    {
        var dir=Path.Combine(Path.GetTempPath(),"meltype-display-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            await new PersonalTypoLearning(Path.Combine(dir,"zenz-personal-typos.json"),new Model(),()=>true).ObserveAsync("chinamoni","chinamini");
            var model=new Model{Delayed=new()};var output="";
            var input=new AndroidInputSession(new Plain(),_=>["通常候補甲","通常候補乙","通常候補丙"],(op,t,_)=>{if(op==1)output+=t;},
                ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true},stateDirectory:dir,contextEvaluator:model);
            foreach(var c in "chinamoni")input.Character(c);input.Character(',');
            var before=input.View!;var candidates=before.Candidates.ToArray();var clauses=before.Clauses!.ToArray();
            model.Delayed.SetResult("ちなみに");input.PollContextEvaluation(Environment.TickCount64);
            if(input.View?.Text!="ちなみに，" || !input.View.Candidates.SequenceEqual(candidates) ||
                !input.View.Clauses!.SequenceEqual(clauses) || input.View.SelectedIndex!=before.SelectedIndex)
                throw new Exception("Correction replaced normal candidate presentation");
            var expected=string.Concat(clauses.Take(before.SelectedClause))+candidates[0];input.Select(0);
            if(output!=expected)throw new Exception("Automatic correction overrode an explicit normal candidate choice");
            Console.WriteLine("PASS: zenz changes only composing text; candidate content/order, clause layout and explicit selection stay normal");
        }
        finally{Directory.Delete(dir,true);}
    }
}
