# Fly HUD – UI kit

Everything from the HUD design, cut into sprites for Unity.
`preview_sprite_sheet.png` shows every file.

## Folders

| Folder | What's inside |
|---|---|
| `PNG@1x/` | Sprites at design size (for a 1280×720 canvas) |
| `PNG@2x/` | Same sprites, double size (use these for 1080p / 1440p) |
| `SVG/` | Vector sources, if you want to edit colours or shapes |
| `Unity/FlyHUD.cs` | Script that drives the bars, droplets, fruit, cooldowns and threat arrow |

Sub-folders: `badge` (fly portrait), `bars`, `spit`, `panels`, `objective` (fruit icons), `slots` (hex ability buttons), `icons`, `minimap`, `world` (reticle, threat arrow).

## Import in Unity

1. Drag the `PNG@2x` folder into `Assets/UI/FlyHUD/`.
2. Select all PNGs → **Texture Type: Sprite (2D and UI)**, **Mesh Type: Full Rect**, **Pixels Per Unit: 200** (for @2x), Compression: None or High Quality → Apply.
3. Canvas → **Canvas Scaler: Scale With Screen Size**, reference **1280 × 720**, Match **0.5**.

### 9-slice the panels (Sprite Editor → Border, values for @2x)

| Sprite | L | R | T | B |
|---|---|---|---|---|
| panel_stats | 36 | 36 | 36 | 36 |
| panel_objective | 34 | 34 | 0 | 0 |
| panel_score | 36 | 4 | 36 | 4 |
| banner_danger | 24 | 24 | 0 | 0 |
| popup_bg | 4 | 20 | 0 | 0 |
| key_badge | 4 | 4 | 4 | 4 |

Then set those Images to **Image Type: Sliced**.

### Image types

| Element | Image Type |
|---|---|
| bar_fill_health / bar_fill_wings | **Filled**, Horizontal, Origin Left (put on top of bar_bg / bar_bg_thin) |
| hex_cooldown_overlay | **Filled**, Vertical, Origin Bottom (on top of the hex slot) |
| icons/*_white | Simple – tint with the Image colour |
| everything else | Simple |

## Building the HUD (positions at 1280×720)

- **Top-left** (anchor top-left, 22,-20): `fly_badge` + `panel_stats` with 3 rows: HEALTH bar, 5 × `drop_full/empty`, WINGS thin bar.
- **Top-centre** (anchor top): `panel_objective` with text "Spit on the fruit" + 5 fruit icons + "3/5". Under it `banner_danger` with `icon_swatter` + "SWATTER INCOMING".
- **Top-right** (anchor top-right, -22,-20): `panel_score` ("GERMS 12 450 ×3"), under it the minimap.
- **Bottom-centre** (anchor bottom, y 18): 4 × hex slot (spit / dash / buzz / land) + icon + `key_badge` with LMB / SHIFT / E / F.
- **Reticle**: `reticle_spit` in front of the fly (or follow the aim point).
- **Hit popup**: `popup_bg` + `drop_full` + "+150 APPLE", spawn on the fruit you hit.

### Minimap (live)

1. Add a top-down orthographic camera above the kitchen → render into a **Render Texture** (256×256).
2. UI: `minimap_mask` (Image + **Mask**, Show Mask Graphic off) → child **RawImage** with the Render Texture → on top `minimap_frame`.
3. Put `marker_player`, `marker_human` (+ `marker_human_ring` pulsing) and `marker_fruit_*` inside the mask.
   `minimap_kitchen_placeholder` is a static map if you don't want a camera.

## Colours (from the game scene)

| Name | Hex | Used for |
|---|---|---|
| Ink | `#2B2A30` | outlines, dark panels |
| Cream | `#F4E9C9` | panels, light text |
| Teal | `#3F7F78` | wings bar, dash, badge |
| Wood | `#B06F3C` / `#6B3F22` | objective panel, land slot |
| Fly red | `#C8323A` | health, danger |
| Slime green | `#9CC23A` | spit |

## Font

**Rubik** (free, OFL) – download from fonts.google.com/specimen/Rubik, then create a TextMeshPro Font Asset.
Weights used: 700–800 for labels and numbers.

## Script

Add `FlyHUD.cs` to the Canvas, drag the Images / texts into the fields, then call from your game:

```csharp
hud.SetHealth(72, 100);
hud.SetSpit(3);
hud.SetWings(stamina, maxStamina);
hud.SetFruitDone(0, true);
hud.SetScore(12450, 3);
hud.SetCooldown(1, 2.4f, 4f);      // slot 1 = dash
hud.SetThreat(humanTransform, flyTransform);  // null to hide
```
Needs TextMeshPro (Window → Package Manager).
