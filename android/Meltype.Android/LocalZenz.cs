// SPDX-License-Identifier: GPL-3.0-or-later
using Android.Content;
using Meltype.AndroidCore;

namespace Meltype.Mobile;

internal static class LocalZenz
{
    private const string Model = "zenz-v3.2-small-Q5_K_M.gguf";
    private static string ModelPath(Context context) => Path.Combine(context.FilesDir!.AbsolutePath, "zenz", Model);
    public static bool Available(Context context)
    {
        try { return File.Exists(ModelPath(context)) || context.Assets!.List("zenz")?.Contains(Model) == true; }
        catch (Java.IO.IOException) { return false; }
    }
    public static NativeZenzEvaluator Create(Context context)
    {
        var app = context.ApplicationContext!;
        var path = ModelPath(app);
        return new NativeZenzEvaluator(path, () =>
        {
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var source = app.Assets!.Open("zenz/" + Model))
                using (var target = File.Create(temporary)) source.CopyTo(target);
                File.Move(temporary, path, overwrite: true);
            }
            catch (Java.IO.IOException error) { throw new IOException("Local model asset is unavailable", error); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        });
    }
}
