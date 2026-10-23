# 0.1.0 candidate checks

Checked on Windows x64 on 23 October 2026. These are local results; the public CI workflow has been added but has not run on GitHub yet.

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

Old development records and recovery material were archived outside the repository before removal. Existing Git history still contains that material; use the clean source export for public import. MIT covers the original editor code and documentation, with separate third-party/game-content terms in [the notices](../THIRD_PARTY_NOTICES.md).

Outstanding release-owner checks: confirm redistribution authorization for the exact proprietary bundle, run on a clean PC, sign the Windows build and complete live Steam driving validation. Independent-side VR model installation remains unverified. No repository visibility change or public release was performed by these checks.
