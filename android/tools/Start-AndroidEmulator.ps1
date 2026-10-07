param([switch]$Headless)
$ErrorActionPreference = 'Stop'
$taskAndroidRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$env:ANDROID_HOME = Join-Path $taskAndroidRoot 'android-sdk'
$env:ANDROID_USER_HOME = Join-Path $taskAndroidRoot 'android-home'
$env:ANDROID_EMULATOR_HOME = $env:ANDROID_USER_HOME
$env:ANDROID_AVD_HOME = Join-Path $env:ANDROID_USER_HOME 'avd'
$taskEmulator = Join-Path $env:ANDROID_HOME 'emulator/emulator.exe'
if (!(Test-Path -LiteralPath $taskEmulator)) { throw 'Android Emulator is not installed.' }
$taskEmulatorArgs = @('-avd','Meltype_API35','-port','5554','-gpu','swiftshader','-no-snapshot','-no-boot-anim','-memory','2048')
if ($Headless) { $taskEmulatorArgs += '-no-window' }
& $taskEmulator @taskEmulatorArgs
