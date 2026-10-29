// HD Sprites, ported to Stardew Valley 1.6 / SMAPI 4.
// Original mod by NinthWorld (https://github.com/ninthworld/HDSprites), GPLv3.
// Draw and texture classes derive from Platonymous' PyTK, GPLv3.
//
// How it works: every texture asset matched by the config is tagged after all
// other mods (including Content Patcher) have edited it. The first time it is drawn, a 2xBR copy is
// built in the background; once ready, SpriteBatch.Draw calls on it are redirected to the HD copy.
// Content packs are no longer read directly: Content Patcher handles them, and their output is what
// gets upscaled.

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;

namespace HDSprites
{
    public class ModEntry : Mod
    {
        private ModConfig Config;
        private List<(string Prefix, int W, int H)> Grids = new();

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();
            this.ApplyConfig();

            HDTexture.Monitor = this.Monitor;

            DrawPatches.Apply(new Harmony(this.ModManifest.UniqueID), this.Monitor);

            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Content.AssetReady += this.OnAssetReady;
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += (_, _) => HDTexture.ProcessQueue(4);
        }

        private void ApplyConfig()
        {
            DrawPatches.Enabled = this.Config.Enabled;
            DrawPatches.MinSpriteSize = Math.Max(0, this.Config.MinSpriteSize);
            HDTexture.MaxTextureSize = Math.Max(1024, this.Config.MaxTextureSize);
            HDTexture.SetConcurrency(this.Config.MaxConcurrentUpscales);

            this.Grids = new List<(string, int, int)>();
            foreach (var pair in this.Config.SpriteGrids)
            {
                string[] parts = pair.Value.ToLowerInvariant().Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                    this.Grids.Add((pair.Key, Math.Max(0, w), Math.Max(0, h)));
                else
                    this.Monitor.Log($"Ignoring sprite grid '{pair.Value}' for '{pair.Key}': use WIDTHxHEIGHT, like 16x16.", LogLevel.Warn);
            }
            // Longest prefix first, so the most specific rule wins.
            this.Grids = this.Grids.OrderByDescending(g => g.Prefix.Length).ToList();
        }

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.DataType != typeof(Texture2D))
                return;

            IAssetName name = e.NameWithoutLocale;
            if (!this.Config.UpscaleMapTiles && name.StartsWith("Maps/"))
                return;
            if (this.Config.Exclude.Any(p => name.StartsWith(p)))
                return;
            if (!this.Config.Include.Any(p => name.StartsWith(p)))
                return;

            (int gridW, int gridH) = this.GetGrid(name);
            string key = name.Name;

            // Run after every other edit, and only tag the texture: its pixels are read later on the draw thread.
            e.Edit(asset => HDTexture.Register(asset.AsImage().Data, key, gridW, gridH), AssetEditPriority.Late + 100_000);
        }

        private void OnAssetReady(object sender, AssetReadyEventArgs e)
        {
            // Covers SMAPI copying fresh pixels into an existing texture (e.g. Content Patcher
            // conditions changing with season or weather).
            HDTexture.MarkDirty(e.NameWithoutLocale.Name);
        }

        private (int, int) GetGrid(IAssetName name)
        {
            foreach (var grid in this.Grids)
            {
                if (name.StartsWith(grid.Prefix))
                    return (grid.W, grid.H);
            }
            return (0, 0);
        }

        private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
        {
            if (this.Config.ToggleKey.JustPressed())
            {
                this.Config.Enabled = !this.Config.Enabled;
                DrawPatches.Enabled = this.Config.Enabled;
                this.Helper.WriteConfig(this.Config);
                this.Monitor.Log($"HD drawing {(this.Config.Enabled ? "on" : "off")}.", LogLevel.Info);
            }
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm == null)
                return;

            gmcm.Register(
                mod: this.ModManifest,
                reset: () => { this.Config = new ModConfig(); this.ApplyConfig(); },
                save: () => { this.Helper.WriteConfig(this.Config); this.ApplyConfig(); }
            );

            gmcm.AddBoolOption(
                mod: this.ModManifest,
                getValue: () => this.Config.Enabled,
                setValue: value => this.Config.Enabled = value,
                name: () => "Enabled",
                tooltip: () => "Draw upscaled textures. Turn off to compare."
            );

            gmcm.AddBoolOption(
                mod: this.ModManifest,
                getValue: () => this.Config.UpscaleMapTiles,
                setValue: value => this.Config.UpscaleMapTiles = value,
                name: () => "Upscale map tiles",
                tooltip: () => "Experimental. Ground, floors and walls. Restart the game after changing."
            );

            gmcm.AddKeybindList(
                mod: this.ModManifest,
                getValue: () => this.Config.ToggleKey,
                setValue: value => this.Config.ToggleKey = value,
                name: () => "Toggle key",
                tooltip: () => "Switches HD drawing on and off."
            );

            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.Config.MinSpriteSize,
                setValue: value => this.Config.MinSpriteSize = value,
                name: () => "Min sprite size",
                tooltip: () => "Sprites thinner than this many pixels are drawn normally. Keeps UI bars and lines crisp.",
                min: 0,
                max: 16
            );
        }
    }

    /// <summary>The subset of Generic Mod Config Menu's API this mod uses.</summary>
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string> tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string> formatValue = null, string fieldId = null);
        void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
    }
}
