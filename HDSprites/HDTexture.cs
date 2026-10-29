// Based on HD Sprites' AssetTexture by NinthWorld, itself modified from PyTK's ScaledTexture2D
// by Platonymous (https://github.com/Platonymous/Stardew-Valley-Mods). GPLv3.
//
// 1.6 port: instead of replacing the game's texture with a copy (which reads pixels at load time,
// and on Android many assets load on a background thread where reading pixels returns blank data),
// the game's own texture is tagged and its pixels are only read later, on the draw thread. The 2x
// copy is built in the background, tall textures are split into overlapping strips that fit the
// GPU's size limit, and the copy is rebuilt when SMAPI propagates new pixels into the texture.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace HDSprites
{
    public class HDTexture
    {
        private enum State { None, Queued, Pending, Ready, Failed }

        private struct Strip
        {
            public Texture2D Texture;
            public int SourceY;
            public int SourceHeight;
        }

        /*********
        ** Shared state
        *********/
        internal static IMonitor Monitor;
        internal static int MaxTextureSize = 4096;
        private static SemaphoreSlim Gate = new(2);

        private static readonly ConditionalWeakTable<Texture2D, HDTexture> Table = new();
        private static readonly Dictionary<string, List<WeakReference<HDTexture>>> ByName = new(StringComparer.OrdinalIgnoreCase);

        // Textures waiting for their pixels to be read (done between frames, on the game thread).
        private static readonly Queue<HDTexture> ReadQueue = new();
        private static SpriteBatch CopyBatch;

        /// <summary>True while this mod draws a texture into a canvas to read it, so the draw patch leaves it alone.</summary>
        internal static bool Copying;

        // One-entry cache: consecutive draws usually use the same texture.
        private static Texture2D LastKey;
        private static HDTexture LastValue;

        /*********
        ** Instance state
        *********/
        public string AssetName { get; private set; }
        private readonly Texture2D Source;
        private int GridW;
        private int GridH;
        private State Status = State.None;
        private Task<Color[]> Job;
        private int JobWidth;
        private int JobHeight;
        private readonly List<Strip> Strips = new();
        private volatile bool Dirty;

        private HDTexture(Texture2D source, string assetName, int gridW, int gridH)
        {
            this.Source = source;
            this.AssetName = assetName;
            this.GridW = gridW;
            this.GridH = gridH;
        }

        internal static void SetConcurrency(int max)
        {
            Gate = new SemaphoreSlim(Math.Max(1, max));
        }

        /// <summary>Tag a game texture for upscaling. Doesn't touch its pixels, so it's safe on any thread.</summary>
        internal static void Register(Texture2D texture, string assetName, int gridW, int gridH)
        {
            if (texture == null)
                return;

            HDTexture entry;
            lock (ByName)
            {
                if (Table.TryGetValue(texture, out entry))
                {
                    entry.AssetName = assetName;
                    entry.GridW = gridW;
                    entry.GridH = gridH;
                    entry.Dirty = true;
                }
                else
                {
                    entry = new HDTexture(texture, assetName, gridW, gridH);
                    Table.Add(texture, entry);
                }

                if (!ByName.TryGetValue(assetName, out var list))
                    ByName[assetName] = list = new List<WeakReference<HDTexture>>();
                list.RemoveAll(r => !r.TryGetTarget(out var t) || t.Source.IsDisposed || ReferenceEquals(t, entry));
                list.Add(new WeakReference<HDTexture>(entry));
            }
        }

        internal static bool IsRegistered(Texture2D texture)
        {
            return texture != null && Table.TryGetValue(texture, out _);
        }

        /// <summary>Flag every tagged texture for an asset so its HD copy is rebuilt on next draw.</summary>
        internal static void MarkDirty(string assetName)
        {
            lock (ByName)
            {
                if (!ByName.TryGetValue(assetName, out var list))
                    return;
                list.RemoveAll(r => !r.TryGetTarget(out var t) || t.Source.IsDisposed);
                foreach (var weak in list)
                {
                    if (weak.TryGetTarget(out var t))
                        t.Dirty = true;
                }
            }
        }

        /// <summary>Get the HD texture and source rectangle for a draw, if ready. Call from the draw thread.</summary>
        internal static bool TryGetHD(Texture2D texture, Rectangle? source, out Texture2D hd, out Rectangle hdSrc)
        {
            hd = null;
            hdSrc = default;

            HDTexture entry;
            if (ReferenceEquals(texture, LastKey))
                entry = LastValue;
            else
            {
                Table.TryGetValue(texture, out entry);
                LastKey = texture;
                LastValue = entry;
            }

            return entry != null && entry.TryGet(source ?? texture.Bounds, out hd, out hdSrc);
        }

        private bool TryGet(Rectangle src, out Texture2D hd, out Rectangle hdSrc)
        {
            hd = null;
            hdSrc = default;

            if (this.Source.IsDisposed)
            {
                this.Reset();
                return false;
            }

            // Size changed under us (SMAPI swapped in different pixels): rebuild.
            if (this.Status == State.Ready && this.Strips.Count > 0
                && (this.Source.Width != this.JobWidth || this.Source.Height != this.JobHeight))
                this.Dirty = true;

            if (this.Dirty)
            {
                this.Dirty = false;
                this.Reset();
            }

            switch (this.Status)
            {
                case State.None:
                    this.Status = State.Queued;
                    ReadQueue.Enqueue(this);
                    return false;

                case State.Queued:
                    return false;

                case State.Pending:
                    if (!this.Job.IsCompleted)
                        return false;
                    this.Finish();
                    if (this.Status != State.Ready)
                        return false;
                    break;

                case State.Failed:
                    return false;
            }

            if (src.X < 0 || src.Right > this.JobWidth)
                return false;

            foreach (Strip strip in this.Strips)
            {
                if (src.Y >= strip.SourceY && src.Bottom <= strip.SourceY + strip.SourceHeight)
                {
                    hd = strip.Texture;
                    hdSrc = new Rectangle(src.X * 2, (src.Y - strip.SourceY) * 2, src.Width * 2, src.Height * 2);
                    return true;
                }
            }

            // Sprite crosses a strip boundary: draw it at normal resolution.
            return false;
        }

        /// <summary>Read pixels for queued textures and start their upscales. Call between frames on the game thread.</summary>
        internal static void ProcessQueue(int max)
        {
            for (int i = 0; i < max && ReadQueue.Count > 0; i++)
            {
                HDTexture entry = ReadQueue.Dequeue();
                if (entry.Status != State.Queued)
                    continue;
                if (entry.Source.IsDisposed)
                {
                    entry.Status = State.None;
                    continue;
                }

                try
                {
                    entry.Begin();
                }
                catch (Exception ex)
                {
                    entry.Status = State.Failed;
                    Monitor?.Log($"Couldn't read {entry.AssetName}: {ex.Message}", LogLevel.Warn);
                }
            }
        }

        /// <summary>
        /// Get a texture's pixels as 32-bit colour. Android stores many game textures in compact formats
        /// that GetData can't read as Color, so draw the texture into a standard canvas and read that.
        /// </summary>
        private static Color[] ReadPixels(Texture2D texture)
        {
            int w = texture.Width;
            int h = texture.Height;
            Color[] data = new Color[w * h];

            GraphicsDevice device = texture.GraphicsDevice;
            RenderTargetBinding[] previous = device.GetRenderTargets();
            Viewport viewport = device.Viewport;

            using RenderTarget2D canvas = new RenderTarget2D(device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
            try
            {
                device.SetRenderTarget(canvas);
                device.Clear(Color.Transparent);

                CopyBatch ??= new SpriteBatch(device);
                Copying = true;
                CopyBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
                CopyBatch.Draw(texture, Vector2.Zero, Color.White);
                CopyBatch.End();
            }
            finally
            {
                Copying = false;
                if (previous.Length > 0)
                    device.SetRenderTargets(previous);
                else
                    device.SetRenderTarget(null);
                device.Viewport = viewport;
            }

            canvas.GetData(data);
            return data;
        }

        private void Begin()
        {
            int w = this.Source.Width;
            int h = this.Source.Height;
            if (w * 2 > MaxTextureSize || w <= 0 || h <= 0)
            {
                this.Status = State.Failed;
                return;
            }

            Color[] data = ReadPixels(this.Source);

            int gw = this.GridW;
            int gh = this.GridH;
            this.JobWidth = w;
            this.JobHeight = h;

            SemaphoreSlim gate = Gate;
            this.Job = Task.Run(async () =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    return Upscaler.Upscale2x(data, w, h, gw, gh);
                }
                finally
                {
                    gate.Release();
                }
            });
            this.Status = State.Pending;
        }

        private void Finish()
        {
            Task<Color[]> job = this.Job;
            this.Job = null;

            if (job.IsFaulted || job.IsCanceled)
            {
                this.Status = State.Failed;
                Monitor?.Log($"Couldn't upscale {this.AssetName}: {job.Exception?.GetBaseException()}", LogLevel.Warn);
                return;
            }

            try
            {
                Color[] hdData = job.Result;
                int w = this.JobWidth;
                int h = this.JobHeight;
                int dw = w * 2;

                int stripSrcHeight = Math.Max(16, MaxTextureSize / 2);
                if (h <= stripSrcHeight)
                {
                    this.AddStrip(hdData, dw, 0, h);
                }
                else
                {
                    // Overlap strips so most sprites sit fully inside at least one of them.
                    int overlap = Math.Min(256, stripSrcHeight / 4);
                    int step = stripSrcHeight - overlap;
                    for (int y = 0; ; y += step)
                    {
                        int sh = Math.Min(stripSrcHeight, h - y);
                        this.AddStrip(hdData, dw, y, sh);
                        if (y + sh >= h)
                            break;
                    }
                }

                this.Status = State.Ready;
            }
            catch (Exception ex)
            {
                this.ReleaseStrips();
                this.Status = State.Failed;
                Monitor?.Log($"Couldn't create HD texture for {this.AssetName}: {ex.Message}", LogLevel.Warn);
            }
        }

        private void AddStrip(Color[] hdData, int dw, int srcY, int srcHeight)
        {
            Texture2D tex = new Texture2D(this.Source.GraphicsDevice, dw, srcHeight * 2);
            tex.SetData(hdData, srcY * 2 * dw, dw * srcHeight * 2);
            this.Strips.Add(new Strip { Texture = tex, SourceY = srcY, SourceHeight = srcHeight });
        }

        private void Reset()
        {
            this.ReleaseStrips();
            this.Job = null; // any running job finishes and is discarded
            this.Status = State.None;
        }

        private void ReleaseStrips()
        {
            foreach (Strip strip in this.Strips)
                strip.Texture?.Dispose();
            this.Strips.Clear();
        }
    }
}
