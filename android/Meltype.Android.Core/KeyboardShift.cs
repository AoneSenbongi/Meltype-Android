// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

public sealed class KeyboardShift
{
    public bool Active { get; private set; }
    public void Toggle() => Active = !Active;
    public void Reset() => Active = false;
    public char Apply(char character)
    {
        var result = Active ? char.ToUpperInvariant(character) : character;
        Reset(); return result;
    }
}
