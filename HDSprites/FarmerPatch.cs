// The game doesn't draw the farmer's body straight from its asset. FarmerRenderer copies the base
// sprite sheet into its own texture and recolours skin, eyes and shoes there. That texture isn't an
// asset, so it never passes through AssetRequested. This patch tags it whenever the game rebuilds
// it, so the body is upscaled along with the hair and clothes layers drawn on top.
//
// Patched by name through reflection, so the mod still builds without the game's DLL and degrades
// cleanly (farmer left at normal resolution) if a game version names things differently.

using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace HDSprites
{
    internal static class FarmerPatch
    {
        private static IMonitor Monitor;
        private static FieldInfo BaseTextureField;
        private static FieldInfo SpriteDirtyField;

        /// <summary>Whether the patch was applied. If false, the farmer is left alone entirely.</summary>
        internal static bool Active { get; private set; }

        /// <summary>Whether the farmer upscaling switch is on.</summary>
        internal static bool Enabled;

        internal const string AssetName = "Characters/Farmer/(rendered body)";

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                Type renderer = AccessTools.TypeByName("StardewValley.FarmerRenderer");
                MethodInfo recolor = renderer != null ? AccessTools.Method(renderer, "executeRecolorActions") : null;
                BaseTextureField = renderer != null ? AccessTools.Field(renderer, "baseTexture") : null;
                SpriteDirtyField = renderer != null ? AccessTools.Field(renderer, "_spriteDirty") : null;

                if (recolor == null || BaseTextureField == null)
                {
                    monitor.Log("Couldn't find the farmer renderer in this game version, so the farmer stays at normal resolution.", LogLevel.Warn);
                    return;
                }

                harmony.Patch(
                    recolor,
                    prefix: new HarmonyMethod(typeof(FarmerPatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(FarmerPatch), nameof(Postfix))
                );
                Active = true;

                if (SpriteDirtyField == null)
                    monitor.Log("Farmer recolour flag not found: skin/shoe changes may need a restart to show in HD.", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Active = false;
                monitor.Log($"Couldn't patch the farmer renderer, so the farmer stays at normal resolution.\n{ex}", LogLevel.Warn);
            }
        }

        // The game calls executeRecolorActions every time it draws the farmer, but only does work when
        // the sprite is flagged dirty. Capture that flag so the HD copy is only rebuilt after real changes.
        private static void Prefix(object __instance, out bool __state)
        {
            __state = false;
            try
            {
                if (SpriteDirtyField?.GetValue(__instance) is bool dirty)
                    __state = dirty;
            }
            catch { /* leave as false */ }
        }

        private static void Postfix(object __instance, bool __state)
        {
            if (!Enabled)
                return;

            try
            {
                if (BaseTextureField.GetValue(__instance) is not Texture2D texture || texture.IsDisposed)
                    return;

                // New texture (first draw, or the game swapped base sheets), or the game just recoloured it.
                if (__state || !HDTexture.IsRegistered(texture))
                    HDTexture.Register(texture, AssetName, 16, 32);
            }
            catch (Exception ex)
            {
                Monitor?.LogOnce($"Farmer upscaling hit an error and may not update: {ex.Message}", LogLevel.Warn);
            }
        }
    }
}
