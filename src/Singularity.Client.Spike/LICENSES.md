# Licences of spike assets (ADR-0009: only PD, CC0, CC BY). Accessed 2026-11-17.

| File | Source | Licence |
|---|---|---|
| `assets/data/ne_50m_admin_0_countries_min.json` | Natural Earth 1:50m Admin 0 Countries (via github.com/nvkelso/natural-earth-vector), reduced to name + geometry | Public domain (naturalearthdata.com/about/terms-of-use) |
| `assets/models/building-a.glb`, `building-d.glb`, `building-j.glb`, `building-t.glb`, `chimney-large.glb`, `water-tower.glb` | Kenney "City Kit Industrial" 2.0, downloaded from opengameart.org/content/city-kit-industrial (`kenney_city-kit-industrial_2.0.zip`) | CC0 (`assets/models/KENNEY_LICENSE.txt`) |
| `assets/models/funky_aircraft_low.obj` | "Funky aircraft" by Savino, opengameart.org/content/funky-aircraft | CC0 (as listed on the page) |
| Runway strip | procedural box mesh in code | none (own) |

Not used: NASA Blue Marble and any photo imagery (licence unverified, off-style per docs/ui/GLOBE_DIRECTION.md).
Kenney's own site refused plain automated download (403); the same Kenney pack was taken from OpenGameArt, where it is published as CC0.
Models are rendered untextured: their embedded materials are replaced by `shaders/object.gdshader`.
