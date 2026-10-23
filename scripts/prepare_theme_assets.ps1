param(
    [Parameter(Mandatory=$true)][string]$GameSourceRoot
)

$ErrorActionPreference = 'Stop'
$themeRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$themeGameSourceRoot = (Resolve-Path -LiteralPath $GameSourceRoot).Path
$themeSource = Join-Path $themeGameSourceRoot 'Flatout\Data\Assets\GUI_FlatOut\Textures\Menus\Logo.png'
$themeDestinationFolder = Join-Path $themeRepoRoot 'LiveryStudio\App\Assets\Theme'
$themeDestination = Join-Path $themeDestinationFolder 'FlatOut4Logo.png'
$themeExpectedHash = 'D93326FF277FB4EA7CB220719ADC688F5FB3EAA2AFB76486B0F972250CF75CCF'

if (-not (Test-Path -LiteralPath $themeSource -PathType Leaf)) {
    throw "Original FlatOut 4 menu logo is missing: $themeSource"
}
if ((Get-FileHash -LiteralPath $themeSource -Algorithm SHA256).Hash -ne $themeExpectedHash) {
    throw 'The source logo differs from the recorded theme asset. Review its provenance before updating it.'
}

New-Item -ItemType Directory -Path $themeDestinationFolder -Force | Out-Null
Copy-Item -LiteralPath $themeSource -Destination $themeDestination -Force
if ((Get-FileHash -LiteralPath $themeDestination -Algorithm SHA256).Hash -ne $themeExpectedHash) {
    throw 'The copied theme logo did not match the source.'
}
Write-Output "Original FlatOut 4 logo refreshed and SHA-256 verified: $themeDestination"
