// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Input;

internal enum AltTap
{
    None,
    /// <summary>Alt を単独で押した (離すまでに他のキーを押さなければ単独押しになる)。</summary>
    Pressed,
    /// <summary>Alt を単独で押して離した。</summary>
    Tapped,
}

/// <summary>
/// 左右の Alt の単独押しを見分ける (#85: 左 Alt で英数、右 Alt で日本語)。
/// Alt を押している間に他のキーを押した (Alt + Tab、Alt + ` など)・クリックした・他の修飾キーと一緒に押したときは単独押しにしない。
/// 他のソフトが送った Alt (Injected) も切り替えに使わない。
/// </summary>
internal sealed class AltTapDetector
{
    private readonly HashSet<int> _held = [];
    private int _alt;

    /// <param name="othersHeld">Alt を押したときに Ctrl・Shift・Win を押していたか。</param>
    public AltTap OnKey(KeyEvent e, bool othersHeld = false)
    {
        if (e.Vk is not (VirtualKeys.LMenu or VirtualKeys.RMenu))
        {
            if (e.IsDown) _alt = 0;
            return AltTap.None;
        }
        if (e.IsDown)
        {
            // 押しっぱなしの繰り返しは、最初の押下の続き。
            if (!_held.Add(e.Vk)) return AltTap.None;
            _alt = _held.Count == 1 && !othersHeld && !e.Injected ? e.Vk : 0;
            return _alt != 0 ? AltTap.Pressed : AltTap.None;
        }
        _held.Remove(e.Vk);
        var tapped = _alt == e.Vk && !e.Injected;
        _alt = 0;
        return tapped ? AltTap.Tapped : AltTap.None;
    }

    /// <summary>マウスのボタンを押した (Alt + クリックは単独押しにしない)。</summary>
    public void Cancel() => _alt = 0;
}
