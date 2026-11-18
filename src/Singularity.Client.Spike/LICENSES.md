# Licences of spike assets (ADR-0009: only PD, CC0, CC BY). Accessed 2026-11-17 (fonts downloaded 2026-11-17).

| File | Source | Licence |
|---|---|---|
| `assets/data/ne_50m_admin_0_countries_min.json` | Natural Earth 1:50m Admin 0 Countries (via github.com/nvkelso/natural-earth-vector), reduced to name + geometry | Public domain (naturalearthdata.com/about/terms-of-use) |
| `assets/models/building-a.glb`, `building-d.glb`, `building-j.glb`, `building-t.glb`, `chimney-large.glb`, `water-tower.glb` | Kenney "City Kit Industrial" 2.0, downloaded from opengameart.org/content/city-kit-industrial (`kenney_city-kit-industrial_2.0.zip`) | CC0 (`assets/models/KENNEY_LICENSE.txt`) |
| `assets/models/funky_aircraft_low.obj` | "Funky aircraft" by Savino, opengameart.org/content/funky-aircraft | CC0 (as listed on the page) |
| `assets/fonts/GolosText.ttf`, `OFL-GolosText.txt` | Golos Text (variable), github.com/google/fonts `ofl/golostext/` (raw.githubusercontent.com/google/fonts/main/ofl/golostext/) | SIL OFL 1.1 (ADR-0009 owner decision #294); interface text |
| `assets/fonts/PTMono.ttf`, `OFL-PTMono.txt` | PT Mono (ParaType), `ofl/ptmono/PTM55FT.ttf` | SIL OFL 1.1; numbers and codes |
| `assets/fonts/Jura.ttf`, `OFL-Jura.txt` | Jura (variable), `ofl/jura/` | SIL OFL 1.1; drafting headings |
| `assets/fonts/Literata.ttf`, `OFL-Literata.txt` | Literata (variable), `ofl/literata/` | SIL OFL 1.1; map labels |
| `assets/audio/ui/*.ogg` (event sounds, listening-pack r2, owner decision #403) | Freesound / BigSoundBank recordings, cut and normalised; mapping event -> E## file in `docs/audio/EVENT_SOUNDS_MAP.md`, source hashes in `docs/audio/listening-pack/CREDITS.yml`. Most are Freesound HQ previews (mp3 re-encoded to ogg). Attribution below | CC0 and CC BY 4.0 (see "Event sounds attribution") |
| `assets/audio/music/irradiated_skies.ogg` | "Irradiated Skies" by Tsorthan Grove, https://opengameart.org/content/irradiated-skies (file `irradiated_skies.ogg`); converted and gain-adjusted by `tools/audio/prepare_external.py`. **Temporary** music | CC0 (licence field of the page: CC0) |
| `assets/audio/music/led_twilight.ogg` | "LED Twilight" by Tsorthan Grove, https://opengameart.org/content/led-twilight (file `led_twlight.ogg`); converted as above. **Temporary** music | CC0 (licence field of the page: CC0) |
| `assets/audio/ambient/pale_window.ogg` | "Light Through a Pale Window" from "Background Drones Vol 1" by Tsorthan Grove, https://opengameart.org/content/background-drones-vol-1 (file `light_through_a_pale_window.mp3`, first 120 s); converted as above. Ambient bed | CC0 (licence field of the page: CC0) |
| `assets/ui/area-icons/*.svg`, `assets/ui/area-pictures/*.svg` | own drawings of the ui-art-director (build-time copy of `docs/ui/assets/`, made by `run-globe-spike.sh`; nothing traced, no third-party art) | own |
| `locale/ui.csv`, `locale/data.csv` | own work: UI strings; node, country and actor names (EN, RU) | own |
| Runway strip | procedural box mesh in code | none (own) |

Not used: NASA Blue Marble and any photo imagery (licence unverified, off-style per docs/ui/GLOBE_DIRECTION.md).
Kenney's own site refused plain automated download (403); the same Kenney pack was taken from OpenGameArt, where it is published as CC0.
Models are rendered untextured: their embedded materials are replaced by `shaders/object.gdshader`.
## Event sounds attribution (`assets/audio/ui/*.ogg`, accessed 2026-11-18)
Changes: cutting, filtering, loudness normalisation (pitch ±2 semitones for signal_clear, pause, unpause); the typewriter sounds (order_done, node_discovered, launch_space) are an automatically cut fragment.

CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/):
- "Bell Ding" by jakebagger, https://freesound.org/people/jakebagger/sounds/499787/ (signal_attention, signal_critical, signal_clear)
- "electrical_switch_02.wav" by joedeshon, https://freesound.org/people/joedeshon/sounds/139062/ (tempo, pause, unpause)
- "morse static.wav" by ERH, https://freesound.org/people/ERH/sounds/34418/ (war_declared)
- Triumph Matura 300 typewriter by YleArkisto, https://freesound.org/people/YleArkisto/sounds/271525/ (order_done, node_discovered, launch_space)

CC0 (no credit required, named for completeness):
- "traditional stamp.wav" by I.fekry, https://freesound.org/people/I.fekry/sounds/470710/ (order_ok)
- "A relay switch being activated." by bassmosphere, https://freesound.org/people/bassmosphere/sounds/384698/ (order_reject, signal_critical)
- "Paper Shuffle and Stack.aif" by moose13088, https://freesound.org/people/moose13088/sounds/182889/ (queue_tick)
- "Stromberg Carlson 1543.wav" by Hockinfinger, https://freesound.org/people/Hockinfinger/sounds/274436/ (signal_alert)
- "Typewriter, bell #2" by Joseph Sardin (BigSoundBank), https://bigsoundbank.com/typewriter-bell-s2845.html (breakthrough)
- "Factory whistle, train whistle" by kentspublicdomain, https://freesound.org/people/kentspublicdomain/sounds/477827/ (line_commissioned, launch_space)
- "Kill Switch (Large Breaker Switch) .WAV" by EchoCinematics, https://freesound.org/people/EchoCinematics/sounds/131599/ (war_declared, launch_nuclear)
- "hum electric transformer in large room1.flac" by kyles, https://freesound.org/people/kyles/sounds/453444/ (launch_nuclear)
- "shuffling papers.wav" by bevibeldesign, https://freesound.org/people/bevibeldesign/sounds/635156/ (ui_tick)

Audio sources accessed 2026-11-17 on opengameart.org (HTTP 200; the licence of each page was read in its "License(s)" field before download). Kenney's site was not used (403 to automated requests, as for models). CC0 needs no attribution; the authors are named here anyway.
