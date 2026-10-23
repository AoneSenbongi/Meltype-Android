param(
    [Parameter(Mandatory)][string]$Ndk,
    [Parameter(Mandatory)][string]$LlamaSource,
    [string]$Cmake='cmake',
    [string]$Ninja='ninja',
    [string[]]$Abis=@('arm64-v8a','x86_64'),
    [string]$BuildRoot=(Join-Path $PSScriptRoot 'build')
)
$ErrorActionPreference='Stop'
$revision=(& git -C $LlamaSource rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $revision -ne '10131b23ee0dbd9d03dfc618cec9fe3402919277') { throw 'Unexpected llama.cpp revision' }
$toolchain=Join-Path $Ndk 'build/cmake/android.toolchain.cmake'
if(-not (Test-Path -LiteralPath $toolchain)) { throw 'Android NDK toolchain is missing' }
foreach($abi in $Abis) {
    if($abi -notin @('arm64-v8a','x86_64')) { throw 'Unsupported ABI' }
    $buildDir=Join-Path $BuildRoot $abi
    & $Cmake -S $PSScriptRoot -B $buildDir -G Ninja "-DCMAKE_MAKE_PROGRAM=$Ninja" "-DCMAKE_TOOLCHAIN_FILE=$toolchain" "-DLLAMA_SOURCE_DIR=$LlamaSource" "-DANDROID_ABI=$abi" -DANDROID_PLATFORM=android-26 -DANDROID_STL=c++_static -DCMAKE_BUILD_TYPE=Release -DGGML_CCACHE=OFF
    if($LASTEXITCODE -ne 0) { throw "Configure failed: $abi" }
    & $Cmake --build $buildDir --target meltype_zenz zenz-smoke -j 4
    if($LASTEXITCODE -ne 0) { throw "Build failed: $abi" }
}
