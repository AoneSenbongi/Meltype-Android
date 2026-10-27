// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
namespace Meltype.AndroidCore;

public sealed record ReleaseUpdate(string Version, string Url, long Size, string Sha256)
{
    public const string Api = "https://api.github.com/repos/AoneSenbongi/Meltype-Android/releases/latest";
    public bool Matches(ReadOnlySpan<byte> bytes) => bytes.Length == Size && Convert.ToHexString(SHA256.HashData(bytes)).Equals(Sha256,StringComparison.OrdinalIgnoreCase);
    public static ReleaseUpdate? Find(string json,string package,string current)
    {
        if(package is not ("jp.aonesenbongi.meltype" or "jp.aonesenbongi.meltype.learning"))return null;
        using var document=JsonDocument.Parse(json);var release=document.RootElement;
        if(release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())return null;
        var tag=release.GetProperty("tag_name").GetString()??"";
        if(!tag.StartsWith("android-",StringComparison.Ordinal))return null;
        var version=tag[8..];
        if(!System.Version.TryParse(version,out var next) || !System.Version.TryParse(current.Split('-')[0],out var installed) || next<=installed)return null;
        var name=package.EndsWith(".learning",StringComparison.Ordinal)?$"Meltype-Android-learning-{version}.apk":$"Meltype-Android-{version}.apk";
        foreach(var asset in release.GetProperty("assets").EnumerateArray())
        {
            if(asset.GetProperty("name").GetString()!=name)continue;
            var url=asset.GetProperty("browser_download_url").GetString();var digest=asset.GetProperty("digest").GetString()??"";var size=asset.GetProperty("size").GetInt64();
            if(url!=$"https://github.com/AoneSenbongi/Meltype-Android/releases/download/{tag}/{name}" || size is <=0 or >536870912 ||
                !digest.StartsWith("sha256:",StringComparison.Ordinal) || digest.Length!=71 || !digest[7..].All(Uri.IsHexDigit))return null;
            return new(version,url,size,digest[7..]);
        }
        return null;
    }
}
