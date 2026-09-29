$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/runtime.ps1"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'build\qwen-tests'
$src = Join-Path $root 'experiments\qwen-voice-probe'
New-Item -ItemType Directory -Force $out | Out-Null
$refs = @(Get-ChildItem $runtime -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.AssemblyName]::GetAssemblyName($_.FullName); '/r:' + $_.FullName } catch [BadImageFormatException] {}
})
$refs += '/r:' + (Join-Path $desktop 'System.Security.Cryptography.ProtectedData.dll')
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /noconfig /nostdlib /target:exe "/out:$out\Tests.dll" @refs "$src\Core.cs" "$src\Store.cs" "$src\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Qwen tests compilation failed' }
Copy-Item "$root/experiments/tencent-streaming-probe/Probe.runtimeconfig.json" "$out/Tests.runtimeconfig.json" -Force
& dotnet "$out/Tests.dll"
if ($LASTEXITCODE -ne 0) { throw 'Qwen offline tests failed' }
