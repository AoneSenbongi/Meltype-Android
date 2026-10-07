// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

public sealed class KeyboardShift
{
    public bool Active { get; private set; }
    public bool Locked { get; private set; }
    public void Toggle()
    {
        if (Locked) Reset();
        else if (Active) Locked = true;
        else Active = true;
    }
    public void Consume() { if (!Locked) Active = false; }
    public void Reset() { Active = false; Locked = false; }
    public char Apply(char character)
    {
        var result = Active ? char.ToUpperInvariant(character) : character;
        Consume(); return result;
    }
}
