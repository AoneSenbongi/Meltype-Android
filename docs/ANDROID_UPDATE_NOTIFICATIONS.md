# Android版の更新プッシュ通知

利用者の指定により、正式Releaseの公開を契機にGitHub ActionsからFirebase Cloud Messaging（FCM）へ送信し、Meltypeが受信して通知する。日次のGitHubポーリングは行わない。新しいFirebaseプロジェクトを作成し、通常版のapplicationIdにAndroidアプリを登録する。

## アプリの動作

- 設定画面に「更新通知」スイッチを置き、初期値はOFFとする。利用者がONにし、Android 13以降で通知を許可した後にFCMの更新トピックを購読する。
- 通知をOFFにすると購読を解除し、表示中の更新通知を消す。Firebaseの自動トークン登録も初期状態では無効にする。Analyticsは導入しない。
- 受信データは種別と版番号だけを使用し、現在の版より新しい正式版で、通知済みの最高版より新しい場合だけ通知する。同じ版・古い版・不正なデータは通知しない。
- 通知をタップすると既存の更新画面を開く。APKは従来どおりGitHubのRelease情報から取得・検証し、通知データから任意のURLを開かない。自動インストールはしない。
- FCMのAndroidクライアントにはGoogle Play開発者サービスとネットワーク接続が必要。通知許可・省電力・端末の状態によって配信が遅れる場合があり、即時到達は保証しない。非対応端末でも従来の手動更新を利用できる。

## 送信処理

GitHub ActionsはAoneSenbongi/Meltype-Androidのrelease/publishedだけを扱い、draftとprereleaseを除外する。android-で始まる正式版タグと、通常版APKの名前・配布元URL・サイズ・SHA256を確認してから、更新トピックへデータメッセージを送信する。

送信認証はGoogle Cloud Workload Identity Federationと専用サービスアカウントで行い、GitHubのリポジトリID・所有者ID・Releaseイベント・ワークフローを制限する。秘密鍵をAPK・ソース・GitHubの長期秘密鍵へ埋め込まない。受信用google-services.jsonはプロジェクト識別用の公開設定であり、送信資格情報とは分ける。

## 通信と保存

通知を有効にした場合、Firebaseへ端末の通知登録情報・構成情報を送信してFCMを利用する。入力内容・辞書・学習データは通知処理から取得しない。端末内には通知のON/OFFと通知済みの最高版を保存する。FCM SDKも通知用の登録情報を端末内に保存する。サーバー側で利用者の入力履歴や通知先一覧を独自収集しない。

## 導入と検証

Firebaseへのログイン、プロジェクト・Androidアプリの登録、FCM API・Workload Identity Federation・送信専用サービスアカウントの設定が必要。設定完了後、受信設定を含むAPKをビルドし、通知許可・拒否・ON/OFF・新旧版判定・重複抑制・通知タップを確認する。実際の配信テストは管理下の試験端末を宛先にし、利用者全体へテスト通知を送らない。

GitHubへの公開とワークフロー有効化は、具体的なコード・本文・配布物・検証結果を提示して承認を得た後に行う。FCM設定と実配信が未検証のAPKを通知機能完成版として公開しない。

## この環境で作成した送信設定

Google Cloudプロジェクトはmeltype-android-updates（番号833875448960）。送信専用アカウントはmeltype-release-notifier@meltype-android-updates.iam.gserviceaccount.com。Workload Identity Providerはprojects/833875448960/locations/global/workloadIdentityPools/github-releases/providers/github。リポジトリID1407496285・所有者ID159512286・releaseイベント・android-タグ・release-notify.ymlのワークフローに制限している。長期秘密鍵は作成しない。

Firebaseの追加は、プロジェクト更新・サービス有効化・IAM管理の権限がある状態でも403で停止した。Firebaseコンソールの初回利用手続きは、プロジェクトを管理するGoogleアカウントの所有者が行う。設定がない間のローカル検査だけAllowUnconfiguredPush=trueを指定できるが、通常のReleaseビルドはgoogle-services.json未配置なら失敗させる。この例外で作成したAPKを公開用に使わない。

更新通知は、この機能を含む版を導入して通知を有効にした後のリリースが対象です。
