[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$StandardApk,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [string]$AndroidSdkRoot = "$PSScriptRoot/../../../../Android/android-sdk"
)
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'AndroidReleaseSigning.ps1'
if (Test-Path -LiteralPath $WorkDirectory) { throw 'Use a new test directory.' }
$null = New-Item -ItemType Directory -Path $WorkDirectory
$work = (Resolve-Path -LiteralPath $WorkDirectory).Path
$signed = Join-Path $work 'signing-check-only.apk'
$results = [Collections.Generic.List[string]]::new()

function Expect-Failure([string]$Name, [string]$Message, [scriptblock]$Operation) {
    $failure = $null
    try { & $Operation | Out-Null } catch { $failure = $_.Exception.Message }
    if (!$failure -or $failure -notmatch [regex]::Escape($Message)) { throw "Unexpected result for ${Name}: $failure" }
    $results.Add($Name)
}

$before = (Get-FileHash -LiteralPath $StandardApk -Algorithm SHA256).Hash
& $script -Action Sign -ApkPath $StandardApk -OutputPath $signed | Out-Null
$results.Add('sign-with-dedicated-key')
& $script -Action Verify -ApkPath $signed | Out-Null
$results.Add('verify-dedicated-certificate-and-alignment')
if ((Get-FileHash -LiteralPath $StandardApk -Algorithm SHA256).Hash -ne $before) { throw 'Input APK changed.' }
$results.Add('input-apk-preserved')
Expect-Failure 'reject-debug-certificate' 'does not match' { & $script -Action Verify -ApkPath $StandardApk }
Expect-Failure 'reject-output-overwrite' 'Output already exists' { & $script -Action Sign -ApkPath $StandardApk -OutputPath $signed }
Expect-Failure 'reject-key-regeneration' 'Signing directory already exists' { & $script -Action Initialize }
Expect-Failure 'reject-missing-key' 'Release signing material missing' {
    & $script -Action Sign -ApkPath $StandardApk -OutputPath "$work/missing-key.apk" -SigningDirectory "$work/no-key"
}

$manifest = Join-Path $work 'AndroidManifest.xml'
[IO.File]::WriteAllText($manifest, '<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="jp.aonesenbongi.meltype" android:versionCode="1" android:versionName="signing-test"><uses-sdk android:minSdkVersion="26"/><application android:debuggable="true" android:label="Signing test"/></manifest>')
& "$AndroidSdkRoot/build-tools/36.0.0/aapt.exe" package -f -M $manifest -I "$AndroidSdkRoot/platforms/android-36/android.jar" -F "$work/debuggable.apk"
if ($LASTEXITCODE -ne 0) { throw 'Could not build debuggable test fixture.' }
Expect-Failure 'reject-debuggable-apk' 'Debuggable APKs must not be released' {
    & $script -Action Sign -ApkPath "$work/debuggable.apk" -OutputPath "$work/rejected-debug.apk"
}
$text = [IO.File]::ReadAllText($manifest).Replace('jp.aonesenbongi.meltype', 'jp.aonesenbongi.meltype.learning').Replace('android:debuggable="true"', 'android:debuggable="false"')
[IO.File]::WriteAllText($manifest, $text)
& "$AndroidSdkRoot/build-tools/36.0.0/aapt.exe" package -f -M $manifest -I "$AndroidSdkRoot/platforms/android-36/android.jar" -F "$work/learning.apk"
if ($LASTEXITCODE -ne 0) { throw 'Could not build learning test fixture.' }
Expect-Failure 'reject-learning-app-id' 'Only the standard Meltype application ID' {
    & $script -Action Sign -ApkPath "$work/learning.apk" -OutputPath "$work/rejected-learning.apk"
}
foreach ($path in @("$work/missing-key.apk", "$work/rejected-debug.apk", "$work/rejected-learning.apk")) {
    if (Test-Path -LiteralPath $path) { throw "Unexpected output: $path" }
}
@{ passed = $results.Count; checks = $results.ToArray(); inputSha256 = $before; signedSha256 = (Get-FileHash -LiteralPath $signed -Algorithm SHA256).Hash } | ConvertTo-Json | Tee-Object -FilePath "$work/result.json"
