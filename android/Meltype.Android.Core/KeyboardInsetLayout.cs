// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

public static class KeyboardInsetLayout
{
    public static int BottomPadding(int systemBottomInset, int windowBottom, int inputBottom) =>
        Math.Max(0, systemBottomInset - Math.Max(0, windowBottom - inputBottom));
}
