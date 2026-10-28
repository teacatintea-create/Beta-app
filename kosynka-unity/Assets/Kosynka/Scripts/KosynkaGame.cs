using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Kosynka
{
    // Точка входа: игра собирает себя сама, сцена может быть пустой
    public static class Boot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Object.FindObjectOfType<KosynkaGame>() != null) return;
            var camGo = new GameObject("Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Pal.Floor;
            cam.cullingMask = 0;
            var go = new GameObject("Kosynka");
            go.AddComponent<KosynkaGame>();
            Object.DontDestroyOnLoad(camGo);
            Object.DontDestroyOnLoad(go);
        }
    }

    public partial class KosynkaGame : MonoBehaviour
    {
        readonly Klondike g = new Klondike();
        readonly System.Random rnd = new System.Random();
        Skin skin;
        readonly ImgLib lib = new ImgLib();

        // текстуры
        readonly Texture2D[] faceTex = new Texture2D[52];
        readonly FaceInfo[] faceInfo = new FaceInfo[52];
        readonly bool[] faceDirty = new bool[52];
        Texture2D backTex, slotTex, shadowTex, flexTex, whiteTex, plateTex;
        readonly Texture2D[] suitTex = new Texture2D[4];
        TableArt table;
        bool backDirty = true, chromeDirty = true, tableDirty = true;
        float savedAt = -1;

        // анимация карт (координаты относительно начала стола)
        readonly Vector2[] pos = new Vector2[52], tgt = new Vector2[52];
        readonly int[] z = new int[52];
        readonly float[] delay = new float[52], flyUntil = new float[52], shakeUntil = new float[52];
        readonly List<int> drawOrder = new List<int>(52);

        // раскладка
        float sc = 1, cw = 60, ch = 84, gap = 6, tabY = 100, topH = 90;
        Vector2 origin;
        Rect stockRect, wasteRect;
        readonly Rect[] foundRects = new Rect[4], tabRects = new Rect[7];

        // перетаскивание
        class Drag
        {
            public List<int> ids;
            public Loc loc;
            public Vector2 start, delta;
            public bool moved;
            public float t0;
        }
        Drag drag;

        // состояние партии
        float elapsed;
        bool timerOn, won, autoRunning;
        float autoNext, winShownAt = -1;
        readonly List<Vector4> rain = new List<Vector4>(); // x, скорость, задержка, вращение

        void Awake()
        {
            Application.targetFrameRate = 60;
            skin = Skin.Load();
            lib.Init();
            lib.Reload();
            libSig = lib.Signature();
            for (int i = 0; i < 52; i++) { faceInfo[i] = new FaceInfo(); faceDirty[i] = true; }
            whiteTex = Texture2D.whiteTexture;
            for (int s = 0; s < 4; s++) suitTex[s] = CardArt.RenderSuitIcon(s);
            flexTex = CardArt.RenderFlex();
            plateTex = CardArt.RenderRound(Color.white, new Color(0, 0, 0, 0), 24, 6);
            RenderDirty(0); // рубашка, стол и слоты сразу, лица карт — по кадрам, начиная с открытых
            NewGame();
        }

        // ---------- перерисовка текстур ----------

        void MarkAllFaces() { for (int i = 0; i < 52; i++) faceDirty[i] = true; }

        void MarkFaces(IEnumerable<int> ids) { foreach (int i in ids) faceDirty[i] = true; }

        void Changed() { savedAt = Time.unscaledTime; }

        void RenderDirty(float budgetMs)
        {
            var sw = Stopwatch.StartNew();
            if (chromeDirty)
            {
                Swap(ref slotTex, CardArt.RenderSlot(skin.radius));
                Swap(ref shadowTex, CardArt.RenderShadow(skin.radius));
                chromeDirty = false;
            }
            if (backDirty)
            {
                Swap(ref backTex, CardArt.RenderBack(skin, lib));
                backDirty = false;
            }
            if (tableDirty)
            {
                if (table != null)
                {
                    if (table.gradient != null) Destroy(table.gradient);
                    if (table.tile != null) Destroy(table.tile);
                    if (table.imageTex != null) Destroy(table.imageTex);
                }
                table = CardArt.RenderTable(skin, lib);
                tableDirty = false;
            }
            // сначала карты, которые видны в редакторе и превью, потом открытые на столе, потом остальные
            foreach (int id in PriorityIds())
            {
                if (!faceDirty[id]) continue;
                if (sw.Elapsed.TotalMilliseconds > budgetMs) return;
                RenderFace(id);
            }
            for (int id = 0; id < 52; id++)
            {
                if (!faceDirty[id]) continue;
                if (sw.Elapsed.TotalMilliseconds > budgetMs) return;
                RenderFace(id);
            }
        }

        IEnumerable<int> PriorityIds()
        {
            if (panelOpen)
            {
                foreach (int id in sel) yield return id;
                foreach (int id in PREVIEW_IDS) yield return id;
            }
            for (int id = 0; id < 52; id++)
                if (g.up[id]) yield return id;
        }

        void RenderFace(int id)
        {
            Swap(ref faceTex[id], CardArt.RenderFace(skin, lib, id, faceInfo[id]));
            faceDirty[id] = false;
        }

        static void Swap(ref Texture2D slot, Texture2D fresh)
        {
            if (slot != null) Destroy(slot);
            slot = fresh;
        }

        // ---------- партия ----------

        void NewGame()
        {
            g.Deal(rnd);
            elapsed = 0;
            timerOn = false;
            won = false;
            autoRunning = false;
            winShownAt = -1;
            drag = null;
            Layout();
            var order = new List<int>();
            foreach (var t in g.tab) order.AddRange(t);
            for (int i = 0; i < 52; i++)
            {
                pos[i] = stockRect.position - origin;
                delay[i] = 0;
            }
            for (int i = 0; i < order.Count; i++) delay[order[i]] = i * 0.026f;
        }

        void StartTimer() { if (!won) timerOn = true; }

        void Fly(IEnumerable<int> ids)
        {
            foreach (int id in ids) flyUntil[id] = Time.unscaledTime + 0.35f;
        }

        void DoMove(List<int> ids, Loc from, Pile p, int idx, bool auto = false)
        {
            g.Move(ids, from, p, idx, !auto);
            StartTimer();
            Fly(ids);
            if (!auto) AfterMove();
        }

        void DrawStock()
        {
            var drawn = g.Draw(skin.drawMode);
            if (drawn == null) return;
            StartTimer();
            Fly(drawn);
            AfterMove();
        }

        void Undo()
        {
            if (autoRunning || !g.Undo()) return;
            won = false;
            winShownAt = -1;
        }

        void AfterMove()
        {
            if (CheckWin()) return;
            if (!autoRunning && g.AutoReady)
            {
                autoRunning = true;
                autoNext = Time.unscaledTime + 0.26f;
            }
        }

        bool CheckWin()
        {
            if (won) return true;
            if (!g.Won) return false;
            won = true;
            timerOn = false;
            winShownAt = Time.unscaledTime + 0.35f;
            rain.Clear();
            for (int i = 0; i < 26; i++)
                rain.Add(new Vector4(Random.value, 0.18f + Random.value * 0.2f, Random.value * 1.5f, Random.value * 360));
            return true;
        }

        void AutoStep()
        {
            int bestT = -1, bestF = -1, bestRank = 99;
            for (int t = 0; t < 7; t++)
            {
                int c = Klondike.Top(g.tab[t]);
                if (c < 0) continue;
                for (int f = 0; f < 4; f++)
                    if (g.CanDrop(new List<int> { c }, Pile.Found, f) && Klondike.Rank(c) < bestRank)
                    {
                        bestRank = Klondike.Rank(c);
                        bestT = t;
                        bestF = f;
                    }
            }
            if (bestT < 0) { autoRunning = false; return; }
            int id = Klondike.Top(g.tab[bestT]);
            DoMove(new List<int> { id }, new Loc { pile = Pile.Tab, idx = bestT, pos = g.tab[bestT].Count - 1 }, Pile.Found, bestF, true);
            if (CheckWin()) autoRunning = false;
            else autoNext = Time.unscaledTime + 0.085f;
        }

        void TapCard(Drag d)
        {
            var ids = d.ids;
            var loc = d.loc;
            Pile tp = Pile.Stock;
            int ti = -1;
            if (ids.Count == 1 && loc.pile != Pile.Found)
                for (int f = 0; f < 4 && ti < 0; f++)
                    if (g.CanDrop(ids, Pile.Found, f)) { tp = Pile.Found; ti = f; }
            if (ti < 0)
            {
                int filled = -1, empty = -1;
                for (int t = 0; t < 7; t++)
                {
                    if (loc.pile == Pile.Tab && loc.idx == t) continue;
                    if (!g.CanDrop(ids, Pile.Tab, t)) continue;
                    if (g.tab[t].Count > 0) { if (filled < 0) filled = t; }
                    else if (empty < 0) empty = t;
                }
                if (filled >= 0) { tp = Pile.Tab; ti = filled; }
                else if (empty >= 0 && !(loc.pile == Pile.Tab && loc.pos == 0)) { tp = Pile.Tab; ti = empty; }
            }
            if (ti >= 0) DoMove(ids, loc, tp, ti);
            else foreach (int id in ids) shakeUntil[id] = Time.unscaledTime + 0.32f;
        }

        bool FindDrop(Drag d, out Pile p, out int idx)
        {
            Pile bp = Pile.Stock;
            int bi = -1;
            var r = new Rect(origin + pos[d.ids[0]], new Vector2(cw, ch));
            float best = 0;
            for (int f = 0; f < 4; f++) Test(Pile.Found, f, foundRects[f]);
            for (int t = 0; t < 7; t++)
            {
                int last = Klondike.Top(g.tab[t]);
                float bottom = last >= 0 ? origin.y + tgt[last].y + ch : tabRects[t].yMax;
                Test(Pile.Tab, t, new Rect(tabRects[t].x, tabRects[t].y, cw, bottom - tabRects[t].y));
            }
            p = bp;
            idx = bi;
            return bi >= 0;

            void Test(Pile tp, int ti, Rect area)
            {
                if (tp == d.loc.pile && ti == d.loc.idx) return;
                float ox = Mathf.Min(r.xMax, area.xMax) - Mathf.Max(r.x, area.x);
                float oy = Mathf.Min(r.yMax, area.yMax) - Mathf.Max(r.y, area.y);
                if (ox <= 0 || oy <= 0 || ox * oy <= best) return;
                if (!g.CanDrop(d.ids, tp, ti)) return;
                best = ox * oy;
                bp = tp;
                bi = ti;
            }
        }

        // ---------- раскладка ----------

        void Layout()
        {
            float W = Screen.width, Hs = Screen.height;
            sc = Mathf.Clamp(Hs / 860f, 0.85f, 2f);
            float pad = 16 * sc;
            float bw = Mathf.Min(W - 2 * pad, 1100 * sc);
            gap = Mathf.Max(4, bw * 0.016f);
            cw = (bw - gap * 6) / 7;
            cw = Mathf.Max(24, Mathf.Min(cw, (Hs - topH - pad) / (1.4f * 3.4f)));
            ch = cw * 1.4f;
            bw = cw * 7 + gap * 6;
            origin = new Vector2(Mathf.Round((W - bw) / 2), Mathf.Round(topH));
            tabY = ch + gap * 2.2f;

            Vector2 Col(int i, float y) => new Vector2(i * (cw + gap), y);
            stockRect = new Rect(origin + Col(0, 0), new Vector2(cw, ch));
            wasteRect = new Rect(origin + Col(1, 0), new Vector2(cw, ch));
            for (int f = 0; f < 4; f++) foundRects[f] = new Rect(origin + Col(3 + f, 0), new Vector2(cw, ch));
            for (int t = 0; t < 7; t++) tabRects[t] = new Rect(origin + Col(t, tabY), new Vector2(cw, ch));

            for (int i = 0; i < g.stock.Count; i++) Place(g.stock[i], Col(0, 0), 10 + i);
            float fan = cw * 0.3f;
            int nShow = skin.drawMode == 3 ? Mathf.Min(3, g.waste.Count) : 1;
            for (int i = 0; i < g.waste.Count; i++)
                Place(g.waste[i], Col(1, 0) + new Vector2(Mathf.Max(0, i - (g.waste.Count - nShow)) * fan, 0), 100 + i);
            for (int f = 0; f < 4; f++)
                for (int i = 0; i < g.found[f].Count; i++) Place(g.found[f][i], Col(3 + f, 0), 200 + i);

            float avail = Mathf.Max(ch * 2.5f, Hs - origin.y - tabY - ch - pad);
            for (int t = 0; t < 7; t++)
            {
                var p = g.tab[t];
                int down = 0, upN = 0;
                for (int i = 0; i < p.Count - 1; i++) { if (g.up[p[i]]) upN++; else down++; }
                float dOff = ch * 0.11f, uOff = ch * 0.27f;
                if (down * dOff + upN * uOff > avail && upN > 0) uOff = Mathf.Max(ch * 0.16f, (avail - down * dOff) / upN);
                if (down * dOff + upN * uOff > avail && down > 0) dOff = Mathf.Max(ch * 0.05f, (avail - upN * uOff) / down);
                float y = tabY;
                for (int i = 0; i < p.Count; i++)
                {
                    Place(p[i], Col(t, y), 300 + i);
                    if (i < p.Count - 1) y += g.up[p[i]] ? uOff : dOff;
                }
            }
        }

        void Place(int id, Vector2 p, int zz)
        {
            tgt[id] = p;
            z[id] = zz;
        }

        // ---------- кадр ----------

        void Update()
        {
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            RenderDirty(12);
            Layout();

            float k = 1 - Mathf.Exp(-dt * 22);
            for (int i = 0; i < 52; i++)
            {
                if (drag != null && drag.moved && drag.ids.Contains(i)) { pos[i] = tgt[i] + drag.delta; continue; }
                if (delay[i] > 0) { delay[i] -= dt; continue; }
                pos[i] = Vector2.Lerp(pos[i], tgt[i], k);
                if ((pos[i] - tgt[i]).sqrMagnitude < 0.04f) pos[i] = tgt[i];
            }

            if (drag != null && !drag.moved && now - drag.t0 > 0.55f && !panelOpen)
            {
                int id = drag.ids[0];
                drag = null;
                OpenEditor(id);
            }

            if (autoRunning && now >= autoNext) AutoStep();
            if (timerOn) elapsed += dt;

            if (savedAt > 0 && now - savedAt > 0.5f)
            {
                skin.Save();
                savedAt = -1;
            }
        }

        void OnApplicationQuit()
        {
            if (savedAt > 0) skin.Save();
        }

        static string Fmt2(float s)
        {
            int t = Mathf.FloorToInt(s);
            return (t / 60) + ":" + (t % 60).ToString("00");
        }
    }
}
