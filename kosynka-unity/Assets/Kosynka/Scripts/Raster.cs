using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Kosynka
{
    // Аффинное преобразование: x' = a*x + c*y + e, y' = b*x + d*y + f (как matrix(a b c d e f) в SVG)
    public struct Affine
    {
        public float a, b, c, d, e, f;

        public static Affine Identity => new Affine { a = 1, d = 1 };
        public static Affine Translate(float x, float y) => new Affine { a = 1, d = 1, e = x, f = y };
        public static Affine Scale(float sx, float sy) => new Affine { a = sx, d = sy };

        public static Affine Rotate(float deg, float cx, float cy)
        {
            float r = deg * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            var rot = new Affine { a = cs, b = sn, c = -sn, d = cs };
            return Translate(cx, cy) * rot * Translate(-cx, -cy);
        }

        // (m * n)(p) = m(n(p))
        public static Affine operator *(Affine m, Affine n)
        {
            return new Affine
            {
                a = m.a * n.a + m.c * n.b,
                b = m.b * n.a + m.d * n.b,
                c = m.a * n.c + m.c * n.d,
                d = m.b * n.c + m.d * n.d,
                e = m.a * n.e + m.c * n.f + m.e,
                f = m.b * n.e + m.d * n.f + m.f,
            };
        }

        public Vector2 Apply(Vector2 p) => new Vector2(a * p.x + c * p.y + e, b * p.x + d * p.y + f);
        public float MeanScale => Mathf.Sqrt(Mathf.Abs(a * d - b * c));
    }

    // Разбор подмножества SVG path: M L H V C Q Z (и строчные варианты)
    public static class SvgPath
    {
        public class Sub
        {
            public readonly List<Vector2> pts = new List<Vector2>();
            public bool closed;
        }

        static readonly Dictionary<string, List<Sub>> cache = new Dictionary<string, List<Sub>>();

        public static List<Sub> Parse(string d)
        {
            if (cache.TryGetValue(d, out var hit)) return hit;
            var subs = new List<Sub>();
            Sub sub = null;
            Vector2 cur = Vector2.zero, start = Vector2.zero;
            char cmd = ' ';
            int i = 0, n = d.Length;

            void Ensure()
            {
                if (sub != null) return;
                sub = new Sub();
                sub.pts.Add(cur);
                subs.Add(sub);
            }

            while (true)
            {
                while (i < n && (char.IsWhiteSpace(d[i]) || d[i] == ',')) i++;
                if (i >= n) break;
                char ch = d[i];
                if (char.IsLetter(ch) && ch != 'e' && ch != 'E')
                {
                    cmd = ch;
                    i++;
                    if (cmd == 'Z' || cmd == 'z')
                    {
                        if (sub != null) sub.closed = true;
                        cur = start;
                        sub = null;
                    }
                    continue;
                }
                bool rel = char.IsLower(cmd);
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                    {
                        var p = new Vector2(Num(d, ref i), Num(d, ref i));
                        if (rel) p += cur;
                        sub = new Sub();
                        sub.pts.Add(p);
                        subs.Add(sub);
                        cur = start = p;
                        cmd = rel ? 'l' : 'L';
                        break;
                    }
                    case 'L':
                    {
                        var p = new Vector2(Num(d, ref i), Num(d, ref i));
                        if (rel) p += cur;
                        Ensure();
                        sub.pts.Add(p);
                        cur = p;
                        break;
                    }
                    case 'H':
                    {
                        float x = Num(d, ref i);
                        var p = new Vector2(rel ? cur.x + x : x, cur.y);
                        Ensure();
                        sub.pts.Add(p);
                        cur = p;
                        break;
                    }
                    case 'V':
                    {
                        float y = Num(d, ref i);
                        var p = new Vector2(cur.x, rel ? cur.y + y : y);
                        Ensure();
                        sub.pts.Add(p);
                        cur = p;
                        break;
                    }
                    case 'C':
                    {
                        var c1 = new Vector2(Num(d, ref i), Num(d, ref i));
                        var c2 = new Vector2(Num(d, ref i), Num(d, ref i));
                        var p = new Vector2(Num(d, ref i), Num(d, ref i));
                        if (rel) { c1 += cur; c2 += cur; p += cur; }
                        Ensure();
                        const int steps = 10;
                        for (int k = 1; k <= steps; k++)
                        {
                            float t = k / (float)steps, u = 1 - t;
                            sub.pts.Add(u * u * u * cur + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * p);
                        }
                        cur = p;
                        break;
                    }
                    case 'Q':
                    {
                        var c1 = new Vector2(Num(d, ref i), Num(d, ref i));
                        var p = new Vector2(Num(d, ref i), Num(d, ref i));
                        if (rel) { c1 += cur; p += cur; }
                        Ensure();
                        const int steps = 8;
                        for (int k = 1; k <= steps; k++)
                        {
                            float t = k / (float)steps, u = 1 - t;
                            sub.pts.Add(u * u * cur + 2 * u * t * c1 + t * t * p);
                        }
                        cur = p;
                        break;
                    }
                    default:
                        i = n; // неизвестная команда — дальше не разбираем
                        break;
                }
            }
            cache[d] = subs;
            return subs;
        }

        static float Num(string s, ref int i)
        {
            int n = s.Length;
            while (i < n && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
            int st = i;
            if (i < n && (s[i] == '-' || s[i] == '+')) i++;
            bool dot = false;
            while (i < n && (char.IsDigit(s[i]) || (s[i] == '.' && !dot)))
            {
                if (s[i] == '.') dot = true;
                i++;
            }
            if (i < n && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < n && (s[i] == '-' || s[i] == '+')) i++;
                while (i < n && char.IsDigit(s[i])) i++;
            }
            if (i == st) { i++; return 0; }
            float.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
            return v;
        }
    }

    public delegate Color PixelShader(float x, float y);

    // Программный растеризатор с антиалиасингом: заливка по правилу nonzero, 4 подстроки на пиксель
    public class Canvas
    {
        public readonly int W, H;
        public readonly Color[] Px;
        public Affine M = Affine.Identity;

        readonly Stack<Affine> stack = new Stack<Affine>();
        readonly float[] cov;
        const int SS = 4;

        struct Edge { public float ymin, ymax, x0, dxdy; public int dir; }
        struct Cross { public float x; public int dir; }

        readonly List<Edge> edges = new List<Edge>();
        readonly List<int> active = new List<int>();
        readonly List<Cross> crossings = new List<Cross>();
        static readonly Comparison<Edge> byYmin = (p, q) => p.ymin.CompareTo(q.ymin);
        static readonly Comparison<Cross> byX = (p, q) => p.x.CompareTo(q.x);

        public Canvas(int w, int h)
        {
            W = w;
            H = h;
            Px = new Color[w * h];
            cov = new float[w + 2];
        }

        public void Clear(Color c)
        {
            for (int i = 0; i < Px.Length; i++) Px[i] = c;
        }

        public void Push(Affine g)
        {
            stack.Push(M);
            M = M * g;
        }

        public void Pop() => M = stack.Pop();

        // ---------- геометрия в локальных координатах ----------

        public static Vector2[] EllipsePoly(float cx, float cy, float rx, float ry, float rotDeg, int n = 40)
        {
            var pts = new Vector2[n];
            float r = rotDeg * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            for (int k = 0; k < n; k++)
            {
                float t = k * Mathf.PI * 2 / n;
                float dx = rx * Mathf.Cos(t), dy = ry * Mathf.Sin(t);
                pts[k] = new Vector2(cx + dx * cs - dy * sn, cy + dx * sn + dy * cs);
            }
            return pts;
        }

        public static Vector2[] RoundRectPoly(float x, float y, float w, float h, float r)
        {
            r = Mathf.Max(0, Mathf.Min(r, Mathf.Min(w, h) / 2));
            if (r < 0.01f) return new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) };
            const int seg = 8;
            var pts = new List<Vector2>(seg * 4 + 4);
            void Arc(float cx, float cy, float a0)
            {
                for (int k = 0; k <= seg; k++)
                {
                    float t = (a0 + 90f * k / seg) * Mathf.Deg2Rad;
                    pts.Add(new Vector2(cx + r * Mathf.Cos(t), cy + r * Mathf.Sin(t)));
                }
            }
            Arc(x + w - r, y + r, -90);
            Arc(x + w - r, y + h - r, 0);
            Arc(x + r, y + h - r, 90);
            Arc(x + r, y + r, 180);
            return pts.ToArray();
        }

        Vector2[] Xf(IList<Vector2> local)
        {
            var o = new Vector2[local.Count];
            for (int k = 0; k < o.Length; k++) o[k] = M.Apply(local[k]);
            return o;
        }

        static float Area(Vector2[] p)
        {
            float s = 0;
            for (int k = 0; k < p.Length; k++)
            {
                var q = p[k];
                var r = p[(k + 1) % p.Length];
                s += q.x * r.y - r.x * q.y;
            }
            return s * 0.5f;
        }

        static Vector2[] Orient(Vector2[] p, int sign)
        {
            if (sign == 0) return p;
            float a = Area(p);
            if ((a > 0 ? 1 : -1) != sign) Array.Reverse(p);
            return p;
        }

        // ---------- публичные операции ----------

        public void FillPath(string d, Color c, PixelShader sh = null)
        {
            var subs = SvgPath.Parse(d);
            var polys = new List<Vector2[]>(subs.Count);
            foreach (var s in subs)
                if (s.pts.Count >= 3) polys.Add(Xf(s.pts));
            Raster(polys, c, sh);
        }

        public void StrokePath(string d, float width, Color c)
        {
            var polys = new List<Vector2[]>();
            float h = width / 2;
            foreach (var s in SvgPath.Parse(d))
            {
                var pts = s.pts;
                int segs = s.closed ? pts.Count : pts.Count - 1;
                for (int k = 0; k < segs; k++)
                {
                    var a = pts[k];
                    var b = pts[(k + 1) % pts.Count];
                    var dir = b - a;
                    if (dir.sqrMagnitude < 1e-8f) continue;
                    var nrm = new Vector2(-dir.y, dir.x).normalized * h;
                    polys.Add(Orient(Xf(new[] { a + nrm, b + nrm, b - nrm, a - nrm }), 1));
                }
                foreach (var p in pts) polys.Add(Orient(Xf(EllipsePoly(p.x, p.y, h, h, 0, 12)), 1));
            }
            Raster(polys, c, null);
        }

        public void FillEllipse(float cx, float cy, float rx, float ry, float rot, Color c, PixelShader sh = null)
        {
            Raster(new List<Vector2[]> { Xf(EllipsePoly(cx, cy, rx, ry, rot)) }, c, sh);
        }

        public void StrokeEllipse(float cx, float cy, float rx, float ry, float rot, float sw, Color c)
        {
            float h = sw / 2;
            var polys = new List<Vector2[]> { Orient(Xf(EllipsePoly(cx, cy, rx + h, ry + h, rot)), 1) };
            if (rx - h > 0 && ry - h > 0) polys.Add(Orient(Xf(EllipsePoly(cx, cy, rx - h, ry - h, rot)), -1));
            Raster(polys, c, null);
        }

        public void FillRoundRect(float x, float y, float w, float h, float r, Color c, PixelShader sh = null)
        {
            Raster(new List<Vector2[]> { Xf(RoundRectPoly(x, y, w, h, r)) }, c, sh);
        }

        public void StrokeRoundRect(float x, float y, float w, float h, float r, float sw, Color c)
        {
            float k = sw / 2;
            var polys = new List<Vector2[]>
            {
                Orient(Xf(RoundRectPoly(x - k, y - k, w + sw, h + sw, r + k)), 1),
                Orient(Xf(RoundRectPoly(x + k, y + k, w - sw, h - sw, Mathf.Max(0, r - k))), -1),
            };
            Raster(polys, c, null);
        }

        // Объединение нескольких локальных многоугольников одной заливкой (без двойной прозрачности на стыках)
        public void FillUnion(List<Vector2[]> localPolys, Color c)
        {
            var polys = new List<Vector2[]>(localPolys.Count);
            foreach (var p in localPolys) polys.Add(Orient(Xf(p), 1));
            Raster(polys, c, null);
        }

        // ---------- сканирующая заливка ----------

        void Raster(List<Vector2[]> polys, Color col, PixelShader sh)
        {
            edges.Clear();
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var poly in polys)
            {
                int n = poly.Length;
                for (int k = 0; k < n; k++)
                {
                    var a = poly[k];
                    var b = poly[(k + 1) % n];
                    if (a.x < minX) minX = a.x;
                    if (a.x > maxX) maxX = a.x;
                    if (a.y < minY) minY = a.y;
                    if (a.y > maxY) maxY = a.y;
                    if (Mathf.Approximately(a.y, b.y)) continue;
                    var top = a.y < b.y ? a : b;
                    var bot = a.y < b.y ? b : a;
                    edges.Add(new Edge
                    {
                        ymin = top.y,
                        ymax = bot.y,
                        x0 = top.x,
                        dxdy = (bot.x - top.x) / (bot.y - top.y),
                        dir = b.y > a.y ? 1 : -1,
                    });
                }
            }
            if (edges.Count == 0) return;
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY));
            int bx0 = Mathf.Max(0, Mathf.FloorToInt(minX));
            int bx1 = Mathf.Min(W - 1, Mathf.CeilToInt(maxX));
            if (y0 > y1 || bx0 > bx1) return;

            edges.Sort(byYmin);
            active.Clear();
            int next = 0;
            const float w = 1f / SS;

            for (int y = y0; y <= y1; y++)
            {
                Array.Clear(cov, bx0, bx1 - bx0 + 2);
                bool any = false;
                for (int s = 0; s < SS; s++)
                {
                    float sy = y + (s + 0.5f) / SS;
                    while (next < edges.Count && edges[next].ymin <= sy) active.Add(next++);
                    for (int k = active.Count - 1; k >= 0; k--)
                        if (edges[active[k]].ymax <= sy) active.RemoveAt(k);
                    crossings.Clear();
                    foreach (int ei in active)
                    {
                        var e = edges[ei];
                        if (sy < e.ymin) continue;
                        crossings.Add(new Cross { x = e.x0 + (sy - e.ymin) * e.dxdy, dir = e.dir });
                    }
                    if (crossings.Count < 2) continue;
                    crossings.Sort(byX);
                    int wind = 0;
                    float start = 0;
                    foreach (var cr in crossings)
                    {
                        int pw = wind;
                        wind += cr.dir;
                        if (pw == 0 && wind != 0) start = cr.x;
                        else if (pw != 0 && wind == 0) { AddSpan(start, cr.x, w, bx0, bx1 + 1); any = true; }
                    }
                }
                if (!any) continue;
                int row = y * W;
                for (int x = bx0; x <= bx1; x++)
                {
                    float cv = cov[x];
                    if (cv <= 0.002f) continue;
                    if (cv > 1) cv = 1;
                    var src = sh != null ? sh(x + 0.5f, y + 0.5f) : col;
                    Over(row + x, src, cv * src.a);
                }
            }
        }

        void AddSpan(float x0, float x1, float w, int lo, int hi)
        {
            if (x0 < lo) x0 = lo;
            if (x1 > hi) x1 = hi;
            if (x1 <= x0) return;
            int i0 = (int)x0, i1 = (int)x1;
            if (i0 == i1) { cov[i0] += (x1 - x0) * w; return; }
            cov[i0] += (i0 + 1 - x0) * w;
            for (int i = i0 + 1; i < i1; i++) cov[i] += w;
            if (i1 < cov.Length) cov[i1] += (x1 - i1) * w;
        }

        public void Over(int i, Color src, float a)
        {
            if (a <= 0) return;
            var d = Px[i];
            float k = d.a * (1 - a);
            float oa = a + k;
            if (oa <= 0) return;
            Px[i] = new Color((src.r * a + d.r * k) / oa, (src.g * a + d.g * k) / oa, (src.b * a + d.b * k) / oa, oa);
        }

        // Покрытие скруглённого прямоугольника в точке (пиксельные координаты), с мягким краем в 1 пиксель
        public static float RoundRectCoverage(float px, float py, Rect r, float rad)
        {
            float hx = r.width / 2, hy = r.height / 2;
            rad = Mathf.Min(rad, Mathf.Min(hx, hy));
            float qx = Mathf.Abs(px - r.center.x) - (hx - rad);
            float qy = Mathf.Abs(py - r.center.y) - (hy - rad);
            float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
            float dist = outside + Mathf.Min(Mathf.Max(qx, qy), 0) - rad;
            return Mathf.Clamp01(0.5f - dist);
        }

        public Texture2D ToTexture(bool mips = true)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, mips);
            var data = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                int src = y * W, dst = (H - 1 - y) * W; // строки холста идут сверху вниз, у текстуры — снизу вверх
                for (int x = 0; x < W; x++) data[dst + x] = Px[src + x];
            }
            tex.SetPixels32(data);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.Apply(mips, true);
            return tex;
        }
    }

    public static class Fmt
    {
        public static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
