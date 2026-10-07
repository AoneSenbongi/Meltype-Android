# Androidエミュレーター

SDKとエミュレーターはE:/Prog/Meltype/Android/android-sdk、仮想端末はE:/Prog/Meltype/Android/android-home/avdへ置く。Cドライブの空きが少ないため、仮想端末のデータをCへ作らない。

Android 15（API 35）、Google APIsのx86_64イメージとPixel 7の構成でMeltype_API35を作成する。エミュレーターは公式の安定版を使い、ダウンロード時に公式のサイズとSHA-1を照合する。今回のPCはWHPXの利用可能チェックが通過している。

android/tools/Start-AndroidEmulator.cmdをダブルクリックして起動する。PowerShellからはStart-AndroidEmulator.ps1を使える。画面が不要な自動テストでは-Headlessを付ける。ADBの対象はemulator-5554に限定する。学習用APKを入れても、誤字・句読点の収集は開始しない。仮想端末に個人の辞書と履歴をコピーしない。

操作試験はMELTYPE_TEST_SERIAL=emulator-5554を設定してandroid/tools/test_input_actions.pyとtest_long_press_clipboard.pyを実行する。設定しない場合、既存の試験は従来どおりUSBの実機を対象にする。

起動と操作の結果はdocs/CURRENT_STATE.mdへ記録する。エミュレーターの結果は、手元のスマホでの点滅や初回表示の解消確認と区別する。
