# Androidエミュレーター

SDKとエミュレーターはE:/Prog/Meltype/Android/android-sdk、仮想端末はE:/Prog/Meltype/Android/android-home/avdへ置く。Cドライブの空きが少ないため、仮想端末のデータをCへ作らない。

Android 15（API 35）、Google APIsのx86_64イメージとPixel 7の構成でMeltype_API35を作成する。エミュレーターは公式の安定版を使い、ダウンロード時に公式のサイズとSHA-1を照合する。今回のPCはWHPXの利用可能チェックが通過している。

`android/tools/Start-AndroidEmulator.ps1 -Headless`で起動する。ADBの対象はemulator-5554に限定する。通常版APKと試験用fixtureを導入し、通常版IMEを選択する。個人の辞書や履歴はコピーしない。1.0.0には誤字収集機能と学習用APKはない。

## 1.0.0の操作試験

1. `test_clause_edit.py --serial emulator-5554`でfixtureをビルドし、部分確定後の未確定範囲を確認する。
2. `test_goji_candidates.py --serial emulator-5554 --output <証跡フォルダー>`で「誤字」「荷台」と英語判定されたcameraの手動文節変更・候補・確定を確認する。
3. `test_number_keyboard.py --serial emulator-5554`で数字6種類の直接入力・削除・初回コピー候補・履歴・パスワード保護・通常配列への復帰を確認する。
4. `test_prediction_shift_clipboard.py --serial emulator-5554 --prediction-only --output <証跡フォルダー>`で次文字予測の境界とShift固定を確認する。

起動と操作の結果はローカルのdocs/CURRENT_STATE.mdへ記録する。エミュレーターの結果は、実機の感度や初回表示、アプリごとの挙動の確認とは区別する。以下の旧試験は対象パッケージ等を確認してから使用する。

## タッチ入力の回帰試験

test_touch_tolerance.pyはキー端の横・縦の小移動、許容幅を超える移動と削除を検証する。test_touch_order.pyは2本の指を模したイベントで、後のキーを先に離す操作、文字と削除の重なり、Shiftと文字の重なりを検証する。test_control_long_press.pyは句読点の長押しと削除の連続入力・離した後の停止を検証する。いずれもMELTYPE_TEST_SERIAL=emulator-5554で実行する。

順序試験の前に、android/tools/TouchInjection.javaをSDKのandroid.jarをクラスパスとしてJDKでコンパイルし、ビルドツールのD8でDEXへ変換する。classes.dexをエミュレーターの/data/local/tmp/TouchInjection.dexへ転送する。試験用ドライバーはAndroid 15のshell環境専用であり、アプリへ同梱しない。指の接触面積や実機の感度を検証する試験ではない。

## 片手モードの試験

`test_one_hand.py`はemulator-5554だけを対象にする。学習用APKとconversion-fixtureを導入し、学習用IMEを選択してから実行する。1回ずつ押して右寄せ・左寄せ・全幅へ戻る順序、左右80％の幅と高さ、ポップアップと余白の操作ボタンがないこと、候補表示中の片手ボタンの非表示と確定後の再表示、再表示とサービス再起動後の配置保存、記号配列とクリップボードを確認する。画面はdistribution/one-hand-cycle-localへ保存する。試験用端末の配置設定だけを変更し、実機の設定・辞書・収集記録は操作しない。検証後は試験用エミュレーターを終了する。

## 文節変更後の未確定範囲

`test_clause_edit.py --serial emulator-5554`で「われわれはうちゅうじんだ」を入力し、前の文節を選んで「ワレワレは」へ変更する。文字列だけでなく、実際の入力欄の未確定範囲が後続に残ることを検査する。後続も「ウチュウジンだ」へ変更し、最終結果「ワレワレはウチュウジンだ」を確認する。conversion-fixtureの試験用ボタンはカーソルを前の文節へ移し、試験表示は入力欄の未確定範囲を取得する。fixtureは配布APKへ含めない。証跡はdistribution/clause-edit-localへ保存する。
