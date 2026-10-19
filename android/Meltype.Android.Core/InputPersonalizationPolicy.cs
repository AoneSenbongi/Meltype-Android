// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

public static class InputPersonalizationPolicy
{
    public static bool Allows(int inputType, int imeOptions)
    {
        if ((imeOptions & 0x1000000) != 0 || (inputType & 15) != 1) return false;
        return (inputType & 0xff0) is not (0x10 or 0x20 or 0x80 or 0x90 or 0xd0 or 0xe0);
    }
}
