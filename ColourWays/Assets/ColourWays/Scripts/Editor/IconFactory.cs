using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ColourWays.EditorTools
{
    /// <summary>
    /// Draws simple glyphs and UI shapes with signed distance functions (plus an optional
    /// halftone shade channel) and saves them as PNG sprites. Only used by the setup tools; the
    /// PNGs are normal assets you can replace with real art.
    /// </summary>
    internal static class IconFactory
    {
        public sealed class Canvas
        {
            public readonly int Width, Height;
            public readonly float[] Alpha;
            /// <summary>Brightness per pixel (1 = white). Tinting in UI multiplies this.</summary>
            public readonly float[] Shade;
            public int Size => Width;

            public Canvas(int width, int height)
            {
                Width = width;
                Height = height;
                Alpha = new float[width * height];
                Shade = new float[width * height];
                for (int i = 0; i < Shade.Length; i++) Shade[i] = 1f;
            }

            public void Draw(Func<Vector2, float> sdf)
            {
                for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float a = Mathf.Clamp01(0.5f - sdf(new Vector2(x + 0.5f, y + 0.5f)));
                    int i = y * Width + x;
                    if (a > Alpha[i]) Alpha[i] = a;
                }
            }

            /// <summary>
            /// Screen-print style dots (45° grid) inside the existing alpha. <paramref name="density"/>
            /// returns 0..1 per pixel; dots darken the shade channel to <paramref name="dotShade"/>.
            /// </summary>
            public void Halftone(Func<Vector2, float> density, float spacing, float dotShade, bool dotsAsAlpha = false)
            {
                for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float dens = Mathf.Clamp01(density(p));
                    if (dens <= 0.001f) continue;
                    Vector2 r = new Vector2(p.x + p.y, p.y - p.x) * (0.70710678f / spacing);
                    Vector2 f = new Vector2(r.x - Mathf.Floor(r.x) - 0.5f, r.y - Mathf.Floor(r.y) - 0.5f);
                    float radius = Mathf.Sqrt(dens) * 0.62f;
                    float cover = Mathf.Clamp01((radius - f.magnitude) * spacing + 0.5f);
                    int i = y * Width + x;
                    if (dotsAsAlpha) Alpha[i] = Mathf.Max(Alpha[i], cover);
                    else Shade[i] = Mathf.Lerp(Shade[i], dotShade, cover);
                }
            }
        }

        // ---------------------------------------------------------------- SDF primitives (pixel units)

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        private static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        private static float Ring(Vector2 p, Vector2 c, float r, float thickness) => Mathf.Abs((p - c).magnitude - r) - thickness;

        private static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 e0 = b - a, e1 = c - b, e2 = a - c;
            Vector2 v0 = p - a, v1 = p - b, v2 = p - c;
            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            Vector2 d0 = new Vector2(pq0.sqrMagnitude, s * (v0.x * e0.y - v0.y * e0.x));
            Vector2 d1 = new Vector2(pq1.sqrMagnitude, s * (v1.x * e1.y - v1.y * e1.x));
            Vector2 d2 = new Vector2(pq2.sqrMagnitude, s * (v2.x * e2.y - v2.y * e2.x));
            Vector2 d = Vector2.Min(Vector2.Min(d0, d1), d2);
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }

        /// <summary>Ring segment from angle a0 to a1 (degrees, counter-clockwise).</summary>
        private static float Arc(Vector2 p, Vector2 c, float radius, float thickness, float a0, float a1)
        {
            Vector2 d = p - c;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float span = Mathf.Repeat(a1 - a0, 360f);
            float rel = Mathf.Repeat(ang - a0, 360f);
            if (rel <= span)
                return Mathf.Abs(d.magnitude - radius) - thickness;
            Vector2 e0 = c + Polar(a0) * radius, e1 = c + Polar(a1) * radius;
            return Mathf.Min((p - e0).magnitude, (p - e1).magnitude) - thickness;
        }

        private static float RoundedBox(Vector2 p, Vector2 c, Vector2 half, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + Vector2.one * r;
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        private static Vector2 Polar(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        // ---------------------------------------------------------------- Instruction glyphs (128 px, y up)

        private const float Stroke = 9f;

        public static void DrawArrow(Canvas c)
        {
            c.Draw(p => Capsule(p, V(64, 20), V(64, 78), Stroke));
            c.Draw(p => Triangle(p, V(30, 72), V(98, 72), V(64, 112)));
        }

        public static void DrawTurn(Canvas c, bool right)
        {
            float M(float x) => right ? x : 128f - x;
            c.Draw(p => Capsule(p, V(M(38), 18), V(M(38), 58), Stroke));
            float a0 = right ? 90f : 0f, a1 = right ? 180f : 90f;
            c.Draw(p => Arc(p, V(M(64), 58), 26f, Stroke, a0, a1));
            c.Draw(p => Capsule(p, V(M(64), 84), V(M(80), 84), Stroke));
            c.Draw(p => Triangle(p, V(M(78), 60), V(M(78), 108), V(M(112), 84)));
        }

        public static void DrawReverse(Canvas c)
        {
            c.Draw(p => Capsule(p, V(86, 18), V(86, 64), Stroke));
            c.Draw(p => Arc(p, V(64, 64), 22f, Stroke, 0f, 180f));
            c.Draw(p => Capsule(p, V(42, 64), V(42, 50), Stroke));
            c.Draw(p => Triangle(p, V(20, 54), V(64, 54), V(42, 20)));
        }

        public static void DrawJump(Canvas c)
        {
            c.Draw(p => RoundedBox(p, V(64, 24), V(13, 13), 4f));
            c.Draw(p => Arc(p, V(64, 40), 40f, Stroke * 0.85f, 30f, 170f));
            Vector2 end = V(64, 40) + Polar(30f) * 40f;
            Vector2 tangent = new Vector2(Mathf.Sin(30f * Mathf.Deg2Rad), -Mathf.Cos(30f * Mathf.Deg2Rad));
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            c.Draw(p => Triangle(p, end + normal * 17f - tangent * 4f, end - normal * 17f - tangent * 4f, end + tangent * 26f));
        }

        // ---------------------------------------------------------------- UI shapes

        public static void DrawRoundedPanel(Canvas c, float radius)
        {
            var centre = V(c.Width * 0.5f, c.Height * 0.5f);
            var half = V(c.Width * 0.5f - 1f, c.Height * 0.5f - 1f);
            c.Draw(p => RoundedBox(p, centre, half, radius));
        }

        public static void DrawRoundedOutline(Canvas c, float radius, float stroke)
        {
            var centre = V(c.Width * 0.5f, c.Height * 0.5f);
            var half = V(c.Width * 0.5f - 1f - stroke, c.Height * 0.5f - 1f - stroke);
            c.Draw(p => Mathf.Abs(RoundedBox(p, centre, half, radius - stroke)) - stroke);
        }

        /// <summary>Soft drop shadow: a rounded box whose edge fades out over <paramref name="blur"/> pixels.</summary>
        public static void DrawSoftShadow(Canvas c, float radius, float blur)
        {
            var centre = V(c.Width * 0.5f, c.Height * 0.5f);
            var half = V(c.Width * 0.5f - blur - 1f, c.Height * 0.5f - blur - 1f);
            for (int y = 0; y < c.Height; y++)
            for (int x = 0; x < c.Width; x++)
            {
                float d = RoundedBox(V(x + 0.5f, y + 0.5f), centre, half, radius);
                float t = Mathf.Clamp01(1f - (d + blur * 0.15f) / blur);
                c.Alpha[y * c.Width + x] = t * t * (3f - 2f * t);
                c.Shade[y * c.Width + x] = 0f;
            }
        }

        public static void DrawDisc(Canvas c)
        {
            float half = c.Width * 0.5f;
            c.Draw(p => Circle(p, V(half, half), half - 1.5f));
        }

        /// <summary>Tray key: rounded rectangle with soft halftone shading toward the bottom.</summary>
        public static void DrawKey(Canvas c, float radius)
        {
            DrawRoundedPanel(c, radius);
            float h = c.Height;
            c.Halftone(p => Mathf.Clamp01((0.42f - p.y / h) / 0.42f) * 0.32f, 5f, 0.84f);
        }

        /// <summary>Coloured chip: disc lit from the top-left, halftone in the shade.</summary>
        public static void DrawChip(Canvas c)
        {
            DrawDisc(c);
            float half = c.Width * 0.5f;
            var light = new Vector2(-0.6f, 0.8f);
            c.Halftone(p =>
            {
                Vector2 n = (p - V(half, half)) / half;
                return Mathf.Clamp01(-Vector2.Dot(n, light) * 0.55f + 0.05f);
            }, 5.5f, 0.74f);
        }

        /// <summary>Dots that fade out upward (sits on top of the bottom panel).</summary>
        public static void DrawHalftoneFade(Canvas c)
        {
            float h = c.Height;
            // White dots in alpha so the UI Image colour tints them.
            c.Halftone(p => Mathf.Pow(Mathf.Clamp01(1f - p.y / h), 1.4f) * 0.5f, 6f, 1f, true);
        }

        public static void DrawCrosshair(Canvas c)
        {
            var m = V(48, 48);
            c.Draw(p => Ring(p, m, 24f, 3.5f));
            c.Draw(p => Circle(p, m, 6f));
            c.Draw(p => Capsule(p, V(48, 8), V(48, 18), 3.5f));
            c.Draw(p => Capsule(p, V(48, 78), V(48, 88), 3.5f));
            c.Draw(p => Capsule(p, V(8, 48), V(18, 48), 3.5f));
            c.Draw(p => Capsule(p, V(78, 48), V(88, 48), 3.5f));
        }

        /// <summary>Circular arrow (96 px). Counter-clockwise unless <paramref name="clockwise"/>.</summary>
        public static void DrawRotate(Canvas c, bool clockwise)
        {
            float M(float x) => clockwise ? 96f - x : x;
            var centre = V(48, 48);
            const float r = 25f, a0 = 70f, a1 = 340f;
            c.Draw(p => Arc(V(M(p.x), p.y), centre, r, 3.8f, a0, a1));
            // Arrowhead at the a0 end, pointing back along the arc (counter-clockwise travel ends here).
            Vector2 end = centre + Polar(a0) * r;
            Vector2 tangent = new Vector2(-Mathf.Sin(a0 * Mathf.Deg2Rad), Mathf.Cos(a0 * Mathf.Deg2Rad));
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            Vector2 tip = end + tangent * 14f, b1 = end + normal * 11f - tangent * 2f, b2 = end - normal * 11f - tangent * 2f;
            c.Draw(p => Triangle(V(M(p.x), p.y), tip, b1, b2));
        }

        public static void DrawRing(Canvas c, float thickness)
        {
            float half = c.Width * 0.5f;
            c.Draw(p => Ring(p, V(half, half), half - thickness - 2f, thickness));
        }

        // ---------------------------------------------------------------- Saving

        public static Sprite Save(string path, int size, Action<Canvas> draw, Vector4 border = default, bool mipmaps = true)
            => Save(path, size, size, draw, border, mipmaps);

        /// <summary>Renders to a PNG (RGB = shade, A = coverage) and imports it as a sprite.</summary>
        public static Sprite Save(string path, int width, int height, Action<Canvas> draw, Vector4 border = default, bool mipmaps = true)
        {
            var canvas = new Canvas(width, height);
            draw(canvas);

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte s = (byte)Mathf.RoundToInt(Mathf.Clamp01(canvas.Shade[i]) * 255f);
                pixels[i] = new Color32(s, s, s, (byte)Mathf.RoundToInt(canvas.Alpha[i] * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = mipmaps;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spriteBorder = border;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
