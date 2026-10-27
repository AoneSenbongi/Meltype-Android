// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
internal static class TouchCorrectionTests
{
    public static void Run()
    {
        var keys = new[] { new TouchKey('w', 0, 0, 40, 54), new TouchKey('e', 40, 0, 40, 54) };
        if (AdaptiveKeyTarget.Capture(keys, 38, 27, c => c == 'e' ? 1 : 0)?.Original.Letter != 'w')
            throw new Exception("Boundary touch replaced the original key before context evaluation");
        var history = new TouchInputHistory();
        var vowelKeys = new[] { new TouchKey('o',0,0,40,54), new TouchKey('i',40,0,40,54) };
        LetterTouch At(float x) => AdaptiveKeyTarget.Capture(vowelKeys,x,27,_=>0)!;
        var raw="";
        foreach(var c in "chinamoni") { history.Append(raw,c,c=='o'?At(38):null);raw+=c; }
        if(!history.Alternatives(raw).Contains("chinamini"))throw new Exception("Boundary evidence did not survive later characters");
        history.Clear();
        if(history.Alternatives(raw).Count!=0)throw new Exception("Cleared touch evidence survived");
        var ambiguous = new RomajiTypoCorrector(new Meltype.Detection.RomajiDetector(), "あくぱ\nあきぱ");
        if(ambiguous.Fix("akpa",true) is not null || ambiguous.Suggestions("akpa",true).Count!=2)
            throw new Exception("Competing readings were selected or discarded before context evaluation");
        var lexicon = RomajiTypoCorrector.Load(new Meltype.Detection.RomajiDetector());
        if(lexicon.TouchSuggestions("kame",["kane"]).Count!=0)
            throw new Exception("Touch evidence overrode an already known Japanese reading");

        foreach(var mode in new[]{"accept","reject","center","reset","delete","english","learning-off","no-model"})
        {
            var model=new Model(mode=="reject");
            var input=new AndroidInputSession(new Converter(),_=>["通常候補甲","通常候補乙"],(_,_,_)=>{},
                ()=>new InputPreferences{LocalZenz=true,LearningEnabled=mode!="learning-off"},contextEvaluator:mode=="no-model"?null:model);
            if(mode=="english")input.SetEnglish(true);
            void Type(bool evidence)
            {
                foreach(var c in "chinamoni")input.Character(c,c=='o'&&evidence?At(mode=="center"?20:38):null);
            }
            Type(true);
            if(mode=="reset"){input.Reset();Type(false);}
            if(mode=="delete"){input.Key(0x08);input.Character('i');}
            if(mode=="english")
            {
                input.PollContextEvaluation(Environment.TickCount64);
                if(model.Requests.Count!=0)throw new Exception("Touch evidence affected explicit English");
                continue;
            }
            if(input.View?.Text=="ちなみに")throw new Exception("Touch correction ran before a boundary");
            input.Character(',');input.PollContextEvaluation(Environment.TickCount64);
            if(mode is "accept" or "no-model" ? input.View?.Text!="ちなみに，" : input.View?.Text=="ちなみに，")
                throw new Exception("Touch correction acceptance/protection failed: "+mode+" shown="+input.View?.Text+" requests="+string.Join(";",model.Requests.Select(r=>r.Reading+":"+string.Join("|",r.Candidates))));
            if(mode=="accept" && model.Requests.Count(r=>!r.KanaConversion)!=1)
                throw new Exception("Touch correction did not share one decision");
            if(model.Requests.Any(r=>r.Candidates.Count>5 || r.Candidates.Count==0))throw new Exception("Unbounded touch request");
        }
        foreach(var edit in new[]{false,true})
        {
            var model=new Model(false){Delayed=new()};
            var input=new AndroidInputSession(new Converter(),_=>["通常候補甲","通常候補乙"],(_,_,_)=>{},
                ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true},contextEvaluator:model);
            foreach(var c in "chinamoni")input.Character(c,c=='o'?At(38):null);
            input.Character(',');input.PollContextEvaluation(Environment.TickCount64);
            var candidates=input.View!.Candidates.ToArray();var clauses=input.View.Clauses?.ToArray();
            var selected=input.View.SelectedClause;
            if(edit)input.Reset();
            model.Delayed.SetResult("ちなみに");input.PollContextEvaluation(Environment.TickCount64);
            if(edit ? input.View is not null : input.View?.Text!="ちなみに，" ||
                !input.View.Candidates.SequenceEqual(candidates) || input.View.SelectedClause!=selected ||
                !(input.View.Clauses??[]).SequenceEqual(clauses??[]))
                throw new Exception("Touch result changed ordinary candidates/selection or survived reset");
        }
        foreach(var boundary in new[]{"space","enter"})
        {
            var model=new Model(false);var committed="";
            var input=new AndroidInputSession(new Converter(),_=>[],(op,text,_)=>{if(op==1)committed+=text;},
                ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true,EnterCommitsAsShown=false},contextEvaluator:model);
            foreach(var c in "chinamoni")input.Character(c,c=='o'?At(38):null);
            if(boundary=="space")input.SpaceKey(false);else input.Key(0x0D);
            input.PollContextEvaluation(Environment.TickCount64);
            if(committed!="ちなみに"+(boundary=="space"?" ":""))throw new Exception("Touch evidence was lost on "+boundary+": "+committed);
        }
        Console.WriteLine("PASS: deferred boundary evidence, readable typo correction, acceptance/rejection, centers, editing, reset, English, learning-off and absent model");
    }
    sealed class Converter:IKanjiConverter
    {
        public string? Convert(string s)=>s;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string s,string? context=null)=>[new(s,s)];
    }
    sealed class Model(bool reject):IContextEvaluator
    {
        public List<ContextRequest> Requests=[];
        public TaskCompletionSource<string?>? Delayed;
        public Task<string?> EvaluateAsync(ContextRequest r,CancellationToken c)
        { Requests.Add(r);return !r.KanaConversion && Delayed is not null ? Delayed.Task : Task.FromResult<string?>(r.KanaConversion||reject?r.Candidates[0]:r.Candidates.FirstOrDefault(s=>s=="ちなみに")); }
    }
}
