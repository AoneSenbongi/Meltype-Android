// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
static class ReleasePushTests
{
    public static void Run()
    {
        Dictionary<string,string> Data(string v) => new() { ["type"]="meltype.release", ["version"]=v };
        if (ReleasePush.NewVersion(Data("1.2.3"),"jp.aonesenbongi.meltype","1.2.2",null)!="1.2.3") throw new Exception("New stable release must notify");
        foreach (var version in new[] {"1.2.2","1.2.1","1.2.3-rc.1","https://evil.test/","1.2.3\n","1.2.3.4",new string('1',40)})
            if (ReleasePush.NewVersion(Data(version),"jp.aonesenbongi.meltype","1.2.2",null)!=null) throw new Exception("Old or malformed release accepted");
        foreach (var notified in new[] {"1.2.3","1.3.0"})
            if (ReleasePush.NewVersion(Data("1.2.3"),"jp.aonesenbongi.meltype","1.2.2",notified)!=null) throw new Exception("Repeated/older notification accepted");
        if (ReleasePush.NewVersion(Data("1.2.4"),"jp.aonesenbongi.meltype","1.2.2","1.2.3")!="1.2.4") throw new Exception("Newer notification suppressed");
        if (ReleasePush.NewVersion(Data("1.2.3"),"jp.aonesenbongi.meltype.learning","1.2.2",null)!=null) throw new Exception("Wrong package accepted");
        var wrong=Data("1.2.3");wrong["type"]="other";
        if (ReleasePush.NewVersion(wrong,"jp.aonesenbongi.meltype","1.2.2",null)!=null || ReleasePush.NewVersion(new Dictionary<string,string>(),"jp.aonesenbongi.meltype","1.2.2",null)!=null) throw new Exception("Unknown message accepted");
        Console.WriteLine("PASS: release push stable/new version filtering, highest-version deduplication, package/type validation");
    }
}
