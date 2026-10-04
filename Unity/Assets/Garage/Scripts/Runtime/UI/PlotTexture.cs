using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Garage.Unity.UI
{
    /// <summary>CPU line plotter into a Texture2D shown by a RawImage (graphs of live data, dyno, scope, maps).</summary>
    public sealed class PlotTexture
    {
        private readonly Color32[] _px;

        public PlotTexture(Transform parent, int width = 900, int height = 300, float preferredHeight = 260)
        {
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            _px = new Color32[width * height];
            RectTransform r = UiKit.Rect(parent, "Plot");
            Image = r.gameObject.AddComponent<RawImage>();
            Image.texture = Texture;
            UiKit.Size(Image, preferredHeight, -1, 1);
            Clear();
            Apply();
        }

        public Texture2D Texture { get; }

        public RawImage Image { get; }

        public int Width => Texture.width;

        public int Height => Texture.height;

        /// <summary>Clears with a dark background and a 10×8 grid.</summary>
        public void Clear()
        {
            Color32 bg = new Color(0.03f, 0.04f, 0.05f, 1f);
            Color32 grid = new Color(0.12f, 0.14f, 0.16f, 1f);
            for (int i = 0; i < _px.Length; i++)
            {
                _px[i] = bg;
            }

            for (int gx = 0; gx <= 10; gx++)
            {
                int x = Mathf.Min(Width - 1, gx * (Width - 1) / 10);
                for (int y = 0; y < Height; y++)
                {
                    _px[y * Width + x] = grid;
                }
            }

            for (int gy = 0; gy <= 8; gy++)
            {
                int y = Mathf.Min(Height - 1, gy * (Height - 1) / 8);
                for (int x = 0; x < Width; x++)
                {
                    _px[y * Width + x] = grid;
                }
            }
        }

        /// <summary>Draws a line in pixel coordinates.</summary>
        public void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            for (int guard = 0; guard < 20000; guard++)
            {
                if (x0 >= 0 && x0 < Width && y0 >= 0 && y0 < Height)
                {
                    _px[y0 * Width + x0] = c;
                    if (y0 + 1 < Height)
                    {
                        _px[(y0 + 1) * Width + x0] = c;
                    }
                }

                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        /// <summary>Plots y(x) series scaled to the given ranges.</summary>
        public void Series(IList<double> xs, IList<double> ys, double xMin, double xMax, double yMin, double yMax, Color c)
        {
            if (xs == null || ys == null || xs.Count < 2 || xMax <= xMin || yMax <= yMin)
            {
                return;
            }

            int px = -1, py = -1;
            Color32 c32 = c;
            for (int i = 0; i < Mathf.Min(xs.Count, ys.Count); i++)
            {
                if (double.IsNaN(ys[i]))
                {
                    continue;
                }

                int x = (int)((xs[i] - xMin) / (xMax - xMin) * (Width - 1));
                int y = (int)((ys[i] - yMin) / (yMax - yMin) * (Height - 1));
                if (px >= 0)
                {
                    Line(px, py, x, y, c32);
                }

                px = x;
                py = y;
            }
        }

        /// <summary>Plots values evenly spaced across the width.</summary>
        public void Series(IList<double> ys, double yMin, double yMax, Color c)
        {
            var xs = new double[ys.Count];
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] = i;
            }

            Series(xs, ys, 0, Mathf.Max(1, ys.Count - 1), yMin, yMax, c);
        }

        /// <summary>Uploads the pixels.</summary>
        public void Apply()
        {
            Texture.SetPixels32(_px);
            Texture.Apply(false);
        }
    }
}
