# Android zenz 引き継ぎ

2026-10-20。ユーザーの依頼により、ここからのAPK作成と実機検証を元のPCへ移す。

これは受領時点の記録。元PCでの継続結果と現在の適用状態はCURRENT_STATE.mdを参照する。

## 受け取りと適用先

共有先: G:/マイドライブ/Codex-transfers/Meltype-Android-zenz-20261020/reply/android-zenz-handoff-v1.zip

現行faa07f8のソースを新しい専用ディレクトリへ展開し、このzipをその上へ重ねる。既存の公開準備用ソースへ直接上書きしない。モデルは同じ共有先の親にあるzenz-v3.2-small-Q5_K_M.ggufを使用する。モデルと個人データはこのzipに含めていない。

仕様はdocs/ANDROID_ZENZ.md、確認結果はverification/にある。manifest.jsonの各ファイルのSHA256を照合する。

## 完了した範囲

1. 小型zenzのC ABI、CPU推論、候補grammar、期限、中止、非同期モデル準備を実装した。
2. Androidセッションへ未変換ローマ字の再評価を接続した。元の打鍵を保持し、正常英語・明示モード・URL等を保護する。既存候補だけから選択し、古い結果と自動選択の学習への逆流を防ぐ。
3. Androidテストプログラムは28グループのPASSを出し、終了コード0。反復誤字の自動英語学習と明示英語の違い、学習ファイル不変、削除・候補選択・確定・取消・遅延応答を含む。
4. PCの実モデルでmanaged adapterから「ニホンゴ→日本語」、誤字候補から「お願いします」の選択を確認。モデル準備570ms、準備後の日本語生成201ms、誤字候補の選択128ms。PCの単発計測であり、Androidでの性能は未確認。
5. arm64-v8a/x86_64のlibmeltype_zenz.soとsmoke実行ファイルをNDK r27dでビルドした。16KB整列、依存先libc/libm/libdlを確認した。

## 次の作業

1. Androidアプリのコンパイルを確認する。このPCではSDKのAndroid workloadを導入せず、アプリ側のC#とAPKパッケージングは未確認。
2. LocalZenzModelに既存の検証済みGGUFの絶対パスを渡し、ローカル候補APKを作る。ネイティブライブラリ、モデル、ライセンスがAPKへ入ること、INTERNET権限がないことを確認する。設定LocalZenzは初期OFFである。
3. 端末を元PCへ接続後、最新のAPK・データを改めて退避する。現在のアプリIDはjp.aonesenbongi.meltype.learning、versionNameは0.4.0-learning、versionCodeは1。通常版appIdとは異なるため、同じappIdと署名のローカル候補を作る。既存署名との一致を証明してからadb install -rを使う。アンインストールとデータ消去はしない。
4. 実機でライブラリ単独smoke、推論時間・メモリ、IMEでの誤字反復→再評価、正常英語、文節操作、部分確定、削除、取消、候補バー、入力欄切替を確認する。
5. 候補生成は現在Mozcの既存ワーカー呼び出しで行う。1500msは評価器の待機期限で、候補生成を含む厳密な総時間の上限ではない。実機で入力遅延を測り、必要なら候補生成を非同期化する。

## 制限と保護

共通Core.Tests全体は元ソースの不足でビルドできない。元のzipにsrc/Meltype.Core/Input/InputSession.csが存在せず、既存TestSupport.cs等がInputSessionを参照してCS0246になることを確認した。Androidテストとは区別して扱う。

このPCから端末へのAPK更新、IME変更、辞書・設定・学習データの変更は行っていない。過去のキーボード再現試験の後は端末操作を停止した。元APKとアプリデータの退避はこのPCの作業フォルダーに保持し、この転送zipへは入れていない。端末接続後に元PCでもバックアップを取得すること。

GitHubへのpush、PR、Release、モデルの外部配布は未承認。既存の公開物と配布候補を変更しない。署名鍵を転送しない。
