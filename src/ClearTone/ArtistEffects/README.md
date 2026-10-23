# Artist effects

`ArtistEffectProfile.cs` owns artist matching and conservative defaults. `../ArtistMotion.cs` owns rendering, transition cancellation and lifecycle. The editable local file is `data/artist-effects/profiles.json`, initialized on first launch. Changes apply on the next launch. An empty array disables artist motifs. These files contain no account or listening data.

Each profile specifies `Id`, `Artists` (exact artist aliases, punctuation and case insensitive), `Colors` (hex RGB), `Stars` (4–32), `Opacity` (0.02–0.25), and `Pattern` (`stars`, `pop-stage`, or `starlines`). Separate artists in a collaboration with `/`, `;`, `|`, or `、`. Song titles do not trigger artist effects. Invalid configuration falls back to the defaults and is logged without interrupting playback.

`pop-stage` uses large rose/gold star outlines at the edges and buoyant glints. `starlines` adds a fine lilac/champagne constellation above the content. Track changes introduce a short full-width sweep, with the central reading area kept quiet. The default ambient strength is 0.23/0.22 and there are still only 18 stars. Existing exact old built-in profiles (18 stars, 0.13 opacity, original aliases and colors) acquire the new style in memory. Any edited values remain unchanged, and existing configuration files are never rewritten by this migration.

The Poppin'Party motif follows its official “キラキラドキドキ” and shining-star identity. Liella!'s official Starlines merchandise uses member-inspired stars. The soft rose/gold and lilac/champagne palettes are ClearTone's interpretation, not official color specifications. Star paths are original procedural vectors, not copied logos or downloaded assets.

- https://bang-dream.com/artist/poppinparty/
- https://anime.bang-dream.com/bandorichan/character/poppinparty/
- https://www.lovelive-anime.jp/yuigaoka/live/sp_1stlive/goods.php

Stars are behind every interactive UI layer. Video playback and exclusive overlays hide them. Continuous movement runs on the Windows compositor only, with 18 vectors by default, no blur and no managed render timer. It stops while paused, minimized, background motion disabled, or OS animations disabled. Constellation vertices remain fixed to their static connecting line. Switching tracks uses a bounded burst and cover/background fades. Rapid switches cancel old bursts and version-gate image callbacks. Closing disposes visual references and stops animations. Scheduling and monitoring are not applicable to these on-demand UI effects.
