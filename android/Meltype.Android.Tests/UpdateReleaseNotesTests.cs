// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using System.Text.Json;

static class UpdateReleaseNotesTests
{
    public static void Run()
    {
        string Release(object? body) => JsonSerializer.Serialize(new {
            draft=false, prerelease=false, tag_name="android-1.2.2", body,
            assets=new[] {new {name="Meltype-Android-1.2.2.apk",size=3,
                digest="sha256:"+new string('a',64),
                browser_download_url="https://github.com/AoneSenbongi/Meltype-Android/releases/download/android-1.2.2/Meltype-Android-1.2.2.apk"}}
        });
        ReleaseUpdate Find(object? body) => ReleaseUpdate.Find(Release(body),"jp.aonesenbongi.meltype","1.2.1")!;
        const string notes="## 変更点\n- テーマを追加\n- 枠線を修正\n\n既存の入力処理は維持します。";
        if (Find(notes).Notes!=notes) throw new Exception("Release description lost content or conditions");
        foreach (var empty in new object?[] {null,123,new {other="ignored"}})
            if (Find(empty).Notes!="") throw new Exception("Missing/non-text description must not block updating");
        var bounded=Find(new string('x',40000)).Notes;
        if (bounded.Length>32800 || !bounded.EndsWith("（以降は省略）")) throw new Exception("Long notes need a bounded and explicit truncation");
        Console.WriteLine("PASS: update notes preserve changes and conditions, tolerate missing body and bound long descriptions");
    }
}
