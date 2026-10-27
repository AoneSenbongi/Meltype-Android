// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
internal static class ClauseReviewTests
{
    sealed class Model : IContextEvaluator
    {
        public int Calls;
        public bool Reject;
        public TaskCompletionSource<string?>? Delayed;
        public Task<string?> EvaluateAsync(ContextRequest r,CancellationToken c)
        { Calls++; return Delayed?.Task ?? Task.FromResult<string?>(Reject?r.Candidates.First():r.Candidates.Last()); }
    }
    sealed class Converter : IKanjiConverter
    {
        public string? Convert(string r)=>r;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string r,string? context=null)=>
            r.StartsWith("ちなもに") && r.Length>4 ? [new("ちなもに","ちなもに"),new(r[4..],r[4..])] :
            r.StartsWith("もに") && r.Length>2 ? [new("もに","もに"),new(r[2..],r[2..])] : [new(r,r)];
    }
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    static async Task RunAsync()
    {
        var dir=Path.Combine(Path.GetTempPath(),"meltype-clause-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await new PersonalTypoLearning(Path.Combine(dir,"zenz-personal-typos.json"),new Model(),()=>true).ObserveAsync("chinamoni","chinamini");
            foreach(var boundary in new[]{"next","punctuation","enter","space"})
            {
                var model=new Model();var committed="";
                var input=new AndroidInputSession(new Converter(),_=>[],(op,text,_)=>{if(op==1)committed+=text;},
                    ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true,EnterCommitsAsShown=false},stateDirectory:dir,contextEvaluator:model);
                foreach(var c in "chinamoni")input.Character(c);
                input.PollContextEvaluation(Environment.TickCount64+10000);
                if(model.Calls!=0)throw new Exception("Idle time evaluated an unfinished clause: calls="+model.Calls+" text="+input.View?.Text);
                if(boundary=="next")foreach(var c in "desu")input.Character(c);
                else if(boundary=="punctuation")input.Character(',');
                else input.Key(boundary=="enter"?0x0D:0x20);
                input.PollContextEvaluation(Environment.TickCount64);
                var expected=boundary=="next"?"ちなみにです":boundary=="punctuation"?"ちなみに，":"ちなみに"+(boundary=="space"?" ":"");
                if(model.Calls!=2 || (boundary is "enter" or "space"?committed:input.View?.Text)!=expected)
                    throw new Exception("Clause boundary did not review immediately: "+boundary+" calls="+model.Calls+" shown="+input.View?.Text+" committed="+committed);
                if(boundary=="next")
                {
                    if(input.View?.Clauses is not {Count:>1} cs || input.View.SelectedClause!=cs.Count-1)throw new Exception("Prefix review lost the active last clause");
                    input.Select(input.View.Candidates.ToList().IndexOf(cs.Last()));
                    if(committed!=expected)throw new Exception("Candidate selection lost the corrected prefix or suffix: "+committed);
                }
            }
            foreach(var change in new[]{"append","delete"})
            {
                var model=new Model{Delayed=new()};
                var input=new AndroidInputSession(new Converter(),_=>[],(_,_,_)=>{},
                    ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true},stateDirectory:dir,contextEvaluator:model);
                foreach(var c in "chinamonidesu")input.Character(c);
                input.PollContextEvaluation(Environment.TickCount64);
                if(model.Calls!=1)throw new Exception("Completed clause did not start an evaluation");
                if(change=="append")foreach(var c in "yo")input.Character(c);
                else for(var i=0;i<7;i++)input.Key(0x08);
                var before=input.View?.Text;
                model.Delayed.SetResult("ちなみに");input.PollContextEvaluation(Environment.TickCount64+100);
                if(input.View?.Text!=(change=="append"?"ちなみにですよ":before))throw new Exception("Completed clause result lost suffix or survived target edit");
            }
            foreach(var boundary in new[]{"next","punctuation"})
            {
                var model=new Model{Delayed=new()};var output="";
                var input=new AndroidInputSession(new Converter(),_=>["通常候補甲","通常候補乙","通常候補丙"],(op,text,_)=>{if(op==1)output+=text;},
                    ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true},stateDirectory:dir,contextEvaluator:model);
                foreach(var c in boundary=="next"?"chinamonidesu":"chinamoni")input.Character(c);
                if(boundary=="punctuation")input.Character(',');
                input.PollContextEvaluation(Environment.TickCount64);
                var candidates=input.View!.Candidates.ToArray();var clauses=input.View.Clauses!.ToArray();
                var selected=input.View.SelectedClause;var selectedIndex=input.View.SelectedIndex;
                model.Delayed.SetResult("ちなみに");input.PollContextEvaluation(Environment.TickCount64+100);
                if(!input.View!.Candidates.SequenceEqual(candidates) || !input.View.Clauses!.SequenceEqual(clauses) ||
                    input.View.SelectedClause!=selected || input.View.SelectedIndex!=selectedIndex)
                    throw new Exception("zenz changed the normal candidate list/order or clause selection: "+boundary);
                if(input.View.Text!=(boundary=="next"?"ちなみにです":"ちなみに，"))throw new Exception("Display-only correction did not update composing text");
            }
            var rejectModel=new Model{Reject=true};
            var rejected=new AndroidInputSession(new Converter(),_=>[],(_,_,_)=>{},
                ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true},stateDirectory:dir,contextEvaluator:rejectModel);
            foreach(var c in "chinamonidesu")rejected.Character(c);
            var originalClauses=rejected.View!.Clauses!.ToArray();
            var originalText=rejected.View.Text;
            rejected.PollContextEvaluation(Environment.TickCount64);
            if(rejected.View?.Text!=originalText || !rejected.View.Clauses!.SequenceEqual(originalClauses))
                throw new Exception("Rejected correction changed clause boundaries");
            var memory=new LanguageMemory(Path.Combine(dir,"language-memory.json"));
            memory.Remember("chinamoni",true,true);
            var protectedModel=new Model();
            var literal=new AndroidInputSession(new Converter(),_=>[],(_,_,_)=>{},
                ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true},stateDirectory:dir,contextEvaluator:protectedModel);
            foreach(var c in "chinamonidesu")literal.Character(c);
            literal.PollContextEvaluation(Environment.TickCount64+10000);
            if(protectedModel.Calls!=0)throw new Exception("Clause review changed an explicit English prefix");
            Console.WriteLine("PASS: clause-triggered review without idle inference; next clause, punctuation, Enter/Space, tail growth and target deletion");
        }
        finally{Directory.Delete(dir,true);}
    }
}
