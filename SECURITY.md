# Security reporting

Do not put credentials, private projects, Steam session data or exploitable security details in a public issue.

Use GitHub's **Security → Report a vulnerability** option if it is enabled for this repository. Otherwise contact Flat2VR Studios through its established private support channel and request a security contact.

Include the affected version, a minimal reproduction, the expected safety boundary and the impact. Portable-file parsers, SVG input, native tools, installation/restoration and Workshop upload scope are especially relevant. Redact personal paths and account identifiers.

Run the source and dependency audits described in [releasing](docs/releasing.md) before a release. A clean scan records the checks performed; it is not a guarantee that no vulnerabilities exist.
