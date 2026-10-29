// Based on HD Sprites' DrawFix by NinthWorld, modified from PyTK's OvSpritebatchNew by Platonymous. GPLv3.
//
// 1.6 port: Harmony 2, positional argument binding (so it doesn't depend on MonoGame's parameter
// names, which differ between builds), and the draw is re-issued with a scaled source rectangle
// and halved scale instead of being converted to an integer destination rectangle. That keeps
// sub-pixel positions intact, so smooth-moving sprites don't jitter.

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using HarmonyLib;
using StardewModdingAPI;

namespace HDSprites
{
    internal static class DrawPatches
    {
        private static IMonitor Monitor;
        private static bool LoggedError;

        internal static bool Enabled = true;
        internal static int MinSpriteSize = 3;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            Patch(harmony, nameof(Prefix_PosSrcColRotOrgVScaleEffDepth),
                typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(Vector2), typeof(SpriteEffects), typeof(float));
            Patch(harmony, nameof(Prefix_PosSrcColRotOrgFScaleEffDepth),
                typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float));
            Patch(harmony, nameof(Prefix_DestSrcColRotOrgEffDepth),
                typeof(Texture2D), typeof(Rectangle), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(SpriteEffects), typeof(float));
            Patch(harmony, nameof(Prefix_PosSrcCol),
                typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color));
            Patch(harmony, nameof(Prefix_DestSrcCol),
                typeof(Texture2D), typeof(Rectangle), typeof(Rectangle?), typeof(Color));
            Patch(harmony, nameof(Prefix_PosCol),
                typeof(Texture2D), typeof(Vector2), typeof(Color));
            Patch(harmony, nameof(Prefix_DestCol),
                typeof(Texture2D), typeof(Rectangle), typeof(Color));
        }

        private static void Patch(Harmony harmony, string prefixName, params Type[] args)
        {
            var target = AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), args);
            if (target == null)
            {
                Monitor.Log($"SpriteBatch.Draw({string.Join(", ", (object[])args)}) not found; that overload won't be upscaled.", LogLevel.Warn);
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(DrawPatches), prefixName));
        }

        /// <summary>Decide whether to redirect this draw to an HD texture.</summary>
        private static bool TryRedirect(Texture2D texture, Rectangle? source, out Texture2D hd, out Rectangle hdSource)
        {
            hd = null;
            hdSource = default;

            if (!Enabled || texture == null || HDTexture.Copying)
                return false;

            if (source is Rectangle r && (r.Width < MinSpriteSize || r.Height < MinSpriteSize))
                return false;

            return HDTexture.TryGetHD(texture, source, out hd, out hdSource);
        }

        private static void LogError(Exception ex)
        {
            if (LoggedError)
                return;
            LoggedError = true;
            Monitor.Log($"Draw redirect failed, falling back to normal drawing for this call. Further errors are hidden.\n{ex}", LogLevel.Error);
        }

        /*********
        ** Prefixes. __0 is the texture, then the rest of the overload's arguments in order.
        ** Each returns false (skip the original) only after issuing the HD draw itself.
        *********/

        // Draw(texture, position, source, color, rotation, origin, Vector2 scale, effects, depth)
        private static bool Prefix_PosSrcColRotOrgVScaleEffDepth(SpriteBatch __instance, Texture2D __0, Vector2 __1, Rectangle? __2, Color __3, float __4, Vector2 __5, Vector2 __6, SpriteEffects __7, float __8)
        {
            try
            {
                if (!TryRedirect(__0, __2, out var hd, out var hdSrc))
                    return true;
                __instance.Draw(hd, __1, hdSrc, __3, __4, __5 * 2f, __6 / 2f, __7, __8);
                return false;
            }
            catch (Exception ex) { LogError(ex); return true; }
        }

        // Draw(texture, position, source, color, rotation, origin, float scale, effects, depth)
        private static bool Prefix_PosSrcColRotOrgFScaleEffDepth(SpriteBatch __instance, Texture2D __0, Vector2 __1, Rectangle? __2, Color __3, float __4, Vector2 __5, float __6, SpriteEffects __7, float __8)
        {
            try
            {
                if (!TryRedirect(__0, __2, out var hd, out var hdSrc))
                    return true;
                __instance.Draw(hd, __1, hdSrc, __3, __4, __5 * 2f, __6 / 2f, __7, __8);
                return false;
            }
            catch (Exception ex) { LogError(ex); return true; }
        }

        // Draw(texture, destination, source, color, rotation, origin, effects, depth)
        private static bool Prefix_DestSrcColRotOrgEffDepth(SpriteBatch __instance, Texture2D __0, Rectangle __1, Rectangle? __2, Color __3, float __4, Vector2 __5, SpriteEffects __6, float __7)
        {
            try
            {
                if (!TryRedirect(__0, __2, out var hd, out var hdSrc))
                    return true;
                __instance.Draw(hd, __1, hdSrc, __3, __4, __5 * 2f, __6, __7);
                return false;
            }
            catch (Exception ex) { LogError(ex); return true; }
        }

        // Draw(texture, position, source, color)
        private static bool Prefix_PosSrcCol(SpriteBatch __instance, Texture2D __0, Vector2 __1, Rectangle? __2, Color __3)
        {
            try
            {
                if (!TryRedirect(__0, __2, out var hd, out var hdSrc))
                    return true;
                __instance.Draw(hd, __1, hdSrc, __3, 0f, Vector2.Zero, 0.5f, SpriteEffects.None, 0f);
                return false;
            }
            catch (Exception ex) { LogError(ex); return true; }
        }

        // Draw(texture, destination, source, color)
        private static bool Prefix_DestSrcCol(SpriteBatch __instance, Texture2D __0, Rectangle __1, Rectangle? __2, Color __3)
        {
            try
            {
                if (!TryRedirect(__0, __2, out var hd, out var hdSrc))
                    return true;
                __instance.Draw(hd, __1, hdSrc, __3);
                return false;
            }
            catch (Exception ex) { LogError(ex); return true; }
        }

        // Draw(texture, position, color)
        private static bool Prefix_PosCol(SpriteBatch __instance, Texture2D __0, Vector2 __1, Color __2)
        {
            try
            {
                if (!TryRedirect(__0, null, out var hd, out var hdSrc))
                    return true;
                __instance.Draw(hd, __1, hdSrc, __2, 0f, Vector2.Zero, 0.5f, SpriteEffects.None, 0f);
                return false;
            }
            catch (Exception ex) { LogError(ex); return true; }
        }

        // Draw(texture, destination, color)
        private static bool Prefix_DestCol(SpriteBatch __instance, Texture2D __0, Rectangle __1, Color __2)
        {
            try
            {
                if (!TryRedirect(__0, null, out var hd, out var hdSrc))
                    return true;
                __instance.Draw(hd, __1, hdSrc, __2);
                return false;
            }
            catch (Exception ex) { LogError(ex); return true; }
        }
    }
}
