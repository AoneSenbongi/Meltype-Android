// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
internal static class KeyboardInteractionTests
{
    public static void Run()
    {
        if(KeyboardSymbols.For('q')!="1" || KeyboardSymbols.For('p')!="0" || KeyboardSymbols.For('k')!="(" || KeyboardSymbols.For('c')!="「」" || KeyboardSymbols.For('-')!="_") throw new Exception("Long press mapping failed");
        var taps = 0; var holds = 0;
        var gesture = new SymbolPress(() => taps++, () => holds++);
        gesture.Down(); gesture.Up(); gesture.Hold();
        if(taps != 1 || holds != 0) throw new Exception("Tap/late timer produced symbol");
        gesture.Down(); gesture.Hold(); gesture.Hold(); gesture.Up();
        if(taps != 1 || holds != 1) throw new Exception("Long press also produced letter or duplicate");
        gesture.Down(); gesture.Cancel(); gesture.Up(); gesture.Hold();
        if(taps != 1 || holds != 1) throw new Exception("Cancelled gesture produced text");
        if(CursorMovement.Target("a😀b",3,3,true)!=1 || CursorMovement.Target("a😀b",1,1,false)!=3
            || CursorMovement.Target("a😀b",1,3,true)!=1 || CursorMovement.Target("a😀b",1,3,false)!=3
            || CursorMovement.Target("",0,0,true)!=0) throw new Exception("Cursor split Unicode or selection");
        var now = DateTimeOffset.UtcNow;
        var clips = new ClipboardHistory(()=>now);
        clips.Add("first"); clips.Add("second"); clips.Add("first"); clips.Add("secret",true);
        if(clips.Items.Count!=2 || clips.Items[0]!="first") throw new Exception("Clipboard dedup or sensitive filtering failed");
        for(var i=0;i<12;i++) clips.Add("item"+i);
        if(clips.Items.Count!=10) throw new Exception("Clipboard bound failed");
        now=now.AddHours(1);
        if(clips.Items.Count!=0) throw new Exception("Clipboard expired items retained");
        clips.Add("x"); clips.Clear(); if(clips.Items.Count!=0) throw new Exception("Clipboard clear failed");
        var folder = Path.Combine(Path.GetTempPath(), "meltype-punctuation-"+Guid.NewGuid());
        try
        {
            var store = new TypoTrainingStore(folder,()=>now);
            store.AppendPunctuation("文末", "．"); if(store.Count!=0) throw new Exception("Captured without opt-in");
            store.Start(); store.AppendPunctuation("文章です", "．"); store.AppendPunctuation("途中", "，");
            store.AppendPunctuation("", "．"); store.AppendPunctuation("secret", "!"); store.AppendPunctuation("English only", ".");
            if(store.Count!=2) throw new Exception("Punctuation capture filtering failed");
            store.Stop(); store.AppendPunctuation("停止中", "．");
            if(store.Count!=2 || !File.ReadAllText(Path.Combine(folder,"events.jsonl")).Contains("punctuation")) throw new Exception("Punctuation stop or kind failed");
        } finally { if(Directory.Exists(folder)) Directory.Delete(folder,true); }
        Console.WriteLine("PASS: long press without extra letter, cancellation, Unicode cursor, clipboard bounds/privacy/expiry");
    }
}
