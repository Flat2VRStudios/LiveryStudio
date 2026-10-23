param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Args
)

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "vswhere not found at $vswhere. Install Visual Studio 2022 or the Visual Studio Installer."
}

$vsInstall = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -version "[17.0,18.0)" -property installationPath
if (-not $vsInstall) {
    throw "Visual Studio 2022 with MSBuild not found. Install Visual Studio 2022 with MSBuild."
}

$msbuild = Join-Path $vsInstall "MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
    throw "MSBuild not found at $msbuild."
}

& $msbuild @Args
exit $LASTEXITCODE