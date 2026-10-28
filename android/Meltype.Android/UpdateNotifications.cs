// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Firebase.Messaging;
using Meltype.AndroidCore;

namespace Meltype.Mobile;

internal static class UpdateNotifications
{
    const string Channel = "app-updates";
    const int NotificationId = 12202;
    static readonly object Gate = new();
    static ISharedPreferences Preferences(Context context) => context.GetSharedPreferences("update-notifications", FileCreationMode.Private)!;
    internal static bool Enabled(Context context) => Preferences(context).GetBoolean("enabled", false);
    internal static bool CanPost(Context context)
    {
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        return manager.AreNotificationsEnabled() && manager.GetNotificationChannel(Channel)?.Importance != NotificationImportance.None;
    }
    internal static void SetEnabled(Context context, bool enabled, Action<bool>? completed = null)
    {
        var wasEnabled = Enabled(context);
        if (!Preferences(context).Edit()!.PutBoolean("enabled", enabled)!.Commit()) { completed?.Invoke(false); return; }
        if (!enabled)
        {
            ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Cancel(NotificationId);
            if (wasEnabled && Firebase.FirebaseApp.InitializeApp(context) is not null)
            {
                FirebaseMessaging.Instance.AutoInitEnabled = false;
                FirebaseMessaging.Instance.UnsubscribeFromTopic(ReleasePush.Topic);
            }
            completed?.Invoke(true); return;
        }
        RefreshSubscription(context, completed);
    }
    internal static void RefreshSubscription(Context context, Action<bool>? completed = null)
    {
        if (!Enabled(context)) { completed?.Invoke(true); return; }
        if (!CanPost(context) || context.PackageName != "jp.aonesenbongi.meltype" ||
            global::Android.Gms.Common.GoogleApiAvailabilityLight.Instance.IsGooglePlayServicesAvailable(context) != 0 ||
            Firebase.FirebaseApp.InitializeApp(context) is null)
        { completed?.Invoke(false); return; }
        var client = FirebaseMessaging.Instance;
        client.AutoInitEnabled = true;
        client.SubscribeToTopic(ReleasePush.Topic).AddOnCompleteListener(new Completion(completed));
    }
    internal static void Receive(Context context, IReadOnlyDictionary<string,string> data)
    {
        lock (Gate)
        {
            if (!Enabled(context) || !CanPost(context)) return;
            var current = context.PackageManager!.GetPackageInfo(context.PackageName!, PackageInfoFlags.MetaData)!.VersionName ?? "0.0.0";
            var version = ReleasePush.NewVersion(data, context.PackageName!, current, Preferences(context).GetString("notified-version", null));
            if (version is null) return;
            var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
            manager.CreateNotificationChannel(new NotificationChannel(Channel, "アプリの更新", NotificationImportance.Low));
            var intent = new Intent(context, typeof(UpdateActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            using var open = PendingIntent.GetActivity(context, NotificationId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
            using var builder = new Notification.Builder(context, Channel)
                .SetSmallIcon(global::Android.Resource.Drawable.StatNotifySync)
                .SetContentTitle("Meltypeの更新があります")
                .SetContentText($"{version} の変更内容を確認できます")
                .SetContentIntent(open).SetAutoCancel(true).SetOnlyAlertOnce(true);
            try
            {
                manager.Notify(NotificationId, builder.Build());
                Preferences(context).Edit()!.PutString("notified-version", version)!.Commit();
            }
            catch (Java.Lang.SecurityException) { /* Permission can be revoked between the check and posting. */ }
        }
    }
    sealed class Completion(Action<bool>? completed) : Java.Lang.Object, global::Android.Gms.Tasks.IOnCompleteListener
    {
        public void OnComplete(global::Android.Gms.Tasks.Task task) => completed?.Invoke(task.IsSuccessful);
    }
}

[Service(Name = "jp.aonesenbongi.meltype.ReleaseMessagingService", Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public sealed class ReleaseMessagingService : FirebaseMessagingService
{
    public override void OnMessageReceived(RemoteMessage message) =>
        UpdateNotifications.Receive(this, new Dictionary<string,string>(message.Data));
}
