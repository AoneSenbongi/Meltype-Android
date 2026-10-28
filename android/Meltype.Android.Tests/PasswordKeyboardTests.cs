using Meltype.AndroidCore;

internal static class PasswordKeyboardTests
{
    public static void Run()
    {
        foreach (var type in new[] { 0x81, 0x91, 0xe1, 0x12, 0x80081, 0x100012 })
            if (!PasswordKeyboardPolicy.RequiresSoftwareKeyboard(type))
                throw new Exception($"Password/PIN must show keyboard: {type:x}");
        foreach (var type in new[] { 0, 1, 2, 3, 4, 0x11, 0x14, 0x24, 0x21, 0x31, 0x71, 0xd1, 0x2002, 0x82, 0x92, 0xe2 })
            if (PasswordKeyboardPolicy.RequiresSoftwareKeyboard(type))
                throw new Exception($"Ordinary editor must follow hardware-keyboard setting: {type:x}");
        Console.WriteLine("PASS: password/PIN keyboard policy distinguishes input classes, URI/date and flags");
    }
}
