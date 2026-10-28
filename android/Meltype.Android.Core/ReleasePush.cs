// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
namespace Meltype.AndroidCore;

public static class ReleasePush
{
    public const string Topic = "meltype-android-releases";
    public static string? NewVersion(IReadOnlyDictionary<string,string> data, string package, string current, string? notified)
    {
        if (package != "jp.aonesenbongi.meltype" ||
            !data.TryGetValue("type", out var type) || type != "meltype.release" ||
            !data.TryGetValue("version", out var version) || version.Length > 32 ||
            !Regex.IsMatch(version, @"\A[0-9]+\.[0-9]+\.[0-9]+\z") ||
            !Version.TryParse(version, out var next) ||
            !Version.TryParse(current.Split('-')[0], out var installed) || next <= installed) return null;
        return Version.TryParse(notified, out var previous) && next <= previous ? null : version;
    }
}
