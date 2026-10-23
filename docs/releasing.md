# Preparing a release

The current candidate is **0.1.0**. See [the recorded checks and remaining limits](release-checks.md). Keep the repository private until the release owner approves the source snapshot and binary distribution. Earlier commits and branches still contain archived development records and copied recovery material; cleaning the current tree does not remove history.

1. Run the source audit, portable checks, full prepared-asset checks and `dotnet list LiveryStudio/App/LiveryStudio.App.csproj package --vulnerable --include-transitive`.
2. Build to a fresh `publish-release` folder. Check the version, startup, existing-project reopening and normal closing.
3. Confirm redistribution authorization and notices for the exact proprietary runtime/game bundle. Original editor code uses the MIT License.
4. Build the native launcher and package with an explicit source revision. The package tool checks manifests and private/debug debris, creates SHA-256 inventories, extracts the ZIP and checks every file again. The ZIP has just `LiveryStudio.exe`, `README.md` and `App/` at the top level. Test the top-level launcher from the extracted package.
5. Export source from a committed revision. The ZIP contains no `.git` directory or earlier history. Review it before creating a fresh public repository or approving a history replacement.

```powershell
./scripts/run_build.ps1 -Project LiveryStudio/Native/Launcher.vcxproj -Action Build -Config Release -Platform x64
python scripts/package-release.py --published LiveryStudio/publish-release --output LiveryStudio/artifacts/releases/0.1.0 --revision '<commit SHA>'
python scripts/export-public-source.py --output LiveryStudio/artifacts/releases/0.1.0 --revision '<commit SHA>'
```

Keep SHA256SUMS, package verification and dependency inventories with the candidate. Never ship working projects, credentials, game saves, receipts, private input manifests, PDBs or `steam_appid.txt`.

The raw MSBuild publish folder is an intermediate input. Distribute the verified package, which puts its dependencies inside `App`. Use `--unpacked '<new folder>'` to retain the verified extraction for smoke testing. The launcher uses its own location, forwards arguments, and starts the existing app without a console or extraction step. Both executables need signing. A fully self-extracting WinUI build was prototyped but failed to locate its XAML resources at startup, so it is not the release path.

Use CHANGELOG.md for release notes. Distinguish texture-only Workshop publishing from local independent-side Steam model installation. The latter currently targets Grosse pointe in the original Steam game; live Steam driving and VR model installation remain pending. A clean-PC run and code signing are outstanding.

Changing repository visibility and publishing are separate release-owner actions. Preparing or checking a ZIP does not publish it.
