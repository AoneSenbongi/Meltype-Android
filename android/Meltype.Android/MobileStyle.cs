// SPDX-License-Identifier: GPL-3.0-or-later
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;

namespace Meltype.Mobile;

// 画面はコードで組んでいるので、色とキーの見た目をまとめる。
internal static class MobileStyle
{
    internal static Color PageBackground => Color.White;
    internal static Color KeyboardBackground => Color.Rgb(210, 213, 219);
    internal static Color CandidateBackground => Color.White;
    internal static Color KeyBackground => Color.White;
    internal static Color FunctionKeyBackground => Color.Rgb(234, 235, 237);
    internal static Color KeyStroke => Color.Rgb(215, 216, 218);
    internal static Color FieldBackground => Color.White;
    internal static Color OutlineStroke => Color.Rgb(210, 210, 210);
    internal static Color Ink => Color.Rgb(32, 32, 32);
    internal static Color Muted => Color.Rgb(93, 111, 124);

    internal static int Dp(Context context, float value) => (int)(value * context.Resources!.DisplayMetrics!.Density + .5f);

    internal static GradientDrawable Outline(Context context)
    {
        var shape = new GradientDrawable();
        shape.SetColor(FieldBackground);
        shape.SetStroke(Dp(context, 1), OutlineStroke);
        return shape;
    }

    internal static void Button(Button button)
    {
        var context = button.Context!;
        button.SetAllCaps(false);
        button.SetTextColor(Ink);
        button.Background = Outline(context);
        button.BackgroundTintList = null;
        button.SetPadding(Dp(context, 8), 0, Dp(context, 8), 0);
        button.SetMinWidth(0);
        button.SetMinimumWidth(0);
        button.SetMinHeight(0);
        button.SetMinimumHeight(0);
        button.StateListAnimator = null;
        button.Elevation = 0;
        button.TranslationZ = 0;
    }

    internal static void KeyboardKey(Button button, bool function = false)
    {
        button.SetAllCaps(false);
        button.SetTextColor(Ink);
        button.SetTypeface(Typeface.Default, TypefaceStyle.Normal);
        var shape = new GradientDrawable();
        shape.SetColor(function ? FunctionKeyBackground : KeyBackground);
        shape.SetStroke(Dp(button.Context!, 1), KeyStroke);
        button.Background = shape;
        button.BackgroundTintList = null;
        button.SetPadding(0, 0, 0, 0);
        button.SetMinWidth(0);
        button.SetMinimumWidth(0);
        button.SetMinHeight(0);
        button.SetMinimumHeight(0);
        button.StateListAnimator = null;
        button.Elevation = 0;
        button.TranslationZ = 0;
    }
}
