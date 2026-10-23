# Livery Studio

A Windows livery editor for **FlatOut 4: Total Insanity** and **FlatOut 4: Total Insanity VR**. Paint on a 3D car, add stickers and race numbers, and keep the artwork editable.

## Download and start

Download the complete Windows x64 ZIP from [Releases](https://github.com/Flat2VRStudios/LiveryStudio/releases), extract it to a writable folder and run the top-level `LiveryStudio.exe`. Only the app, README and `App` folder appear at the top level; keep them together. The runtime, content tools and license notices are inside `App`.

Requires Windows 10 version 1809 or newer, or Windows 11, with Direct3D 11 graphics. You can design without the game installed. Steam publishing requires a signed-in account that owns the selected game.

## Make it yours

1. Pick a car in **Simple mode**.
2. Try a look, choose a finish, stamp a sticker or paint on the body.
3. Experiment with Undo. Save a portable `.flproject` to retain the artwork and history.
4. Choose **Use in game** to build, then export or publish through Saved builds.

**Advanced mode** adds layers, text, patterns, material controls, reusable groups, image preparation, SVG artwork, masks and precise placement. Its Add menu shares the quick creative tools. Photo and Spin export stills and animated turntables. The mode button names the mode it opens.

## Independent bumper painting

Some factory parts share texture coordinates. For **Grosse pointe**, choose **Paint → Independent sides** to create a corrected `.flcar` project. **Car Builder → Use in game → Build & drive** installs its model and paint locally into the original FlatOut 4 Steam game. Select Grosse pointe's **first livery** in **Garage → Customize → Liveries**. **Restore original car** reverses the owned installation and retains your project.

This is separate from texture-only Workshop publishing. It currently supports Grosse pointe in the original Steam game; live Steam driving and VR model installation are not yet verified. The optional custom-exterior builder requires a compatible editable game installation.

## Documentation

- [User guide](LiveryStudio/README.md) and [Simple mode](docs/simple-mode.md).
- [Build and test](docs/build.md), [release process](docs/releasing.md) and [changelog](CHANGELOG.md).
- [Contributing](CONTRIBUTING.md), [security reporting](SECURITY.md) and [third-party notices](THIRD_PARTY_NOTICES.md).

## License

Original editor code and documentation are available under the [MIT License](LICENSE). Edits, forks and reuse are welcome. See [contributing](CONTRIBUTING.md).

Game assets, branding, native tools and third-party libraries retain separate terms; the editor license does not relicense them.
