// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Meltype.Composition;
using Meltype.Detection;
namespace Meltype.AndroidCore;

// Learns bounded, explicitly edited romaji pairs. Never stores editor context or model output.
public sealed class PersonalTypoLearning(string path, IContextEvaluator evaluator, Func<bool> enabled)
{
    sealed record Entry(string Wrong, string Right, int Support);
    sealed record Model(int Version, Entry[] Entries);
    readonly object _gate = new();
    readonly RomajiDetector _roman = new();
    readonly RomajiTypoCorrector _lexicon = RomajiTypoCorrector.Load(new RomajiDetector());
    static readonly Lazy<HashSet<string>> English = new(() => new(WordList.ParseWords(DictionarySource.ReadEmbedded("english.txt")), StringComparer.Ordinal));
    List<Entry>? _entries;
    readonly Dictionary<string, RomajiFix?> _generalized = new(StringComparer.Ordinal);
    sealed record Edit(string Wrong, string Right, bool Duplicate = false, bool Swap = false);
    static Edit Difference(string wrong, string right)
    {
        var at = 0;
        while(at < Math.Min(wrong.Length,right.Length) && wrong[at] == right[at]) at++;
        if(wrong.Length == right.Length)
            return wrong[at..].Length > 1 && wrong[at] == right[at+1] && wrong[at+1] == right[at]
                ? new(wrong.Substring(at,2),right.Substring(at,2),Swap:true)
                : new(wrong[at].ToString(),right[at].ToString());
        if(wrong.Length < right.Length) return new("",right[at].ToString());
        return new(wrong[at].ToString(),"",Duplicate:
            at > 0 && wrong[at-1] == wrong[at] || at+1 < wrong.Length && wrong[at+1] == wrong[at]);
    }
    RomajiFix? Generalize(string raw, List<Entry> entries)
    {
        if(entries.Count==0 || English.Value.Contains(raw) || _lexicon.IsKnownReading(_roman.ConvertLenient(raw,true))) return null;
        var scores = new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var group in entries.GroupBy(e=>e.Wrong).Where(g=>g.Select(e=>e.Right).Distinct().Count()==1)
            .SelectMany(g=>g).GroupBy(e=>Difference(e.Wrong,e.Right)))
        {
            var edit = group.Key;
            var support = group.Sum(e=>e.Support);
            var proposals = new Dictionary<string,int>(StringComparer.Ordinal);
            for(var at=0;at<=raw.Length;at++)
            {
                string? right = null;
                var weight = support;
                if(edit.Swap && at+1 < raw.Length && raw[at] != raw[at+1])
                {
                    right = raw[..at]+raw[at+1]+raw[at]+raw[(at+2)..];
                    if(raw.AsSpan(at,2).SequenceEqual(edit.Wrong)) weight *= 2;
                }
                else if(edit.Duplicate && at < raw.Length &&
                    (at>0 && raw[at-1]==raw[at] || at+1<raw.Length && raw[at+1]==raw[at]))
                    right = raw.Remove(at,1);
                else if(!edit.Swap && !edit.Duplicate && raw.AsSpan(at).StartsWith(edit.Wrong,StringComparison.Ordinal))
                    right = raw[..at]+edit.Right+raw[(at+edit.Wrong.Length)..];
                if(right!=null) proposals[right] = Math.Max(proposals.GetValueOrDefault(right),weight);
            }
            foreach(var proposal in proposals)
                scores[proposal.Key] = scores.GetValueOrDefault(proposal.Key)+proposal.Value;
        }
        var candidates = scores.Where(p=>Letters(p.Key) && !English.Value.Contains(p.Key))
            .Select(p=>(Right:p.Key,Kana:_roman.ConvertLenient(p.Key,true),Score:p.Value))
            .Where(p=>_lexicon.IsKnownReading(p.Kana))
            .GroupBy(p=>p.Kana).Select(g=>g.OrderByDescending(p=>p.Score).First())
            .OrderByDescending(p=>p.Score).Take(2).ToArray();
        if(candidates.Length==0 || candidates.Length>1 && candidates[0].Score==candidates[1].Score) return null;
        return new(raw,candidates[0].Right,candidates[0].Kana);
    }
    static bool Letters(string s) => s.Length is >= 4 and <= 32 && s.All(char.IsAsciiLetterLower);
    static bool SingleEdit(string a, string b)
    {
        if (a == b || Math.Abs(a.Length-b.Length)>1) return false;
        if (a.Length==b.Length)
        {
            var differences=Enumerable.Range(0,a.Length).Where(i=>a[i]!=b[i]).ToArray();
            return differences.Length==1 || differences.Length==2 && differences[1]==differences[0]+1 && a[differences[0]]==b[differences[1]] && a[differences[1]]==b[differences[0]];
        }
        var longer=a.Length>b.Length?a:b;var shorter=a.Length>b.Length?b:a;
        var at=0;while(at<shorter.Length && longer[at]==shorter[at])at++;
        return longer.AsSpan(at+1).SequenceEqual(shorter.AsSpan(at));
    }
    bool Valid(string wrong,string right) => Letters(wrong) && Letters(right) && SingleEdit(wrong,right) &&
        !English.Value.Contains(wrong) && !English.Value.Contains(right) &&
        !_lexicon.IsKnownReading(_roman.ConvertLenient(wrong,true)) && _lexicon.IsKnownReading(_roman.ConvertLenient(right,true));
    List<Entry> Entries()
    {
        if (_entries!=null)return _entries;
        try
        {
            if(new FileInfo(path) is { Exists:true, Length:<=65536 })
            {
                var model=JsonSerializer.Deserialize<Model>(File.ReadAllText(path));
                if(model is { Version:1, Entries:not null } && model.Entries.Length<=256)
                    return _entries=model.Entries.Where(e=>e is not null && e.Wrong!=null && e.Right!=null && e.Support is >=1 and <=1000 && Valid(e.Wrong,e.Right)).ToList();
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { }
        return _entries=[];
    }
    public RomajiFix? Find(string raw)
    {
        if(!enabled() || !Letters(raw))return null;
        lock(_gate)
        {
            var entries=Entries();
            var matches=entries.Where(e=>e.Wrong==raw).ToArray();
            if(matches.Length>1)return null;
            if(matches.Length==1)
            {
                var right=matches[0].Right;
                return new RomajiFix(raw,right,_roman.ConvertLenient(right,true));
            }
            if(_generalized.TryGetValue(raw,out var cached))return cached;
            var fix=Generalize(raw,entries);
            if(_generalized.Count>=128)_generalized.Clear();
            _generalized[raw]=fix;
            return fix;
        }
    }
    public async Task ObserveAsync(string wrong,string right,string left="",string following="",CancellationToken cancellation=default)
    {
        if(!enabled() || cancellation.IsCancellationRequested || !Valid(wrong,right))return;
        var wrongKana=_roman.ConvertLenient(wrong,true);var rightKana=_roman.ConvertLenient(right,true);
        var request=new ContextRequest(wrongKana.Any(char.IsAsciiLetterLower)?rightKana:"",left[^Math.Min(64,left.Length)..],following[..Math.Min(32,following.Length)], [wrongKana,rightKana], RomanTypo:wrongKana.Any(char.IsAsciiLetterLower));
        if(!ZenzCandidateRules.AllowsCandidate(request,rightKana))return;
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(1500);
            var selected=await evaluator.EvaluateAsync(request,timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            if(selected!=rightKana || !enabled() || timeout.IsCancellationRequested)return;
            lock(_gate)
            {
                if(!enabled() || timeout.IsCancellationRequested)return;
                var entries=Entries();var previous=entries.FirstOrDefault(e=>e.Wrong==wrong && e.Right==right);
                if(previous==null && entries.Count>=256)return;
                var next=entries.Where(e=>e!=previous).Append(new Entry(wrong,right,Math.Min(1000,(previous?.Support??0)+1))).ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    File.WriteAllText(temporary,JsonSerializer.Serialize(new Model(1,next)));
                    if(!enabled() || timeout.IsCancellationRequested)return;
                    File.Move(temporary,path,true);_entries=next.ToList();_generalized.Clear();
                }
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
        }
        catch(Exception e) { Meltype.Diagnostics.Log.Warn("個人誤字の学習を採用できませんでした: " + e.GetType().Name); }
    }
}
