// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
internal static class NeuralConversionTests
{
    sealed class Model : IContextEvaluator
    {
        public List<ContextRequest> Requests=[];
        public string Generated="橋を";
        public bool RejectTypo;
        public bool KeepSurface;
        public TaskCompletionSource<string?>? Generation;
        public Task<string?> EvaluateAsync(ContextRequest r,CancellationToken c)
        {
            Requests.Add(r);
            return r.Candidates.Count==1 ? Generation?.Task??Task.FromResult<string?>(KeepSurface?r.Candidates[0]:Generated) :
                Task.FromResult<string?>(RejectTypo?r.Candidates[0]:r.Candidates.Last());
        }
    }
    sealed class Converter : IKanjiConverter
    {
        public string? Convert(string r)=>r switch{"はし"=>"箸","はしを"=>"箸を","わたる"=>"渡る","すみまsね"=>"済魔sね","すみまsね，"=>"済魔s寝，",_=>r};
        public IReadOnlyList<ConversionClause>? ConvertClauses(string r,string? context=null)=>
            r.StartsWith("きょうはいい") && r.Length>6 ? [new("きょうは","今日は"),new("いい","いい"),new(r[6..],r[6..])] :
            r.StartsWith("きょうは") && r.Length>4 ? [new("きょうは","今日は"),new(r[4..],r[4..])] :
            r.StartsWith("はしを") && r.Length>3 ? [new("はしを","箸を"),new(r[3..],Convert(r[3..])!)] : [new(r,Convert(r)!)];
    }
    static IReadOnlyList<string> Candidates(string r)=>r switch
    {
        "はし"=>["箸","橋","端"],"はしを"=>["箸を","橋を","端を"],"ちなみに"=>["ちなみに","因みに"],_=>[]
    };
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    static async Task RunAsync()
    {
        var policy=new ContextRequest("はしを","川を","",["箸を"],KanaConversion:true);
        if(!ZenzCandidateRules.AllowsCandidate(policy,"橋を") || ZenzCandidateRules.AllowsCandidate(policy,"橋をa7f1") ||
            ZenzCandidateRules.AllowsCandidate(policy,"橋を\n") || ZenzCandidateRules.AllowsCandidate(policy with{RomanTypo=true},"橋を"))
            throw new Exception("Neural generation policy accepts unsafe output or rejects Japanese conversion");
        var model=new Model();var output="";
        var input=new AndroidInputSession(new Converter(),Candidates,(op,t,_)=>{if(op==1)output+=t;},
            ()=>new InputPreferences{LocalZenz=true,EnterCommitsAsShown=false},surrounding:()=>("川の向こうへ行くために", ""),contextEvaluator:model);
        foreach(var c in "hashiwowataru")input.Character(c);
        var before=input.View!;var candidates=before.Candidates.ToArray();var clauses=before.Clauses!.ToArray();
        input.PollContextEvaluation(Environment.TickCount64);
        if(input.View?.Text!="橋を渡る" || model.Requests.Count!=1)throw new Exception("Normal kana conversion did not use zenz at a clause boundary: "+input.View?.Text+" requests="+model.Requests.Count+" readings="+string.Join("|",model.Requests.Select(r=>r.Reading))+" clauses="+string.Join("|",clauses));
        if(!input.View.Candidates.SequenceEqual(candidates) || !input.View.Clauses!.SequenceEqual(clauses))throw new Exception("Neural conversion changed normal candidate presentation");
        if(!model.Requests[0].Left.Contains("川"))throw new Exception("Neural conversion lost preceding context");
        input.Key(0x0D);if(output!="橋を渡る")throw new Exception("Neural conversion changed at commit");

        var keptOutput="";var keptModel=new Model{KeepSurface=true};
        var kept=new AndroidInputSession(new Converter(),Candidates,(op,t,_)=>{if(op==1)keptOutput+=t;},
            ()=>new InputPreferences{LocalZenz=true,EnterCommitsAsShown=false},contextEvaluator:keptModel);
        foreach(var c in "hashi")kept.Character(c);kept.Key(0x0D);
        if(keptOutput!="箸" || kept.View is not null)throw new Exception("An unchanged neural answer swallowed Enter");

        var unchangedModel=new Model{KeepSurface=true};
        var continuing=new AndroidInputSession(new Converter(),Candidates,(_,_,_)=>{},()=>new InputPreferences{LocalZenz=true},contextEvaluator:unchangedModel);
        foreach(var c in "kyouhaii")continuing.Character(c);continuing.PollContextEvaluation(Environment.TickCount64);
        var firstCount=unchangedModel.Requests.Count;
        foreach(var c in "tenki")continuing.Character(c);continuing.PollContextEvaluation(Environment.TickCount64);
        if(firstCount==0 || unchangedModel.Requests.Count<=firstCount)throw new Exception("An unchanged earlier clause blocked evaluation of the next clause: first="+firstCount+" total="+unchangedModel.Requests.Count+" readings="+string.Join("|",unchangedModel.Requests.Select(r=>r.Reading)));

        foreach(var bad in new[]{"橋を渡るa7f1","勝手な別の文章"})
        {
            var invalid=new Model{Generated=bad};
            var session=new AndroidInputSession(new Converter(),Candidates,(_,_,_)=>{},()=>new InputPreferences{LocalZenz=true},contextEvaluator:invalid);
            foreach(var c in "hashiwowataru")session.Character(c);
            var original=session.View!.Text;session.PollContextEvaluation(Environment.TickCount64);
            if(session.View?.Text!=original)throw new Exception("Unaligned or literal-changing generated text was applied");
        }
        var shifting=new Model{KeepSurface=true};
        var punctuated=new AndroidInputSession(new Converter(),Candidates,(_,_,_)=>{},()=>new InputPreferences{LocalZenz=true},contextEvaluator:shifting);
        foreach(var c in "sumimasne")punctuated.Character(c);punctuated.Character(',');punctuated.PollContextEvaluation(Environment.TickCount64);
        if(punctuated.View?.Text?.Contains('s')==true || shifting.Requests.Count<1)throw new Exception("Punctuation resegmentation cancelled typo review");
        var dir=Path.Combine(Path.GetTempPath(),"meltype-neural-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            await new PersonalTypoLearning(Path.Combine(dir,"zenz-personal-typos.json"),new Model(),()=>true).ObserveAsync("chinamoni","chinamini");
            await new PersonalTypoLearning(Path.Combine(dir,"zenz-personal-typos.json"),new Model(),()=>true).ObserveAsync("onegaishimsu","onegaishimasu");
            var finishingModel=new Model{KeepSurface=true};
            var finishing=new AndroidInputSession(new Converter(),Candidates,(_,_,_)=>{},
                ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true},stateDirectory:dir,contextEvaluator:finishingModel);
            foreach(var c in "hashiwowataru")finishing.Character(c);finishing.PollContextEvaluation(Environment.TickCount64);
            foreach(var c in "onegaishimsu")finishing.Character(c);finishing.Character(',');finishing.PollContextEvaluation(Environment.TickCount64);
            if(!finishingModel.Requests.Any(r=>r.RomanTypo && r.Reading.Contains("おねがいします")) || finishing.View?.Text.Contains('m')==true)
                throw new Exception("A prior clause review prevented final typo re-evaluation");
            foreach(var reject in new[]{false,true})
            {
                var combined=new Model{Generated="因みに",RejectTypo=reject};
                var session=new AndroidInputSession(new Converter(),Candidates,(_,_,_)=>{},()=>new InputPreferences{LocalZenz=true,LearningEnabled=true},stateDirectory:dir,contextEvaluator:combined);
                foreach(var c in "chinamoni")session.Character(c);session.Character(',');
                var original=session.View!.Text;
                session.PollContextEvaluation(Environment.TickCount64);
                if(reject ? combined.Requests.Count!=1 || session.View?.Text!=original :
                    combined.Requests.Count!=2 || session.View?.Text!="因みに，")throw new Exception("Typo decision and neural conversion were not combined correctly");
            }
            var waiting=new Model{Generation=new()};var committed="";
            var timed=new AndroidInputSession(new Converter(),Candidates,(op,t,_)=>{if(op==1)committed+=t;},
                ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true,EnterCommitsAsShown=false},stateDirectory:dir,contextEvaluator:waiting);
            foreach(var c in "chinamoni")timed.Character(c);timed.Key(0x0D);
            timed.PollContextEvaluation(Environment.TickCount64+2000);
            if(committed!="ちなみに")throw new Exception("Generation timeout discarded accepted typo correction or reset the total deadline");
            waiting.Generation.SetResult("因みに");timed.PollContextEvaluation(Environment.TickCount64+2500);
            if(committed!="ちなみに")throw new Exception("Late generation changed committed text");
        }
        finally{Directory.Delete(dir,true);}
        Console.WriteLine("PASS: neural kana conversion at clause boundaries, personal typo decision, ordinary candidate alignment, unchanged candidate UI and shared timeout");
    }
}
