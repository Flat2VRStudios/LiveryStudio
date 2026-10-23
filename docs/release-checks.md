# 0.1.0 candidate checks

Checked on Windows x64 on 23 October 2026. The table records local results. GitHub CI runs the portable, workflow, draft replacement and vehicle transaction suites, builds the image-codec harness and launcher, and checks dependencies. The image-codec harness did not complete on the hosted runner; its 14 runtime checks passed on the interactive Windows desktop.

| Check | Result |
| --- | --- |
| Prepared-asset suite, including GPU comparisons | 2,008 passed |
| Portable suite without game inputs | 423 passed; also included in the prepared-asset total |
| Headless workflow and recovery | 103 passed |
| Draft replacement | 35 passed |
| Image/photo encoding and cancellation | 14 passed |
| Synthetic vehicle transactions | 53 passed |
| CLI build | Passed |
| Windows app publish | Passed using the prepared local native/game bundle |
| Candidate startup and project restoration | Saved four-layer design loaded and rendered; normal close succeeded |
| Tidy portable layout | Top-level native launcher opens the editor from a path containing spaces; design and 3D preview restored, normal close succeeded |
| Python build scripts | Compilation passed |
| NuGet dependency audit | No known vulnerable packages reported for the app and its transitive dependencies |
| Credential-pattern scan of historical Git blobs | 1,406 blobs checked; no pattern matches |

The source audit checks credentials, private machine paths, generated/private directories and local documentation links. Source export reruns it against the committed snapshot. Windows packaging verifies the runtime manifests and hashes every extracted file. It exposes only the launcher, README and App folder at the top level. Keep the generated verification records, inventories and SHA256SUMS beside their ZIPs.

The shader preparation change was compared against the previously tested shader: CG text matched after trailing-whitespace normalization, and XML structure/attributes matched. The proprietary native tools were reused for this candidate rather than rebuilt from their original inputs.

Old development records and recovery material were archived before removal. The public repository starts with a clean source snapshot; original development history and old preview releases are retained in a separate private archive. MIT covers the original editor code and documentation, with separate third-party/game-content terms in [the notices](../THIRD_PARTY_NOTICES.md).

Outstanding validation: run on a clean PC, sign both Windows executables and complete live Steam driving validation. Independent-side VR model installation remains unverified. The [public preview release](https://github.com/Flat2VRStudios/LiveryStudio/releases/tag/v0.1.0) includes the tidy Windows ZIP, source and verification records. Third-party redistribution terms still apply to the supplied bundle.
