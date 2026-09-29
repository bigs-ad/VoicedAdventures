$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$runtimes = @(& $dotnet --list-runtimes)
function Find-Runtime([string]$Name) {
    $matches = @($runtimes | ForEach-Object {
        if ($_ -match ('^' + [regex]::Escape($Name) + ' (9\.0\.\d+) \[(.+)\]$')) {
            [pscustomobject]@{ Version=[version]$Matches[1]; Path=(Join-Path $Matches[2] $Matches[1]) }
        }
    } | Where-Object { $_.Version -ge [version]'9.0.13' } | Sort-Object Version -Descending)
    if (!$matches.Count) { throw "Install the Windows x64 .NET 9 Desktop Runtime: missing $Name 9.0.13 or later 9.0.x" }
    return $matches[0].Path
}
$runtime = Find-Runtime 'Microsoft.NETCore.App'
$desktop = Find-Runtime 'Microsoft.WindowsDesktop.App'
