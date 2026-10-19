// SPDX-License-Identifier: GPL-3.0-or-later
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;

namespace Meltype.Mobile;

internal static class MobileStyle
{
    internal static readonly Color KeyboardBackground = Color.Rgb(210, 213, 219);
    internal static readonly Color Ink = Color.Rgb(32, 32, 32);
    internal static readonly Color Muted = Color.Rgb(93, 111, 124);
    internal static int Dp(Context context, float value) => (int)(value * context.Resources!.DisplayMetrics!.Density + .5f);
    internal static GradientDrawable Outline(Context context)
    {
        var shape = new GradientDrawable(); shape.SetColor(Color.White);
        shape.SetStroke(Dp(context, 1), Color.Rgb(210, 210, 210)); return shape;
    }
    internal static void Button(Button button)
    {
        var context = button.Context!;
        button.SetAllCaps(false); button.SetTextColor(Ink);
        button.Background = Outline(context);
        button.BackgroundTintList = null;
        button.SetPadding(Dp(context, 8), 0, Dp(context, 8), 0);
        button.SetMinWidth(0); button.SetMinimumWidth(0);
        button.SetMinHeight(0); button.SetMinimumHeight(0);
        button.StateListAnimator = null;
        button.Elevation = 0; button.TranslationZ = 0;
    }
    internal static void KeyboardKey(Button button, bool function = false)
    {
        button.SetAllCaps(false); button.SetTextColor(Color.Rgb(32, 32, 32));
        button.SetTypeface(Typeface.Default, TypefaceStyle.Normal);
        var shape = new GradientDrawable();
        shape.SetColor(function ? Color.Rgb(234, 235, 237) : Color.White); shape.SetStroke(1, Color.Rgb(215, 216, 218));
        button.Background = shape; button.BackgroundTintList = null;
        button.SetPadding(0, 0, 0, 0);
        button.SetMinWidth(0); button.SetMinimumWidth(0);
        button.SetMinHeight(0); button.SetMinimumHeight(0);
        button.StateListAnimator = null;
        button.Elevation = 0; button.TranslationZ = 0;
    }
}
