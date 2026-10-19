// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
static class InputPersonalizationTests
{
    public static void Run()
    {
        foreach(var input in new[] { 0, 2, 3, 4, 0x81, 0x91, 0xe1, 0x21, 0x11, 0xd1 })
            if(InputPersonalizationPolicy.Allows(input,0)) throw new Exception("Personalized input allowed in excluded field");
        if(!InputPersonalizationPolicy.Allows(1,0) || InputPersonalizationPolicy.Allows(1,0x1000000))
            throw new Exception("Normal/learning-prohibited field policy changed");
        Console.WriteLine("PASS: input personalization policy; typo-acquisition types absent");
    }
}
