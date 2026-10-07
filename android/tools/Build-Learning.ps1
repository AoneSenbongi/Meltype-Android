$ErrorActionPreference = 'Stop'
$taskAndroidRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$taskSourceRoot = Join-Path $taskAndroidRoot 'source'
$taskPackageRoot = Join-Path $taskAndroidRoot 'distribution/learning-local'
& (Join-Path $taskAndroidRoot 'dotnet/dotnet.exe') build (Join-Path $taskSourceRoot 'android/Meltype.Android') -c Release -p:TrainingBuild=true "-p:AndroidSdkDirectory=$taskAndroidRoot/android-sdk" "-p:JavaSdkDirectory=$taskAndroidRoot/jdk" "-p:RestorePackagesPath=$taskAndroidRoot/nuget"
if ($LASTEXITCODE -ne 0) { throw 'Learning APK build failed.' }
New-Item -ItemType Directory -Path $taskPackageRoot -Force | Out-Null
$taskApk = Join-Path $taskPackageRoot 'Meltype-Android-learning.apk'
Copy-Item -LiteralPath (Join-Path $taskSourceRoot 'android/Meltype.Android/bin/Release/net10.0-android/jp.aonesenbongi.meltype.learning-Signed.apk') -Destination $taskApk
$taskHash = (Get-FileHash -LiteralPath $taskApk -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($taskApk + '.sha256'), "$taskHash  Meltype-Android-learning.apk`n", [Text.UTF8Encoding]::new($false))
Write-Output $taskApk
