using System.Collections.Generic;
using UnityEngine;

namespace WildCardPoker.Unity
{
    /// <summary>
    /// Generates the placeholder art at runtime (rounded cards, felt, chips, card backs) so the game runs with zero
    /// imported assets. Every sprite is requested through <see cref="Theme"/>, so commissioned art can replace these
    /// one at a time without touching game code.
    /// </summary>
    public static class ProceduralArt
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>White rounded rectangle, 9-sliced so it scales to any size with crisp corners.</summary>
        public static Sprite RoundedRect(int radius = 16)
        {
            string key = "rounded" + radius;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            int size = radius * 2 + 4;
            var tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0, Mathf.Max(radius - x, x - (size - 1 - radius)));
                float dy = Mathf.Max(0, Mathf.Max(radius - y, y - (size - 1 - radius)));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var border = new Vector4(radius + 1, radius + 1, radius + 1, radius + 1);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, border);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Anti-aliased filled circle.</summary>
        public static Sprite Circle(int size = 128)
        {
            string key = "circle" + size;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float r = size / 2f - 1;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d + 0.5f) * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>A casino chip: colored disc with white edge inserts and an inner ring.</summary>
        public static Sprite Chip(Color color)
        {
            string key = "chip" + ColorUtility.ToHtmlStringRGB(color);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            const int size = 96;
            var tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            var c = new Vector2(size / 2f, size / 2f);
            float r = size / 2f - 1;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f) - c;
                float d = p.magnitude;
                float alpha = Mathf.Clamp01(r - d + 0.5f);
                Color col = color;
                float angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg + 180f;
                if (d > r * 0.78f && Mathf.Repeat(angle, 45f) < 15f) col = Color.white;           // edge spots
                if (Mathf.Abs(d - r * 0.62f) < 1.6f) col = Color.Lerp(color, Color.white, 0.7f); // inner ring
                if (d > r - 2f) col = Color.Lerp(col, Color.black, 0.35f);                         // rim shade
                col.a = alpha;
                pixels[y * size + x] = col;
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Card back: deep color with a fine diamond lattice and a gold frame.</summary>
        public static Sprite CardBack(Color baseColor, Color accent)
        {
            string key = "back" + ColorUtility.ToHtmlStringRGB(baseColor) + ColorUtility.ToHtmlStringRGB(accent);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            const int w = 168, h = 236, radius = 14;
            var tex = NewTexture(w, h);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float alpha = RoundedAlpha(x, y, w, h, radius);
                Color col = baseColor;
                bool frame = x < 12 || y < 12 || x >= w - 12 || y >= h - 12;
                bool innerLine = (x == 16 || y == 16 || x == w - 17 || y == h - 17) && x >= 16 && y >= 16 && x <= w - 17 && y <= h - 17;
                if (frame) col = Color.white;
                else if (innerLine) col = accent;
                else
                {
                    float u = Mathf.Repeat(x + y, 16f), v = Mathf.Repeat(x - y + 1000, 16f);
                    if (u < 1.5f || v < 1.5f) col = Color.Lerp(baseColor, accent, 0.55f);
                }
                col.a = alpha;
                pixels[y * w + x] = col;
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Table felt: radial gradient with subtle noise so it reads as cloth, not a flat fill.</summary>
        public static Sprite Felt(Color center, Color edge)
        {
            string key = "felt" + ColorUtility.ToHtmlStringRGB(center);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            const int w = 512, h = 288;
            var tex = NewTexture(w, h);
            var pixels = new Color32[w * h];
            var rng = new System.Random(23);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float nx = (x - w / 2f) / (w / 2f), ny = (y - h / 2f) / (h / 2f);
                float t = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny) / 1.3f);
                Color col = Color.Lerp(center, edge, t * t);
                float grain = (float)rng.NextDouble() * 0.035f - 0.0175f;
                col.r += grain; col.g += grain; col.b += grain;
                col.a = 1;
                pixels[y * w + x] = col;
            }
            tex.SetPixels32(pixels);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            Cache[key] = sprite;
            return sprite;
        }

        private static float RoundedAlpha(int x, int y, int w, int h, int radius)
        {
            float dx = Mathf.Max(0, Mathf.Max(radius - x, x - (w - 1 - radius)));
            float dy = Mathf.Max(0, Mathf.Max(radius - y, y - (h - 1 - radius)));
            return Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
        }

        private static Texture2D NewTexture(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
    }

    /// <summary>Colors and sprites in one place. Swap values here (or assign imported sprites) to restyle the game.</summary>
    public static class Theme
    {
        public static readonly Color FeltCenter = new Color(0.07f, 0.42f, 0.25f);
        public static readonly Color FeltEdge = new Color(0.02f, 0.16f, 0.10f);
        public static readonly Color Rail = new Color(0.20f, 0.11f, 0.06f);
        public static readonly Color Gold = new Color(0.96f, 0.78f, 0.30f);
        public static readonly Color WildGlow = new Color(1f, 0.82f, 0.15f, 0.95f);
        public static readonly Color CardRed = new Color(0.80f, 0.08f, 0.12f);
        public static readonly Color CardBlack = new Color(0.08f, 0.08f, 0.10f);
        public static readonly Color PanelBg = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color ButtonFold = new Color(0.62f, 0.14f, 0.14f);
        public static readonly Color ButtonCall = new Color(0.16f, 0.42f, 0.70f);
        public static readonly Color ButtonRaise = new Color(0.85f, 0.55f, 0.10f);
        public static readonly Color Text = new Color(0.97f, 0.95f, 0.90f);
        public static readonly Color Muted = new Color(0.75f, 0.75f, 0.72f);

        public static Sprite CardFace => ProceduralArt.RoundedRect(14);
        public static Sprite CardBack => ProceduralArt.CardBack(new Color(0.45f, 0.06f, 0.10f), Gold);
        public static Sprite Panel => ProceduralArt.RoundedRect(18);
        public static Sprite Felt => ProceduralArt.Felt(FeltCenter, FeltEdge);
        public static Sprite Circle => ProceduralArt.Circle();

        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 32);
                return _font;
            }
        }
    }
}
