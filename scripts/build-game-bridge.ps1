param([string]$OutputDirectory,[switch]$TestsOnly)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/runtime.ps1"
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'build\tencent-streaming-probe-game'
if($OutputDirectory){$out=[IO.Path]::GetFullPath($OutputDirectory)}
$src=Join-Path $root 'experiments\tencent-streaming-probe'
$decoder=Join-Path $root 'experiments\file-read-egress'
$csc="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force $out | Out-Null
& $csc /nologo "/out:$out\AutoConnectionTests.exe" "$src\AutoConnection.cs" "$src\AutoConnectionTests.cs"
if($LASTEXITCODE -ne 0){throw 'Connection tests compilation failed'}
& "$out\AutoConnectionTests.exe"
if($LASTEXITCODE -ne 0){throw 'Connection tests failed'}
$refs=@(Get-ChildItem $runtime -Filter '*.dll' | ForEach-Object {
 try { [void][Reflection.AssemblyName]::GetAssemblyName($_.FullName); '/r:'+ $_.FullName } catch [BadImageFormatException] { }
})
& $csc /nologo /noconfig /nostdlib /target:exe "/out:$out\GameTests.dll" @refs "$src\GameProtocol.cs" "$src\GameTests.cs"
if($LASTEXITCODE -ne 0){throw 'Game tests compilation failed'}
Copy-Item "$src/Probe.runtimeconfig.json" "$out/GameTests.runtimeconfig.json" -Force
& dotnet "$out/GameTests.dll"
if($LASTEXITCODE -ne 0){throw 'Game tests failed'}
& $csc /nologo /noconfig /nostdlib /target:exe "/out:$out\AutoTests.dll" @refs "$src\GameProtocol.cs" "$src\AutoProtocol.cs" "$src\AutoNarration.cs" "$src\AutoTests.cs" "$decoder\FrameDecoder.cs"
if($LASTEXITCODE -ne 0){throw 'Auto tests compilation failed'}
Copy-Item "$src/Probe.runtimeconfig.json" "$out/AutoTests.runtimeconfig.json" -Force
Push-Location $root
try {
 $fixtureOutput=& node "$root/node_modules/fengari-node-cli/src/lua-cli.js" "$root/experiments/file-read-egress/auto-game-tests.lua" fixture
 if($LASTEXITCODE -ne 0){throw 'Lua cross-language fixture failed'}
} finally { Pop-Location }
[IO.File]::WriteAllLines("$out/lua-segments.hex",[string[]]$fixtureOutput,[Text.Encoding]::ASCII)
& dotnet "$out/AutoTests.dll" "$out/lua-segments.hex"
if($LASTEXITCODE -ne 0){throw 'Auto tests failed'}
& $csc /nologo "/out:$out\RealtimeTests.exe" "$decoder\RealtimeTests.cs" "$decoder\FrameDecoder.cs"
if($LASTEXITCODE -ne 0){throw 'Decoder tests compilation failed'}
& "$out\RealtimeTests.exe"
if($LASTEXITCODE -ne 0){throw 'Decoder tests failed'}
& $csc /nologo /noconfig /nostdlib /target:exe "/out:$out\BridgeLifecycleTests.dll" @refs "$src\GameProtocol.cs" "$src\GameCapture.cs" "$src\AutoProtocol.cs" "$decoder\FrameDecoder.cs" "$src\BridgeLifecycleTests.cs"
if($LASTEXITCODE -ne 0){throw 'Lifecycle tests compilation failed'}
Copy-Item "$src/Probe.runtimeconfig.json" "$out/BridgeLifecycleTests.runtimeconfig.json" -Force
& dotnet "$out/BridgeLifecycleTests.dll" --offline
if($LASTEXITCODE -ne 0){throw 'Lifecycle tests failed'}
if($TestsOnly){return}
$package=Join-Path $root 'build\toolchains\traceevent-3.2.6\package\lib\netstandard2.0'
$sha=[Security.Cryptography.SHA512]::Create()
try {
 $hash=[Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes((Join-Path $root 'build\toolchains\traceevent-3.2.6\package.zip'))))
 if($hash -cne 'bLBIoZv5q/4WZCGR0kAMMWPiRalVcoR/6CYTPJJavLBUIyAnzQq8JcaaODVEznbCUfzdXwbVklxL5dpLUbRJ/w=='){throw 'TraceEvent package hash mismatch'}
} finally {$sha.Dispose()}
$refs+=('/r:'+(Join-Path $package 'Microsoft.Diagnostics.Tracing.TraceEvent.dll'))
$refs+=(Join-Path $root 'experiments\turtle-compat\PacketTransport.cs')
$refs+=(Join-Path $root 'experiments\turtle-compat\FileTransport.cs')
& $csc /nologo /noconfig /nostdlib /target:exe /platform:x64 "/out:$out\QuestVoiceReceiver.dll" @refs "$src\GameProtocol.cs" "$src\GameCapture.cs" "$src\GameReceiver.cs" "$src\AutoProtocol.cs" "$src\AutoReceiver.cs" "$decoder\FrameDecoder.cs"
if($LASTEXITCODE -ne 0){throw 'Receiver compilation failed'}
Copy-Item "$src/Probe.runtimeconfig.json" "$out/QuestVoiceReceiver.runtimeconfig.json" -Force
foreach($name in @('Microsoft.Diagnostics.Tracing.TraceEvent.dll','Microsoft.Diagnostics.FastSerialization.dll')) {
 Copy-Item -LiteralPath (Join-Path $package $name) -Destination $out -Force
}
$fixture=Join-Path $out 'receiver-fixture'
New-Item -ItemType Directory -Force $fixture | Out-Null
for($i=0;$i -lt 16;$i++){[IO.File]::WriteAllBytes((Join-Path $fixture ('H'+$i.ToString('X')+'.qvr')),[byte[]]@(0))}
foreach($name in @('B','S')){[IO.File]::WriteAllBytes((Join-Path $fixture ($name+'.qvr')),[byte[]]@(0))}
Write-Output "Built game receiver $out"
