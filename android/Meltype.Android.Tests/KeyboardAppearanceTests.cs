// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;

static class KeyboardAppearanceTests
{
    public static void Run()
    {
        foreach (var systemDark in new[] { false, true })
        {
            if (KeyboardAppearance.IsDark(KeyboardTheme.Light, systemDark)) throw new Exception("Light must override OS dark");
            if (!KeyboardAppearance.IsDark(KeyboardTheme.Dark, systemDark)) throw new Exception("Dark must override OS light");
            if (KeyboardAppearance.IsDark(KeyboardTheme.System, systemDark) != systemDark) throw new Exception("System must follow OS");
            if (KeyboardAppearance.IsDark((KeyboardTheme)99, systemDark) != systemDark) throw new Exception("Unknown mode must follow OS");
        }
        Console.WriteLine("PASS: manual light/dark override both OS modes; system and unknown mode follow OS");
    }
}
