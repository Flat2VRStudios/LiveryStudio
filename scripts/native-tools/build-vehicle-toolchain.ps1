param(
    [Parameter(Mandatory = $true)][string]$GameSourceRoot,
    [Parameter(Mandatory = $true)][string]$Crt90Root,
    [Parameter(Mandatory = $true)][string]$Crt90Manifest,
    [string]$OutputRoot = (Join-Path $env:LOCALAPPDATA ('LiveryStudioBuilds\v-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))),
    [switch]$ProbeAssets,
    [switch]$ObserveModuleLoads
)

# Developer build step. The generated worker requires no compiler installation
# to process game assets, but this script requires the existing game source build inputs.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($ObserveModuleLoads -and -not $ProbeAssets) { throw 'Module observation requires -ProbeAssets.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$outputPath = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $outputPath) { throw 'Use a new output folder to preserve earlier results.' }
$gameSourcePath = (Resolve-Path -LiteralPath $GameSourceRoot).Path
$crtPath = (Resolve-Path -LiteralPath $Crt90Root).Path
$crtManifestPath = (Resolve-Path -LiteralPath $Crt90Manifest).Path
Push-Location $repoRoot
try {
    $profiles = @(
        @{Name='compiler'; Project='Compiler'; Options=@('--geometry-only')},
        @{Name='importer'; Project='Importer'; Options=@('--stable-vertex-keys')},
        @{Name='writer'; Project='Writer'; Options=@()},
        @{Name='cooker'; Project='WriterPhysX'; Options=@()},
        @{Name='reader'; Project='Compiler'; Options=@('--entry-source', (Join-Path $repoRoot 'LiveryStudio\Native\VehicleSceneProbe.cpp'))}
    )
    foreach ($profile in $profiles) {
        $buildPath = Join-Path $outputPath $profile.Name
        & python (Join-Path $PSScriptRoot 'build-vehicle-compiler.py') --game-source $gameSourcePath --output $buildPath --project $profile.Project @($profile.Options)
        if ($LASTEXITCODE -ne 0) { throw ('Project generation failed: ' + $profile.Name) }
        & (Join-Path $repoRoot 'scripts\msbuild_vs2022.ps1') (Join-Path $buildPath ('Vehicle' + $profile.Project + '.vcxproj')) /t:Build /p:Configuration=Release /p:Platform=x64 /p:BuildProjectReferences=false /nologo /verbosity:minimal *> (Join-Path $buildPath 'build.log')
        if ($LASTEXITCODE -ne 0) { throw ('Build failed; inspect ' + (Join-Path $buildPath 'build.log')) }
        Write-Output ($profile.Name + ' built')
    }
    & python (Join-Path $PSScriptRoot 'record-vehicle-build.py') --game-source $gameSourcePath --build-root $outputPath --output (Join-Path $outputPath 'build-provenance.json')
    if ($LASTEXITCODE -ne 0) { throw 'Build provenance collection failed.' }
    & python (Join-Path $PSScriptRoot 'prepare-vehicle-worker.py') --game-source $gameSourcePath --compiler (Join-Path $outputPath 'compiler\bin\Compiler_r.exe') --importer (Join-Path $outputPath 'importer\bin\Importer64_r.dll') --writer (Join-Path $outputPath 'writer\bin\Writer64_r.dll') --physics-cooker (Join-Path $outputPath 'cooker\bin\WriterPhysX64_r.dll') --crt90 $crtPath --crt90-manifest $crtManifestPath --output (Join-Path $outputPath 'worker')
    if ($LASTEXITCODE -ne 0) { throw 'Worker packaging failed.' }
    if ($ProbeAssets) {
        $probeReport = Join-Path $outputPath 'compiler-probe.json'
        & python (Join-Path $PSScriptRoot 'probe-vehicle-compiler.py') --runtime (Join-Path $outputPath 'worker') --archive (Join-Path $gameSourcePath 'Flatout\Data1\Assets\GRAPH\Cars.zip') --workspace (Join-Path $outputPath 'probe') --output $probeReport --representatives --include-scenes --reader (Join-Path $outputPath 'reader\bin\Compiler_r.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Native probe runner failed.' }
        & python (Join-Path $PSScriptRoot 'verify-vehicle-compiler.py') --probe $probeReport --output (Join-Path $outputPath 'compiler-validation.json')
        if ($LASTEXITCODE -ne 0) { throw 'Native compiler validation failed.' }
        if ($ObserveModuleLoads) {
            & python (Join-Path $PSScriptRoot 'audit-vehicle-runtime.py') --probe $probeReport --output (Join-Path $outputPath 'runtime-validation.json')
            if ($LASTEXITCODE -ne 0) { throw 'Module-load audit failed.' }
        }
    }
    Write-Output ('Native worker: ' + (Join-Path $outputPath 'worker'))
}
finally { Pop-Location }
