param([string]$Adb = 'E:/Prog/Meltype/Android/android-sdk/platform-tools/adb.exe')
$ErrorActionPreference = 'Stop'
$package = 'jp.aonesenbongi.meltype.learning'
& $Adb -d shell am force-stop $package
& $Adb -d shell run-as $package rm -f files/selftest.txt
& $Adb -d shell am start -W -n "$package/jp.aonesenbongi.meltype.MainActivity" --ez selftest true
if ($LASTEXITCODE -ne 0) { throw 'Learning app did not start.' }
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    $result = (& $Adb -d shell "run-as $package cat files/selftest.txt 2>/dev/null || true") -join "`n"
    if ($result.StartsWith('PASS')) { Write-Output $result; exit 0 }
    if ($result.StartsWith('FAIL')) { throw $result }
    Start-Sleep -Milliseconds 500
}
throw 'Learning app did not complete its on-device conversion test.'
