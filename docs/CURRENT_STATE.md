# 現在の状態

## 有効な目的

Android版をPC版から分離して`AoneSenbongi/Meltype-Android`で開発・配布する。作業用のソース、SDK・JDK・.NETと配布物を`E:/Prog/Meltype/Android`へまとめる。元のMeltypeの履歴と著作権・GPL表記を保持する。

## 実装と公開済みの検証

Android 0.2.0はQWERTYのローマ字入力、日英自動判別、英語専用モードへの切替、Mozcライブ変換、入力中の候補選択を実装する。英字3段と操作1段、数字・記号への切替、専用アイコンと導入画面がある。入力は端末内で処理し、INTERNET権限を要求しない。

公開APKの入力処理は`318da90`に対応する。元のリポジトリのActionsで、Android 15 x86_64エミュレーターのキー操作・候補選択・日英切替・Shift・句読点を確認済み。0.2.0のAPKは試作用の署名。実機での入力速度、各アプリとの相性、画面回転、長時間入力は未検証のためprereleaseとして残す。

## 分離の状態と残作業

独立リポジトリを作成し、作業先をProg以下へ移した。GitHubの同一アカウントへの二つ目のFork作成は既存のPC版を返したため、GitHubのFork表示は付けず、Git履歴とREADMEの出典で派生元を示す。PC版のremoteは変更しない。

README・配布案内・セキュリティ説明をAndroid専用のURLへ変更し、mainへ公開する。公開済み0.2.0のAPK・ハッシュ・ソースZIPを同一ファイルのまま新しいReleaseへ移す。旧リポジトリのReleaseは削除せず移転先を案内する。分離後の公開先とハッシュを確認して、この状態資料を更新する。

## 保存場所

ソースは`E:/Prog/Meltype/Android/source`、ビルド環境はその親の`dotnet`・`android-sdk`・`jdk`・`nuget`、検証結果と配布物は`distribution`に置く。移動直後のビルド生成物には古い絶対パスが残るため、次のビルドでは新しいSDKとNuGetのパスを指定して復元する。
