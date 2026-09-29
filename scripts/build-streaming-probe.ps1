param([switch]$TestsOnly,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/runtime.ps1"
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'build\tencent-streaming-probe'
if($OutputDirectory){$out=[IO.Path]::GetFullPath($OutputDirectory)}
$src=Join-Path $root 'experiments\tencent-streaming-probe'
$csc="$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
New-Item -ItemType Directory -Force $out | Out-Null
$refs=@(Get-ChildItem $runtime -Filter '*.dll' | ForEach-Object {
 try { [void][Reflection.AssemblyName]::GetAssemblyName($_.FullName); '/r:'+ $_.FullName } catch [BadImageFormatException] { }
})
$refs+=('/r:'+(Join-Path $desktop 'System.Security.Cryptography.ProtectedData.dll'))
& $csc /nologo /noconfig /nostdlib /target:exe "/out:$out\Tests.dll" @refs "$src\Core.cs" "$src\Tests.cs" "$src\CredentialStore.cs" "$src\CredentialTests.cs"
if($LASTEXITCODE -ne 0){throw 'Tests compilation failed'}
Copy-Item "$src/Probe.runtimeconfig.json" "$out/Tests.runtimeconfig.json" -Force
& dotnet "$out/Tests.dll"
if($LASTEXITCODE -ne 0){throw 'Tests failed'}
if($TestsOnly){return}
& $csc /nologo "/out:$out\PlaybackGainTests.exe" "$src\PlaybackGain.cs" "$src\PlaybackGainTests.cs"
if($LASTEXITCODE -ne 0){throw 'Playback gain tests compilation failed'}
& "$out\PlaybackGainTests.exe"
if($LASTEXITCODE -ne 0){throw 'Playback gain tests failed'}
& "$PSScriptRoot/build-game-bridge.ps1" -OutputDirectory $out
if($LASTEXITCODE -ne 0){throw 'Game bridge build failed'}
& "$PSScriptRoot/test-local-speech.ps1" -OutputDirectory $out
if($LASTEXITCODE -ne 0){throw 'Local speech checks failed'}
& "$PSScriptRoot/test-session-audio-cache.ps1" -OutputDirectory $out
if($LASTEXITCODE -ne 0){throw 'Session audio cache checks failed'}
& "$PSScriptRoot/test-narration-queue.ps1" -OutputDirectory $out
if($LASTEXITCODE -ne 0){throw 'Narration queue checks failed'}
$pins=@{'naudio.core'='794645DBFD30E4880663D52D9D9224D55C301FA54228FF623C95D572C7887347'; 'naudio.winmm'='48DA978D715F219489C050EE8C32C64F80FF73718EBA237EBCBC636BA5902BE3'}
foreach($id in $pins.Keys){
 $pkg=Join-Path $root "build/toolchains/naudio-2.2.1/$id"
 if((Get-FileHash "$pkg.zip" -Algorithm SHA256).Hash -ne $pins[$id]){throw 'Dependency checksum mismatch'}
 Expand-Archive -LiteralPath "$pkg.zip" -DestinationPath $pkg -Force
 foreach($dll in Get-ChildItem "$pkg/lib/netstandard2.0" -Filter '*.dll'){
  Copy-Item $dll.FullName $out -Force
  $refs+=('/r:'+$dll.FullName)
 }
}
foreach($name in @('System.Windows.Forms','System.Windows.Forms.Primitives','System.Drawing.Common','Accessibility','System.Private.Windows.Core')) { $refs+=('/r:'+(Join-Path $desktop "$name.dll")) }
$refs+="/win32icon:$root\assets\quest-voice\QuestVoice.ico"
$refs+="/resource:$root\assets\quest-voice\QuestVoice.ico,QuestVoice.ico"
$refs+="$src\QwenPlayback.cs"
$refs+="$root\experiments\qwen-voice-probe\Core.cs"
$refs+="$root\experiments\qwen-voice-probe\Store.cs"
& $csc /nologo /noconfig /nostdlib /target:winexe /platform:x64 "/out:$out\QuestVoiceStreaming.dll" @refs "$src\PlaybackGain.cs" "$src\Core.cs" "$src\Audio.cs" "$src\Program.cs" "$src\BasicWindow.cs" "$src\SettingsWindow.cs" "$src\SessionWindow.cs" "$src\SessionAudioCache.cs" "$src\AutoConnection.cs" "$src\LocalSpeech.cs" "$src\VoicePreferences.cs" "$src\AutoWindow.cs" "$src\AutoNarration.cs" "$src\CredentialStore.cs" "$src\GameCapture.cs" "$src\GameProtocol.cs" "$src\AutoProtocol.cs" "$root\experiments\file-read-egress\FrameDecoder.cs"
if($LASTEXITCODE -ne 0){throw 'Window compilation failed'}
Copy-Item "$src/Probe.runtimeconfig.json" "$out/QuestVoiceStreaming.runtimeconfig.json" -Force
Copy-Item "$src/NAudio-LICENSE.txt" $out -Force
& $csc /nologo /target:winexe /r:System.Windows.Forms.dll "/win32icon:$root\assets\quest-voice\QuestVoice.ico" "/out:$out\QuestVoiceStreaming.exe" "$src\Launcher.cs"
if($LASTEXITCODE -ne 0){throw 'Launcher compilation failed'}
Write-Output "Built $out"
