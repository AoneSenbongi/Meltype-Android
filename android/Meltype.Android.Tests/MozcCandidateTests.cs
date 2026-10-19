// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Meltype.AndroidCore;
internal static class MozcCandidateTests
{
    public static void Run()
    {
        foreach (var fault in new[] { "", "stalled", "reading" })
        {
            var shifts=0;
            byte[] Transport(byte[] input)
            {
                var kind=input[1];
                if(kind==1) return N(1,1);
                if(kind!=3) return [];
                var merged=input.AsSpan().IndexOf(new byte[]{32,4})>=0;
                if(merged) shifts++;
                var reading=fault=="reading" ? "ごし" : "ごじ";
                return merged && fault!="stalled"
                    ? B(5,G(2,S(4,"五時"),S(6,reading))).Concat(B(14,B(2,S(4,"五時")),B(2,S(4,"誤字")),B(2,S(4,"護持")))).ToArray()
                    : B(5,G(2,S(4,"五"),S(6,"ご")),G(2,S(4,"時"),S(6,"じ"))).Concat(B(14,B(2,S(4,"五")))).ToArray();
            }
            var converter=new AndroidMozcConverter(Transport,learning:false);
            var clauses=converter.ConvertClauses("ごじ");
            if(clauses?.Count!=2 || shifts!=0) throw new Exception("Normal conversion segmentation changed");
            var words=converter.Candidates("ごじ");
            if(fault=="")
            {
                if(!words.Contains("誤字") || shifts!=1) throw new Exception("Whole-reading candidates lost 誤字 when engine split ご|じ");
                var live=new AndroidInputSession(converter,converter.Candidates,(_,_,_)=>{});
                foreach(var c in "goji") live.Character(c);
                if(live.View?.Candidates.Contains("誤字")!=true) throw new Exception("Whole-reading candidate missing in live bar");
                live.Key(0x25,shift:true);live.Key(0x27,shift:true);
                if(live.View?.Candidates.Contains("誤字")!=true) throw new Exception("Whole-reading candidate missing after manual Shift resize");
            }
            else if(words.Count!=0 || shifts!=1) throw new Exception("Invalid/no-growth candidate resize was accepted or retried");
        }
        Console.WriteLine("PASS: whole-reading goji homophones, unchanged automatic segmentation, bounded resize and reading validation");
    }
    static byte[] V(int n){var b=new List<byte>();while(n>127){b.Add((byte)((n&127)|128));n>>=7;}b.Add((byte)n);return b.ToArray();}
    static byte[] N(int f,int n)=>V(f<<3).Concat(V(n)).ToArray();
    static byte[] B(int f,params byte[][] parts){var p=parts.SelectMany(x=>x).ToArray();return V((f<<3)|2).Concat(V(p.Length)).Concat(p).ToArray();}
    static byte[] S(int f,string s)=>B(f,Encoding.UTF8.GetBytes(s));
    static byte[] G(int f,params byte[][] p)=>V((f<<3)|3).Concat(p.SelectMany(x=>x)).Concat(V((f<<3)|4)).ToArray();
}
