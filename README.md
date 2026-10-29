# HD Sprites (1.6 port)

Unofficial port of [HD Sprites](https://github.com/ninthworld/HDSprites) by NinthWorld to Stardew Valley 1.6 and SMAPI 4. GPLv3, same as the original.

## What changed from 1.4.0

- **Runtime upscaling.** No bundled HD PNGs. Textures are upscaled with 2xBR from whatever the game has loaded, after Content Patcher edits, so recolours like Earthy Recolour come out HD.
- **No built-in content pack reader.** Content Patcher handles packs; this mod upscales the result.
- **Background upscaling.** The first time a texture is drawn, its HD copy is built off the main thread. Until it's ready, the normal texture is drawn.
- **Sprite grids.** Map tiles, tilesheets, characters and portraits are upscaled cell by cell, so neighbouring tiles don't bleed into each other.
- **GPU size limit.** Tall textures are split into overlapping strips no taller than `MaxTextureSize` (default 4096, safe for most phones).
- **Smoother movement.** Draws keep sub-pixel positions instead of rounding to whole pixels.
- **New ID** (`GeorgeCleland.HDSprites`) so SMAPI doesn't block it as the old version.

Not carried over: the farmer body is excluded (the game rebuilds it into its own texture, so only clothes would get smoothed), and SpriteText fonts are excluded.

## Build

Needs the .NET 6 SDK and Stardew Valley 1.6 + SMAPI installed on the PC (ModBuildConfig finds them).

```
cd HDSprites
dotnet build -c Release
```

The build copies the mod into your PC's `Mods` folder and puts a zip in `bin/Release/net6.0/`. For Android, copy the `HDSprites` folder from that zip into the phone's `Mods` folder.

## Config

`config.json` is created on first launch. Also editable in Generic Mod Config Menu.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Draw HD textures |
| `ToggleKey` | `F10` | Toggle on/off |
| `MaxTextureSize` | `4096` | Largest GPU texture side |
| `MaxConcurrentUpscales` | `2` | Background upscale jobs at once |
| `MinSpriteSize` | `3` | Sprites thinner than this draw normally |
| `Include` / `Exclude` | see file | Asset name prefixes |
| `SpriteGrids` | see file | Per-prefix cell size, e.g. `"Maps/": "16x16"` |

## Credits

- NinthWorld: HD Sprites
- Platonymous: ScaledTexture2D and SpriteBatch patch approach (PyTK)
- Ascari: 2xBR filter (JavaScript original)
