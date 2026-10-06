# Meltype Androidのセキュリティと入力データの扱い

対象はAoneSenbongi/Meltype-Androidの試作版0.2.0です。雪代／Yukishiro氏のMeltypeを基にした非公式Androidキーボードで、元作者やGoogleの公式配布版ではありません。PC版の説明は[PC版のセキュリティ文書](https://github.com/AoneSenbongi/Meltype/blob/google-native-ime/SECURITY.md)を参照してください。

以下は公開ソースとRelease用Manifestを確認した説明です。第三者によるセキュリティ監査の結果や安全性の保証ではありません。

## Androidの入力データ

### 入力処理と権限

AndroidのInputMethodServiceとして、選択中の入力欄の文字を処理します。日英判別、変換、学習は端末内で行い、OSS版MozcとOSS辞書を同梱します。GboardやWindowsのGoogle日本語入力のプログラム、辞書、学習履歴とは連携しません。

0.2.0のRelease用Manifestには`INTERNET`を含む`uses-permission`の宣言がありません。入力の送信、広告、解析SDK、クラウド変換は実装していません。キーボードサービスは公開されていますが、接続にはOSの`android.permission.BIND_INPUT_METHOD`が必要です。連絡先、位置情報、マイク、アクセシビリティサービスの権限は要求しません。

Androidがパスワード欄、数字・電話・日時入力欄と通知した場合は直接入力し、変換と学習を行いません。パスワード欄でも利用者が押したキーを入力先に渡す役割はあります。アプリが入力欄の種類を正しく指定しない場合の検出は保証できません。

### 学習と削除

Mozcの辞書と学習データはアプリ専用の内部保存領域の`files/mozc`に置きます。通常の日本語入力では学習が有効です。0.2.0には利用者向けの学習停止設定や辞書管理画面はありません。

Androidのアプリ用アクセス制限を使い、Fork独自の保存データ暗号化は行っていません。`android:allowBackup="false"`で標準のアプリバックアップを無効にしていますが、端末メーカー独自の移行機能やroot権限でのアクセスまで保証するものではありません。アンインストールするとアプリの学習データも削除されます。

通常の起動では共有コアの診断ログをファイルへ出力しません。開発用のselftestを実行すると、固定の試験入力と結果を内部の`selftest.txt`へ書きます。診断情報やエラーの共有前には内容を確認してください。



## 配布物、署名、検証範囲

配布元は[Android専用リポジトリのReleases](https://github.com/AoneSenbongi/Meltype-Android/releases)です。0.2.0のAPKは試作用の鍵で署名しており、Google Playでの配布や公開者の本人確認を受けた署名ではありません。旧APKと署名が違う場合は上書きできず、削除すると学習データを失います。自動更新はありません。リポジトリを分離しても公開済み0.2.0のAPKと署名は変更しません。

ReleaseのSHA-256とダウンロードしたファイルのハッシュを比較できます。ハッシュの一致はファイルが同じであることの確認であり、無害性や公開者の本人性を証明しません。GitHubアカウントの侵害に対する独立した検証にもなりません。対応するソースと第三者ライセンス通知を同じReleaseで公開しています。

入力処理のテストとエミュレーターでのキー操作を確認しています。第三者の侵入試験、全入力先でのパスワード検出、全端末での安全性確認は実施していません。実機での入力速度、各アプリとの相性、画面回転、長時間入力は未検証です。

## 脆弱性の報告

このAndroid派生版に関する報告先はAoneSenbongi/Meltype-Androidの管理者です。[GitHubの非公開の脆弱性報告](https://github.com/AoneSenbongi/Meltype-Android/security/advisories/new)を有効にしています。悪用可能な再現手順や機密データは公開Issueに載せず、非公開の報告機能を使ってください。返信期限や修正期限は約束していません。

通常の不具合は[このリポジトリのIssue](https://github.com/AoneSenbongi/Meltype-Android/issues)へ報告できます。実際のパスワード、個人の入力履歴、辞書、学習データを公開しないでください。再現用には個人情報を含まない文字列を使ってください。
