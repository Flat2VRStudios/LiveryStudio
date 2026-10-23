# Draft replacement regression checks

Run `dotnet run --project LiveryStudio/DraftChangeChecks -c Release` from the repository root.

This headless harness links the actual `App/DraftChanges.cs`, `TextureImports.cs`, and `DraftRecovery.cs`. Minimal host controls, picker/storage tasks, and Painter callbacks let checks hold each asynchronous boundary and invoke the real handlers again. The host redirects only `WorkshopService.DefaultWorkspace` to a unique scratch directory; sample lookup delegates to the real Core service. Image decoding, project bundles, editor documents, and recovery files use production Core code.

Coverage includes ownership of pending picker/drop/flush operations; canceled and malformed imports preserving the draft and compiled output; explicit texture destinations, ambiguous map rejection, PNG/TGA decoding and filename routing; recovery of unsaved fields, incomplete drafts, old car identity and flushed maps; failure to write recovery blocking replacement; and Studio/Design navigation. This does not establish native WinUI behavior, preview rendering, or in-game import.

Scratch evidence is retained beneath the harness output's `scratch` directory. `DraftChangeCoreProject` can target an isolated Core project for builds that must avoid the shared Core output.
