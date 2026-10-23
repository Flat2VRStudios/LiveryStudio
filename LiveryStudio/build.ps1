param(
    [string]$WorkshopTools = (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\FlatOut 4 Total Insanity Workshop Tools'),
    [Parameter(Mandatory=$true)][string]$GameSourceRoot,
    [switch]$Package,
    [string]$VehicleCompilerRoot = '',
    [ValidatePattern('^publish(?:-[a-zA-Z0-9-]+)?$')]
    [string]$PublishFolder = 'publish'
)
$ErrorActionPreference = 'Stop'
$studioRoot = $PSScriptRoot
$repoRoot = Split-Path $studioRoot -Parent
$GameSourceRoot = (Resolve-Path -LiteralPath $GameSourceRoot -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath (Join-Path $GameSourceRoot 'Flatout\Data\Assets')) -or -not (Test-Path -LiteralPath (Join-Path $GameSourceRoot 'Flatout\Code\Sources\FODefines.h'))) {
    throw 'GameSourceRoot must point to a game source checkout containing its original source assets and car definitions.'
}
$bundleRoot = Join-Path $studioRoot 'artifacts\workshop-runtime'
$publishRoot = Join-Path $studioRoot $PublishFolder
# The original packager writes a diagnostic log beside its executable during smoke builds.
# Remove only these build-owned generated logs before collecting or packaging the bundle.
foreach ($generatedLog in @((Join-Path $bundleRoot 'Bin\KtPkg.log'),(Join-Path $publishRoot 'Workshop\Bin\KtPkg.log'))) {
    if (Test-Path -LiteralPath $generatedLog) { Remove-Item -LiteralPath $generatedLog -Force }
}
& (Join-Path $studioRoot 'generate_icon.ps1')
$runtimeNames = @('d3dx9_43.dll','D3DCOMPILER_43.dll','XINPUT1_3.dll','MSVCP100.dll','MSVCR100.dll','MSVCP110.dll','MSVCR110.dll')
foreach ($name in @('Compiler_r.exe','KtPkg.exe','SteamWorkShopSender.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $WorkshopTools "Bin\$name"))) { throw "Missing original build input: $name in $WorkshopTools" }
}
New-Item -ItemType Directory -Path (Join-Path $bundleRoot 'Bin'),(Join-Path $bundleRoot 'Doc') -Force | Out-Null
foreach ($part in @('Bin','Doc')) {
    $sourceRoot = Join-Path $WorkshopTools $part
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        if ($file.Extension -in @('.log','.pdb','.ilk','.lib','.exp') -or $file.Name -eq 'steam_appid.txt') { continue }
        $relative = [IO.Path]::GetRelativePath($sourceRoot,$file.FullName)
        $destination = Join-Path (Join-Path $bundleRoot $part) $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
}
$legacySystemRoot = Join-Path $env:WINDIR 'SysWOW64'
foreach ($name in $runtimeNames) {
    $source = Join-Path $legacySystemRoot $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing x86 native runtime build input: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $bundleRoot "Bin\$name") -Force
}
& (Join-Path $studioRoot 'prepare_compiler_compatibility.ps1')
# The x64 Steamworks publisher explicitly targets either VR or the original game.
Copy-Item -LiteralPath (Join-Path $GameSourceRoot 'Flatout/Data/steam_api64.dll') -Destination (Join-Path $bundleRoot 'Bin/steam_api64.dll') -Force
$manifest = [ordered]@{
    Description = 'Original FlatOut 4 native compiler, packager, uploader, plugins, car templates, legacy x86 dependencies, and pinned DXVK compiler compatibility runtime.'
    Files = @(Get-ChildItem -LiteralPath $bundleRoot -Recurse -File | Where-Object { $_.Name -notin @('bundle-manifest.json','steam_appid.txt') -and $_.Extension -notin @('.log','.pdb','.ilk','.lib','.exp') } | ForEach-Object {
        [ordered]@{ Path = [IO.Path]::GetRelativePath($bundleRoot,$_.FullName).Replace('\','/'); Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $bundleRoot 'bundle-manifest.json') -Encoding utf8
Push-Location $repoRoot
try {
    & .\scripts\run_build.ps1 -Project LiveryStudio/Native/Launcher.vcxproj -Action Build -Config Release -Platform x64
    if ($LASTEXITCODE -ne 0) { throw 'Portable launcher build failed' }
    & .\scripts\run_build.ps1 -Project LiveryStudio/Native/CompilerGraphicsProbe.vcxproj -Action Build -Config Release -Platform Win32
    if ($LASTEXITCODE -ne 0) { throw 'Compiler graphics probe build failed' }
    & LiveryStudio/artifacts/compiler-runtime/CompilerGraphicsProbe.exe (Join-Path $bundleRoot 'Compatibility\d3d9.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Bundled compiler compatibility runtime failed its device and texture-allocation test' }
    & .\scripts\run_build.ps1 -Project LiveryStudio/Native/MeshConverter.vcxproj -Action Build -Config Release -Platform x64
    if ($LASTEXITCODE -ne 0) { throw 'Preview mesh converter build failed' }
    $python = (Get-Command python -ErrorAction Stop).Source
    & $python LiveryStudio/prepare_models.py --game-source $GameSourceRoot
    if ($LASTEXITCODE -ne 0) { throw 'Car model preparation failed' }
    & $python LiveryStudio/prepare_preview_assets.py --game-source $GameSourceRoot
    if ($LASTEXITCODE -ne 0) { throw 'Original preview material packing failed' }
    & .\scripts\run_build.ps1 -Project LiveryStudio/Native/Preview.vcxproj -Action Build -Config Release -Platform x64
    if ($LASTEXITCODE -ne 0) { throw 'Native Direct3D preview build failed' }
    if (-not $VehicleCompilerRoot) {
        $locator = Join-Path $studioRoot 'artifacts/vehicle-compiler-final-root.txt'
        if (Test-Path -LiteralPath $locator) { $VehicleCompilerRoot = (Get-Content -LiteralPath $locator -Raw).Trim() }
    }
    if (-not $VehicleCompilerRoot -or -not (Test-Path -LiteralPath (Join-Path $VehicleCompilerRoot 'worker/Bin/Compiler_r.exe'))) {
        throw 'Build the isolated vehicle compiler first with scripts/native-tools/build-vehicle-toolchain.ps1, then pass -VehicleCompilerRoot. End users receive the compiled worker in the app.'
    }
    & .\scripts\msbuild_vs2022.ps1 LiveryStudio/Native/VehicleSceneBridge.vcxproj /t:Build /p:Configuration=Release /p:Platform=x64 "/p:GameSourceRoot=$GameSourceRoot" "/p:OutDir=$VehicleCompilerRoot/bridge/bin/" "/p:IntDir=$VehicleCompilerRoot/bridge/obj/" /nologo /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Vehicle scene composer build failed' }
    $vehicleRuntime = Join-Path $studioRoot 'artifacts/vehicle-runtime'
    & $python LiveryStudio/prepare_vehicle_donors.py --game-source $GameSourceRoot --output (Join-Path $vehicleRuntime 'Donors') --only 0
    if ($LASTEXITCODE -ne 0) { throw 'Bundled vehicle donor preparation failed' }
    $effectWriter = Join-Path $VehicleCompilerRoot 'paint-writer'
    & $python scripts/native-tools/build-vehicle-compiler.py --game-source $GameSourceRoot --output $effectWriter --project WriterWin32
    if ($LASTEXITCODE -ne 0) { throw 'Paint effect writer preparation failed' }
    & .\scripts\msbuild_vs2022.ps1 (Join-Path $effectWriter 'VehicleWriterWin32.vcxproj') /t:Build /p:Configuration=Release /p:Platform=x64 /p:BuildProjectReferences=false /nologo /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Paint effect writer build failed' }
    $effectBuild = Join-Path $VehicleCompilerRoot ('paint-effect-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    & $python scripts/native-tools/build-paint-sides.py --game-source $GameSourceRoot --runtime (Join-Path $VehicleCompilerRoot 'worker') --writer (Join-Path $effectWriter 'bin/WriterWin3264_r.dll') --output $effectBuild
    if ($LASTEXITCODE -ne 0) { throw 'Independent paint shader build failed' }
    & $python LiveryStudio/prepare_vehicle_runtime.py --compiler-root $VehicleCompilerRoot --game-source $GameSourceRoot --output $vehicleRuntime --paint-effect (Join-Path $effectBuild 'Data/WIN32/EFFECTS/PAINTSIDES.PFX')
    if ($LASTEXITCODE -ne 0) { throw 'Bundled vehicle runtime preparation failed' }
    & .\scripts\run_build.ps1 -Project LiveryStudio/App/LiveryStudio.App.csproj -Action Restore -Config Release -Platform x64
    if ($LASTEXITCODE -ne 0) { throw 'App restore failed' }
    & .\scripts\run_build.ps1 -Project LiveryStudio/Tests/LiveryStudio.Tests.csproj -Action Restore -Config Release -Platform x64
    if ($LASTEXITCODE -ne 0) { throw 'Tests restore failed' }
    & .\scripts\run_build.ps1 -Project LiveryStudio/Tests/LiveryStudio.Tests.csproj -Action Build -Config Release -Platform x64
    if ($LASTEXITCODE -ne 0) { throw 'Tests build failed' }
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    $previousGameSourceRoot = $env:LIVERY_STUDIO_GAME_SOURCE
    try {
        $env:LIVERY_STUDIO_GAME_SOURCE = $GameSourceRoot
        & $dotnet LiveryStudio/Tests/bin/x64/Release/net9.0/LiveryStudio.Tests.dll
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    } finally { $env:LIVERY_STUDIO_GAME_SOURCE = $previousGameSourceRoot }
    & .\scripts\run_build.ps1 -Project LiveryStudio/App/LiveryStudio.App.csproj -Action Publish -Config Release -Platform x64 -ExtraArgs "/p:OutDir=../artifacts/build/;PublishDir=../$PublishFolder/"
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed' }
} finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $studioRoot 'README.md') -Destination (Join-Path $publishRoot 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $studioRoot 'Native\third_party\ufbx\LICENSE') -Destination (Join-Path $publishRoot 'ufbx-LICENSE.txt') -Force
foreach ($index in 0..26) {
    $name = '{0:00}.flsm' -f $index
    $model = Join-Path $publishRoot "Models\$name"
    if (-not (Test-Path -LiteralPath $model) -or (Get-FileHash -LiteralPath $model).Hash -ne (Get-FileHash -LiteralPath (Join-Path $studioRoot "artifacts\models\$name")).Hash) { throw "Missing or stale preview model: $name" }
}
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'LiveryPreview.dll'))) { throw 'Native 3D renderer is missing' }
foreach ($file in $manifest.Files) {
    $path = Join-Path (Join-Path $publishRoot 'Workshop') $file.Path
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.SHA256) { throw "Incomplete published bundle: $($file.Path)" }
}
$previewManifest = Get-Content -LiteralPath (Join-Path $studioRoot 'artifacts\preview-assets\asset-manifest.json') -Raw | ConvertFrom-Json
foreach ($previewFile in $previewManifest) {
    $previewPath = Join-Path (Join-Path $publishRoot 'Preview') $previewFile.Path
    if (-not (Test-Path -LiteralPath $previewPath) -or (Get-FileHash -LiteralPath $previewPath).Hash -ne $previewFile.SHA256) { throw "Missing or stale original preview asset: $($previewFile.Path)" }
}
$vehicleManifest = Get-Content -LiteralPath (Join-Path $vehicleRuntime 'bundle-manifest.json') -Raw | ConvertFrom-Json
foreach ($vehicleFile in $vehicleManifest.Files) {
    $path = Join-Path (Join-Path $publishRoot 'VehicleTools') $vehicleFile.Path
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $vehicleFile.SHA256) { throw "Incomplete bundled car worker: $($vehicleFile.Path)" }
}
foreach ($notice in @('LICENSE','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $notice) -Destination (Join-Path $publishRoot $notice) -Force
}
if ($Package) {
    $revision = (& git -C $repoRoot rev-parse HEAD).Trim()
    $portableRoot = Join-Path $studioRoot ($PublishFolder + '-portable')
    & python (Join-Path $repoRoot 'scripts/package-release.py') --published $publishRoot --output (Join-Path $studioRoot 'artifacts/releases/0.1.0') --revision $revision --unpacked $portableRoot
    if ($LASTEXITCODE -ne 0) { throw 'Release verification failed' }
    Write-Host "Ready: $(Join-Path $portableRoot 'LiveryStudio.exe')"
} else {
    Write-Host "Published build input: $publishRoot. Use -Package to create the tidy portable release."
}
