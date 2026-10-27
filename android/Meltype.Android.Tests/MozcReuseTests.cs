using System.Text;
using Meltype.AndroidCore;
using Meltype.Composition;

internal static class MozcReuseTests
{
    public static void Run()
    {
        int calls=0;string reading="かな";bool learn=true,fail=false;
        byte[] Transport(byte[] input)
        {
            calls++;
            var text=Encoding.UTF8.GetString(input);
            if(input[1]==1)return N(1,1);
            if(input[1]==5 && text.Contains("ほか"))reading="ほか";
            else if(input[1]==5 && text.Contains("かな"))reading="かな";
            if(input[1]!=3)return [];
            if(fail){fail=false;throw new InvalidOperationException("transient failure");}
            var result=reading=="かな"?"仮名":"他";
            return B(5,G(2,S(4,result),S(6,reading))).Concat(B(14,B(2,S(4,result)),B(2,S(4,"別候補")))).ToArray();
        }
        var converter=new AndroidMozcConverter(Transport){LearningEnabled=()=>learn};
        var first=converter.ConvertClauses("かな","文脈A")!;
        converter.Candidates("ほか");
        int before=calls;
        var reused=converter.ConvertClauses("かな","文脈A")!;
        if(calls!=before)throw new Exception("Identical conversion repeated native work after another candidate lookup");
        if(!reused.SequenceEqual(first)||!converter.Candidates("かな").SequenceEqual(new[]{"仮名","別候補"}))
            throw new Exception("Reuse did not restore the original clauses and candidates");
        if(reused is IList<ConversionClause> mutable)mutable[0]=new("かな","改変");
        var candidates=converter.Candidates("かな");
        if(candidates is IList<string> words)words[0]="改変";
        if(converter.ConvertClauses("かな","文脈A")![0].Text!="仮名"||converter.Candidates("かな")[0]!="仮名")
            throw new Exception("Caller mutation leaked into a saved conversion");
        before=calls;converter.ConvertClauses("かな","文脈B");
        if(calls==before)throw new Exception("Different context reused a conversion");
        learn=false;before=calls;converter.Candidates("かな");
        if(calls==before)throw new Exception("Learning preference change reused old candidates");
        learn=true;converter.ConvertClauses("かな","文脈A");
        converter.Learn("文脈A",[new("かな","仮名")]);
        before=calls;converter.ConvertClauses("かな","文脈A");
        if(calls==before)throw new Exception("Learning attempt did not invalidate saved conversion");
        for(int i=0;i<40;i++)converter.ConvertClauses("かな","context"+i);
        before=calls;converter.ConvertClauses("かな","文脈A");
        if(calls==before)throw new Exception("Conversion reuse grew without a bound");
        fail=true;var failed=converter.ConvertClauses("かな","retry");
        if(failed is not null||converter.ConvertClauses("かな","retry") is null)throw new Exception("A failed conversion was saved");
        Console.WriteLine("PASS: bounded Mozc reuse, candidate restoration, context/mode/learning invalidation, immutable snapshots and retry after failure");
    }
    static byte[] V(int n){var b=new List<byte>();while(n>127){b.Add((byte)((n&127)|128));n>>=7;}b.Add((byte)n);return b.ToArray();}
    static byte[] N(int f,int n)=>V(f<<3).Concat(V(n)).ToArray();
    static byte[] B(int f,params byte[][] parts){var p=parts.SelectMany(x=>x).ToArray();return V((f<<3)|2).Concat(V(p.Length)).Concat(p).ToArray();}
    static byte[] S(int f,string s)=>B(f,Encoding.UTF8.GetBytes(s));
    static byte[] G(int f,params byte[][] p)=>V((f<<3)|3).Concat(p.SelectMany(x=>x)).Concat(V((f<<3)|4)).ToArray();
}
