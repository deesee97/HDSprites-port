// 2xBR filter.
// Originally written in JavaScript by Ascari (https://github.com/carlosascari/2xBR-Filter),
// converted to C#/XNA by NinthWorld for HD Sprites (GPLv3).
// 1.6 port: works on raw pixel arrays so it can run off the main thread, uses no shared
// state, and can respect a sprite grid so neighbouring tiles don't bleed into each other.

using System;
using Microsoft.Xna.Framework;

namespace HDSprites
{
    public static class Upscaler
    {
        private const int YWeight = 48;
        private const int UWeight = 7;
        private const int VWeight = 6;

        private static double D(Color a, Color b)
        {
            int r = Math.Abs(a.R - b.R);
            int g = Math.Abs(a.G - b.G);
            int bl = Math.Abs(a.B - b.B);
            double y = r * 0.299 + g * 0.587 + bl * 0.114;
            double u = r * -0.168736 + g * -0.331264 + bl * 0.5;
            double v = r * 0.5 + g * -0.418688 + bl * -0.081312;
            return y * YWeight + u * UWeight + v * VWeight;
        }

        // Texture data in the game is premultiplied, so a straight linear mix is correct.
        private static Color Mix(Color a, Color b)
        {
            return new Color(
                (byte)((a.R + b.R) >> 1),
                (byte)((a.G + b.G) >> 1),
                (byte)((a.B + b.B) >> 1),
                (byte)((a.A + b.A) >> 1));
        }

