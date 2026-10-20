# Architecture

`src/ClearTone` contains the native C# / WinUI 3 UI, Win2D lyrics and spectrum renderer, NAudio playback/EQ, study clock, and public Bilibili audio resolver. It does not embed a browser.

`service` is a local Node service that calls the separately installed NetEase API package. QR sign-in, playlists, media proxying and local analysis dispatch live here. Credentials stay in Windows DPAPI storage and are never passed to the Python process.

`analysis` contains optional CPU-only vocal/beat/pitch/meter analysis. Intermediate source audio is removed after the job. Numerical timelines remain in the user's cache. Cached results do not establish musical ground truth.

## Bundle and data

```text
ClearTone/
  app/ClearTone.exe
  runtime/node.exe
  service/
  analysis/
  analysis-runtime/     optional, locally installed
  analysis-models/      optional, locally downloaded
```

- `CLEARTONE_HOME`: bundle root, defaults to the parent of the executable's `app` directory.
- `CLEARTONE_DATA`: writable data root, defaults to `%LOCALAPPDATA%/ClearTone`.
- `CLEARTONE_PORT`: loopback service port, defaults to `18754`. Use a different port when running isolated development instances.

The build/installation scripts do not migrate accounts or study records. ClearTone's explicit record import keeps the larger total rather than summing potentially overlapping histories.

The study clock uses monotonic elapsed time. Checkpoints are saved every second while running, and again on orderly close. Abrupt power loss can still lose changes since the last completed write. Rest does not accumulate time.

An explicitly created `portable.mode` file at the bundle root selects its `data` subdirectory when `CLEARTONE_DATA` is absent. Release bundles never include this marker or personal data.

## Listening reports

`ListeningCounter` samples the WASAPI output clock with a monotonic wall-time bound and a decoded-PCM supply bound. It never uses media position as elapsed listening time. The analysis worker and downloads cannot add time. Pausing samples and closes the active interval. Seeking changes the media position without adding the skipped distance. Precision is whole seconds at submission, and the final subsecond/device-stop tail may be conservatively lost.

`ListeningSync` saves an independent per-account outbox every five seconds and finalizes a session on track change, end, logout or graceful close. A recovered checkpoint submits only the duration already saved. Playback never waits for a network report. Close waits at most five seconds. A fresh song load creates a fresh session identifier, while pause/resume keeps the same session.

The local service records each account/session identifier before making an external request. Duplicate local requests, concurrent retries and restarts reuse the ledger result. An ambiguous external outcome is marked `uncertain` and never automatically resubmitted, because the upstream endpoint has no verified idempotency key. This favors avoiding duplicate statistics over guaranteed delivery. Corrupt ledger data disables reporting while leaving the music service available. Credentials and study records are never part of a report.

## Rendering

Album colors and blur are prepared once per cover. Native composition moves the resulting background. Lyrics render cached textures and subscribe to frame callbacks only while scrolling. Playback spectrum updates at 20 Hz with a precomputed Hann window. Minimized windows skip visual refresh while playback and study accounting continue.

The opt-in `--test-control` switch enables local test commands only in the selected data directory. Use synthetic, isolated data. It is not enabled by the normal launcher.
