// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

public static class NumericKeyboard
{
    public static string[]? Rows(int inputType) => (inputType & 15) switch
    {
        2 => ["123", "456", "789", $"{((inputType & 0x1000) != 0 ? '-' : ' ')}0{((inputType & 0x2000) != 0 ? '.' : ' ')}"],
        3 => ["123", "456", "789", "*0#"],
        _ => null
    };
}
