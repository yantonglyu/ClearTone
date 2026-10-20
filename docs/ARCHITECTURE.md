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

## Rendering

Album colors and blur are prepared once per cover. Native composition moves the resulting background. Lyrics render cached textures and subscribe to frame callbacks only while scrolling. Playback spectrum updates at 20 Hz with a precomputed Hann window. Minimized windows skip visual refresh while playback and study accounting continue.

The opt-in `--test-control` switch enables local test commands only in the selected data directory. Use synthetic, isolated data. It is not enabled by the normal launcher.
