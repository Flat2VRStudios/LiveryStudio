# Simple mode

Start with the car and four trays: **Looks, Colors, Stickers and Paint**. The car picker includes pictures of all 27 cars. Advanced mode opens the full existing studio.

The mode button names its destination: **Advanced mode** opens Advanced, and **Simple mode** returns to Simple. In Advanced, **Add → Quick tools · looks, colors & stickers** opens the same quick trays alongside the full inspector, including built-in artwork, palette shuffle, numbers, repeat stamping and the paint eraser. Image drag-and-drop works in both modes. Applying a starter from the painting workspace is one Undo in either mode.

- **Looks:** six designs previewed on your selected car. Pick a card, inspect the full-size result, then Keep look or Cancel. Keeping a look replaces the artwork in one Undo step; Undo restores the prior artwork, paint colors and finish. Shuffle colors changes a coordinated palette. Surprise me previews another look and palette before you keep it.
- **Colors:** palette chips, body-color swatches, a full picker, and Matte/Gloss/Metallic/Chrome/Candy/Pearl finishes. Choosing body paint gives the car a clean paint foundation while keeping strokes and stickers. Stock paint restores the original texture beneath your artwork. Palette swaps update linked starter accents and readable number ink; personal images and unlinked brush strokes keep their colors.
- **Stickers:** eight original built-in vector designs, Your image, Number/name and Recent artwork. Imported PNG transparency is preserved; TGA and self-contained SVG are supported too. Drop an image onto the canvas to prepare it, hover to preview, then click to place. Keep stamping repeats the same image; Edit last sticker selects it for moving, resizing or turning. Both doors adds matching, readable artwork.
- **Number/name:** add a paired badge, or update the existing starter badge without stacking a second number over it. The entire change is one Undo step.
- **Paint:** choose color and size, then draw on the body. Erase paint reveals the original paint and underlying stickers; it does not cover them with a background color. Each complete stroke or erase gesture is one Undo step.

Click a sticker on the car to edit it directly. Its center handle moves it, its corner resizes it, and its round handle turns it. Nearby actions duplicate, remove, unlock or pair the artwork. **Arrange** finds covered artwork. Drag empty space to turn the car; right-drag also works while painting or stamping. Left, Right, Front, Top and Whole car views remain beside the canvas.

**Undo and Redo** remain visible with the tray hidden. Ctrl+Z / Ctrl+Shift+Z work outside text fields. Hold **before** to compare with the original state. The visible result updates before autosave encodes its PNG; Save project, closing, export and building wait for the latest save. **Save project** creates a portable .flproject with source artwork and history.

Simple placement uses a shallow projection and visible exterior surfaces to avoid reaching inner door skins. It protects triangles whose texture areas overlap another exterior part, including the Grosse pointe bumper corners. Hover previews and saved strokes use the same mask. A visible **Linked parts protected** message explains skipped parts. This protection is deliberately conservative: a partly shared triangle is protected in full. Making linked parts independently paintable would require changing the car's UV layout. Advanced mode can paint them together. Existing brush strokes offer **Remove paint spill** when selected in Arrange; the repair is one Undo. Some original panels also are not paintable.

Advanced's **Protect shared parts** checkbox is on for new placements. Turn it off deliberately to paint linked parts together; selected older layers can also use **Allow layer across panels**. The shared paint eraser keeps protection on and removes paint at full strength while preserving stickers and original body texture.

**Photo / Spin** offers a PNG photo or spinning GIF. **Use in game** offers building a package, the existing build/import/launch workflow, and game setup. Painting or choosing a look does not launch the game or upload anything.

Under **Settings → Display & quality**, choose Low, Balanced or High preview resolution and texture filtering. Balanced uses physical display pixels, including scaled displays; High uses 125%, Low 75%. Buffers are bounded to a 4K edge and 8.3 million pixels. Cancel restores the previous quality; Save settings remembers it independently of the design. Game textures and photo exports retain their own resolution.

New history uses editor format **9** for finishes, clean paint, exterior placement and erased strokes. Existing formats 1–8 remain readable. Updated projects need this build; older editors cannot read the new history format.


**Independent sides:** Paint > Independent sides creates a corrected Grosse pointe car project and opens it in Car Builder. Both bumper corners remain paintable with shared-part protection enabled. The original studio draft is preserved. Save car stores the corrected model, artwork and full Undo history. Car Builder > Use in game > Build & drive installs the corrected car locally in the original FlatOut 4 Steam game; choose Grosse pointe's first livery. Restore original car reverses the installation. Live Steam driving and VR model installation remain unverified.

Setup discovers Steam libraries automatically. Bundled compiler files stay internal; manual installation selection and the texture guide are expandable. Retail installs offer launch and Workshop, while compatible editable installations also offer direct imports. The UI calls the game FlatOut 4.
