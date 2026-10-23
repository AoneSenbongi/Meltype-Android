[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Initialize', 'Sign', 'Verify')][string]$Action,
    [string]$ApkPath,
    [string]$OutputPath,
    [string]$SigningDirectory = (Join-Path $env:LOCALAPPDATA 'Meltype/AndroidReleaseSigning'),
    [string]$AndroidSdkRoot = "$PSScriptRoot/../../../../Android/android-sdk",
    [string]$JdkRoot = "$PSScriptRoot/../../../../Android/jdk"
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$java = Join-Path $JdkRoot 'bin/java.exe'
$keytool = Join-Path $JdkRoot 'bin/keytool.exe'
$buildTools = Join-Path $AndroidSdkRoot 'build-tools/36.0.0'
$signer = Join-Path $buildTools 'lib/apksigner.jar'
$aapt = Join-Path $buildTools 'aapt.exe'
$zipalign = Join-Path $buildTools 'zipalign.exe'
$keyStore = Join-Path $SigningDirectory 'meltype-android-release.jks'
$passwordFile = Join-Path $SigningDirectory 'password.dpapi.xml'
$certificateFile = Join-Path $SigningDirectory 'certificate.der'
$aliasName = 'meltype-android-release'

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    $result = & $Executable @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Tool failed ($LASTEXITCODE): $Executable`n$($result -join "`n")" }
    return ($result -join "`n")
}

function Test-ReleaseApk([string]$Path) {
    $badging = Invoke-Checked $aapt @('dump', 'badging', $Path)
    if ($badging -notmatch "(?m)^package: name='jp\.aonesenbongi\.meltype'") {
        throw 'Only the standard Meltype application ID may be released.'
    }
    if ($badging -match '(?m)^application-debuggable') { throw 'Debuggable APKs must not be released.' }
}

function Test-ReleaseSignature([string]$Path) {
    Test-ReleaseApk $Path
    $verification = Invoke-Checked $java @('-jar', $signer, 'verify', '--verbose', '--print-certs', $Path)
    $expected = (Get-FileHash -LiteralPath $certificateFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $digests = [regex]::Matches($verification, '(?m)^Signer #\d+ certificate SHA-256 digest: ([0-9a-fA-F]+)\s*$')
    if ($digests.Count -ne 1 -or $digests[0].Groups[1].Value.ToLowerInvariant() -ne $expected) {
        throw 'APK signing certificate does not match the dedicated release certificate.'
    }
    $null = Invoke-Checked $zipalign @('-c', '-P', '16', '4', $Path)
    return $verification
}

if ($Action -eq 'Initialize') {
    if (Test-Path -LiteralPath $SigningDirectory) { throw 'Signing directory already exists. Do not regenerate release keys.' }
    $null = New-Item -ItemType Directory -Path $SigningDirectory
    $acl = Get-Acl -LiteralPath $SigningDirectory
    $acl.SetAccessRuleProtection($true, $false)
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $SigningDirectory -AclObject $acl
    $password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    ConvertTo-SecureString -String $password -AsPlainText -Force | Export-Clixml -LiteralPath $passwordFile
    $oldPassword = [Environment]::GetEnvironmentVariable('MELTYPE_RELEASE_STORE_PASSWORD', 'Process')
    try {
        $env:MELTYPE_RELEASE_STORE_PASSWORD = $password
        $null = Invoke-Checked $keytool @('-genkeypair', '-keystore', $keyStore, '-storetype', 'JKS', '-storepass:env', 'MELTYPE_RELEASE_STORE_PASSWORD', '-keypass:env', 'MELTYPE_RELEASE_STORE_PASSWORD', '-alias', $aliasName, '-keyalg', 'RSA', '-keysize', '3072', '-sigalg', 'SHA256withRSA', '-validity', '10000', '-dname', 'CN=Meltype Android Release')
        $null = Invoke-Checked $keytool @('-exportcert', '-keystore', $keyStore, '-storepass:env', 'MELTYPE_RELEASE_STORE_PASSWORD', '-alias', $aliasName, '-file', $certificateFile)
    } finally {
        [Environment]::SetEnvironmentVariable('MELTYPE_RELEASE_STORE_PASSWORD', $oldPassword, 'Process')
        $password = $null
    }
    Write-Output "Release key created: $keyStore"
    Write-Output "Certificate SHA-256: $((Get-FileHash -LiteralPath $certificateFile -Algorithm SHA256).Hash)"
    return
}

foreach ($required in @($keyStore, $passwordFile, $certificateFile)) {
    if (!(Test-Path -LiteralPath $required -PathType Leaf)) { throw "Release signing material missing: $required" }
}
if (!$ApkPath) { throw 'ApkPath is required.' }
$inputApk = (Resolve-Path -LiteralPath $ApkPath).Path
if ($Action -eq 'Verify') {
    Test-ReleaseSignature $inputApk
    return
}
if (!$OutputPath) { throw 'OutputPath is required.' }
$outputApk = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $outputApk) { throw 'Output already exists; refusing to overwrite.' }
if (!(Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($outputApk)) -PathType Container)) { throw 'Output directory must exist.' }
Test-ReleaseApk $inputApk
$temporary = "$outputApk.$([Guid]::NewGuid().ToString('N')).pending.apk"
$aligned = "$temporary.aligned.apk"
$oldPassword = [Environment]::GetEnvironmentVariable('MELTYPE_RELEASE_STORE_PASSWORD', 'Process')
$passwordPointer = [IntPtr]::Zero
try {
    $secret = Import-Clixml -LiteralPath $passwordFile
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    $env:MELTYPE_RELEASE_STORE_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $null = Invoke-Checked $zipalign @('-P', '16', '-f', '4', $inputApk, $aligned)
    $null = Invoke-Checked $java @('-jar', $signer, 'sign', '--ks', $keyStore, '--ks-key-alias', $aliasName, '--ks-pass', 'env:MELTYPE_RELEASE_STORE_PASSWORD', '--key-pass', 'env:MELTYPE_RELEASE_STORE_PASSWORD', '--out', $temporary, $aligned)
    $verification = Test-ReleaseSignature $temporary
    [IO.File]::Move($temporary, $outputApk)
    Write-Output $verification
    Write-Output "Signed APK: $outputApk"
    Write-Output "APK SHA-256: $((Get-FileHash -LiteralPath $outputApk -Algorithm SHA256).Hash)"
} finally {
    [Environment]::SetEnvironmentVariable('MELTYPE_RELEASE_STORE_PASSWORD', $oldPassword, 'Process')
    if ($passwordPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer) }
    foreach ($temporaryFile in @($temporary, $aligned, "$temporary.idsig")) {
        if (Test-Path -LiteralPath $temporaryFile) { Remove-Item -LiteralPath $temporaryFile }
    }
}
