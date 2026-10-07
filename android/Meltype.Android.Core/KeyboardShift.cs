// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

public sealed class KeyboardShift
{
    public bool Active { get; private set; }
    public bool Locked { get; private set; }
    private long _lastTap = long.MinValue;
    public void Toggle() => Toggle(Environment.TickCount64);
    public void Toggle(long timestampMilliseconds)
    {
        if (Locked) Reset();
        else if (Active && timestampMilliseconds >= _lastTap && timestampMilliseconds - _lastTap <= 300) Locked = true;
        else Active = !Active;
        _lastTap = timestampMilliseconds;
    }
    public void Consume() { if (!Locked) { Active = false; _lastTap = long.MinValue; } }
    public void Reset() { Active = false; Locked = false; _lastTap = long.MinValue; }
    public char Apply(char character)
    {
        var result = Active ? char.ToUpperInvariant(character) : character;
        Consume(); return result;
    }
}
