param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/runtime.ps1"
$root=Split-Path $PSScriptRoot -Parent
$src=Join-Path $root 'experiments\tencent-streaming-probe'
$out=Join-Path $root 'build/local-speech-tests'
if($OutputDirectory){$out=[IO.Path]::GetFullPath($OutputDirectory)}
foreach($name in @('LocalSpeech.cs','LocalSpeechHost.cs','VoicePreferences.cs')){
 if(!(Test-Path (Join-Path $src $name))){throw "FAIL: local speech implementation missing: $name"}
}
New-Item -ItemType Directory -Force $out | Out-Null
$csc="$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
$speech=Get-ChildItem "$env:WINDIR/Microsoft.NET/assembly/GAC_MSIL/System.Speech" -Filter System.Speech.dll -Recurse | Select-Object -First 1
if(!$speech){throw 'System.Speech Framework assembly is unavailable'}
& $csc /nologo /target:exe "/r:$($speech.FullName)" "/out:$out\LocalSpeechHost.exe" "$src\LocalSpeechHost.cs"
if($LASTEXITCODE -ne 0){throw 'Local speech helper compilation failed'}
& $csc /nologo /target:exe "/out:$out\LocalSpeechFakeHost.exe" "$src\LocalSpeechFakeHost.cs"
if($LASTEXITCODE -ne 0){throw 'Fake helper compilation failed'}
$refs=@(Get-ChildItem $runtime -Filter '*.dll' | ForEach-Object {
 try{[void][Reflection.AssemblyName]::GetAssemblyName($_.FullName);'/r:'+ $_.FullName}catch [BadImageFormatException]{}
})
& $csc /nologo /noconfig /nostdlib /target:exe "/out:$out\LocalSpeechTests.dll" @refs "$src\LocalSpeechTests.cs" "$src\LocalSpeech.cs" "$src\VoicePreferences.cs"
if($LASTEXITCODE -ne 0){throw 'Local speech tests compilation failed'}
Copy-Item "$src/Probe.runtimeconfig.json" "$out/LocalSpeechTests.runtimeconfig.json" -Force
& dotnet "$out/LocalSpeechTests.dll" "$out/LocalSpeechFakeHost.exe"
if($LASTEXITCODE -ne 0){throw 'Local speech tests failed'}
& "$out/LocalSpeechHost.exe" --check
if($LASTEXITCODE -eq 2){Write-Output 'No installed enabled Chinese voice. Local playback will report this explicitly.'}
elseif($LASTEXITCODE -ne 0){throw 'Local speech self-check failed'}
