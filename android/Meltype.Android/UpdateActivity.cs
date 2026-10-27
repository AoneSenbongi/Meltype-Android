// SPDX-License-Identifier: GPL-3.0-or-later
using global::Android.App;
using global::Android.Content;
using global::Android.Content.PM;
using global::Android.OS;
using global::Android.Widget;
using Meltype.AndroidCore;
using System.Net.Http;
using System.Security.Cryptography;
using Path = System.IO.Path;
namespace Meltype.Mobile;

[Activity(Name="jp.aonesenbongi.meltype.UpdateActivity",Exported=false)]
public sealed class UpdateActivity : Activity
{
    readonly CancellationTokenSource _cancel=new();
    TextView _status=null!;Button _action=null!;ProgressBar _progress=null!;
    ReleaseUpdate? _update;string? _downloaded;
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        var layout=new LinearLayout(this){Orientation=Orientation.Vertical};
        var gap=MobileStyle.Dp(this,20);layout.SetPadding(gap,gap,gap,gap);
        _status=new TextView(this){TextSize=20};layout.AddView(_status);
        _progress=new ProgressBar(this,null,global::Android.Resource.Attribute.ProgressBarStyleHorizontal){Visibility=global::Android.Views.ViewStates.Gone,Max=100};layout.AddView(_progress);
        _action=new Button(this){Text="更新を確認"};MobileStyle.Button(_action);layout.AddView(_action,new LinearLayout.LayoutParams(-1,MobileStyle.Dp(this,48)){TopMargin=MobileStyle.Dp(this,16)});_action.Click+=async(_,_)=>await Act();
        var frame=new FrameLayout(this);frame.AddView(layout,new FrameLayout.LayoutParams(-1,-1));
        frame.SetOnApplyWindowInsetsListener(new MainActivity.SetupInsets(MobileStyle.Dp(this,12),()=>{}));
        SetContentView(frame);frame.RequestApplyInsets();_ = Check();
    }
    HttpClient Client()
    {
        var client=new HttpClient{Timeout=TimeSpan.FromMinutes(10)};client.DefaultRequestHeaders.UserAgent.ParseAdd("Meltype-Android-Update/1.0");return client;
    }
    async Task Check()
    {
        _action.Enabled=false;_status.Text="確認中…";
        try
        {
            var current=PackageManager!.GetPackageInfo(PackageName!,PackageInfoFlags.MetaData)!.VersionName??"0.0.0";
            using var client=Client();using var response=await client.GetAsync(ReleaseUpdate.Api,HttpCompletionOption.ResponseHeadersRead,_cancel.Token);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>1048576)throw new IOException("Release metadata too large");
            await response.Content.LoadIntoBufferAsync(1048576, _cancel.Token);
            var json=await response.Content.ReadAsStringAsync(_cancel.Token);if(json.Length>1048576)throw new IOException("Release metadata too large");
            _update=ReleaseUpdate.Find(json,PackageName!,current);
            _status.Text=_update is null?"利用できる更新はありません":$"{current} → {_update.Version}";_action.Text=_update is null?"再確認":"更新";
        }
        catch(System.OperationCanceledException) { }
        catch(Exception) { _status.Text="更新を確認できませんでした";_action.Text="再試行"; }
        finally { if(!IsDestroyed)_action.Enabled=true; }
    }
    async Task Act()
    {
        if(_update is null){await Check();return;}
        if(_downloaded is not null){RequestInstall();return;}
        _action.Enabled=false;_progress.Visibility=global::Android.Views.ViewStates.Visible;
        var path=Path.Combine(CacheDir!.AbsolutePath,"update.apk");var pending=path+".pending";
        try
        {
            using var client=Client();using var response=await client.GetAsync(_update.Url,HttpCompletionOption.ResponseHeadersRead,_cancel.Token);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is { } length && length!=_update.Size)throw new IOException("Size mismatch");
            using(var input=await response.Content.ReadAsStreamAsync(_cancel.Token))
            using(var output=File.Create(pending))
            {
                var buffer=new byte[65536];long total=0;int count;
                while((count=await input.ReadAsync(buffer,_cancel.Token))>0)
                {
                    total+=count;if(total>_update.Size)throw new IOException("Size exceeded");await output.WriteAsync(buffer.AsMemory(0,count),_cancel.Token);
                    _progress.Progress=(int)(total*100/_update.Size);_status.Text=$"ダウンロード中 {_progress.Progress}%";
                }
                if(total!=_update.Size)throw new IOException("Incomplete APK");
            }
            using(var stream=File.OpenRead(pending))if(!Convert.ToHexString(await SHA256.HashDataAsync(stream,_cancel.Token)).Equals(_update.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Digest mismatch");
            ValidateApk(pending);File.Move(pending,path,true);_downloaded=path;RequestInstall();
        }
        catch(System.OperationCanceledException) { }
        catch(Exception) { _status.Text="更新できませんでした";_action.Text="再試行"; }
        finally { if(File.Exists(pending))File.Delete(pending);if(!IsDestroyed){_action.Enabled=true;_progress.Visibility=global::Android.Views.ViewStates.Gone;} }
    }
    static long Code(PackageInfo info)
    {
        if(OperatingSystem.IsAndroidVersionAtLeast(28))return info.LongVersionCode;
#pragma warning disable CS0618, CA1422
        return info.VersionCode;
#pragma warning restore CS0618, CA1422
    }
    void ValidateApk(string path)
    {
        var flags=OperatingSystem.IsAndroidVersionAtLeast(28)?PackageInfoFlags.SigningCertificates:PackageInfoFlags.Signatures;
        var installed=PackageManager!.GetPackageInfo(PackageName!,flags)!;var apk=PackageManager.GetPackageArchiveInfo(path,flags)??throw new IOException("Invalid APK");
        if(apk.PackageName!=PackageName || Code(apk)<=Code(installed))throw new IOException("Incompatible package/version");
        string Fingerprint(global::Android.Content.PM.Signature s)=>Convert.ToHexString(SHA256.HashData(s.ToByteArray()!));
        string[] active,history;
        if(OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            active=(installed.SigningInfo?.GetApkContentsSigners()??[]).Select(Fingerprint).ToArray();
            history=(apk.SigningInfo?.GetSigningCertificateHistory()??apk.SigningInfo?.GetApkContentsSigners()??[]).Select(Fingerprint).ToArray();
        }
        else
        {
#pragma warning disable CS0618, CA1422
            active=(installed.Signatures??[]).Select(Fingerprint).ToArray();history=(apk.Signatures??[]).Select(Fingerprint).ToArray();
#pragma warning restore CS0618, CA1422
        }
        if(active.Length!=1 || !history.Contains(active[0],StringComparer.Ordinal))throw new IOException("Signing certificate mismatch");
    }
    void RequestInstall()
    {
        if(!PackageManager!.CanRequestPackageInstalls())
        {
            StartActivityForResult(new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,global::Android.Net.Uri.Parse("package:"+PackageName)),41);return;
        }
        Install();
    }
    protected override void OnActivityResult(int request,Result result,Intent? data)
    {
        base.OnActivityResult(request,result,data);
        if(request==41 && _downloaded is not null && PackageManager!.CanRequestPackageInstalls())Install();
    }
    void Install()
    {
        if(_downloaded is null)return;
        try
        {
            ValidateApk(_downloaded);var installer=PackageManager!.PackageInstaller;
            using var parameters=new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);parameters.SetAppPackageName(PackageName);parameters.SetSize(new FileInfo(_downloaded).Length);
            var id=installer.CreateSession(parameters);
            try
            {
                using var session=installer.OpenSession(id);
                using(var output=session.OpenWrite("base.apk",0,new FileInfo(_downloaded).Length))
                using(var input=File.OpenRead(_downloaded)){input.CopyTo(output!);session.Fsync(output!);}
                var callback=new Intent(this,typeof(UpdateInstallReceiver)).SetAction(PackageName+".UPDATE_RESULT");
                var flags=PendingIntentFlags.UpdateCurrent;if(OperatingSystem.IsAndroidVersionAtLeast(31))flags|=PendingIntentFlags.Mutable;
                using var pending=PendingIntent.GetBroadcast(this,id,callback,flags)!;session.Commit(pending.IntentSender!);_status.Text="更新の確認待ち";
            }
            catch { installer.AbandonSession(id);throw; }
        }
        catch(Exception){_status.Text="更新できませんでした";}
    }
    protected override void OnDestroy(){_cancel.Cancel();base.OnDestroy();}
}

[BroadcastReceiver(Exported=false)]
public sealed class UpdateInstallReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context,Intent? intent)
    {
        if(context is null || intent is null)return;
        var status=intent.GetIntExtra(PackageInstaller.ExtraStatus,1);
        if(status==-1)
        {
#pragma warning disable CS0618, CA1422
            var confirm=intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
#pragma warning restore CS0618, CA1422
            if(confirm is not null)context.StartActivity(confirm.AddFlags(ActivityFlags.NewTask));
        }
        else if(status!=0)Toast.MakeText(context,"更新できませんでした",ToastLength.Long)?.Show();
    }
}
