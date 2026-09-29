$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/runtime.ps1"
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root 'experiments\tencent-streaming-probe'
$out = Join-Path $root 'build\provider-tests'
New-Item -ItemType Directory -Force $out | Out-Null
$refs = @(Get-ChildItem $runtime -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.AssemblyName]::GetAssemblyName($_.FullName); '/r:' + $_.FullName } catch [BadImageFormatException] {}
})
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /noconfig /nostdlib /target:exe "/out:$out\Tests.dll" @refs "$src\ReleaseProviderTests.cs" "$src\VoicePreferences.cs" "$src\AutoNarration.cs" "$src\AutoProtocol.cs" "$src\GameProtocol.cs" "$root\experiments\file-read-egress\FrameDecoder.cs"
if ($LASTEXITCODE -ne 0) { throw 'Release provider tests compilation failed' }
Copy-Item "$src/Probe.runtimeconfig.json" "$out/Tests.runtimeconfig.json" -Force
& dotnet "$out/Tests.dll"
if ($LASTEXITCODE -ne 0) { throw 'Release provider tests failed' }
