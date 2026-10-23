# Third-party and game-content notices

The MIT License covers the original editor code and documentation. It does not relicense these components or grant rights to the games.

| Component | Terms and location |
| --- | --- |
| ufbx | Dual MIT/public-domain options; original text in `LiveryStudio/Native/third_party/ufbx/LICENSE`. |
| StbImageSharp | Separate upstream license in `LiveryStudio/App/Assets/licenses/StbImageSharp-LICENSE.txt`. |
| DXVK compiler compatibility runtime | Upstream zlib license, retained at `Workshop/Compatibility/DXVK-LICENSE.txt` in releases containing it. The acquisition script checks archive, binary and license hashes. |
| .NET, Windows App SDK and Windows SDK | Microsoft terms supplied with their products and packages. Record the NuGet dependency inventory for each release. |
| Game content tools, Steamworks, Autodesk FBX, PhysX, FMOD, Tootle and C/C++/DirectX runtimes | Proprietary components supplied through authorized build inputs; their own redistribution terms apply. They are not included in the public source snapshot. |
| Branding, car thumbnails, meshes, textures, example-project base maps and preview environments | Game content or derived presentation assets; rights remain with their owners. The editor license grants no right to use these assets in another game. |

Maintainers must confirm redistribution authorization for the exact native/game bundle before publishing a binary. File inventories and package checks do not establish redistribution rights. Preserve applicable vendor notices when supplying authorized runtime inputs.
