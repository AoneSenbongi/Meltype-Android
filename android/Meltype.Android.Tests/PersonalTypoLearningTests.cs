// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
internal static class PersonalTypoLearningTests
{
    sealed class Model : IContextEvaluator
    {
        public int Calls;
        public bool Reject;
        public TaskCompletionSource<string?>? Delayed;
        public Task<string?> EvaluateAsync(ContextRequest request, CancellationToken cancellation)
        { Calls++; return Delayed?.Task ?? Task.FromResult<string?>(Reject ? request.Candidates.First() : request.Candidates.Last()); }
    }
    sealed class Plain : IKanjiConverter
    {
        public string? Convert(string reading) => reading;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string reading, string? context=null) => [new(reading,reading)];
    }
    public static void Run() => RunAsync().GetAwaiter().GetResult();
    static async Task RunAsync()
    {
        var folder=Path.Combine(Path.GetTempPath(),"meltype-personal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var path=Path.Combine(folder,"zenz-personal-typos.json");var enabled=true;var model=new Model();
            var learner=new PersonalTypoLearning(path,model,()=>enabled);
            if(learner.Find("chinamoni")!=null)throw new Exception("Unobserved personal typo applied");
            await learner.ObserveAsync("chinamoni","chinamini","秘密の前文","後文");
            if(learner.Find("chinamoni")?.Kana!="ちなみに")throw new Exception("First approved personal edit was not usable");
            if(new PersonalTypoLearning(path,model,()=>enabled).Find("chinamoni")?.Kana!="ちなみに")throw new Exception("Personal correction did not persist");
            if(File.ReadAllText(path).Contains("秘密") || File.ReadAllText(path).Contains("後文"))throw new Exception("Context was persisted");
            var saved=File.ReadAllBytes(path);enabled=false;
            await learner.ObserveAsync("chinamoni","chinamini");
            if(learner.Find("chinamoni")!=null || !saved.SequenceEqual(File.ReadAllBytes(path)))throw new Exception("Learning OFF used/changed personal data");
            enabled=true;var calls=model.Calls;
            foreach(var wrong in new[]{"github","camera","a7f1s","https://x.com","Chinamoni","shinai"})await learner.ObserveAsync(wrong,"chinamini");
            if(model.Calls!=calls)throw new Exception("Protected/non-typo edit reached zenz");
            var rejectedPath=Path.Combine(folder,"rejected.json");
            var rejected=new PersonalTypoLearning(rejectedPath,new Model{Reject=true},()=>true);
            await rejected.ObserveAsync("chinamoni","chinamini");
            if(rejected.Find("chinamoni")!=null || File.Exists(rejectedPath))throw new Exception("Rejected personal edit was learned");
            foreach (var example in new[] {
                (Wrong:"arigatpu", Right:"arigatou", Unseen:"omedetpu", Expected:"omedetou"),
                (Wrong:"onegaishimsu", Right:"onegaishimasu", Unseen:"arigtou", Expected:"arigatou"),
                (Wrong:"arigatxou", Right:"arigatou", Unseen:"omedetxou", Expected:"omedetou"),
                (Wrong:"ariggatou", Right:"arigatou", Unseen:"omedettou", Expected:"omedetou"),
                (Wrong:"sumimasne", Right:"sumimasen", Unseen:"omedetuo", Expected:"omedetou") })
            {
                var generalizedFolder=Path.Combine(folder,example.Wrong);
                var generalizedPath=Path.Combine(generalizedFolder,"zenz-personal-typos.json");
                var personal=new PersonalTypoLearning(generalizedPath,model,()=>enabled);
                if(personal.Find(example.Unseen)!=null)throw new Exception("Unlearned pattern was applied");
                await personal.ObserveAsync(example.Wrong,example.Right);
                if(new PersonalTypoLearning(generalizedPath,model,()=>enabled).Find(example.Unseen)?.Right!=example.Expected)
                    throw new Exception("Personal edit did not generalize: "+example.Wrong+" -> "+example.Unseen+" learned="+personal.Find(example.Wrong)+" result="+personal.Find(example.Unseen));
                foreach(var reject in new[]{false,true})
                {
                    var output="";
                    var applying=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)output+=text;},
                        ()=>new InputPreferences{LocalZenz=true,LearningEnabled=true},stateDirectory:generalizedFolder,
                        contextEvaluator:new Model{Reject=reject});
                    foreach(var c in example.Unseen)applying.Character(c);
                    applying.Character(',');
                    var baseline=applying.View?.Text;
                    for(var tick=0;tick<8;tick++){applying.PollContextEvaluation(Environment.TickCount64+250);await Task.Delay(5);}
                    var expectedKana=new Meltype.Detection.RomajiDetector().ConvertLenient(example.Expected,true)+"，";
                    if(applying.View?.Text!=(reject?baseline:expectedKana))
                        throw new Exception("Generalized session did not honor zenz decision: "+example.Unseen+" reject="+reject+" text="+applying.View?.Text);
                    var savedPattern=File.ReadAllBytes(generalizedPath);
                    applying.Key(0x0D);await applying.PendingTypoLearning;
                    if(output!=(reject?baseline:expectedKana) || !savedPattern.SequenceEqual(File.ReadAllBytes(generalizedPath)))
                        throw new Exception("Generalized commit changed text or reinforced itself");
                }
                foreach(var literal in new[]{"camera","github","a7f1s","shinai","arigatou"})
                    if(personal.Find(literal)!=null)throw new Exception("Generalization changed protected/valid input: "+literal);
            }
            var ranked=new PersonalTypoLearning(Path.Combine(folder,"ranked.json"),model,()=>true);
            await ranked.ObserveAsync("omedeton","omedetou");
            await ranked.ObserveAsync("sumimasn","sumimasen");
            if(ranked.Find("omedeton")==null || ranked.Find("sumimasn")==null)throw new Exception("Pattern weighting fixtures were not learned");
            if(ranked.Find("shimasn")!=null)throw new Exception("Equally supported different readings were applied");
            await ranked.ObserveAsync("omedeton","omedetou");
            if(ranked.Find("shimasn")?.Right!="shimasu")throw new Exception("Repeated personal tendency did not change candidate ranking/cache");
            var cancelModel=new Model{Delayed=new()};var cancelledPath=Path.Combine(folder,"cancelled.json");
            var cancelled=new PersonalTypoLearning(cancelledPath,cancelModel,()=>enabled);using var cts=new CancellationTokenSource();
            var work=cancelled.ObserveAsync("chinamoni","chinamini",cancellation:cts.Token);cts.Cancel();cancelModel.Delayed.SetResult("ちなみに");await work;
            if(File.Exists(cancelledPath))throw new Exception("Cancelled edit was learned");
            File.Delete(path);
            var prefs=new InputPreferences{LocalZenz=true,LearningEnabled=true};
            var session=new AndroidInputSession(new Plain(),_=>[],(_,_,_)=>{},()=>prefs,stateDirectory:folder,contextEvaluator:model);
            foreach(var c in "chinamoni")session.Character(c);
            for(var j=0;j<2;j++)session.Key(0x08);
            foreach(var c in "mini")session.Character(c);
            session.Key(0x0D);await session.PendingTypoLearning;
            if(new PersonalTypoLearning(path,model,()=>enabled).Find("chinamoni")?.Kana!="ちなみに")throw new Exception("Session edits were not learned");
            foreach(var c in "chinamoni")session.Character(c);
            session.Character(',');
            for(var i=0;i<8;i++){session.PollContextEvaluation(Environment.TickCount64+250);await Task.Delay(5);}
            if(session.View?.Text!="ちなみに，")throw new Exception("Learned typo did not automatically correct: "+session.View?.Text);
            var before=File.ReadAllBytes(path);session.Key(0x0D);await session.PendingTypoLearning;
            if(!before.SequenceEqual(File.ReadAllBytes(path)))throw new Exception("Automatic correction reinforced itself");
            var immutable=File.ReadAllBytes(path);
            foreach(var policy in new[]{(true,false,true),(false,true,true),(true,true,false)})
            {
                var blocked=new AndroidInputSession(new Plain(),_=>[],(_,_,_)=>{},()=>new InputPreferences{LocalZenz=policy.Item1,LearningEnabled=policy.Item2},policy.Item3,stateDirectory:folder,contextEvaluator:model);
                foreach(var c in "chinamoni")blocked.Character(c);
                for(var tick=0;tick<4;tick++){blocked.PollContextEvaluation(Environment.TickCount64+250);await Task.Delay(5);}
                if(blocked.View?.Text=="ちなみに")throw new Exception("Disabled/private session used learned personal data");
                for(var j=0;j<2;j++)blocked.Key(0x08);
                foreach(var c in "mini")blocked.Character(c);
                blocked.Key(0x0D);await blocked.PendingTypoLearning;
                if(!immutable.SequenceEqual(File.ReadAllBytes(path)))throw new Exception("Disabled/private session changed learned data");
            }
            var conflicting=new PersonalTypoLearning(Path.Combine(folder,"conflict.json"),model,()=>true);
            await conflicting.ObserveAsync("shimasn","shimasu");
            if(conflicting.Find("shimasn")==null)throw new Exception("Conflict test never learned its initial correction");
            await conflicting.ObserveAsync("shimasn","shimasen");
            if(conflicting.Find("shimasn")!=null)throw new Exception("Ambiguous personal correction was applied");
            session.Reset();prefs=prefs with{LearningEnabled=true};
            foreach(var c in "chinamoni")session.Character(c);
            session.Character(',');
            prefs=prefs with{LearningEnabled=false};
            for(var i=0;i<5;i++){session.PollContextEvaluation(Environment.TickCount64+250);await Task.Delay(5);}
            if(session.View?.Text?.Contains("ちなみに")==true)throw new Exception("Disabling learning applied a queued personal correction");
            Console.WriteLine("PASS: zenz-confirmed personal edit learning after one approval, generalized key/edit patterns, zenz acceptance/rejection, persistence, privacy, cancellation and automatic correction without self-reinforcement");
        }
        finally{Directory.Delete(folder,true);}
    }
}
