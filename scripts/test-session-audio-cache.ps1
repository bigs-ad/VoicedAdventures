param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$src=Join-Path $root 'experiments\tencent-streaming-probe'
if(!$OutputDirectory){$OutputDirectory=Join-Path $root 'build/cache-tests'}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$csc="$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
& $csc /nologo /target:exe "/out:$OutputDirectory\SessionAudioCacheTests.exe" "$src\SessionAudioCache.cs" "$src\SessionAudioCacheTests.cs"
if($LASTEXITCODE -ne 0){throw 'Cache tests compilation failed'}
& "$OutputDirectory/SessionAudioCacheTests.exe"
if($LASTEXITCODE -ne 0){throw 'Cache tests failed'}
