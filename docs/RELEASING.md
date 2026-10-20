# Windows releases

The durable release workflow is `scripts/build.ps1` plus `scripts/package-release.ps1` and `installer/ClearTone.iss`. This is a manual release workflow, not a scheduled task. No automatic monitoring is installed.

1. Use a clean checkout and review `SECURITY.md`, current dependency advisories and licenses. Set the C# and installer versions consistently.
2. Run the core and service tests. Build into a **new** directory. Never use an installed user's app as a packaging source.
3. Install [Inno Setup 7](https://jrsoftware.org/isdl.php). Verify its publisher signature. Use the x64 `Microsoft.VC*.CRT` directory under Visual Studio's `VC/Redist/MSVC` tree, not system DLLs.
4. Build the installer:

```powershell
./scripts/build.ps1 -Output ./artifacts/build-0.1.1
./scripts/package-release.ps1 -Bundle ./artifacts/build-0.1.1 -Version 0.1.1 `
  -ISCC 'path\to\ISCC.exe' `
  -VCRuntime 'path\to\VC\Redist\MSVC\version\x64\Microsoft.VC145.CRT'
```

The package script verifies runtime signatures, rejects personal state/cache/model files, compiles the explicit directory allowlist and writes `SHA256SUMS.txt`. Include the compiler's complete `license.txt` as `licenses/Inno-Setup-LICENSE.txt` in the source before building. Review every added source and distribution file, including third-party notices. Public package fixtures are not user credentials.

The denylist also excludes listening outboxes/ledgers and their temporary copies, plus `portable.mode`. Audit the installed payload before its first launch, then compare each shipped file's SHA-256 with the clean bundle. Keep anonymous startup, synthetic playback and any authorized private-account verification in separate data directories. Never publish live account snapshots or personal statistics in release notes or Git history. Upload only the installer and `SHA256SUMS.txt`, not a whole artifacts directory. Tag the exact tested build source commit, and identify subsequent documentation-only updates separately.

5. Inspect the installer visually. Test install, anonymous startup, synthetic playback and timer persistence with a dedicated `CLEARTONE_DATA` directory and service port. `/TESTINSTALL=1` suppresses shortcuts for a test install. Use a new install directory. Test upgrade and uninstall, including preservation of synthetic records. Never point the tests at real account data.
6. Record exact validation and limitations in `docs/VALIDATION.md`. Create a Git tag and a draft GitHub Release for that commit. Upload the installer and checksums, compare asset hashes, then publish. Stop publication on any build/test/data-audit failure.

The installer is per-user and does not require elevation. .NET, Windows App SDK, Node and the C++ CRT ship app-locally. Heavy Python models remain optional. Do not bundle accounts, music, test fixtures, model caches or developer configuration. Do not add an uninstall action deleting `%LOCALAPPDATA%/ClearTone`.

For maintenance, recheck dependencies and app-local runtimes at each release. If a reported regression affects playback or records, withdraw the affected binary and produce a corrected version. Release checks are developer-owned scripts, not an installed agent or background schedule.
