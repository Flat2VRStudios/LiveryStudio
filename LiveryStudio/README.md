# Livery Studio user guide

## Create and edit

Extract the full release and run the top-level `LiveryStudio.exe`. Keep the `App` folder beside it; it contains the runtime, content tools and license notices. The editor remembers your working design and saves edits automatically. **Save project** creates a portable copy to back up or share.

Simple mode has six trays:

- **Looks:** preview a design, shuffle colors or try Surprise me. Keep or cancel the preview.
- **Colors:** body swatches and matte, chrome, candy, pearl and other finishes.
- **Stickers:** built-in art, names and race numbers, repeat stamping and imported PNG/TGA/SVG artwork.
- **Paint:** brush, paint eraser, size, mirrored painting and independent-side conversion.
- **Arrange:** select, move, resize, rotate, duplicate and copy artwork to the other door.
- **Photo / Spin:** still photos, contact sheets and animated turntables.

**Hold before** compares the design with the original. Advanced mode provides full layers, material painting, wrap/seam controls, text, patterns, image preparation, SVGs, masks, shared design colors and reusable groups. Its **Add → Quick tools** menu shares the creative trays.

## Camera and Undo

Drag in View to orbit; right-drag while painting. Scroll zooms, middle-drag pans and Ctrl+wheel changes brush/sticker size. Working-angle buttons and Camera offer front, rear, side, top and fitted views.

Ctrl+Z undoes and Ctrl+Shift+Z redoes outside text fields. A completed stroke or applied design change is one history step. Restored pixels appear before background saving completes. In Layers, search, multi-select, lock, group, arrange or temporarily inspect artwork. Original image bytes and editable recipes remain in the project.

Import self-contained SVGs with lettering converted to paths. Scripts, external resources, filters and embedded bitmaps are rejected. Image preparation removes flat backgrounds and crops padding. Decal effects and erase/restore edit stencils without destroying the original artwork. Keep or cancel a preview before applying.

**Settings → Display & quality** changes viewport resolution and filtering, not baked texture resolution. Actual game lighting and damage can differ from the preview.

## Files

| Format | Contents |
| --- | --- |
| `.flproject` | Editable livery, original artwork and Undo history. |
| `.flcar` | Corrected/imported car, donor recipe, artwork and history. |
| `.flart` | Reusable editable artwork group. |
| Package ZIP | Compiled game files; retain the editable project separately. |

Opening a portable file creates a working copy. Working files live in `%LOCALAPPDATA%/FlatOutLiveryStudio`. Saved builds retain source and compiled files; Previous draft and recovery copies help recover replaced work. These personal files do not belong in a release ZIP.

## Use in game

Setup detects the original FlatOut 4 Steam installation and offers a manual fallback. Bundled content tools require no separate folder setting.

Build an ordinary livery, then use Saved builds to export or publish. **Publish** selects FlatOut 4 VR or the original game and asks before publicly uploading the compiled package and chosen preview through your signed-in Steam account. Publishing the same saved build updates its item for that game. Editable project files are excluded. Steam may require ownership or acceptance of its Workshop agreement.

Direct livery imports require a compatible editable installation. The app verifies owned files and retains backups. Restart the game, then select the car and livery in Garage.

## Independent sides and Car Builder

Shared-part protection avoids ambiguous factory UV regions. For Grosse pointe, **Paint → Independent sides** creates a corrected car with separate bumper paint coordinates and retained design history. Save a `.flcar` and open it in Car Builder.

**Use in game → Build & drive** installs that model and paint locally in the original FlatOut 4 Steam game. Choose Grosse pointe's **first livery** in **Garage → Customize → Liveries**. Close the game before installing or restoring. **Restore original car** restores the original loading list and removes the owned override without deleting your project. Live Steam driving and VR model installation remain unverified. This route is separate from texture-only Workshop publishing.

The optional exterior builder accepts static FBX, OBJ and self-contained GLB models. Fit the exterior, update the preview, paint and save a `.flcar`. The first donor is Grosse pointe; its wheels, handling and collision remain connected. Custom collision, new vehicle slots, skinned models and new detachable-panel rigs are unsupported. Custom-exterior installation requires a compatible editable game installation.

## Support

Report the app/Windows versions, car, mode and reproduction steps. Redact personal paths and account details from logs. Back up projects before sharing a minimal reproduction. SECURITY.md covers private security reporting; LICENSE covers the original editor code under MIT; third-party and game assets retain separate terms.
