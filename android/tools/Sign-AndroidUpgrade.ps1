[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApkPath,
    [Parameter(Mandatory)][string]$PreviousApkPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$SigningDirectory = (Join-Path $env:LOCALAPPDATA 'Meltype/AndroidReleaseSigning'),
    [string]$PreviousKeyStore = (Join-Path $env:LOCALAPPDATA 'Xamarin/Mono for Android/debug.keystore'),
    [string]$AndroidSdkRoot = "$PSScriptRoot/../../../../Android/android-sdk",
    [string]$JdkRoot = "$PSScriptRoot/../../../../Android/jdk"
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$java = Join-Path $JdkRoot 'bin/java.exe'
$keytool = Join-Path $JdkRoot 'bin/keytool.exe'
$tools = Join-Path $AndroidSdkRoot 'build-tools/36.0.0'
$signer = Join-Path $tools 'lib/apksigner.jar'
$zipalign = Join-Path $tools 'zipalign.exe'
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw 'Output already exists.' }
if (!(Test-Path -LiteralPath $PreviousKeyStore -PathType Leaf)) { throw 'Previous signing key is missing.' }
function Checked([string]$Exe, [string[]]$Arguments) {
    $result = & $Exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Signing tool failed: $Exe`n$($result -join "`n")" }
    return ($result -join "`n")
}
function Digest([string]$Verification) {
    $certDigests = [regex]::Matches($Verification, '(?m)^Signer #\d+ certificate SHA-256 digest: ([0-9a-fA-F]+)\s*$')
    if ($certDigests.Count -ne 1) { throw 'Expected exactly one active signing certificate.' }
    return $certDigests[0].Groups[1].Value.ToLowerInvariant()
}
$stem = "$output.$([Guid]::NewGuid().ToString('N'))"
$direct = "$stem.direct.apk"
$oldCertificate = "$stem.old.der"
$pending = "$stem.pending.apk"
$lineage = Join-Path $SigningDirectory 'previous-debug-to-release.lineage'
$oldLegacyPassword = [Environment]::GetEnvironmentVariable('MELTYPE_PREVIOUS_STORE_PASSWORD', 'Process')
$oldReleasePassword = [Environment]::GetEnvironmentVariable('MELTYPE_RELEASE_STORE_PASSWORD', 'Process')
$passwordPointer = [IntPtr]::Zero
try {
    # This validates the standard app ID, non-debuggable manifest, and dedicated key first.
    & "$PSScriptRoot/AndroidReleaseSigning.ps1" -Action Sign -ApkPath $ApkPath -OutputPath $direct -SigningDirectory $SigningDirectory -AndroidSdkRoot $AndroidSdkRoot -JdkRoot $JdkRoot | Out-Null
    $expected = (Get-FileHash -LiteralPath (Join-Path $SigningDirectory 'certificate.der') -Algorithm SHA256).Hash.ToLowerInvariant()
    $previous = Digest (Checked $java @('-jar', $signer, 'verify', '--min-sdk-version', '26', '--max-sdk-version', '27', '--print-certs', $PreviousApkPath))
    $env:MELTYPE_PREVIOUS_STORE_PASSWORD = 'android'
    $null = Checked $keytool @('-exportcert', '-keystore', $PreviousKeyStore, '-storepass:env', 'MELTYPE_PREVIOUS_STORE_PASSWORD', '-alias', 'androiddebugkey', '-file', $oldCertificate)
    if ((Get-FileHash -LiteralPath $oldCertificate -Algorithm SHA256).Hash.ToLowerInvariant() -ne $previous) { throw 'Previous key does not match the published APK.' }
    $secret = Import-Clixml -LiteralPath (Join-Path $SigningDirectory 'password.dpapi.xml')
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    $env:MELTYPE_RELEASE_STORE_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $oldSigner = @('--ks', $PreviousKeyStore, '--ks-key-alias', 'androiddebugkey', '--ks-pass', 'env:MELTYPE_PREVIOUS_STORE_PASSWORD', '--key-pass', 'env:MELTYPE_PREVIOUS_STORE_PASSWORD')
    $newSigner = @('--ks', (Join-Path $SigningDirectory 'meltype-android-release.jks'), '--ks-key-alias', 'meltype-android-release', '--ks-pass', 'env:MELTYPE_RELEASE_STORE_PASSWORD', '--key-pass', 'env:MELTYPE_RELEASE_STORE_PASSWORD')
    if (!(Test-Path -LiteralPath $lineage)) {
        $null = Checked $java (@('-jar', $signer, 'rotate', '--out', $lineage, '--old-signer') + $oldSigner + @('--new-signer') + $newSigner)
    }
    $history = Checked $java @('-jar', $signer, 'lineage', '--in', $lineage, '--print-certs')
    if (!$history.Contains($expected) -or !$history.Contains($previous)) { throw 'Signing history does not match both certificates.' }
    $null = Checked $java (@('-jar', $signer, 'sign', '--v1-signing-enabled', 'false', '--v2-signing-enabled', 'true', '--v3-signing-enabled', 'true', '--v4-signing-enabled', 'false', '--rotation-min-sdk-version', '28', '--lineage', $lineage) + $oldSigner + @('--next-signer') + $newSigner + @('--out', $pending, $direct))
    $modern = Checked $java @('-jar', $signer, 'verify', '--min-sdk-version', '28', '--print-certs', $pending)
    $legacy = Checked $java @('-jar', $signer, 'verify', '--min-sdk-version', '26', '--max-sdk-version', '27', '--print-certs', $pending)
    if ((Digest $modern) -ne $expected) { throw 'Android 9+ must use the dedicated release certificate.' }
    if ((Digest $legacy) -ne $previous) { throw 'Android 8.x compatibility certificate mismatch.' }
    $null = Checked $zipalign @('-c', '-P', '16', '4', $pending)
    [IO.File]::Move($pending, $output)
    @{ apkSha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant(); android9PlusCertificate = $expected; android8Certificate = $previous; rotationMinimumSdk = 28 } | ConvertTo-Json
} finally {
    [Environment]::SetEnvironmentVariable('MELTYPE_PREVIOUS_STORE_PASSWORD', $oldLegacyPassword, 'Process')
    [Environment]::SetEnvironmentVariable('MELTYPE_RELEASE_STORE_PASSWORD', $oldReleasePassword, 'Process')
    if ($passwordPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer) }
    foreach ($temporary in @($direct, $oldCertificate, $pending, "$pending.idsig")) {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
}
