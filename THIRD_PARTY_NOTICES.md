# Third-party components

ClearTone's own source is MIT licensed. Dependency licenses apply to their respective components and are not replaced by the top-level LICENSE.

| Component | Use | License / upstream |
|---|---|---|
| .NET and Windows App SDK / WinUI | Runtime and native UI | [Microsoft Windows App SDK](https://github.com/microsoft/WindowsAppSDK), MIT and bundled component notices |
| WinUI template scaffolding | Application and window structure | Microsoft, MIT |
| Win2D | Canvas rendering | [Microsoft Win2D](https://github.com/microsoft/Win2D), MIT |
| NAudio 3.3.0 | Playback, DSP and EQ | [NAudio](https://github.com/naudio/NAudio), MIT |
| Node.js | Local service runtime | [Node.js LICENSE](https://github.com/nodejs/node/blob/main/LICENSE), MIT plus bundled dependencies |
| @neteasecloudmusicapienhanced/api 4.41.1 | NetEase API adapter | [api-enhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced), MIT |
| Demucs 4.0.1 | Optional vocal separation | [Demucs](https://github.com/facebookresearch/demucs), MIT |
| Beat This! 1.1.0 | Optional beat/downbeat inference | [Beat This!](https://github.com/CPJKU/beat_this), MIT for code and published weights |
| librosa / NumPy / SciPy / soxr | Optional signal analysis | ISC / BSD-3-Clause / BSD-3-Clause / LGPL-2.1-or-later components respectively, consult installed distributions |
| PyTorch / torchaudio | Optional CPU model inference | BSD-style licenses and bundled notices |
| imageio-ffmpeg | Optional decoder launcher | BSD-2-Clause wrapper, bundled FFmpeg has separate LGPL/GPL build conditions |

The NetEase dependency includes `@unblockneteasemusic/server` under LGPL-3.0-only transitively. ClearTone disables unblocking when making requests. Disabling a feature does not remove the dependency's license obligations. Its unmodified JavaScript source is available through the pinned npm package and is included when npm installs it. Do not remove its LICENSE or restrict replacement/modification of that library. `node-forge` offers BSD-3-Clause OR GPL-2.0; use its BSD-3-Clause option here. Other JavaScript dependency licenses are recorded in `licenses/npm-inventory.json` and remain in installed package directories.

Python environments, model weights, audio files and album art are not distributed in this source repository. If you distribute a binary bundle or an optional Python environment, also ship the exact versions' complete license files and satisfy their redistribution conditions, including applicable source availability obligations. The build script preserves installed Node package files. The runtime's own complete notice is retained under `licenses/`.

Platform-provided music, lyrics and images remain subject to their owners' rights. MIT licensing of this code does not license those materials or grant platform account access.
