using Android.Content;
using System.Text.Json;
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Mobile;
// Fixed inputs only. Sessions have no state directory or access to personal dictionaries.
internal static class FastZenzSelfTest
{
    record Fixture(string Id,string Raw,string CorrectRaw,string[] Expected,int TouchAt=-1);
    static readonly Fixture[] Fixtures=[
        new("warewsre-sentence","warewsrehauchuujinda","warewarehauchuujinda",["我々は宇宙人だ"]),
        new("warewsre-word","warewsre","wareware",["我々"]),
        new("arigatpu-tail","arigatpugozaimasu","arigatougozaimasu",["ありがとうございます","有難うございます"]),
        new("arigatpu-prefix","doumoarigatpu","doumoarigatou",["どうもありがとう"]),
        new("onegaishimsu-prefix","yoroshikuonegaishimsu","yoroshikuonegaishimasu",["よろしくお願いします","宜しくお願いします"]),
        new("sumimasne-tail","sumimasnega","sumimasenga",["すみませんが"]),
        new("touch-chinamoni-tail","chinamoniashitahayasumidesu","chinaminiashitahayasumidesu",["ちなみに明日は休みです","因みに明日は休みです"],6),
        new("touch-arifatou-tail","arifatougozaimasu","arigatougozaimasu",["ありがとうございます","有難うございます"],3)
    ];
    public static void Start(Context context) => _ = Task.Run(async () =>
    {
            var rows=new List<object>();
            try
            {
                foreach(var mode in new[]{"fast"})
                {
                    var native=mode=="off"?null:LocalZenz.Create(context);
                    using var evaluator=native is null?null:new CachedContextEvaluator(native);
                    if(native is not null && !await native.WarmUpAsync())throw new Exception("Model preparation failed");
                    foreach(var cadence in new[]{"burst","settled"})
                    foreach(var item in Fixtures.SelectMany(f=>new[]{(f.Id,f.Raw,f.Expected,f.TouchAt),(f.Id+"-normal",f.CorrectRaw,f.Expected,-1)}).Concat(new[]{("english","camera",new[]{"camera"},-1),("model","a7f1s",new[]{"a7f1s"},-1)}))
                    {
                        evaluator?.ClearCaches();
                        var progress=$"{mode}/{cadence} {rows.Count+1}/36 {item.Item1}";
                        File.WriteAllText(Path.Combine(context.FilesDir!.AbsolutePath,"zenz-fast-progress.txt"),progress);
                        var engine=MozcJniBridge.Create(context,learning:false);string committed="";
                        var session=new AndroidInputSession(engine,engine.Candidates,(op,value,_)=>{if(op==1)committed+=value;},()=>new InputPreferences{LocalZenz=true,LearningEnabled=true,EnterCommitsAsShown=false},contextEvaluator:evaluator,coherentRomanReview:true);
                        var intermediate=new List<object>();
                        for(var i=0;i<item.Item2.Length;i++)
                        {
                            LetterTouch? touch=null;
                            if(i==item.Item4)
                            {
                                var wrong=item.Item2[i];var row=wrong=='f'?"asdfghjkl":"qwertyuiop";var target=wrong=='f'?'g':'i';var at=row.IndexOf(wrong);
                                touch=AdaptiveKeyTarget.Capture(row.Select((c,k)=>new TouchKey(c,k*40,0,40,54)).ToArray(),at*40+(row.IndexOf(target)>at?32:8),27,_=>0);
                            }
                            session.Character(item.Item2[i],touch);
                            if(cadence=="settled")await Drain(session);
                            intermediate.Add(Snapshot(session));
                        }
                        await Drain(session);var live=Snapshot(session);var liveText=session.View?.Text??"";
                        session.Character(',');await Drain(session);var boundary=Snapshot(session);var shown=session.View?.Text??"";
                        session.Key(0x0D);await Drain(session);
                        rows.Add(new{id=item.Item1,raw=item.Item2,mode,cadence,expected=item.Item3,live,boundary,committed,liveCorrect=item.Item3.Contains(liveText),correct=item.Item3.Contains(committed.TrimEnd('，','、',',','。','.')),commitMatchesShown=shown==committed,pending=session.ContextEvaluationPending,intermediate});
                        File.WriteAllText(Path.Combine(context.FilesDir!.AbsolutePath,"zenz-fast-partial.json"),JsonSerializer.Serialize(rows));
                    }
                }
                File.WriteAllText(Path.Combine(context.FilesDir!.AbsolutePath,"zenz-fast-selftest.json"),JsonSerializer.Serialize(new{status="COMPLETE",rows}));
                
            }
            catch(Exception e){File.WriteAllText(Path.Combine(context.FilesDir!.AbsolutePath,"zenz-fast-selftest.json"),JsonSerializer.Serialize(new{status="ERROR",error=e.ToString(),rows}));}
    });
    static object Snapshot(AndroidInputSession session)=>new{text=session.View?.Text,clauses=session.View?.Clauses,aligned=session.View?.Text==string.Concat(session.View?.Clauses??[]),candidates=session.View?.Candidates};
    static async Task Drain(AndroidInputSession session)
    {
        var until=System.Environment.TickCount64+4000;
        while(session.ContextEvaluationPending && System.Environment.TickCount64<until){session.PollContextEvaluation(System.Environment.TickCount64);await Task.Delay(4);}
    }
}
