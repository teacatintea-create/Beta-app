using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kosynka
{
    public static class Pal
    {
        public static Color Hex(string h)
        {
            if (string.IsNullOrEmpty(h) || h[0] != '#') return Color.magenta;
            try
            {
                if (h.Length == 4)
                {
                    int r = Convert.ToInt32(new string(h[1], 2), 16), g = Convert.ToInt32(new string(h[2], 2), 16), b = Convert.ToInt32(new string(h[3], 2), 16);
                    return new Color(r / 255f, g / 255f, b / 255f, 1);
                }
                if (h.Length >= 7)
                {
                    int n = Convert.ToInt32(h.Substring(1, 6), 16);
                    return new Color(((n >> 16) & 255) / 255f, ((n >> 8) & 255) / 255f, (n & 255) / 255f, 1);
                }
            }
            catch (FormatException) { }
            return Color.magenta;
        }

        public static string ToHex(Color c)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255), g = Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255), b = Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255);
            return "#" + r.ToString("x2") + g.ToString("x2") + b.ToString("x2");
        }

        public static Color A(Color c, float a) { c.a = a; return c; }
        public static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        public static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        public static readonly Color Floor = Hex("#1b201e");
        public static readonly Color FloorHi = Hex("#262d2a");
        public static readonly Color Chalk = Hex("#f1ece2");
        public static readonly Color Muted = Hex("#a3aba2");
        public static readonly Color Bronze = Hex("#d9963a");
        public static readonly Color BronzeHi = Hex("#f4c46e");
        public static readonly Color Ink = Hex("#17191b");
        public static readonly Color Cream = Hex("#fbf6ec");
        public static readonly Color SlotLine = new Color(241 / 255f, 236 / 255f, 226 / 255f, 0.2f);
    }

    // Градиент лица карты: вертикальный или радиальный из точки (50%, 62%)
    public class Grad
    {
        public Color[] c;
        public float[] t;
        public bool radial;

        public Grad(bool radial, params object[] stops)
        {
            this.radial = radial;
            int n = stops.Length / 2;
            c = new Color[n];
            t = new float[n];
            for (int i = 0; i < n; i++)
            {
                c[i] = Pal.Hex((string)stops[i * 2]);
                t[i] = Convert.ToSingle(stops[i * 2 + 1]);
            }
        }

        public static Grad Solid(Color col) => new Grad(false) { c = new[] { col }, t = new[] { 0f } };

        public Color Eval(float x, float y, float w, float h)
        {
            float k;
            if (radial)
            {
                float dx = x - 0.5f * w, dy = y - 0.62f * h;
                float far = Mathf.Sqrt(0.25f * w * w + 0.3844f * h * h);
                k = Mathf.Sqrt(dx * dx + dy * dy) / (0.62f * far);
            }
            else k = y / h;
            if (c.Length == 1 || k <= t[0]) return c[0];
            for (int i = 1; i < c.Length; i++)
                if (k <= t[i]) return Color.Lerp(c[i - 1], c[i], (k - t[i - 1]) / Mathf.Max(1e-4f, t[i] - t[i - 1]));
            return c[c.Length - 1];
        }
    }

    public class FaceSet
    {
        public Grad face, court, ace;
        public Color edge, plate;
        public bool dark;
    }

    // Что нужно дорисовать поверх текстуры карты текстом (через IMGUI)
    public class FaceInfo
    {
        public Color ink = Color.black;
        public Color plate = Color.white;
        public bool cornerPlate;
        public string caption = "";
        public string courtLetter;
        public float letterY;
    }

    public class TableArt
    {
        public Color baseColor;
        public Texture2D gradient;   // растягивается на весь экран
        public Texture2D tile;       // повторяется
        public float tilePx;         // размер плитки в логических пикселях
        public bool tileStretchY;    // плитка тянется по вертикали (доски)
        public ImgData image;        // картинка на весь экран (обрезка по размеру)
        public Texture2D imageTex;
    }

    public static class CardArt
    {
        public const int TW = 200, TH = 280;

        public static readonly string[][] SKINS =
        {
            new[] { "#efc39b", "#b98257" }, new[] { "#d99a62", "#9a6136" }, new[] { "#b97644", "#7a4520" },
            new[] { "#8a5430", "#55301a" }, new[] { "#e6ae7c", "#a8703f" }, new[] { "#c4834f", "#87512a" },
            new[] { "#f6d5bd", "#c99a7a" }, new[] { "#b58b5a", "#7d5a32" }, new[] { "#5a3520", "#2f1a0c" },
        };
        public static readonly string[] HAIRS = { "#24160d", "#5b3a1c", "#d6ac52", "#121212", "#8b3a19", "#e9e4da", "#c0392b", "#3b6fd8", "#e2557a" };
        public static readonly string[] TRUNKS = { "#d3203a", "#e2557a", "#1e2230", "#24402f" };
        public static readonly (string key, string label)[] EXTRAS =
        {
            ("mus", "Усы"), ("shades", "Очки"), ("beard", "Борода"), ("band", "Повязка"), ("crown", "Корона"),
            ("tiara", "Диадема"), ("belt", "Пояс"), ("cape", "Плащ"), ("gold", "Золотые искры"),
        };
        public static readonly string[] RANKS = { "", "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

        // ---------- выбор вида карты ----------

        public static Look BaseLook(int rank)
        {
            var L = new Look();
            switch (rank)
            {
                case 1: L.pose = "double"; L.hair = "pomp"; L.belt = true; L.gold = true; break;
                case 2: L.pose = "double"; L.hair = "buzz"; break;
                case 3: L.pose = "crab"; L.hair = "bald"; break;
                case 4: L.pose = "lift"; L.hair = "flat"; break;
                case 5: L.pose = "double"; L.hair = "mohawk"; break;
                case 6: L.pose = "crab"; L.hair = "pomp"; L.mus = true; break;
                case 7: L.pose = "double"; L.hair = "flat"; L.shades = true; break;
                case 8: L.pose = "lift"; L.hair = "bald"; L.mus = true; break;
                case 9: L.pose = "crab"; L.hair = "buzz"; L.shades = true; break;
                case 10: L.pose = "double"; L.hair = "mohawk"; L.mus = true; break;
                case 11: L.pose = "crab"; L.hair = "pomp"; L.band = true; L.mus = true; break;
                case 12: L.pose = "double"; L.hair = "long"; L.tiara = true; L.mus = true; break;
                case 13: L.pose = "double"; L.hair = "flat"; L.crown = true; L.beard = true; L.cape = true; L.belt = true; break;
            }
            return L;
        }

        public static Look DefaultLook(int id)
        {
            int suit = id / 13, rank = id % 13 + 1;
            var L = BaseLook(rank);
            L.skin = (rank * 5 + suit * 2) % 6;
            L.hairColor = HAIRS[(rank * 3 + suit) % 5];
            L.trunks = TRUNKS[suit];
            return L;
        }

        public static Look LookOf(Skin s, int id) => s.cards[id].hasLook ? s.cards[id].look : DefaultLook(id);

        public static ImgData ImgFor(Skin s, ImgLib lib, int id) => lib.Get(s.cards[id].img) ?? lib.Get(s.assign[id]);

        // Что реально нарисовано: "image" | "buff" | "classic"
        public static string Kind(Skin s, ImgLib lib, int id)
        {
            var ov = s.cards[id];
            string plain = s.art == "custom" ? s.fallback : s.art;
            string art = ov.art != "" ? ov.art : (s.art == "custom" ? "image" : s.art);
            if (art == "image") return ImgFor(s, lib, id) != null ? "image" : plain;
            return art;
        }

        public static FaceSet FaceSetFor(string key, string customHex)
        {
            switch (key)
            {
                case "white":
                    return new FaceSet { face = new Grad(false, "#ffffff", 0), court = new Grad(false, "#ffffff", 0, "#f1f1f1", 1), ace = new Grad(false, "#ffffff", 0), edge = Pal.Hex("#cfcfcf"), plate = new Color(1, 1, 1, 0.9f) };
                case "vintage":
                    return new FaceSet { face = new Grad(false, "#f3e6c8", 0, "#e8d4a8", 0.6f, "#dcc08c", 1), court = new Grad(false, "#f0dcae", 0, "#e2c68c", 1), ace = new Grad(true, "#f7e3b0", 0, "#e8d4a8", 1), edge = Pal.Hex("#b89a62"), plate = new Color(240 / 255f, 226 / 255f, 190 / 255f, 0.9f) };
                case "dark":
                    return new FaceSet { face = new Grad(false, "#2b3034", 0, "#1f2326", 1), court = new Grad(false, "#3a3326", 0, "#262119", 1), ace = new Grad(true, "#3d3626", 0, "#22262a", 1), edge = Pal.Hex("#41474d"), plate = new Color(31 / 255f, 35 / 255f, 38 / 255f, 0.88f), dark = true };
                case "color":
                {
                    var c = Pal.Hex(customHex);
                    return new FaceSet { face = Grad.Solid(c), court = Grad.Solid(c), ace = Grad.Solid(c), edge = Pal.Mul(c, 0.8f), plate = Pal.A(c, 0.88f), dark = Pal.Lum(c) < 0.45f };
                }
                default:
                    return new FaceSet { face = new Grad(false, "#fffdf8", 0, "#fbf6ec", 0.55f, "#efe4cd", 1), court = new Grad(false, "#fff8e6", 0, "#f7e7bf", 0.6f, "#ecd29a", 1), ace = new Grad(true, "#fff1c4", 0, "#fbf6ec", 1), edge = Pal.Hex("#d8cbb2"), plate = new Color(251 / 255f, 246 / 255f, 236 / 255f, 0.88f) };
            }
        }

        public static Color SuitColor(Skin s, int suit, bool dark)
        {
            bool four = s.suitColors == "four";
            var red = Pal.Hex(s.red);
            var black = Pal.Hex(s.black);
            if (dark && Pal.Lum(black) < 0.45f) black = Pal.Hex("#eceae2");
            switch (suit)
            {
                case 0: return red;
                case 1: return four ? Pal.Hex(dark ? "#5b8ff0" : "#1f5fd1") : red;
                case 2: return black;
                default: return four ? Pal.Hex(dark ? "#3fbf6a" : "#16823a") : black;
            }
        }

        // ---------- лицо карты ----------

        public static Texture2D RenderFace(Skin s, ImgLib lib, int id, FaceInfo info)
        {
            int suit = id / 13, rank = id % 13 + 1;
            var ov = s.cards[id];
            var fs = ov.face != "" ? FaceSetFor(ov.face, ov.faceColor) : FaceSetFor(s.face, null);
            var ink = ov.hasColor ? Pal.Hex(ov.color) : SuitColor(s, suit, fs.dark);

            var cv = new Canvas(TW, TH);
            cv.Clear(Pal.A(fs.edge, 0));
            float r = s.radius / 100f * TW;
            var g = rank > 10 ? fs.court : rank == 1 ? fs.ace : fs.face;
            cv.FillRoundRect(0, 0, TW, TH, r, Color.white, (x, y) => g.Eval(x, y, TW, TH));
            if (rank > 10 && s.courtFrame)
            {
                float bw = 0.035f * TW;
                cv.StrokeRoundRect(bw / 2, bw / 2, TW - bw, TH - bw, Mathf.Max(0, r - bw / 2), bw, Pal.Hex("#e0b23b"));
            }

            info.ink = ink;
            info.plate = fs.plate;
            info.cornerPlate = false;
            info.courtLetter = null;
            info.caption = ov.caption ?? "";

            string kind = Kind(s, lib, id);
            if (kind == "image")
            {
                string area = ov.area != "" ? ov.area : s.area;
                string fit = ov.fit != "" ? ov.fit : s.fit;
                float px = ov.posX >= 0 ? ov.posX : 50;
                float py = ov.posY >= 0 ? ov.posY : (s.focus == "top" ? 0 : s.focus == "bottom" ? 100 : 50);
                Rect box = area == "full" ? new Rect(0, 0, TW, TH) : new Rect(0.05f * TW, 0.22f * TH, 0.9f * TW, 0.74f * TH);
                DrawImage(cv, ImgFor(s, lib, id), box, area == "full" ? r : 0.04f * TW, fit, px, py, Mathf.Max(100, ov.zoom));
                info.cornerPlate = area == "full";
            }
            else if (kind == "classic") Classic(cv, suit, rank, ink, info);
            else
            {
                float ax = 0.03f * TW, ay = 0.21f * TH, aw = 0.94f * TW, ah = 0.77f * TH;
                float k = Mathf.Min(aw / 100f, ah / 122f);
                float ox = ax + (aw - 100 * k) / 2, oy = ay + ah - 122 * k;
                cv.Push(Affine.Translate(ox, oy + 8 * k) * Affine.Scale(k, k));
                new Fig(cv).Figure(suit, LookOf(s, id), ink);
                cv.Pop();
            }
            cv.StrokeRoundRect(1, 1, TW - 2, TH - 2, Mathf.Max(0, r - 1), 2, fs.edge);
            return cv.ToTexture();
        }

        static readonly float[][][] PIPS =
        {
            null, null,
            new[] { new[] { 50f, 10 }, new[] { 50f, 90 } },
            new[] { new[] { 50f, 10 }, new[] { 50f, 50 }, new[] { 50f, 90 } },
            new[] { new[] { 25f, 10 }, new[] { 75f, 10 }, new[] { 25f, 90 }, new[] { 75f, 90 } },
            new[] { new[] { 25f, 10 }, new[] { 75f, 10 }, new[] { 50f, 50 }, new[] { 25f, 90 }, new[] { 75f, 90 } },
            new[] { new[] { 25f, 10 }, new[] { 75f, 10 }, new[] { 25f, 50 }, new[] { 75f, 50 }, new[] { 25f, 90 }, new[] { 75f, 90 } },
            new[] { new[] { 25f, 10 }, new[] { 75f, 10 }, new[] { 50f, 30 }, new[] { 25f, 50 }, new[] { 75f, 50 }, new[] { 25f, 90 }, new[] { 75f, 90 } },
            new[] { new[] { 25f, 10 }, new[] { 75f, 10 }, new[] { 50f, 30 }, new[] { 25f, 50 }, new[] { 75f, 50 }, new[] { 50f, 70 }, new[] { 25f, 90 }, new[] { 75f, 90 } },
            new[] { new[] { 25f, 10 }, new[] { 75f, 10 }, new[] { 25f, 36.7f }, new[] { 75f, 36.7f }, new[] { 50f, 50 }, new[] { 25f, 63.3f }, new[] { 75f, 63.3f }, new[] { 25f, 90 }, new[] { 75f, 90 } },
            new[] { new[] { 25f, 10 }, new[] { 75f, 10 }, new[] { 50f, 23 }, new[] { 25f, 36.7f }, new[] { 75f, 36.7f }, new[] { 25f, 63.3f }, new[] { 75f, 63.3f }, new[] { 50f, 77 }, new[] { 25f, 90 }, new[] { 75f, 90 } },
        };

        static void Classic(Canvas cv, int suit, int rank, Color ink, FaceInfo info)
        {
            if (rank == 1)
            {
                float s = 0.5f * TW;
                SuitShape(cv, suit, TW / 2f - s / 2, 0.58f * TH - s / 2, s, s, ink);
                return;
            }
            if (rank > 10)
            {
                float bx = 0.16f * TW, by = 0.27f * TH, bw = TW - 2 * bx, bh = TH - by - 0.08f * TH;
                cv.StrokeRoundRect(bx, by, bw, bh, 0.05f * TW, 0.015f * TW, ink);
                float total = 0.52f * TW, top = by + bh / 2 - total / 2;
                info.courtLetter = RANKS[rank];
                info.letterY = (top + 0.15f * TW) / TH;
                float ss = 0.18f * TW;
                SuitShape(cv, suit, TW / 2f - ss / 2, top + 0.34f * TW, ss, ss, ink);
                return;
            }
            float ax = 0.14f * TW, ay = 0.26f * TH, aw = TW - 2 * ax, ah = TH - ay - 0.07f * TH, ps = 0.16f * TW;
            foreach (var p in PIPS[rank])
            {
                float cx = ax + aw * p[0] / 100, cy = ay + ah * p[1] / 100;
                bool flip = p[1] > 50;
                if (flip) cv.Push(Affine.Rotate(180, cx, cy));
                SuitShape(cv, suit, cx - ps / 2, cy - ps / 2, ps, ps, ink);
                if (flip) cv.Pop();
            }
        }

        // ---------- масти ----------

        const string HeartPath = "M50 90 C22 68 4 50 4 30 C4 15 16 5 30 5 C40 5 47 11 50 19 C53 11 60 5 70 5 C84 5 96 15 96 30 C96 50 78 68 50 90 Z";
        const string DiamondPath = "M50 3 L86 50 L50 97 L14 50 Z";
        const string SpadePath = "M50 4 C36 24 8 40 8 60 C8 74 19 82 31 82 C39 82 45 78 47 73 C46 82 42 90 34 96 L66 96 C58 90 54 82 53 73 C55 78 61 82 69 82 C81 82 92 74 92 60 C92 40 64 24 50 4 Z";
        const string ClubStem = "M46 52 L54 52 C55 76 59 88 67 96 L33 96 C41 88 45 76 46 52 Z";

        public static void SuitShape(Canvas cv, int suit, float x, float y, float w, float h, Color col)
        {
            cv.Push(Affine.Translate(x, y) * Affine.Scale(w / 100f, h / 100f));
            switch (suit)
            {
                case 0: cv.FillPath(HeartPath, col); break;
                case 1: cv.FillPath(DiamondPath, col); break;
                case 2: cv.FillPath(SpadePath, col); break;
                default:
                    cv.FillUnion(new List<Vector2[]>
                    {
                        Canvas.EllipsePoly(50, 27, 19, 19, 0),
                        Canvas.EllipsePoly(27, 57, 19, 19, 0),
                        Canvas.EllipsePoly(73, 57, 19, 19, 0),
                        Canvas.EllipsePoly(50, 52, 12, 12, 0),
                        SvgPath.Parse(ClubStem)[0].pts.ToArray(),
                    }, col);
                    break;
            }
            cv.Pop();
        }

        // ---------- картинки ----------

        public static void DrawImage(Canvas cv, ImgData im, Rect box, float rad, string fit, float px, float py, float zoom)
        {
            if (im == null) return;
            float iw = im.W, ih = im.H;
            float s = fit == "contain" ? Mathf.Min(box.width / iw, box.height / ih) : Mathf.Max(box.width / iw, box.height / ih);
            float w0 = iw * s, h0 = ih * s;
            float l0 = box.x + (box.width - w0) * px / 100, t0 = box.y + (box.height - h0) * py / 100;
            float ox = box.x + box.width * px / 100, oy = box.y + box.height * py / 100, z = zoom / 100;
            float left = ox + (l0 - ox) * z, top = oy + (t0 - oy) * z, w = w0 * z, h = h0 * z;
            int lv = im.LevelFor(w / iw);
            float lw = im.sizes[lv].x, lh = im.sizes[lv].y;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(box.x)), x1 = Mathf.Min(cv.W - 1, Mathf.CeilToInt(box.xMax));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(box.y)), y1 = Mathf.Min(cv.H - 1, Mathf.CeilToInt(box.yMax));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float m = Canvas.RoundRectCoverage(x + 0.5f, y + 0.5f, box, rad);
                    if (m <= 0) continue;
                    float u = (x + 0.5f - left) / w * lw - 0.5f, v = (y + 0.5f - top) / h * lh - 0.5f;
                    if (u < -0.5f || v < -0.5f || u > lw - 0.5f || v > lh - 0.5f) continue;
                    var c = im.Sample(lv, u, v);
                    cv.Over(y * cv.W + x, c, m * c.a);
                }
        }

        // ---------- рубашка ----------

        public static Texture2D RenderBack(Skin s, ImgLib lib)
        {
            var cv = new Canvas(TW, TH);
            cv.Clear(Pal.A(Pal.Cream, 0));
            float r = s.radius / 100f * TW, b = 0.045f * TW;
            cv.FillRoundRect(0, 0, TW, TH, r, Pal.Cream);
            var inner = new Rect(b, b, TW - 2 * b, TH - 2 * b);
            float ir = Mathf.Max(0, r - b);
            var bg = Pal.Hex(s.backColor);
            var img = s.back == "image" ? lib.Get(s.backImage) : null;
            if (img != null)
            {
                cv.FillRoundRect(inner.x, inner.y, inner.width, inner.height, ir, bg);
                DrawImage(cv, img, inner, ir, "cover", 50, 50, 100);
            }
            else
            {
                var tile = s.back == "image" ? null : RenderTile(s.back, Pal.Hex(s.backInk), Mathf.Max(8, Mathf.RoundToInt(0.34f * TW)));
                if (tile == null) cv.FillRoundRect(inner.x, inner.y, inner.width, inner.height, ir, bg);
                else
                {
                    int ts = tile.W;
                    float cx = inner.center.x - ts / 2f, cy = inner.center.y - ts / 2f;
                    cv.FillRoundRect(inner.x, inner.y, inner.width, inner.height, ir, bg, (x, y) =>
                    {
                        var t = SampleWrap(tile, x - cx, y - cy);
                        return Color.Lerp(bg, new Color(t.r, t.g, t.b, 1), t.a);
                    });
                }
            }
            cv.StrokeRoundRect(inner.x + 0.7f, inner.y + 0.7f, inner.width - 1.4f, inner.height - 1.4f, ir, 1.4f, new Color(244 / 255f, 196 / 255f, 110 / 255f, 0.55f));
            return cv.ToTexture();
        }

        static Canvas RenderTile(string kind, Color ink, int ts)
        {
            var cv = new Canvas(ts, ts);
            cv.Clear(Pal.A(ink, 0));
            switch (kind)
            {
                case "dumbbell":
                {
                    cv.M = Affine.Scale(ts / 24f, ts / 24f);
                    cv.Push(Affine.Rotate(-35, 12, 12));
                    var c = Pal.A(ink, 0.5f);
                    cv.FillUnion(new List<Vector2[]>
                    {
                        Canvas.RoundRectPoly(4.5f, 8, 3, 8, 1), Canvas.RoundRectPoly(16.5f, 8, 3, 8, 1),
                        Canvas.RoundRectPoly(2.5f, 9.5f, 2, 5, 0.8f), Canvas.RoundRectPoly(19.5f, 9.5f, 2, 5, 0.8f),
                        Canvas.RoundRectPoly(7.5f, 11, 9, 2, 0),
                    }, c);
                    cv.Pop();
                    return cv;
                }
                case "stripes":
                    cv.M = Affine.Scale(ts / 12f, ts / 12f);
                    cv.StrokePath("M-3 3L3-3M0 12L12 0M9 15L15 9", 2.4f, Pal.A(ink, 0.5f));
                    return cv;
                case "dots":
                    cv.M = Affine.Scale(ts / 12f, ts / 12f);
                    cv.FillUnion(new List<Vector2[]> { Canvas.EllipsePoly(3, 3, 1.7f, 1.7f, 0), Canvas.EllipsePoly(9, 9, 1.7f, 1.7f, 0) }, Pal.A(ink, 0.55f));
                    return cv;
                case "checks":
                    cv.M = Affine.Scale(ts / 12f, ts / 12f);
                    cv.FillPath("M0 0h6v6H0zM6 6h6v6H6z", Pal.A(ink, 0.4f));
                    return cv;
            }
            return null;
        }

        static Color SampleWrap(Canvas t, float x, float y)
        {
            int w = t.W, h = t.H;
            x -= 0.5f;
            y -= 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            int Wr(int v, int n) => ((v % n) + n) % n;
            Color P(int xx, int yy) => t.Px[Wr(yy, h) * w + Wr(xx, w)];
            return Color.Lerp(Color.Lerp(P(x0, y0), P(x0 + 1, y0), fx), Color.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx), fy);
        }

        // ---------- стол ----------

        public static TableArt RenderTable(Skin s, ImgLib lib)
        {
            var t = new TableArt { baseColor = Pal.Floor };
            switch (s.table)
            {
                case "felt":
                case "blue":
                {
                    bool felt = s.table == "felt";
                    var inner = Pal.Hex(felt ? "#2f7a4b" : "#2a5f8f");
                    var outer = Pal.Hex(felt ? "#15422a" : "#102c47");
                    t.baseColor = outer;
                    var cv = new Canvas(96, 96);
                    float far = Mathf.Sqrt(0.25f + 0.85f * 0.85f) * 0.7f;
                    for (int y = 0; y < 96; y++)
                        for (int x = 0; x < 96; x++)
                        {
                            float dx = (x + 0.5f) / 96 - 0.5f, dy = (y + 0.5f) / 96 - 0.15f;
                            cv.Px[y * 96 + x] = Color.Lerp(inner, outer, Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / far));
                        }
                    t.gradient = cv.ToTexture(false);
                    t.gradient.filterMode = FilterMode.Bilinear;
                    t.tile = Speckle(5, new[] { (0, 0, new Color(1, 1, 1, 0.035f)) });
                    t.tilePx = 5;
                    break;
                }
                case "wood":
                {
                    t.baseColor = Pal.Hex("#5a3a22");
                    var cv = new Canvas(180, 4);
                    var p1 = Pal.Hex("#5a3a22");
                    var p2 = Pal.Hex("#654229");
                    var p3 = Pal.Hex("#563720");
                    for (int x = 0; x < 180; x++)
                    {
                        var c = x < 70 ? p1 : x < 130 ? p2 : p3;
                        if (x % 41 < 2) c = Color.Lerp(c, Color.black, 0.14f);
                        for (int y = 0; y < 4; y++) cv.Px[y * 180 + x] = c;
                    }
                    t.tile = cv.ToTexture(false);
                    t.tile.wrapMode = TextureWrapMode.Repeat;
                    t.tile.filterMode = FilterMode.Bilinear;
                    t.tilePx = 180;
                    t.tileStretchY = true;
                    break;
                }
                case "dark":
                    t.baseColor = Pal.Hex("#111316");
                    break;
                case "color":
                    t.baseColor = Pal.Hex(s.tableColor);
                    break;
                case "image":
                    t.baseColor = Pal.Hex("#111316");
                    t.image = lib.Get(s.tableImage);
                    if (t.image != null) t.imageTex = ImageTexture(t.image);
                    break;
                default:
                    t.baseColor = Pal.Floor;
                    t.tile = Speckle(77, new[] { (7, 0, new Color(241 / 255f, 236 / 255f, 226 / 255f, 0.035f)), (11, 1, new Color(0, 0, 0, 0.28f)) });
                    t.tilePx = 77;
                    break;
            }
            return t;
        }

        // Точечная крошка: (шаг сетки, сдвиг-вариант, цвет)
        static Texture2D Speckle(int size, (int step, int variant, Color col)[] layers)
        {
            var cv = new Canvas(size, size);
            cv.Clear(new Color(0, 0, 0, 0));
            foreach (var (step, variant, col) in layers)
            {
                int ox = variant == 1 ? 3 : 0, oy = variant == 1 ? 5 : 0;
                for (int y = oy; y < size; y += step)
                    for (int x = ox; x < size; x += step)
                        cv.Over(y * size + x, col, col.a);
            }
            var tex = cv.ToTexture(false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            return tex;
        }

        public static Texture2D ImageTexture(ImgData im)
        {
            int w = im.W, h = im.H;
            var px = im.levels[0];
            var data = new Color32[w * h];
            for (int y = 0; y < h; y++) Array.Copy(px, y * w, data, (h - 1 - y) * w, w);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            tex.SetPixels32(data);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.Apply(true, true);
            return tex;
        }

        // ---------- служебные текстуры ----------

        public static Texture2D RenderSlot(float radiusPct)
        {
            var cv = new Canvas(TW, TH);
            cv.Clear(new Color(1, 1, 1, 0));
            float r = radiusPct / 100f * TW;
            cv.FillRoundRect(0, 0, TW, TH, r, new Color(241 / 255f, 236 / 255f, 226 / 255f, 0.05f));
            cv.StrokeRoundRect(2, 2, TW - 4, TH - 4, Mathf.Max(0, r - 2), 3, Pal.SlotLine);
            return cv.ToTexture();
        }

        // Мягкая тень: текстура больше карты на 10% ширины с каждой стороны
        public static Texture2D RenderShadow(float radiusPct)
        {
            const int w = 120, h = 160, m = 10;
            var cv = new Canvas(w, h);
            cv.Clear(new Color(0, 0, 0, 0));
            float r = radiusPct / 100f * 100;
            for (int k = 0; k < 6; k++)
            {
                float e = m - k * 1.8f;
                cv.FillRoundRect(m - e, m - e, 100 + 2 * e, 140 + 2 * e, r + e, new Color(0, 0, 0, 0.07f));
            }
            return cv.ToTexture();
        }

        public static Texture2D RenderSuitIcon(int suit)
        {
            var cv = new Canvas(64, 64);
            cv.Clear(new Color(1, 1, 1, 0));
            SuitShape(cv, suit, 2, 2, 60, 60, Color.white);
            return cv.ToTexture();
        }

        // Кнопочные подложки для интерфейса (9-slice)
        public static Texture2D RenderRound(Color fill, Color border, int size = 32, float r = 7, float bw = 1.5f)
        {
            var cv = new Canvas(size, size);
            cv.Clear(Pal.A(fill, 0));
            cv.FillRoundRect(0, 0, size, size, r, fill);
            if (border.a > 0) cv.StrokeRoundRect(bw / 2, bw / 2, size - bw, size - bw, Mathf.Max(0, r - bw / 2), bw, border);
            var tex = cv.ToTexture(false);
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        // Бицепс для победного дождя
        public static Texture2D RenderFlex()
        {
            var cv = new Canvas(96, 96);
            cv.Clear(new Color(0.85f, 0.6f, 0.38f, 0));
            cv.M = Affine.Scale(96 / 46f, 96 / 46f) * Affine.Translate(0, -9);
            new Fig(cv).Arm("double", Pal.Hex(SKINS[1][0]), Pal.Hex(SKINS[1][1]));
            return cv.ToTexture();
        }
    }

    // Порт рисунка качка из HTML-версии (координаты SVG viewBox 0 -8 100 122)
    public class Fig
    {
        readonly Canvas c;
        static readonly Affine Mirror = Affine.Translate(100, 0) * Affine.Scale(-1, 1);
        static readonly Color White = Color.white;

        public Fig(Canvas canvas) { c = canvas; }

        static Color H(string hex) => Pal.Hex(hex);

        void E(float cx, float cy, float rx, float ry, Color? f, Color? s, float sw = 1.2f, float rot = 0)
        {
            if (f.HasValue) c.FillEllipse(cx, cy, rx, ry, rot, f.Value);
            if (s.HasValue) c.StrokeEllipse(cx, cy, rx, ry, rot, sw, s.Value);
        }

        void P(string d, Color? f, Color? s, float sw = 1.2f)
        {
            if (f.HasValue) c.FillPath(d, f.Value);
            if (s.HasValue) c.StrokePath(d, sw, s.Value);
        }

        void Rr(float x, float y, float w, float h, float r, Color f, Color? s = null, float sw = 0.9f)
        {
            c.FillRoundRect(x, y, w, h, r, f);
            if (s.HasValue) c.StrokeRoundRect(x, y, w, h, r, sw, s.Value);
        }

        void Limb(string d, float w, Color S, Color D)
        {
            c.StrokePath(d, w + 2.4f, D);
            c.StrokePath(d, w, S);
        }

        void Shine(float cx, float cy, float rx, float ry, float rot = 0) => c.FillEllipse(cx, cy, rx, ry, rot, new Color(1, 1, 1, 0.82f));

        void Star(float x, float y, float r, Color col)
        {
            string X = Fmt.F(x), Y = Fmt.F(y);
            string d = "M" + X + " " + Fmt.F(y - r) + "Q" + X + " " + Y + " " + Fmt.F(x + r) + " " + Y + "Q" + X + " " + Y + " " + X + " " + Fmt.F(y + r)
                       + "Q" + X + " " + Y + " " + Fmt.F(x - r) + " " + Y + "Q" + X + " " + Y + " " + X + " " + Fmt.F(y - r) + "Z";
            c.FillPath(d, col);
        }

        void Both(Action a)
        {
            a();
            c.Push(Mirror);
            a();
            c.Pop();
        }

        public void Arm(string pose, Color S, Color D)
        {
            if (pose == "double")
            {
                Limb("M12 47 L15.5 21", 9, S, D); E(15.5f, 17.5f, 5.6f, 5, S, D); Limb("M31 46 L13 46", 11, S, D);
                E(21.5f, 40, 7.8f, 6.6f, S, D); E(31, 44.5f, 8, 7.5f, S, D);
                Shine(19.5f, 37.5f, 3.4f, 1.3f, -18); Shine(29, 41, 2.4f, 1.1f, -35); Shine(14, 31, 0.9f, 3);
            }
            else if (pose == "crab")
            {
                Limb("M27 48 L22 64", 11, S, D); E(29, 45, 8.6f, 8, S, D); Limb("M22 65 L42 72", 9, S, D);
                E(44.5f, 71, 5, 5, S, D); Shine(27, 41.5f, 2.6f, 1.2f, -35); Shine(31, 69.5f, 3.4f, 1, 15); Shine(21, 56, 1, 3);
            }
            else
            {
                Limb("M29 42 L18 24", 10.5f, S, D); Limb("M17.5 24 L18 3", 8.5f, S, D); E(18, 1, 5, 4.6f, S, D);
                E(23.5f, 32.5f, 6.2f, 4.2f, S, D, 1.2f, 58); E(30, 44, 8, 7.5f, S, D);
                Shine(21.5f, 30, 2.8f, 1, 58); Shine(28, 40.5f, 2.4f, 1.1f, -35);
            }
        }

        static string HairPath(string hair)
        {
            switch (hair)
            {
                case "flat": return "M41.4 19 L41.4 10 Q50 8 58.6 10 L58.6 19 Q57 14 50 14 Q43 14 41.4 19 Z";
                case "pomp": return "M41.4 19 Q39 9 48 6.5 Q60 5 59 13 Q59 16 58.6 19 Q56 13 50 13.5 Q44 13 41.4 19 Z";
                case "buzz":
                case "long": return "M41.6 18 Q42 11 50 10.6 Q58 11 58.4 18 Q55 14 50 14 Q45 14 41.6 18 Z";
                case "mohawk": return "M47.6 14 L47 3 Q50 1 53 3 L52.4 14 Z";
            }
            return null;
        }

        void Head(Look L, Color S, Color D, Color Hc, Color accent)
        {
            Both(() => E(41.6f, 23, 1.9f, 2.7f, S, D, 1));
            P("M41.5 21 C41.5 13 45 11 50 11 C55 11 58.5 13 58.5 21 L58 26 C57 31 54 33 50 33 C46 33 43 31 42 26 Z", S, D);
            string hair = HairPath(L.hair);
            if (hair != null) P(hair, Hc, null);
            else Shine(46.5f, 14.2f, 2.6f, 1, -15);
            if (L.beard) P("M41.8 23 C42 31 45 34.5 50 34.5 C55 34.5 58 31 58.2 23 C56 29 54 30.5 50 30.5 C46 30.5 44 29 41.8 23 Z", Hc, null);
            if (!L.shades) Both(() => E(46.4f, 21.6f, 1.05f, 1.15f, H("#1a1a1a"), null));
            Both(() => P("M43.8 18.7 L48.2 19.9", null, Hc, 1.5f));
            P("M50 21.8 L48.9 25 L51 25.2", null, D, 0.8f);
            P("M45.5 26.6 Q50 30.8 54.5 26.6 Q50 27.8 45.5 26.6 Z", White, H("#5a2a1a"), 0.7f);
            if (L.mus) P("M45.2 25.6 Q47.5 23.6 50 24.8 Q52.5 23.6 54.8 25.6 Q52 26.2 50 25.6 Q48 26.2 45.2 25.6 Z", Hc, null);
            if (L.shades)
            {
                P("M43.6 20 L48.6 20 L48.3 22.6 Q46 24 43.9 22.6 Z M51.4 20 L56.4 20 L56.1 22.6 Q54 24 51.7 22.6 Z", H("#111"), H("#111"), 0.6f);
                P("M48.6 20.5 L51.4 20.5", null, H("#111"), 0.8f);
                Shine(45.3f, 21, 1.1f, 0.45f, -20);
            }
            if (L.band) P("M41.5 15.6 Q50 13 58.5 15.6 L58.5 18.2 Q50 15.6 41.5 18.2 Z", accent, null);
            if (L.crown)
            {
                P("M42 12 L41.5 4 L46 8 L50 1.5 L54 8 L58.5 4 L58 12 Z", H("#f2c14e"), H("#9a7414"), 0.9f);
                E(50, 9, 1.3f, 1.3f, H("#d3203a"), null); E(45.2f, 10, 0.9f, 0.9f, H("#2b5fd9"), null); E(54.8f, 10, 0.9f, 0.9f, H("#2b5fd9"), null);
            }
            if (L.tiara)
            {
                P("M43 12.5 Q50 6.5 57 12.5 Q50 10 43 12.5 Z", H("#f2c14e"), H("#9a7414"), 0.7f);
                E(50, 8.8f, 1.4f, 1.4f, H("#e2557a"), H("#9a7414"), 0.5f);
            }
            Shine(55.6f, 25.2f, 0.8f, 1.6f, -10);
        }

        public void Figure(int suit, Look L, Color ink)
        {
            var sk = CardArt.SKINS[Mathf.Clamp(L.skin, 0, CardArt.SKINS.Length - 1)];
            Color S = H(sk[0]), D = H(sk[1]), Hc = H(L.hairColor), trunks = H(L.trunks);
            bool red = suit < 2;
            Color accent = H(red ? "#d3203a" : "#2b5fd9");

            CardArt.SuitShape(c, suit, 16, 24, 68, 70, Pal.A(ink, 0.08f));
            if (L.gold)
            {
                var g = H("#e2b43c");
                Star(10, 30, 5, g); Star(89, 22, 4, g); Star(91, 82, 3.2f, g); Star(8, 88, 3, g);
            }
            else
            {
                var b = H("#d9963a");
                Star(9, 76, 2.6f, b); Star(91, 66, 2.2f, b);
            }
            if (L.cape) P("M30 42 C22 64 16 88 12 113 L88 113 C84 88 78 64 70 42 Z", H(red ? "#8e1b2b" : "#3b2a63"), H(red ? "#5c0f1b" : "#241845"));
            if (L.pose == "lift")
            {
                c.StrokePath("M2 1 L98 1", 2.4f, H("#5f656b"));
                Both(() => { Rr(2, -7, 5, 16, 1, H("#26292c")); Rr(7.5f, -4, 3.5f, 10, 0.8f, H("#3b4045")); });
            }
            if (L.hair == "long") P("M41 16 Q41 7 50 7 Q59 7 59 16 L62 38 Q57 36 56.5 28 L43.5 28 Q43 36 38 38 Z", Hc, null);
            Both(() =>
            {
                P("M38 88 C31 96 32 106 35 113 L49.6 113 C50 104 50 96 50 89 Z", S, D);
                P("M42 95 C44 101 44 106 42 111", null, D, 0.9f);
                Shine(38.5f, 100, 1.1f, 4.2f, 8);
            });
            P("M29 41 C38 36 62 36 71 41 C78 49 75 58 70 63 C65 70 62 77 61 86 L39 86 C38 77 35 70 30 63 C25 58 22 49 29 41 Z", S, D);
            P("M44 36 L44 28 L56 28 L56 36 Z", S, null);
            P("M35 42 Q43 32.5 50 34 Q57 32.5 65 42 Q50 38 35 42 Z", S, D);
            Both(() =>
            {
                P("M50 44 C44 41 37 42 34 46 C33 53 37 57 42 57 C46 57 49 55 50 53 Z", S, D);
                Shine(40, 47.5f, 3.6f, 1.2f, -10);
            });
            foreach (float y in new[] { 60f, 66.5f, 73f })
            {
                Rr(44.6f, y, 5, 5.6f, 2, S, D);
                Rr(50.4f, y, 5, 5.6f, 2, S, D);
            }
            Shine(46.4f, 61.6f, 1.1f, 0.6f); Shine(52.2f, 68.1f, 1.1f, 0.6f); Shine(46.4f, 74.6f, 1.1f, 0.6f);
            Both(() => P("M33.5 60 l3.2 1.2 M34.5 64.5 l3 1.2", null, D, 0.9f));
            P("M38.5 84 L61.5 84 L63 93 Q56 94 50 90 Q44 94 37 93 Z", trunks, new Color(0, 0, 0, 0.35f), 0.8f);
            if (L.belt)
            {
                Rr(38.6f, 79.5f, 22.8f, 6, 1.5f, H("#e0b23b"), H("#8a6a12"), 0.8f);
                Rr(45, 78, 10, 9, 2, H("#f7dc84"), H("#8a6a12"), 0.8f);
                Star(50, 82.5f, 2.6f, H("#b8860b"));
            }
            Both(() => Arm(L.pose, S, D));
            Head(L, S, D, Hc, accent);
            Star(64, 50, 2.6f, White); Star(35, 70, 1.9f, White); Star(57, 104, 2, White);
        }
    }
}
