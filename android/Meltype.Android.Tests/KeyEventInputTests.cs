using Meltype.AndroidCore;

internal static class KeyEventInputTests
{
    public static void Run()
    {
        static KeyInput? Map(int code,int unicode=0,bool shift=false,bool shortcut=false,bool editor=true,bool ready=true,bool composing=false)
            => KeyEventInput.Map(code,unicode,shift,shortcut,editor,ready,composing);
        if(Map(42,'n')?.Character!='n'||Map(29,'A',true)?.Character!='A')throw new Exception("Printable keys lost");
        if(Map(62,shift:true) is not {Key:0x20,Shift:true}||Map(66)?.Key!=0x0D||Map(160)?.Key!=0x0D||Map(67)?.Key!=0x08)throw new Exception("Editing keys lost");
        if(Map(21)?.Key!=0x25||Map(22,shift:true) is not {Key:0x27,Shift:true})throw new Exception("Cursor modifiers lost");
        if(Map(19) is not null||Map(19,composing:true)?.Key!=0x26||Map(111,composing:true)?.Key!=0x1B)throw new Exception("Composition-only keys leak into normal editor");
        foreach(var code in new[]{29,62,66,67,21})
            if(Map(code,'a',editor:false) is not null||Map(code,'a',ready:false) is not null||Map(code,'a',shortcut:true) is not null)throw new Exception("Restricted editor or shortcut was intercepted");
        if(Map(29,0x3042) is not null||Map(29,unchecked((int)0x80000041)) is not null||Map(211) is not null||Map(140) is not null)throw new Exception("Unsupported key received dedicated behavior");
        Console.WriteLine("PASS: shared key input, editing, modifiers, restricted editors and ordinary shortcut passthrough");
    }
}
