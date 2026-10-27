// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

public readonly record struct KeyInput(int Key=0,char Character='\0',bool Shift=false);
public static class KeyEventInput
{
    public static KeyInput? Map(int code,int unicode,bool shift,bool shortcut,bool editor,bool ready,bool composing)
    {
        if(!editor||!ready||shortcut)return null;
        int key=code switch {62=>0x20,66 or 160=>0x0D,67=>0x08,21=>0x25,22=>0x27,
            19 when composing=>0x26,20 when composing=>0x28,111 when composing=>0x1B,_=>0};
        if(key!=0)return new(Key:key,Shift:shift);
        return unicode is >=0x21 and <=0x7E?new(Character:(char)unicode):null;
    }
}
