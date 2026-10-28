using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kosynka
{
    // Картинка игрока: пиксели сверху вниз + уменьшенные копии для качественного масштабирования
    public class ImgData
    {
        public string name;
        public readonly List<Color32[]> levels = new List<Color32[]>();
        public readonly List<Vector2Int> sizes = new List<Vector2Int>();
        public Texture2D thumb;

        public int W => sizes[0].x;
        public int H => sizes[0].y;

        // Уровень, у которого масштаб вывода ближе всего к 1:1 (scale — во сколько раз картинка уменьшается)
        public int LevelFor(float scale)
        {
            int lv = 0;
            while (lv + 1 < levels.Count && scale <= 0.5f)
            {
                scale *= 2;
                lv++;
            }
            return lv;
        }

        public Color Sample(int lv, float u, float v)
        {
            var px = levels[lv];
            int w = sizes[lv].x, h = sizes[lv].y;
            u = Mathf.Clamp(u, 0, w - 1);
            v = Mathf.Clamp(v, 0, h - 1);
            int x0 = (int)u, y0 = (int)v;
            int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
            float fx = u - x0, fy = v - y0;
            Color a = px[y0 * w + x0], b = px[y0 * w + x1], c = px[y1 * w + x0], d = px[y1 * w + x1];
            return Color.Lerp(Color.Lerp(a, b, fx), Color.Lerp(c, d, fx), fy);
        }
    }

    // Библиотека картинок: файлы PNG и JPG из папки «Картинки» рядом с игрой
    public class ImgLib
    {
        public readonly List<ImgData> list = new List<ImgData>();
        readonly Dictionary<string, ImgData> byName = new Dictionary<string, ImgData>();
        public string Dir { get; private set; }
        public int Failed { get; private set; }

        const int MaxSide = 720;
        const int ThumbSide = 96;

        public void Init()
        {
            string exeDir = Path.GetDirectoryName(Application.dataPath) ?? Application.persistentDataPath;
            Dir = Path.Combine(exeDir, "Картинки");
            try { Directory.CreateDirectory(Dir); }
            catch (Exception)
            {
                Dir = Path.Combine(Application.persistentDataPath, "Картинки");
                try { Directory.CreateDirectory(Dir); } catch (Exception) { }
            }
        }

        public ImgData Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return byName.TryGetValue(name, out var d) ? d : null;
        }

        // Отпечаток содержимого папки: имя, размер и время изменения файлов
        public string Signature()
        {
            if (string.IsNullOrEmpty(Dir) || !Directory.Exists(Dir)) return "";
            var sb = new System.Text.StringBuilder();
            try
            {
                foreach (var f in Directory.GetFiles(Dir))
                {
                    var fi = new FileInfo(f);
                    sb.Append(fi.Name).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append(';');
                }
            }
            catch (Exception) { }
            return sb.ToString();
        }

        public void Reload()
        {
            foreach (var d in list)
                if (d.thumb != null) UnityEngine.Object.Destroy(d.thumb);
            list.Clear();
            byName.Clear();
            Failed = 0;
            if (string.IsNullOrEmpty(Dir) || !Directory.Exists(Dir)) return;
            var files = new List<string>(Directory.GetFiles(Dir));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
                var d = Load(f);
                if (d == null) { Failed++; continue; }
                list.Add(d);
                byName[d.name] = d;
            }
        }

        static ImgData Load(string path)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception) { return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            int w = tex.width, h = tex.height;
            var raw = tex.GetPixels32();
            UnityEngine.Object.Destroy(tex);

            // GetPixels32 отдаёт строки снизу вверх — переворачиваем
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) Array.Copy(raw, (h - 1 - y) * w, px, y * w, w);

            float k = Mathf.Min(1f, MaxSide / (float)Mathf.Max(w, h));
            if (k < 1f)
            {
                int nw = Mathf.Max(1, Mathf.RoundToInt(w * k)), nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
                px = Resize(px, w, h, nw, nh);
                w = nw;
                h = nh;
            }

            var d = new ImgData { name = Path.GetFileName(path) };
            d.levels.Add(px);
            d.sizes.Add(new Vector2Int(w, h));
            while (w > 8 && h > 8)
            {
                int nw = w / 2, nh = h / 2;
                px = Resize(px, w, h, nw, nh);
                w = nw;
                h = nh;
                d.levels.Add(px);
                d.sizes.Add(new Vector2Int(w, h));
            }
            d.thumb = MakeThumb(d);
            return d;
        }

        // Усреднение по прямоугольным областям (для уменьшения)
        static Color32[] Resize(Color32[] src, int w, int h, int nw, int nh)
        {
            var dst = new Color32[nw * nh];
            for (int y = 0; y < nh; y++)
            {
                int sy0 = y * h / nh, sy1 = Mathf.Max(sy0 + 1, (y + 1) * h / nh);
                for (int x = 0; x < nw; x++)
                {
                    int sx0 = x * w / nw, sx1 = Mathf.Max(sx0 + 1, (x + 1) * w / nw);
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int sy = sy0; sy < sy1; sy++)
                        for (int sx = sx0; sx < sx1; sx++)
                        {
                            var c = src[sy * w + sx];
                            r += c.r; g += c.g; b += c.b; a += c.a; n++;
                        }
                    dst[y * nw + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            }
            return dst;
        }

        static Texture2D MakeThumb(ImgData d)
        {
            int lv = 0;
            while (lv + 1 < d.levels.Count && Mathf.Max(d.sizes[lv + 1].x, d.sizes[lv + 1].y) >= ThumbSide) lv++;
            int w = d.sizes[lv].x, h = d.sizes[lv].y;
            float k = Mathf.Min(1f, ThumbSide / (float)Mathf.Max(w, h));
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * k)), nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
            var px = Resize(d.levels[lv], w, h, nw, nh);
            var flipped = new Color32[nw * nh];
            for (int y = 0; y < nh; y++) Array.Copy(px, y * nw, flipped, (nh - 1 - y) * nw, nw);
            var tex = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
            tex.SetPixels32(flipped);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false, true);
            return tex;
        }
    }
}
