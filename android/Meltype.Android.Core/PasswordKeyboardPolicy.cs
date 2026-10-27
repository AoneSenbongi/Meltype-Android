// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

public static class PasswordKeyboardPolicy
{
    public static bool RequiresSoftwareKeyboard(int inputType) =>
        (inputType & 15) switch
        {
            1 => (inputType & 0xff0) is 0x80 or 0x90 or 0xe0,
            2 => (inputType & 0xff0) == 0x10,
            _ => false,
        };
}
