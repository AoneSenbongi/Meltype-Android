// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
internal static class KeyboardInteractionTests
{
    public static void Run()
    {
        foreach (var (inset, windowBottom, inputBottom, expected) in new[]
        {
            (48, 800, 800, 48), // IME buttons overlap the input view.
            (48, 800, 752, 0),  // OS already reserves its navigation bar.
            (48, 800, 776, 24), // Only part of the bar overlaps.
            (24, 800, 752, 0),
            (0, 800, 800, 0),
            (126, 2400, 2400, 126), // Density-scaled caption bar, not a fixed dp height.
            (63, 2400, 2337, 0)
        })
        {
            for (var repeat = 0; repeat < 3; repeat++)
                if (KeyboardInsetLayout.BottomPadding(inset, windowBottom, inputBottom) != expected)
                    throw new Exception("Navigation inset overlaps or adds an extra gap");
        }
        Console.WriteLine("PASS: navigation overlay, separately reserved bar, partial overlap, density and repeated layout");
        var modeTaps = 0; var modeMenus = 0;
        var modeGesture = new SymbolPress(() => modeTaps++, () => modeMenus++, longPressEnabled: false);
        modeGesture.Down(); modeGesture.Hold();
        if (modeMenus != 0) throw new Exception("Mode menu opened on long press");
        modeGesture.Up();
        if (modeTaps != 1) throw new Exception("Mode tap was lost");
        modeGesture.Down(); modeGesture.Move(0, -24, 24); modeGesture.Up(); modeGesture.Hold();
        if (modeMenus != 1 || modeTaps != 1) throw new Exception("Mode flick must open once without changing mode");
        modeGesture.Down(); modeGesture.Cancel(); modeGesture.Move(0, -40, 24); modeGesture.Up();
        if (modeMenus != 1 || modeTaps != 1) throw new Exception("Cancelled mode flick fired");
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
        gesture.Down(); gesture.Move(2, -23, 24); gesture.Up();
        if (taps != 2 || holds != 1) throw new Exception("Small upward movement became a flick");
        gesture.Down(); gesture.Move(2, -24, 24); gesture.Move(0, -60, 24); gesture.Hold(); gesture.Up();
        if (taps != 2 || holds != 2) throw new Exception("Upward flick duplicated symbol or typed a letter");
        foreach (var (dx, dy) in new[] { (24f, 0f), (0f, 24f), (24f, -24f), (30f, -24f) })
        {
            gesture.Down();
            if (gesture.Move(dx, dy, 24)) throw new Exception("Side/down movement accepted as upward");
            gesture.Cancel(); gesture.Up();
        }
        gesture.Down(); gesture.Move(0, -30, 0); gesture.Up();
        if (taps != 3 || holds != 2) throw new Exception("A key without a visible hint flicked");
        gesture.Down(); gesture.Cancel(); gesture.Move(0, -40, 24); gesture.Hold(); gesture.Up();
        if (taps != 3 || holds != 2) throw new Exception("Cancelled flick produced text");
        order = ""; first.Down(); second.Down(); first.Move(0, -40, 24); first.Up(); second.Up();
        if (order != "ab") throw new Exception("Old flick overtook overlapping keys: " + order);
        order = ""; first.Down(); first.Move(0, -24, 24); second.Down(); second.Up(); first.Up();
        if (order != "1b") throw new Exception("Flick before overlapping key changed input order: " + order);
        foreach (var letter in letters)
        {
            var output = "";
            var symbol = new SymbolPress(() => output += letter, () => output += KeyboardSymbols.For(letter));
            symbol.Down(); symbol.Move(0, -24, 24); symbol.Hold(); symbol.Up();
            if (output != KeyboardSymbols.For(letter)) throw new Exception("Flick mapping differs: " + letter);
        }
        Console.WriteLine("PASS: upward flick threshold/direction, all 27 symbols, cancellation, late timers and overlapping input");
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
        offer.Open(101, "copied");
        if (offer.Text != "copied") throw new Exception("Repeated input-view start must keep the new clipboard offer");
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
        if (restarted.Text != "private") throw new Exception("An ineligible editor must not consume a copy before an eligible editor can offer it");
        clips.Add("copied");
        if (!clips.Items.Contains("copied")) throw new Exception("Dismiss must retain clipboard history");
        Console.WriteLine("PASS: long press without extra letter, cancellation, Unicode cursor, clipboard bounds/privacy/expiry");
    }
}
