using System.Collections.Generic;
using StardewModdingAPI.Utilities;

namespace HDSprites
{
    public class ModConfig
    {
        /// <summary>Whether HD textures are drawn.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Key that toggles HD drawing on and off (handy for before/after checks).</summary>
        public KeybindList ToggleKey { get; set; } = KeybindList.Parse("F10");

        /// <summary>Largest texture side the GPU should be given, in pixels. Many phones cap at 4096.</summary>
        public int MaxTextureSize { get; set; } = 4096;

        /// <summary>How many textures can be upscaled in the background at once.</summary>
        public int MaxConcurrentUpscales { get; set; } = 2;

        /// <summary>Draw sprites thinner than this (in source pixels) without upscaling. Keeps UI bars and 1px lines crisp.</summary>
        public int MinSpriteSize { get; set; } = 3;

        /// <summary>Upscale map tilesheets (ground, floors, walls). Off by default: on Android these currently draw black.</summary>
        public bool UpscaleMapTiles { get; set; } = false;

        /// <summary>Asset name prefixes to upscale.</summary>
        public List<string> Include { get; set; } = new()
        {
            "Animals/",
            "Buildings/",
            "Characters/",
            "LooseSprites/",
            "Maps/",
            "Portraits/",
            "TerrainFeatures/",
            "TileSheets/",
        };

        /// <summary>Asset name prefixes to leave alone. These win over Include.</summary>
        public List<string> Exclude { get; set; } = new()
        {
            // The game rebuilds the farmer body into its own texture, so upscaling only the
            // clothes/hair layers makes them look mismatched against a pixel-art body.
            "Characters/Farmer/",
            // SpriteText fonts turn to mush when smoothed.
            "LooseSprites/font_",
        };

        /// <summary>
        /// Sprite grid per asset prefix, as "WIDTHxHEIGHT". The upscaler won't blend pixels across
        /// grid lines, which stops neighbouring tiles bleeding into each other (visible seams on maps).
        /// Longest matching prefix wins. "0x0" means no grid.
        /// </summary>
        public Dictionary<string, string> SpriteGrids { get; set; } = new()
        {
            ["Maps/"] = "16x16",
            ["TerrainFeatures/hoeDirt"] = "16x16",
            ["TerrainFeatures/Flooring"] = "16x16",
            ["TileSheets/"] = "16x16",
            ["TileSheets/Craftables"] = "16x32",
            ["TileSheets/furniture"] = "16x16",
            ["Characters/"] = "16x32",
            ["Characters/Monsters/"] = "0x0",
            ["Portraits/"] = "64x64",
            ["LooseSprites/Cursors"] = "0x0",
        };
    }
}
