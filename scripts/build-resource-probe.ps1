param([switch]$Long,[switch]$TestsOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & node node_modules/fengari-node-cli/src/lua-cli.js -e 'local ok,err=pcall(dofile,"experiments/file-read-egress/book-text-tests.lua");if not ok then print(err);os.exit(1) end'
    if($LASTEXITCODE -ne 0){throw 'Book text tests failed'}
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/transport-timing-tests.lua
    if ($LASTEXITCODE -ne 0) { throw 'Lua transport frame pacing tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js -e 'local ok,err=pcall(dofile,"experiments/file-read-egress/identity-tests.lua");if not ok then print(err);os.exit(1) end'
    if ($LASTEXITCODE -ne 0) { throw 'Lua identity tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/game-tests.lua
    if ($LASTEXITCODE -ne 0) { throw 'Lua resource tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/long-game-tests.lua
    if ($LASTEXITCODE -ne 0) { throw 'Lua long resource tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/text-game-tests.lua
    if ($LASTEXITCODE -ne 0) { throw 'Lua text resource tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/text-game-tests.lua fast
    if ($LASTEXITCODE -ne 0) { throw 'Lua fast resource tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/hex-game-tests.lua
    if ($LASTEXITCODE -ne 0) { throw 'Lua hex resource tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/repeat-game-tests.lua
    if ($LASTEXITCODE -ne 0) { throw 'Lua repeat resource tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/npc-game-tests.lua
    if ($LASTEXITCODE -ne 0) { throw 'Lua NPC resource tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/npc-game-tests.lua burst
    if ($LASTEXITCODE -ne 0) { throw 'Lua NPC burst tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/idbench-game-tests.lua
    if ($LASTEXITCODE -ne 0) { throw 'Lua ID benchmark tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/auto-game-tests.lua
    if ($LASTEXITCODE -ne 0) { throw 'Lua automatic transport tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/controls-game-tests.lua
    if ($LASTEXITCODE -ne 0) { throw 'Lua game controls tests failed.' }
    & node node_modules/fengari-node-cli/src/lua-cli.js experiments/file-read-egress/controls-game-tests.lua legacy
    if ($LASTEXITCODE -ne 0) { throw 'Lua legacy settings tests failed.' }
    if($TestsOnly){return}
    $source = Join-Path $root 'experiments/file-read-egress/addon/VoicedAdventures'
    $out = Join-Path $root 'build/file-read-egress/VoicedAdventures'
    $sounds = Join-Path $out 'Sounds'
    New-Item -ItemType Directory -Path $sounds -Force | Out-Null
    foreach ($name in @('Main.lua','VoicedAdventures.toc','OWNERSHIP.txt','Icon.tga')) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $out $name) -Force
    }
    # Bundle into the already-discovered Lua path so deployment only needs /reload.
    $modelPath=Join-Path $source 'IdentityModels.lua'
    $parts=@((Join-Path $source 'Main.lua'),$modelPath,(Join-Path $source 'NpcRaceFallback.lua'))
    $parts+=@((Join-Path $source 'QuestSpeakers.lua'),(Join-Path $source 'Identity.lua'),(Join-Path $source 'BookText.lua'),(Join-Path $source 'Auto.lua'),(Join-Path $source 'Controls.lua'))
    $bundle=($parts | ForEach-Object {[IO.File]::ReadAllText($_)}) -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $out 'Main.lua'),$bundle,[Text.UTF8Encoding]::new($false))
    $names = @('A','B','S') + @(1..7 | ForEach-Object { "fresh$_" }) + @(0..15 | ForEach-Object { 'H{0:X}' -f $_ })
    foreach ($name in $names) {
        $length = 1600
        if ($Long -and $name -eq 'B') { $length = 1MB }
        if ($Long -and $name -eq 'S') { $length = 8MB }
        if ($name -match '^H[0-9A-F]$') { $length = 1MB }
        $path = Join-Path $sounds "$name.wav"
        $stream = [IO.File]::Create($path)
        $writer = [IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([uint32]($length + 36))
            $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $writer.Write([uint32]16)
            $writer.Write([uint16]1); $writer.Write([uint16]1); $writer.Write([uint32]16000)
            $writer.Write([uint32]32000); $writer.Write([uint16]2); $writer.Write([uint16]16)
            $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([uint32]$length)
            $writer.Write([byte[]]::new($length))
        } finally { $writer.Dispose(); $stream.Dispose() }
        $bytes = [IO.File]::ReadAllBytes($path)
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $actual = [Convert]::ToBase64String($sha.ComputeHash($bytes, 44, $length))
            $expected = [Convert]::ToBase64String($sha.ComputeHash([byte[]]::new($length)))
        } finally { $sha.Dispose() }
        if ($bytes.Length -ne ($length + 44) -or [BitConverter]::ToUInt32($bytes,4) -ne ($length + 36) -or
            [Text.Encoding]::ASCII.GetString($bytes,8,8) -ne 'WAVEfmt ' -or
            [BitConverter]::ToUInt32($bytes,24) -ne 16000 -or
            [BitConverter]::ToUInt16($bytes,34) -ne 16 -or
            [BitConverter]::ToUInt32($bytes,40) -ne $length -or
            $actual -ne $expected) { throw 'WAV validation failed.' }
        Write-Output "PASS $name.wav: $($bytes.Length) bytes, PCM samples all zero"
    }
    Write-Output "PASS $($names.Count) mono PCM WAVs; long mode: $Long"
    Write-Output "Built standalone addon: $out"
} finally { Pop-Location }
