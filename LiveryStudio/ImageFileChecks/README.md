# Image file checks

Windows-only regression harness linking the production `App/ImageFiles.cs` and using the actual WinRT PNG encoder/decoder. It requires the .NET 9 SDK and Windows 10 or later; it opens no app window.

From the repository root:

```powershell
dotnet run --project LiveryStudio/ImageFileChecks/LiveryStudio.ImageFileChecks.csproj -c Release
```

Six checks cover concurrent same-key cache creation, reproduction of replacement failure while a PNG is held open for display, immutable-cache retries, normal export replacement, non-race destination errors, and temporary-file cleanup. The old replacement path is deliberately exercised inside a caught exception to prove the failure condition. Test files remain under the harness output's ignored `results` folder.

To avoid rebuilding Core while another task owns its outputs, pass an existing compatible assembly:

```powershell
dotnet run --project LiveryStudio/ImageFileChecks/LiveryStudio.ImageFileChecks.csproj -c Release -p:ImageFileCoreAssembly="$PWD/LiveryStudio/publish-tutorial-stable/LiveryStudio.Core.dll"
```

This verifies the file-race boundary. Native layer-list scrolling and repeated selection remain the app-level smoke test after publishing.