        /// <summary>Upscale pixel data 2x. Thread-safe.</summary>
        /// <param name="src">Source pixels, row-major.</param>
        /// <param name="w">Source width.</param>
        /// <param name="h">Source height.</param>
        /// <param name="gridW">Sprite grid width, or 0 for none.</param>
        /// <param name="gridH">Sprite grid height, or 0 for none.</param>
        public static Color[] Upscale2x(Color[] src, int w, int h, int gridW, int gridH)
        {
            int dw = w * 2;
            Color[] dst = new Color[dw * h * 2];
            Color[] m = new Color[21];

            for (int y = 0; y < h; ++y)
            {
                int cy0 = gridH > 0 ? (y / gridH) * gridH : 0;
                int cy1 = gridH > 0 ? Math.Min(cy0 + gridH, h) - 1 : h - 1;

                for (int x = 0; x < w; ++x)
                {
                    int cx0 = gridW > 0 ? (x / gridW) * gridW : 0;
                    int cx1 = gridW > 0 ? Math.Min(cx0 + gridW, w) - 1 : w - 1;

                    Color P(int px, int py)
                    {
                        // Outside the image: transparent (matches the original look on sprite edges).
                        if (px < 0 || px >= w || py < 0 || py >= h)
                            return Color.Transparent;
                        // Outside this sprite's grid cell: repeat the edge pixel so nothing bleeds in.
                        if (px < cx0) px = cx0; else if (px > cx1) px = cx1;
                        if (py < cy0) py = cy0; else if (py > cy1) py = cy1;
                        return src[py * w + px];
                    }

                    /*  Matrix, 10 is the current pixel:
                            -2  -1   0  +1  +2
                        -2       [ 0][ 1][ 2]
                        -1   [ 3][ 4][ 5][ 6][ 7]
                         0   [ 8][ 9][10][11][12]
                        +1   [13][14][15][16][17]
                        +2       [18][19][20]
                    */
                    m[0] = P(x - 1, y - 2); m[1] = P(x, y - 2); m[2] = P(x + 1, y - 2);
                    m[3] = P(x - 2, y - 1); m[4] = P(x - 1, y - 1); m[5] = P(x, y - 1); m[6] = P(x + 1, y - 1); m[7] = P(x + 2, y - 1);
                    m[8] = P(x - 2, y); m[9] = P(x - 1, y); m[10] = P(x, y); m[11] = P(x + 1, y); m[12] = P(x + 2, y);
                    m[13] = P(x - 2, y + 1); m[14] = P(x - 1, y + 1); m[15] = P(x, y + 1); m[16] = P(x + 1, y + 1); m[17] = P(x + 2, y + 1);
                    m[18] = P(x - 1, y + 2); m[19] = P(x, y + 2); m[20] = P(x + 1, y + 2);

                    Color c = m[10];
                    int o00 = (y * 2) * dw + x * 2;
                    int o10 = o00 + 1;
                    int o01 = o00 + dw;
                    int o11 = o01 + 1;

                    // Fully transparent area with transparent surroundings: skip the maths.
                    if (c.A == 0 && m[5].A == 0 && m[9].A == 0 && m[11].A == 0 && m[15].A == 0
                        && m[4].A == 0 && m[6].A == 0 && m[14].A == 0 && m[16].A == 0)
                    {
                        dst[o00] = dst[o10] = dst[o01] = dst[o11] = c;
                        continue;
                    }

                    double d_10_9 = D(m[10], m[9]);
                    double d_10_5 = D(m[10], m[5]);
                    double d_10_11 = D(m[10], m[11]);
                    double d_10_15 = D(m[10], m[15]);
                    double d_10_14 = D(m[10], m[14]);
                    double d_10_6 = D(m[10], m[6]);
                    double d_4_8 = D(m[4], m[8]);
                    double d_4_1 = D(m[4], m[1]);
                    double d_9_5 = D(m[9], m[5]);
                    double d_9_15 = D(m[9], m[15]);
                    double d_9_3 = D(m[9], m[3]);
                    double d_5_11 = D(m[5], m[11]);
                    double d_5_0 = D(m[5], m[0]);
                    double d_10_4 = D(m[10], m[4]);
                    double d_10_16 = D(m[10], m[16]);
                    double d_6_12 = D(m[6], m[12]);
                    double d_6_1 = D(m[6], m[1]);
                    double d_11_15 = D(m[11], m[15]);
                    double d_11_7 = D(m[11], m[7]);
                    double d_5_2 = D(m[5], m[2]);
                    double d_14_8 = D(m[14], m[8]);
                    double d_14_19 = D(m[14], m[19]);
                    double d_15_18 = D(m[15], m[18]);
                    double d_9_13 = D(m[9], m[13]);
                    double d_16_12 = D(m[16], m[12]);
                    double d_16_19 = D(m[16], m[19]);
                    double d_15_20 = D(m[15], m[20]);
                    double d_15_17 = D(m[15], m[17]);

                    // Top left
                    double a1 = d_10_14 + d_10_6 + d_4_8 + d_4_1 + 4 * d_9_5;
                    double b1 = d_9_15 + d_9_3 + d_5_11 + d_5_0 + 4 * d_10_4;
                    dst[o00] = a1 < b1 ? Mix(d_10_9 <= d_10_5 ? m[9] : m[5], c) : c;

                    // Top right
                    double a2 = d_10_16 + d_10_4 + d_6_12 + d_6_1 + 4 * d_5_11;
                    double b2 = d_11_15 + d_11_7 + d_9_5 + d_5_2 + 4 * d_10_6;
                    dst[o10] = a2 < b2 ? Mix(d_10_5 <= d_10_11 ? m[5] : m[11], c) : c;

                    // Bottom left
                    double a3 = d_10_4 + d_10_16 + d_14_8 + d_14_19 + 4 * d_9_15;
                    double b3 = d_9_5 + d_9_13 + d_11_15 + d_15_18 + 4 * d_10_14;
                    dst[o01] = a3 < b3 ? Mix(d_10_9 <= d_10_15 ? m[9] : m[15], c) : c;

                    // Bottom right
                    double a4 = d_10_6 + d_10_14 + d_16_12 + d_16_19 + 4 * d_11_15;
                    double b4 = d_9_15 + d_15_20 + d_15_17 + d_5_11 + 4 * d_10_16;
                    dst[o11] = a4 < b4 ? Mix(d_10_11 <= d_10_15 ? m[11] : m[15], c) : c;
                }
            }

            return dst;
        }
    }
}
