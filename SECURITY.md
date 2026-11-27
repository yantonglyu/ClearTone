# Security and dependency status

Keep this service bound to loopback. Do not expose it through a reverse proxy or forward its port. Do not attach account files, media URLs, session tokens or personal data to public bug reports.

For security-sensitive reports, use GitHub's private vulnerability reporting when available. If unavailable, open a minimal issue asking for a private contact without publishing exploit details or credentials.

## v0.2.1 dependency check (2026-11-27)

For the locked production Node dependency tree, `npm audit --omit=dev` flagged seven package entries (three high and four moderate), including parent packages affected through their dependencies. The entries cover the NetEase API adapter, basic-ftp, body-parser, follow-redirects, get-uri, node-forge and pac-proxy-agent. Exploitability in ClearTone has not been established for each entry. This playback release retains the tested dependency lockfile.

## Earlier dependency review (2026-10-20)

The lockfile overrides `basic-ftp` to 6.2.3, addressing [GHSA-c475-qrg2-pj4r](https://github.com/advisories/GHSA-c475-qrg2-pj4r). The application does not expose FTP or PAC configuration.

`npm audit` still flags `node-forge` and its parent API package for [GHSA-86w9-cpqp-85rv](https://github.com/advisories/GHSA-86w9-cpqp-85rv), with no patched version available at the time of review. Inspection of the API adapter's crypto helper found RSA public-key encryption, not the affected PKCS#1 signature-verification operation. This is a bounded call-site review, not a claim that all transitive paths are vulnerability-free. Recheck before distributing a new release, and do not add signature-verification use through this dependency without resolving the advisory.

The first binary release is an early public version. It has not undergone a comprehensive external security audit. App-local Microsoft C++ runtime libraries are shipped with the installer. These copies are updated through ClearTone releases, not automatically by Windows Update. Recheck runtime updates when building each release.
