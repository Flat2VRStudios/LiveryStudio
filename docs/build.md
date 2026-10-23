# Build and test

## Public checks

Install .NET 9 on Windows and run from the repository root:

```powershell
dotnet run --project LiveryStudio/Tests/LiveryStudio.Tests.csproj -c Release -- --portable
dotnet build LiveryStudio/Cli/LiveryStudio.Cli.csproj -c Release
python scripts/audit-public.py
```

The CI workflow also runs the headless workflow, draft replacement and vehicle transaction harnesses in `WorkflowChecks`, `DraftChangeChecks` and `VehicleChecks/LiveryStudio.VehicleRegressionChecks.csproj`. Supply a new `--workspace` directory to the vehicle regression runner. It builds `ImageFileChecks`, whose WinRT storage and image-codec checks should be run from an interactive Windows desktop: hosted execution did not complete during release validation. The native portable launcher is compiled with Visual Studio 2022 C++ tools; it has no game-source dependency.

Portable checks use synthetic geometry and temporary game-layout fixtures. They need no Steam account, installed game or proprietary SDK and do not install a mod or publish a Workshop item. Full GPU, all-car and worker checks require prepared assets and run without `--portable`.

## Full Windows build

Inputs: Visual Studio 2022 with C++ desktop tools and a Windows SDK, .NET 9, Python 3 with Pillow, an authorized game-source checkout, original Workshop Tools and matching native libraries/runtimes. Public source alone cannot reproduce proprietary tools or game assets.

```powershell
./scripts/native-tools/build-vehicle-toolchain.ps1 -GameSourceRoot 'C:/Development/FlatOut4' -Crt90Root '<authorized CRT folder>' -Crt90Manifest '<authorized CRT manifest>' -OutputRoot '<new native build folder>'
./LiveryStudio/build.ps1 -GameSourceRoot 'C:/Development/FlatOut4' -WorkshopTools '<Workshop Tools folder>' -VehicleCompilerRoot '<native build folder>' -PublishFolder publish-release
```

The build reads the game checkout and writes generated files outside it. Original shaders are generated into a private build directory from an explicit source revision. Game-source paths and build provenance belong in local artifacts rather than public records.

```powershell
$env:LIVERY_STUDIO_GAME_SOURCE = 'C:/Development/FlatOut4'
dotnet run --project LiveryStudio/Tests/LiveryStudio.Tests.csproj -c Release
```

Core and CLI builds do not require game inputs. Full WinUI/all-car checks require models, preview assets, the renderer, Workshop runtime and vehicle runtime under `LiveryStudio/artifacts`. See [releasing](releasing.md) for packaging a prepared app.
