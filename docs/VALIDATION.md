# Validation

Validated on Windows x64, 2026-10-20.

## Completed

- Restored the pinned Node dependency graph into the source directory with `npm ci --ignore-scripts`.
- Published the native WinUI application with .NET SDK 10.0.201 and generated a NuGet dependency lockfile.
- Launched the resulting bundle with a separate data directory and service port. A responsive native window appeared.
- Started with an empty account and synthetic 44.1 kHz WAV. Verified play, pause, decoded sample rate, lyric drag/follow and study checkpoints. The test did not use private media or account files.
- Passed the core PCM FFT, EQ transfer/headroom, low-sample-rate filter guard, bilingual timestamp, monotonic time, rest exclusion and atomic persistence checks.
- Passed service tests covering independent startup, signed-out status, origin/header rejection, media-token rejection, like input validation, playlist ownership and duplicate collection.
- Parsed the PowerShell scripts and compiled Python source syntax without errors.
- Inspected the Git publication list to exclude data, credentials, media, model weights, logs, binaries and developer-specific paths.

## v0.1.0 installer

- Built a per-user x64 installer using signed Inno Setup 7.1.0, with bundled .NET 10.0.5, Windows App SDK 2.5.1, Node 24.13.0 and app-local x64 Microsoft C++ CRT 14.50.35710.
- Inspected the actual Chinese welcome and license pages, including the cat artwork and readable font layout.
- Installed into an isolated directory without creating shortcuts or touching an existing installation.
- Compared SHA-256 hashes for all 5,453 payload files against the clean build bundle.
- Launched with a new data directory, separate service port, a nonexistent `DOTNET_ROOT`, and a PATH containing only Windows directories. Confirmed signed-out status, zero study time and a responsive window. Loaded coreclr, WinUI and Win2D from the installed application directory.
- Played a generated 44.1 kHz WAV and verified pause, lyric browsing/follow and study persistence. Synthetic study total increased from 3,600 to 3,603.085 seconds. No account file was created.
- Rechecked npm advisories. The existing two high-severity audit entries refer to the same underlying node-forge advisory documented in SECURITY.md. NuGet vulnerability refresh emitted NU1900 because its advisory source was unreachable during the core test run, so that refresh was not verified.
- Completed an in-place upgrade with the isolated player and service stopped. A first attempt with an orphaned test service was blocked by a locked file and rolled back. Users should exit the application normally before upgrading.
- Uninstalled the test application. Verified that its executable was removed and both external synthetic study state and an untracked synthetic record inside the test install directory remained unchanged/preserved.
- The existing personal installation was not replaced, restarted or migrated. The installer and integration checks used only separate test paths and generated audio.

## Listening sync (v0.1.1)

- Passed device-clock accounting checks for pause/resume, seek jumps, output reset, decoded-data bounds, and background reads. Passed durable-outbox recovery/account isolation and local-service concurrency/restart deduplication tests. Ambiguous submissions are not retried. A damaged ledger prevents reports without stopping the service.
- Published and ran the native update. Synthetic 44.1 kHz playback verified that pause and seeking do not add listening time. Lyric drag/follow and study persistence checks still passed.
- With the account owner's explicit permission, backed up and updated a personal installation. A fresh pre-install snapshot preserved the exact study total, complete queue and byte-identical encrypted account file. Personal data and live evidence remain outside the repository and release assets.
- Performed authorized real playback interspersed with a pause and forward seek. Pause and seek added zero measured seconds, and only one completed session was submitted. Study timing continued throughout playback and pause. No historical listening was backfilled.
- Read-only NetEase checks confirmed the item appeared first in recent history, its weekly play count increased once, and daily/weekly minute totals changed. The separate lifetime-total endpoint did not change immediately. These are distinct statistics, not a guarantee that every server aggregate updates at the same time. No account identifiers, song identifiers or personal history are included in this report.
- Rechecked after graceful close and normal-mode restart: recent-play timestamp and weekly count were unchanged, and no second report appeared in the ledger. The lifetime total still had not changed roughly two minutes after submission. Restored active study timing, verified the encrypted account hash remained identical, and confirmed installed native bytes matched the tested build. No new playback errors were logged.
- Installer validation for v0.1.1 is recorded separately below. Future product planning is documentation only.

## v0.1.1 installer

- Built from source commit `3f82706226d7c9c9cac962225b13d990c48e9a89` into a new bundle, without using a personal installation as input. Later roadmap/release-validation edits are documentation only. The `v0.1.1` tag identifies this tested build source.
- Passed the core regression suite and all five Node service/listening tests. Rechecked npm advisories: the same existing node-forge signature-verification advisory remains documented in SECURITY.md. NuGet advisory retrieval emitted NU1900 during the core run, so that advisory refresh is unverified.
- Built with the existing signed Inno Setup compiler and signature-checked Microsoft CRT. Installer styling is unchanged from the inspected v0.1.0 design.
- Installed into a fresh isolated directory with test shortcuts disabled. Compared SHA-256 hashes for all 5,454 shipped files with the clean bundle. Found zero private state/account/listening-ledger/cache files before startup.
- Launched with an empty data directory, independent service port, nonexistent `DOTNET_ROOT`, and Windows-only PATH. Confirmed a responsive native window, signed-out status, zero study time, and no account vault.
- In a second isolated data directory, played generated 44.1 kHz PCM and verified pause/seek accounting, lyric browsing/follow, and synthetic study checkpointing. No listening report was submitted for the synthetic local audio. No new errors were logged.
- Gracefully closed the test app, upgraded that isolated installation, and uninstalled it. Exact hashes confirmed the external synthetic study state was retained. An untracked synthetic record inside the test installation was also preserved.
- Installer: `ClearTone-0.1.1-win-x64-Setup.exe`, 117,058,323 bytes. SHA-256: `52563dc5db4c6be719f4ccb3582d9534849ccca89b844b83d9fbd3625a7035c0`.
- This release-validation run did not replace or restart the personal installation. Future roadmap features were not deployed, and no private account test was repeated for packaging.

## General validation limits

This is not a clean-OS installation test. Build tools were already installed. The full optional Python/model environment was not downloaded anew for this release. Real QR login, all third-party songs, every Bilibili video and every network region are not covered by these tests. Platform access remains dependent on account permissions and upstream availability.

Dependency advisories are documented in [SECURITY.md](../SECURITY.md), including a remaining upstream advisory. A passing functional test is not a comprehensive security audit.

No performance guarantee is made for model inference. No scheduled monitoring is installed by the application or this repository.
