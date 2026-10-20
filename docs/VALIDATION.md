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

## Limits

This is not a clean-OS installation test. Build tools were already installed. The full optional Python/model environment was not downloaded anew for this release. Real QR login, all third-party songs, every Bilibili video and every network region are not covered by these tests. Platform access remains dependent on account permissions and upstream availability.

Dependency advisories are documented in [SECURITY.md](../SECURITY.md), including a remaining upstream advisory. A passing functional test is not a comprehensive security audit.

No performance guarantee is made for model inference. No scheduled monitoring is installed by the application or this repository.
