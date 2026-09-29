#requires -Version 7.0
param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (!$SkipRestore) { & "$PSScriptRoot/restore.ps1" }
    & node --test tests/release-audit.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Release source audit failed' }
    & node --test tests/player-guide.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Player guide checks failed' }
    & "$PSScriptRoot/build-resource-probe.ps1"
    & "$PSScriptRoot/build-streaming-probe.ps1"
    & "$PSScriptRoot/test-release-providers.ps1"
    & "$PSScriptRoot/test-qwen.ps1"
    & "$PSScriptRoot/test-turtle-packets.ps1"
    foreach ($name in @('addon-tests.lua','file-transport-tests.lua')) {
        & node node_modules/fengari-node-cli/src/lua-cli.js "experiments/turtle-compat/$name"
        if ($LASTEXITCODE -ne 0) { throw "Turtle tests failed: $name" }
    }
    $addon = Join-Path $root 'build/turtle/VoicedAdventures'
    New-Item -ItemType Directory -Force $addon | Out-Null
    $bundle = (@('PacketTransport.lua','FileTransport.lua','Addon.lua') | ForEach-Object {
        [IO.File]::ReadAllText((Join-Path $root "experiments/turtle-compat/$_"))
    }) -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $addon 'Main.lua'),$bundle,[Text.UTF8Encoding]::new($false))
    $source = Join-Path $root 'build/file-read-egress/VoicedAdventures'
    foreach ($name in @('Icon.tga','OWNERSHIP.txt')) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $addon -Force }
    $toc = [IO.File]::ReadAllText((Join-Path $source 'VoicedAdventures.toc'))
    $toc = [regex]::Replace($toc,'(?m)^## Interface:.*$','## Interface: 11200')
    [IO.File]::WriteAllText((Join-Path $addon 'VoicedAdventures.toc'),$toc,[Text.UTF8Encoding]::new($false))
    New-Item -ItemType Directory -Force (Join-Path $addon 'Sounds') | Out-Null
    foreach ($name in @('B','S') + @(0..15 | ForEach-Object { 'H{0:X}' -f $_ })) {
        Copy-Item -LiteralPath (Join-Path $source "Sounds/$name.wav") -Destination (Join-Path $addon 'Sounds') -Force
    }
    Write-Output 'PASS modern and Turtle builds; offline tests complete. No game installation modified.'
} finally { Pop-Location }
