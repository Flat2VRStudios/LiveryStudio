# Vehicle regression checks

The existing `Program.cs` remains the native build smoke runner. The separate regression project tests synthetic GLB files, portable `.flcar` integrity and editable history, immutable source replacement, and the ten-file vehicle transaction journal. It creates fresh synthetic game-layout folders and never modifies an installed game or runs a native tool.

Build and run from the repository root in PowerShell. Supply a new fixture directory each time; previous runs are retained for inspection.

```powershell
$run = Join-Path $env:LOCALAPPDATA ('LiveryStudioBuilds/vehicle-regression-' + [Guid]::NewGuid().ToString('N'))
dotnet build LiveryStudio/VehicleChecks/LiveryStudio.VehicleRegressionChecks.csproj -c Release --artifacts-path (Join-Path $run 'build')
dotnet (Join-Path $run 'build/bin/LiveryStudio.VehicleRegressionChecks/release/LiveryStudio.VehicleRegressionChecks.dll') --workspace (Join-Path $run 'fixtures')
```

`fixtures/results.json` records individual results and the actual Core DLL hash. Outputs remain outside the source tree. The entry source intentionally uses a `.cs.txt` suffix and explicit compile items so it does not add a second top-level entry point to the existing smoke runner.

Recovery tests materialize partial states from real `VehicleImportManager.Install` journals: before writes, after geometry/paint/PACLOG/registration writes, and during restore. They check resumability, byte-identical original file sets, idempotence, and refusal to overwrite outside edits. This does not simulate sudden power loss, test filesystem durability, establish native material/collision correctness, or validate the game.
