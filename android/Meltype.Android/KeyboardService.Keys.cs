// SPDX-License-Identifier: GPL-3.0-or-later
using Android.Views;
using Meltype.AndroidCore;

namespace Meltype.Mobile;

public sealed partial class KeyboardService
{
    private readonly HashSet<Keycode> _consumedKeys=[];
    private bool _keyTextEditor;

    public override bool OnKeyDown(Keycode keyCode,global::Android.Views.KeyEvent? e)
    {
        if(e==null)return base.OnKeyDown(keyCode,e);
        var input=KeyEventInput.Map((int)keyCode,e.UnicodeChar,e.IsShiftPressed,
            e.IsCtrlPressed||e.IsAltPressed||e.IsMetaPressed,
            _keyTextEditor&&!_restricted&&CurrentInputConnection!=null,_ready,
            !string.IsNullOrEmpty(_shownView?.Text));
        if(input is not {} key)return base.OnKeyDown(keyCode,e);
        _consumedKeys.Add(keyCode);
        _pendingLetterTouch=null;
        if(key.Key==0)Input(key.Character);
        else if(key.Key==0x20)InputSpace(key.Shift);
        else Special(key.Key,key.Shift);
        return true;
    }
    public override bool OnKeyUp(Keycode keyCode,global::Android.Views.KeyEvent? e)
        =>_consumedKeys.Remove(keyCode)||base.OnKeyUp(keyCode,e);
}
