# Project, build and import workflow checks

From the repository root, with the .NET 9 SDK:

```powershell
dotnet run --project LiveryStudio/WorkflowChecks/LiveryStudio.WorkflowChecks.csproj --configuration Release --artifacts-path LiveryStudio/artifacts/workflow-checks
```

This executable links the app's actual `GameWorkflow.cs`, `InstalledLibrary.cs`, `DraftRecovery.cs`, and `ProjectFiles.cs` and references the actual Core project. Its 87 checks exercise operation ownership while a texture save is pending, rejected reentry, cancellation cleanup, captured authoring fields, output invalidation, target/receipt preflight, missing editable originals, generated-map recovery, and declined updates. Installed-library checks cover empty/changed/removed states, eligible selection actions, same-target refresh retention, stale scan rejection, folder changes, and retry after failure. Recovery checks cover incomplete form fields, retained editor snapshots, portable archive contents, independent working editor files from raw recovery, and independently editable reopened portable sources. The artifact-path option isolates both projects' build outputs from the app's normal build directories.

Project-file checks call the production Open/Save handlers with controllable picker and Painter tasks. They cover pending-picker and pending-flush reentry, cancellation without draft/build changes, malformed archives/JSON/editor histories/pixels, project/editor car mismatch, hidden corrupt artwork, incomplete-current-draft preservation, failed recovery, explicit Design/Studio navigation, legacy XML and JSON compatibility, safe filename suggestions, post-flush save capture, and preserving an existing destination after a failed save. The underlying project archives, decoded textures and recovery files use real Core code.

`HeadlessHost.cs` replaces the surrounding window, controls, dialogs, file pickers, output-operation guard host, and Painter. Dialogs always decline; pickers can remain pending, cancel, or return a path, and Painter can hold a flush or simulate completed map saves. Project-file tests inject their scratch workspace through the partial test host constructor. The checks use real Core draft, document, target, and import-receipt validation against uniquely named scratch fixtures. They do not compile a livery, render WinUI, compose real texture maps, close or launch a game, or write to a real installation. Successful native bakes and live UI interactions require separate validation.

Fixtures stay under the executable's `scratch` directory for failure inspection. No game assets or compiler installation are required; placeholder files are used only where preflight checks existence, and recovery checks create small valid TGA images. Incomplete `.draft.json` recovery retains existing asset references; it is not a portable archive. A failing assertion exits nonzero.

For a deliberately pinned source snapshot, pass `-p:WorkflowCoreProject=<absolute path to that snapshot's LiveryStudio.Core.csproj>` in addition to a unique artifacts path. Omit it for the repository's current Core source. This changes only the test reference and does not edit either project.
