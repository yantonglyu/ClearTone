# Security and dependency status

Keep this service bound to loopback. Do not expose it through a reverse proxy or forward its port. Do not attach account files, media URLs, session tokens or personal data to public bug reports.

For security-sensitive reports, use GitHub's private vulnerability reporting when available. If unavailable, open a minimal issue asking for a private contact without publishing exploit details or credentials.

## Dependency review: 2026-10-20

The lockfile overrides `basic-ftp` to 6.2.3, addressing [GHSA-c475-qrg2-pj4r](https://github.com/advisories/GHSA-c475-qrg2-pj4r). The application does not expose FTP or PAC configuration.

`npm audit` still flags `node-forge` and its parent API package for [GHSA-86w9-cpqp-85rv](https://github.com/advisories/GHSA-86w9-cpqp-85rv), with no patched version available at the time of review. Inspection of the API adapter's crypto helper found RSA public-key encryption, not the affected PKCS#1 signature-verification operation. This is a bounded call-site review, not a claim that all transitive paths are vulnerability-free. Recheck before distributing a new release, and do not add signature-verification use through this dependency without resolving the advisory.

The repository is a source preview. It has not undergone a comprehensive external security audit.
