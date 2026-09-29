#requires -Version 7.0
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$zip = Join-Path $artifacts 'VoicedAdventures-v0.1.0.zip'
if (Test-Path -LiteralPath $zip) { throw "Archive already exists: $zip. Move or remove it explicitly before packaging again." }
if (!$SkipBuild) { & "$PSScriptRoot/build.ps1" }
$stage = Join-Path $root ('build/package-' + [Guid]::NewGuid().ToString('N'))
$manifest = [Collections.Generic.List[string]]::new()
function Add-ReleaseFile([string]$Source,[string]$Destination) {
    $path = Join-Path $stage $Destination
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $Source) -Destination $path
    $manifest.Add($Destination.Replace('\','/'))
}
foreach ($name in @('QuestVoiceStreaming.dll','QuestVoiceStreaming.runtimeconfig.json','QuestVoiceReceiver.dll','QuestVoiceReceiver.runtimeconfig.json','LocalSpeechHost.exe','NAudio.Core.dll','NAudio.WinMM.dll','Microsoft.Diagnostics.Tracing.TraceEvent.dll','Microsoft.Diagnostics.FastSerialization.dll','NAudio-LICENSE.txt')) {
    Add-ReleaseFile "build/tencent-streaming-probe/$name" "Assistant/$name"
}
Add-ReleaseFile 'build/tencent-streaming-probe/QuestVoiceStreaming.exe' 'Assistant/VoicedAdventures.exe'
Add-ReleaseFile 'docs/TraceEvent-LICENSE.txt' 'Assistant/TraceEvent-LICENSE.txt'
foreach ($variant in @('Modern','Turtle')) {
    $source = if ($variant -eq 'Modern') { 'build/file-read-egress/VoicedAdventures' } else { 'build/turtle/VoicedAdventures' }
    foreach ($name in @('Main.lua','VoicedAdventures.toc','Icon.tga','OWNERSHIP.txt')) {
        Add-ReleaseFile "$source/$name" "$variant/AddOns/VoicedAdventures/$name"
    }
    $names = @('B','S') + @(0..15 | ForEach-Object { 'H{0:X}' -f $_ })
    if ($variant -eq 'Modern') { $names += @('A') + @(1..7 | ForEach-Object { "fresh$_" }) }
    foreach ($name in $names) {
        Add-ReleaseFile "$source/Sounds/$name.wav" "$variant/AddOns/VoicedAdventures/Sounds/$name.wav"
    }
}
foreach ($name in @('README.md','THIRD-PARTY.md','docs/RELEASE-NOTES.md')) { Add-ReleaseFile $name $name }
$hashes = [ordered]@{}
foreach ($name in ($manifest | Sort-Object)) {
    $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash
}
[IO.File]::WriteAllText((Join-Path $stage 'SHA256.json'),($hashes | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$manifest.Add('SHA256.json')
New-Item -ItemType Directory -Force $artifacts | Out-Null
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $actual = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName.Replace('\','/') })
    if ($actual.Count -ne $manifest.Count -or @(Compare-Object ($manifest | Sort-Object) ($actual | Sort-Object)).Count) {
        throw 'Release archive does not match its explicit whitelist'
    }
    foreach ($entry in ($archive.Entries | Where-Object { $_.Name -and $_.FullName -ne 'SHA256.json' })) {
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($hash -cne $hashes[$entry.FullName.Replace('\','/')]) { throw "Archive hash mismatch: $($entry.FullName)" }
    }
} finally { $archive.Dispose() }
$extracted = Join-Path $root ('build/package-check-' + [Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $zip -DestinationPath $extracted
& dotnet (Join-Path $extracted 'Assistant/QuestVoiceStreaming.dll') --ui-check
if ($LASTEXITCODE -ne 0) { throw 'Extracted package UI check failed' }
& (Join-Path $extracted 'Assistant/LocalSpeechHost.exe') --check
if ($LASTEXITCODE -notin @(0,2)) { throw 'Extracted package system speech check failed' }
[pscustomobject]@{ Archive=$zip; Files=$manifest.Count; SHA256=(Get-FileHash $zip -Algorithm SHA256).Hash; Installed=$false } | ConvertTo-Json
