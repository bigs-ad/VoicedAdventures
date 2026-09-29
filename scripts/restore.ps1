#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$packages = @(
    @{ Id='naudio.core'; Version='2.2.1'; Target='naudio-2.2.1/naudio.core'; Algorithm='SHA256'; Hash='794645DBFD30E4880663D52D9D9224D55C301FA54228FF623C95D572C7887347' },
    @{ Id='naudio.winmm'; Version='2.2.1'; Target='naudio-2.2.1/naudio.winmm'; Algorithm='SHA256'; Hash='48DA978D715F219489C050EE8C32C64F80FF73718EBA237EBCBC636BA5902BE3' },
    @{ Id='microsoft.diagnostics.tracing.traceevent'; Version='3.2.6'; Target='traceevent-3.2.6/package'; Algorithm='SHA512'; Hash='bLBIoZv5q/4WZCGR0kAMMWPiRalVcoR/6CYTPJJavLBUIyAnzQq8JcaaODVEznbCUfzdXwbVklxL5dpLUbRJ/w==' }
)
foreach ($package in $packages) {
    $target = Join-Path $root ('build/toolchains/' + $package.Target)
    $zip = $target + '.zip'
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    if (!(Test-Path -LiteralPath $zip)) {
        $url = 'https://api.nuget.org/v3-flatcontainer/{0}/{1}/{0}.{1}.nupkg' -f $package.Id,$package.Version
        Invoke-WebRequest -Uri $url -OutFile $zip
    }
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm $package.Algorithm).Hash
    if ($package.Algorithm -eq 'SHA512') { $hash = [Convert]::ToBase64String([Convert]::FromHexString($hash)) }
    if ($hash -cne $package.Hash) { throw "Dependency checksum mismatch: $($package.Id). Remove $zip and retry." }
    Expand-Archive -LiteralPath $zip -DestinationPath $target -Force
}
Push-Location $root
try {
    & npm ci --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'npm dependency restore failed' }
} finally { Pop-Location }
