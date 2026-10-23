param(
    [Parameter(Mandatory=$true)]
    [string]$Project,
    
    [Parameter(Mandatory=$true)]
    [string]$Action,
    
    [Parameter(Mandatory=$true)]
    [string]$Config,
    
    [string]$Platform = "",
    
    [string]$ExtraArgs = ""
)

$argsList = @(
    "$PSScriptRoot\..\$Project",
    "/t:$Action",
    "/p:VisualStudioVersion=17.0",
    "/p:Configuration=$Config"
)

if (-not [string]::IsNullOrEmpty($Platform) -and $Platform -ne "Any CPU") {
    # Bare value, no embedded quotes — when Platform contains a space
    # ("Any CPU" is filtered above), MSBuild handles tokenization and
    # injecting literal `"` characters here ends up inside OutDir
    # paths (bin\"x64"\Retail) and trips MSB4184.
    $argsList += "/p:Platform=$Platform"
}

$argsList += "/m"
$argsList += "/nologo"
$argsList += "/verbosity:minimal"

if (-not [string]::IsNullOrEmpty($ExtraArgs)) {
    $argsList += "$ExtraArgs"
}

Write-Host "Running: msbuild_vs2022.ps1 $argsList" -ForegroundColor Cyan
& "$PSScriptRoot\msbuild_vs2022.ps1" @argsList
exit $LASTEXITCODE
