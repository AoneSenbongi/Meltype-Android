// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;
internal static class RuleBasedCorrectionTests
{
    public static void Run()
    {
        var keys = new[] { new TouchKey('o',0,0,40,54), new TouchKey('i',40,0,40,54) };
        var touch = AdaptiveKeyTarget.Capture(keys,38,27,_=>0);
        var committed = "";
        var input = new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)committed+=text;},
            ()=>new InputPreferences{LearningEnabled=true,LocalZenz=false,EnterCommitsAsShown=false});
        foreach(var c in "chinamoni")input.Character(c,c=='o'?touch:null);
        input.SpaceKey(false);
        if(committed!="ちなみに " || input.ContextEvaluationPending)
            throw new Exception("Touch correction needs zenz or left a pending inference: "+committed);

        foreach(var boundary in new[]{"enter","punctuation","next"})
        {
            var output="";
            var session=new AndroidInputSession(new Plain(),_=>["普通の候補甲","普通の候補乙"],(op,text,_)=>{if(op==1)output+=text;},
                ()=>new InputPreferences{LearningEnabled=true,EnterCommitsAsShown=false,ShowTypedKeys=true});
            foreach(var c in "chinamoni")session.Character(c,c=='o'?touch:null);
            if(session.View?.Text=="ちなみに")throw new Exception("Rule correction ran before a boundary");
            if(boundary=="enter")session.Key(0x0D);
            else if(boundary=="next")foreach(var c in "desu")session.Character(c);
            else session.Character(',');
            var actual=boundary=="enter"?output:session.View?.Text;
            var expected=boundary=="enter"?"ちなみに":boundary=="next"?"ちなみにです":"ちなみに，";
            if(actual!=expected || session.ContextEvaluationPending)throw new Exception("Rule boundary failed: "+boundary+" "+actual);
            if(boundary=="punctuation")
            {
                var baseline=new AndroidInputSession(new Plain(),_=>["普通の候補甲","普通の候補乙"],(_,_,_)=>{});
                foreach(var c in "chinamoni,")baseline.Character(c);
                if(!session.View!.Candidates.SequenceEqual(baseline.View!.Candidates) ||
                    !(session.View.Clauses??[]).SequenceEqual(baseline.View.Clauses??[]) || session.View.Typed!="chinamoni,")
                    throw new Exception("Rule review changed candidates/clauses or original keystrokes");
                session.Select(0);
                if(output==expected || output.Length==0)throw new Exception("Rule review overrode an explicit candidate choice");
            }
        }
        foreach(var blocked in new[]{"no-touch","center","no-learning","private","english","delete","reset","failed-conversion"})
        {
            var prefs=new InputPreferences{LearningEnabled=blocked!="no-learning",LocalZenz=true,EnterCommitsAsShown=false};
            var session=new AndroidInputSession(new Plain(blocked=="failed-conversion"),_=>[],(_,_,_)=>{},()=>prefs,learningAllowed:blocked!="private");
            if(blocked=="english")session.SetEnglish(true);
            void Type(bool evidence)
            { foreach(var c in "chinamoni")session.Character(c,c=='o'&&evidence?touch:null); }
            var evidence=blocked is not ("no-touch" or "center");
            Type(evidence);
            if(blocked=="delete"){session.Key(0x08);session.Character('i');}
            if(blocked=="reset"){session.Reset();Type(false);}
            session.Character(',');
            if(session.View?.Text=="ちなみに，" || session.ContextEvaluationPending)
                throw new Exception("Unsafe or unavailable rule correction applied: "+blocked);
        }
        foreach(var word in new[]{"camera","github","a7f1s","kame","chinamini"})
        {
            var output="";
            var session=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)output+=text;},
                ()=>new InputPreferences{EnterCommitsAsShown=false});
            foreach(var c in word)session.Character(c);
            var before=session.View?.Text??"";session.Key(0x0D);
            if(output!=before || session.ContextEvaluationPending)throw new Exception("Normal/protected input changed on commit: "+word);
        }
        var ruleOutput="";
        var rule=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)ruleOutput+=text;},
            ()=>new InputPreferences{LearningEnabled=true,EnterCommitsAsShown=false});
        foreach(var c in "arigatpu")rule.Character(c);
        rule.Key(0x0D);
        if(ruleOutput!="ありがとう" || rule.ContextEvaluationPending)throw new Exception("Existing conservative typo rule still requires a model: "+ruleOutput);
        foreach(var asShown in new[]{false,true})
        {
            var output="";
            var session=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)output+=text;},
                ()=>new InputPreferences{LearningEnabled=true,LocalZenz=true,EnterCommitsAsShown=asShown});
            foreach(var c in "chinamoni")session.Character(c,c=='o'?touch:null);
            var before=session.View!.Text;session.Key(0x0D);
            if(output!=(asShown?before:"ちなみに") || session.ContextEvaluationPending)
                throw new Exception("Saved zenz preference or as-shown Enter changed the rule-only policy");
        }
        Console.WriteLine("PASS: model-free touch/rule correction at clause/punctuation/Space/Enter, original candidates/keys, selection, protected input, failures and no pending inference");
    }
    public static void WholeWordAfterPrefix()
    {
        var keys=new[]{new TouchKey('f',0,0,40,54),new TouchKey('g',40,0,40,54)};
        var touch=AdaptiveKeyTarget.Capture(keys,35,27,_=>0);
        var input=new AndroidInputSession(new PrefixConverter(),_=>[],(_,_,_)=>{});
        foreach(var c in "arifatou")input.Character(c,c=='f'?touch:null);
        var before=input.View?.Text;
        input.Character(',');
        if(input.View?.Text!="ありがとう，")throw new Exception("A partial touch correction blocked whole-word review: "+before+" -> "+input.View?.Text);
    }
    sealed class PrefixConverter:IKanjiConverter
    {
        public string? Convert(string s)=>s switch{"ありが"=>"アリが","とう"=>"等",_=>s};
        public IReadOnlyList<ConversionClause>? ConvertClauses(string s,string? context=null)=>
            s.StartsWith("ありふぁ")&&s.Length>4?[new("ありふぁ","ありファ"),new(s[4..],Convert(s[4..])!)]:[new(s,Convert(s)!)];
    }
    sealed class Plain(bool fail=false):IKanjiConverter
    {
        public string? Convert(string s)=>fail&&s=="ちなみに"?null:s;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string s,string? context=null)=>
            fail&&s=="ちなみに"?null:s.EndsWith("です")&&s.Length>2 ? [new(s[..^2],s[..^2]),new("です","です")] : [new(s,s)];
    }
}
