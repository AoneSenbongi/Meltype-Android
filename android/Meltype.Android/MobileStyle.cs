// SPDX-License-Identifier: GPL-3.0-or-later
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;

namespace Meltype.Mobile;

// 画面はコードで組んでいるので、端末のダークテーマに合わせて色だけを切り替える。
// Apply を呼んでから View を作る。IME は構成変更のたびに Apply し直して作り直す。
internal static class MobileStyle
{
    internal static bool Dark { get; private set; }

    internal static Color PageBackground => Dark ? Color.Rgb(22, 24, 27) : Color.White;
    internal static Color KeyboardBackground => Dark ? Color.Rgb(27, 29, 33) : Color.Rgb(210, 213, 219);
    internal static Color CandidateBackground => Dark ? Color.Rgb(27, 29, 33) : Color.White;
    internal static Color KeyBackground => Dark ? Color.Rgb(43, 46, 51) : Color.White;
    internal static Color FunctionKeyBackground => Dark ? Color.Rgb(35, 38, 42) : Color.Rgb(234, 235, 237);
    internal static Color KeyStroke => Dark ? Color.Rgb(58, 62, 68) : Color.Rgb(215, 216, 218);
    internal static Color FieldBackground => Dark ? Color.Rgb(34, 37, 42) : Color.White;
    internal static Color OutlineStroke => Dark ? Color.Rgb(60, 64, 70) : Color.Rgb(210, 210, 210);
    internal static Color Ink => Dark ? Color.Rgb(232, 234, 237) : Color.Rgb(32, 32, 32);
    internal static Color Muted => Dark ? Color.Rgb(168, 176, 185) : Color.Rgb(93, 111, 124);

    internal static bool IsDark(Context context) =>
        (context.Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;

    internal static void Apply(Context context) => Dark = IsDark(context);

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
