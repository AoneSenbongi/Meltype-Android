// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
internal static class KeyboardInteractionTests
{
    public static void Run()
    {
        var letters = "qwertyuiopasdfghjkl-zxcvbnm";
        string[] symbols = ["1","2","3","4","5","6","7","8","9","0",
            "@","#","¥","%","&","-","+","()","/","「」","_","'",":",";","\"","!","?"];
        for (var i = 0; i < letters.Length; i++)
            if (KeyboardSymbols.For(letters[i]) != symbols[i])
                throw new Exception($"Long press and symbol layout differ at {letters[i]}: expected {symbols[i]}, got {KeyboardSymbols.For(letters[i])}");
        var taps = 0; var holds = 0;
        var order = "";
        var sequence = new KeyPressOrder();
        var first = new SymbolPress(() => order += "a", () => order += "1", sequence);
        var second = new SymbolPress(() => order += "b", () => order += "2", sequence);
        first.Down(); second.Down(); second.Up(); first.Up();
        if (order != "ab") throw new Exception("Overlapping keys changed press order: " + order);
        first.Hold(); second.Hold();
        if (order != "ab") throw new Exception("Old timer produced extra symbol");
        order = ""; first.Down(); sequence.BeforePress(); order += "delete"; first.Up();
        if (order != "adelete") throw new Exception("Control overtook preceding letter");
        order = ""; first.Down(); first.Hold(); second.Down(); second.Up(); first.Up();
        if (order != "1b") throw new Exception("Held symbol produced extra letter");
        order = ""; first.Down(); first.Cancel(); second.Down(); second.Up();
        if (order != "b") throw new Exception("Cancelled press survived next key");
        order = ""; first.Down(); second.Down(); first.Cancel(); sequence.BeforePress(); second.Up();
        if (order != "ab") throw new Exception("Older cancel cleared newer pending key");
        if (!KeyTouchBounds.Contains(-1, 55, 36, 54, 8) ||
            KeyTouchBounds.Contains(-9, 20, 36, 54, 8) ||
            KeyTouchBounds.Contains(20, 62, 36, 54, 8))
            throw new Exception("Touch slop lost small movement or accepted large movement");
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
        var offer = new ClipboardSuggestion();
        offer.Open(101, "copied");
        if (offer.Text != "copied") throw new Exception("First opening must offer clipboard");
        offer.Dismiss();
        offer.Open(101, "copied");
        if (offer.Text != null) throw new Exception("Reopen must not offer consumed copy");
        offer.Open(102, "copied");
        if (offer.Text != "copied") throw new Exception("Recopying same text must offer again");
        offer.Dismiss(); // Starting input, confirming, pasting or hiding consumes the visible offer.
        if (offer.Text != null) throw new Exception("Input must dismiss offer");
        var restarted = new ClipboardSuggestion(offer.LastTimestamp);
        restarted.Open(102, "copied");
        if (restarted.Text != null) throw new Exception("Restart must not reoffer consumed copy");
        restarted.Open(103, null);
        restarted.Open(103, "private");
        if (restarted.Text != null) throw new Exception("Ineligible first editor must consume copy");
        clips.Add("copied");
        if (!clips.Items.Contains("copied")) throw new Exception("Dismiss must retain clipboard history");
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
