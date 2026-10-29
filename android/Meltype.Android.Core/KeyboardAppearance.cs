// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

public enum KeyboardTheme { System, Light, Dark }

public static class KeyboardAppearance
{
    public static bool IsDark(KeyboardTheme theme, bool systemDark) => theme switch
    {
        KeyboardTheme.Light => false,
        KeyboardTheme.Dark => true,
        _ => systemDark
    };
}
