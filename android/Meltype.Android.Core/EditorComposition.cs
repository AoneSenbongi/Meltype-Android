// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

public static class EditorComposition
{
    public static bool ShouldReset(bool preeditActive, int selectionStart, int selectionEnd, int composingStart, int composingEnd) =>
        preeditActive && (composingStart < 0 || composingEnd < 0) ||
        composingStart >= 0 && (selectionStart < composingStart || selectionEnd < composingStart ||
            selectionStart > composingEnd || selectionEnd > composingEnd);
}
