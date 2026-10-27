// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using System.Text.Json;
using System.Security.Cryptography;
internal static class AndroidUpdateTests
{
    public static void Run()
    {
        var bytes=new byte[]{1,2,3};var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string Release(string name,string digest,string url="https://github.com/AoneSenbongi/Meltype-Android/releases/download/android-1.1.1/Meltype-Android-1.1.1.apk") => JsonSerializer.Serialize(new {tag_name="android-1.1.1",draft=false,prerelease=false,assets=new[]{new{name,browser_download_url=url,size=3,digest}}});
        var normal=ReleaseUpdate.Find(Release("Meltype-Android-1.1.1.apk","sha256:"+hash),"jp.aonesenbongi.meltype","1.1.0");
        if(normal?.Version!="1.1.1")throw new Exception("Normal package update not selected");
        if(ReleaseUpdate.Find(Release("Meltype-Android-learning-1.1.1.apk","sha256:"+hash,"https://github.com/AoneSenbongi/Meltype-Android/releases/download/android-1.1.1/Meltype-Android-learning-1.1.1.apk"),"jp.aonesenbongi.meltype.learning","1.1.0-personal-local.20261023")?.Version!="1.1.1")throw new Exception("Matching learning update not selected");
        if(ReleaseUpdate.Find(Release("Meltype-Android-1.1.1.apk","sha256:"+hash),"jp.aonesenbongi.meltype.learning","1.1.0")!=null)throw new Exception("Learning package offered normal APK");
        if(ReleaseUpdate.Find(Release("Meltype-Android-1.1.1.apk","sha256:"+hash),"jp.aonesenbongi.meltype","1.1.1")!=null)throw new Exception("Same version offered as update");
        if(ReleaseUpdate.Find(Release("Meltype-Android-1.1.1.apk","sha256:"+hash,"https://example.com/evil.apk"),"jp.aonesenbongi.meltype","1.1.0")!=null)throw new Exception("Untrusted download URL accepted");
        if(ReleaseUpdate.Find(Release("Meltype-Android-1.1.1.apk",""),"jp.aonesenbongi.meltype","1.1.0")!=null)throw new Exception("Missing digest accepted");
        if(!normal!.Matches(bytes) || normal.Matches(new byte[]{1,2,4}))throw new Exception("APK digest verification failed");
        Console.WriteLine("PASS: update package separation, version ordering, fixed download origin and digest validation");
    }
}
