# Licences of spike assets (ADR-0009: only PD, CC0, CC BY). Accessed 2026-11-17 (fonts downloaded 2026-11-17).

| File | Source | Licence |
|---|---|---|
| `assets/models/building-a.glb`, `building-d.glb`, `building-j.glb`, `building-t.glb`, `chimney-large.glb`, `water-tower.glb` | Kenney "City Kit Industrial" 2.0, downloaded from opengameart.org/content/city-kit-industrial (`kenney_city-kit-industrial_2.0.zip`) | CC0 (`assets/models/KENNEY_LICENSE.txt`) |
| `assets/models/funky_aircraft_low.obj` | "Funky aircraft" by Savino, opengameart.org/content/funky-aircraft | CC0 (as listed on the page) |
| `assets/fonts/GolosText.ttf`, `OFL-GolosText.txt` | Golos Text (variable), github.com/google/fonts `ofl/golostext/` (raw.githubusercontent.com/google/fonts/main/ofl/golostext/) | SIL OFL 1.1 (ADR-0009 owner decision #294); interface text |
| `assets/fonts/PTMono.ttf`, `OFL-PTMono.txt` | PT Mono (ParaType), `ofl/ptmono/PTM55FT.ttf` | SIL OFL 1.1; numbers and codes |
| `assets/fonts/Jura.ttf`, `OFL-Jura.txt` | Jura (variable), `ofl/jura/` | SIL OFL 1.1; drafting headings |
| `assets/fonts/Literata.ttf`, `OFL-Literata.txt` | Literata (variable), `ofl/literata/` | SIL OFL 1.1; map labels |
| `assets/audio/` (music, ui, ambient) | Third-party audio: authors, licences (CC BY 4.0 / CC0), URLs and changes are listed only in `assets/credits.json` (shown on the Credits screen, checked by `CreditsTests`); mapping event -> file in `docs/audio/EVENT_SOUNDS_MAP.md` | see `assets/credits.json` |
| `assets/ui/area-icons/*.svg`, `assets/ui/area-pictures/*.svg` | own drawings of the ui-art-director (build-time copy of `docs/ui/assets/`, made by `run-globe-spike.sh`; nothing traced, no third-party art) | own |
| `locale/ui.csv`, `locale/data.csv` | own work: UI strings; node, country and actor names (EN, RU) | own |
| Runway strip | procedural box mesh in code | none (own) |

Not used: NASA Blue Marble and any photo imagery (licence unverified, off-style per docs/ui/GLOBE_DIRECTION.md).
Kenney's own site refused plain automated download (403); the same Kenney pack was taken from OpenGameArt, where it is published as CC0.
Models are rendered untextured: their embedded materials are replaced by `shaders/object.gdshader`.
