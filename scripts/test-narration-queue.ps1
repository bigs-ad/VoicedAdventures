param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/runtime.ps1"
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'build\narration-queue-tests'
if($OutputDirectory){$out=[IO.Path]::GetFullPath($OutputDirectory)}
$src=Join-Path $root 'experiments\tencent-streaming-probe'
$csc="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force $out | Out-Null
$refs=@(Get-ChildItem $runtime -Filter '*.dll' | ForEach-Object {try{[void][Reflection.AssemblyName]::GetAssemblyName($_.FullName);'/r:'+ $_.FullName}catch [BadImageFormatException]{}})
& $csc /nologo /noconfig /nostdlib /target:exe "/out:$out\QueueTests.dll" @refs "$src\GameProtocol.cs" "$src\AutoProtocol.cs" "$src\AutoNarration.cs" "$src\QueueTests.cs" "$root\experiments\file-read-egress\FrameDecoder.cs"
if($LASTEXITCODE -ne 0){throw 'Queue tests compilation failed'}
Copy-Item "$src/Probe.runtimeconfig.json" "$out/QueueTests.runtimeconfig.json" -Force
& dotnet "$out/QueueTests.dll"
if($LASTEXITCODE -ne 0){throw 'Queue tests failed'}
