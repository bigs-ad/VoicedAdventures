$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $out=Join-Path $root 'build\turtle-compat'
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    $fixture=& node node_modules/fengari-node-cli/src/lua-cli.js experiments/turtle-compat/packet-fixture.lua
    if($LASTEXITCODE -ne 0){throw 'Lua packet fixture failed'}
    [IO.File]::WriteAllLines((Join-Path $out 'packet-fixture.txt'),[string[]]$fixture,[Text.UTF8Encoding]::new($false))
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo "/out:$out\PacketTests.exe" experiments\turtle-compat\PacketTransport.cs experiments\turtle-compat\PacketTests.cs
    if($LASTEXITCODE -ne 0){throw 'Packet tests compilation failed'}
    & (Join-Path $out 'PacketTests.exe') (Join-Path $out 'packet-fixture.txt')
    if($LASTEXITCODE -ne 0){throw 'Packet tests failed'}
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo "/out:$out\FileTransportTests.exe" experiments\turtle-compat\PacketTransport.cs experiments\turtle-compat\FileTransport.cs experiments\turtle-compat\FileTransportTests.cs
    if($LASTEXITCODE -ne 0){throw 'File transport tests compilation failed'}
    & (Join-Path $out 'FileTransportTests.exe') (Join-Path $out 'packet-fixture.txt')
    if($LASTEXITCODE -ne 0){throw 'File transport tests failed'}
} finally {Pop-Location}
