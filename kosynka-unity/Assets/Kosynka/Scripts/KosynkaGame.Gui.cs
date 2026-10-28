using System.Collections.Generic;
using UnityEngine;

namespace Kosynka
{
    // Отрисовка стола, карт, верхней панели и экрана победы (IMGUI)
    public partial class KosynkaGame
    {
        GUIStyle sBtn, sBtnPrimary, sSegOn, sTab, sTabOn, sText, sMuted, sNote, sLab, sTitle, sBrand, sBrandAccent,
            sRank, sCaption, sLetter, sSlotLbl, sWinTitle, sWinText, sChip, sSheet, sBox, sSwatch, sSwatchOn, sField;
        Font fDisplay, fBody;
        readonly Dictionary<string, Font> cornerFonts = new Dictionary<string, Font>();
        float stylesScale = -1;

        static Font OsFont(params string[] names)
        {
            try { return Font.CreateDynamicFontFromOSFont(names, 32); }
            catch (System.Exception) { return null; }
        }

        Font CornerFont(string key)
        {
            if (cornerFonts.TryGetValue(key, out var f)) return f;
            switch (key)
            {
                case "rubik": f = OsFont("Segoe UI Semibold", "Segoe UI", "Arial", "Segoe UI Symbol"); break;
                case "serif": f = OsFont("Georgia", "Times New Roman", "Segoe UI Symbol"); break;
                case "mono": f = OsFont("Consolas", "Courier New", "Segoe UI Symbol"); break;
                default: f = fDisplay; break;
            }
            cornerFonts[key] = f;
            return f;
        }

        GUIStyle MakeStyle(Font font, Color col, TextAnchor align = TextAnchor.UpperLeft, FontStyle fs = FontStyle.Normal)
        {
            var st = new GUIStyle { font = font, alignment = align, fontStyle = fs, richText = false };
            st.normal.textColor = col;
            st.hover.textColor = col;
            st.active.textColor = col;
            return st;
        }

        GUIStyle MakeButton(Texture2D bg, Texture2D hover, Color text)
        {
            var st = MakeStyle(fBody, text, TextAnchor.MiddleCenter);
            st.normal.background = bg;
            st.hover.background = hover;
            st.active.background = hover;
            st.focused.background = bg;
            st.focused.textColor = text;
            st.border = new RectOffset(8, 8, 8, 8);
            return st;
        }

        void EnsureStyles()
        {
            if (sBtn == null)
            {
                fDisplay = OsFont("Arial Black", "Impact", "Arial Bold", "Arial", "Segoe UI Symbol");
                fBody = OsFont("Segoe UI", "Arial", "Segoe UI Symbol");
                var clear = new Color(0, 0, 0, 0);
                sBtn = MakeButton(CardArt.RenderRound(Pal.FloorHi, Pal.SlotLine), CardArt.RenderRound(Pal.FloorHi, Pal.Bronze), Pal.Chalk);
                sBtnPrimary = MakeButton(CardArt.RenderRound(Pal.Bronze, clear), CardArt.RenderRound(Pal.BronzeHi, clear), Pal.Ink);
                sSegOn = MakeButton(CardArt.RenderRound(Pal.Bronze, clear), CardArt.RenderRound(Pal.BronzeHi, clear), Pal.Ink);
                sTab = MakeStyle(fBody, Pal.Muted, TextAnchor.MiddleCenter);
                sTab.hover.textColor = Pal.Chalk;
                sTabOn = MakeStyle(fBody, Pal.BronzeHi, TextAnchor.MiddleCenter);
                sText = MakeStyle(fBody, Pal.Chalk);
                sMuted = MakeStyle(fBody, Pal.Muted);
                sNote = MakeStyle(fBody, Pal.Muted);
                sNote.wordWrap = true;
                sLab = MakeStyle(fBody, Pal.Muted);
                sTitle = MakeStyle(fDisplay, Pal.Chalk);
                sBrand = MakeStyle(fDisplay, Pal.Chalk);
                sBrandAccent = MakeStyle(fDisplay, Pal.BronzeHi);
                sRank = MakeStyle(fDisplay, Color.white);
                sCaption = MakeStyle(fBody, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                sCaption.clipping = TextClipping.Clip;
                sLetter = MakeStyle(fDisplay, Color.white, TextAnchor.MiddleCenter);
                sSlotLbl = MakeStyle(fDisplay, Pal.SlotLine, TextAnchor.MiddleCenter);
                sWinTitle = MakeStyle(fDisplay, Pal.BronzeHi, TextAnchor.MiddleCenter);
                sWinText = MakeStyle(fBody, Pal.Chalk, TextAnchor.MiddleCenter);
                sWinText.wordWrap = true;
                sChip = MakeStyle(fBody, Pal.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                sSheet = new GUIStyle { normal = { background = CardArt.RenderRound(Pal.FloorHi, clear, 16, 0, 0) } };
                sBox = new GUIStyle { normal = { background = CardArt.RenderRound(Pal.FloorHi, Pal.Bronze, 32, 12, 1.5f) }, border = new RectOffset(12, 12, 12, 12) };
                sSwatch = new GUIStyle { normal = { background = CardArt.RenderRound(new Color(1, 1, 1, 0), Pal.SlotLine, 32, 16, 2) }, border = new RectOffset(14, 14, 14, 14) };
                sSwatchOn = new GUIStyle { normal = { background = CardArt.RenderRound(new Color(1, 1, 1, 0), Pal.BronzeHi, 32, 16, 3) }, border = new RectOffset(14, 14, 14, 14) };
                sField = new GUIStyle(GUI.skin.textField) { font = fBody };
            }
            if (!Mathf.Approximately(stylesScale, sc))
            {
                stylesScale = sc;
                int F(float v) => Mathf.Max(8, Mathf.RoundToInt(v * sc));
                var btnPad = new RectOffset(F(12), F(12), F(8), F(8));
                foreach (var st in new[] { sBtn, sBtnPrimary, sSegOn })
                {
                    st.fontSize = F(13);
                    st.padding = btnPad;
                }
                sTab.fontSize = sTabOn.fontSize = F(14);
                sTab.padding = sTabOn.padding = new RectOffset(F(8), F(8), F(10), F(10));
                sText.fontSize = F(14);
                sMuted.fontSize = F(13);
                sNote.fontSize = F(12);
                sLab.fontSize = F(11);
                sTitle.fontSize = F(22);
                sBrand.fontSize = sBrandAccent.fontSize = F(28);
                sWinTitle.fontSize = F(30);
                sWinText.fontSize = F(15);
                sChip.fontSize = F(9);
                sField.fontSize = F(14);
                sField.padding = new RectOffset(F(8), F(8), F(7), F(7));
            }
        }

        void OnGUI()
        {
            EnsureStyles();
            var e = Event.current;
            bool winVisible = won && winShownAt > 0 && Time.unscaledTime >= winShownAt;
            bool modal = panelOpen || winVisible;

            DrawTable();
            DrawHud(!modal);
            DrawBoard();
            if (!modal) BoardInput(e);
            if (winVisible) DrawWin();
            if (panelOpen) DrawPanel();
            Keys(e);

            // Изменения состояния применяем после прохода IMGUI, чтобы раскладка GUILayout не менялась посреди события
            if (pending.Count > 0)
            {
                var acts = pending.ToArray();
                pending.Clear();
                foreach (var a in acts) a();
            }
        }

        readonly List<System.Action> pending = new List<System.Action>();
        void Do(System.Action a) => pending.Add(a);

        void Keys(Event e)
        {
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Escape && panelOpen) { Do(ClosePanel); e.Use(); return; }
            if (e.keyCode == KeyCode.Z && (e.control || e.command) && !panelOpen) { Do(Undo); e.Use(); return; }
            if (e.keyCode == KeyCode.F11) { Screen.fullScreen = !Screen.fullScreen; e.Use(); }
        }

        // ---------- стол ----------

        void DrawTable()
        {
            if (Event.current.type != EventType.Repaint) return;
            var full = new Rect(0, 0, Screen.width, Screen.height);
            GUI.color = table.baseColor;
            GUI.DrawTexture(full, whiteTex);
            GUI.color = Color.white;
            if (table.gradient != null) GUI.DrawTexture(full, table.gradient, ScaleMode.StretchToFill);
            if (table.imageTex != null) GUI.DrawTexture(full, table.imageTex, ScaleMode.ScaleAndCrop);
            if (table.tile != null)
            {
                float tp = table.tilePx * sc;
                GUI.DrawTextureWithTexCoords(full, table.tile, new Rect(0, 0, full.width / tp, table.tileStretchY ? 1 : full.height / tp));
            }
        }

        // ---------- верхняя панель ----------

        void DrawHud(bool interactive)
        {
            float pad = 16 * sc, y = 14 * sc, W = Screen.width;
            var c1 = new GUIContent("Масляная ");
            var c2 = new GUIContent("косынка");
            Vector2 s1 = sBrand.CalcSize(c1), s2 = sBrandAccent.CalcSize(c2);
            float rowH = Mathf.Max(s1.y, 36 * sc);
            GUI.Label(new Rect(pad, y + (rowH - s1.y) / 2, s1.x, s1.y), c1, sBrand);
            GUI.Label(new Rect(pad + s1.x, y + (rowH - s2.y) / 2, s2.x, s2.y), c2, sBrandAccent);

            var stats = new GUIContent("Ходы: " + g.moves + "     Время: " + Fmt2(elapsed));
            var ss = sMuted.CalcSize(stats);
            float statsX = pad + s1.x + s2.x + 18 * sc;
            GUI.Label(new Rect(statsX, y + (rowH - ss.y) / 2, ss.x, ss.y), stats, sMuted);

            string[] labels = { "Колода", skin.drawMode == 3 ? "По 3 карты" : "По 1 карте", "Отменить", Screen.fullScreen ? "В окне" : "Во весь экран", "Новая раздача" };
            float bgap = 8 * sc, total = 0;
            var widths = new float[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                var st = i == labels.Length - 1 ? sBtnPrimary : sBtn;
                widths[i] = st.CalcSize(new GUIContent(labels[i])).x;
                total += widths[i] + (i > 0 ? bgap : 0);
            }
            float bx = W - pad - total, by = y;
            float bottom = y + rowH;
            if (bx < statsX + ss.x + 16 * sc)
            {
                by = y + rowH + 8 * sc;
                bx = Mathf.Max(pad, W - pad - total);
                bottom = by + rowH;
            }
            bool was = GUI.enabled;
            for (int i = 0; i < labels.Length; i++)
            {
                var st = i == labels.Length - 1 ? sBtnPrimary : sBtn;
                GUI.enabled = interactive && (i != 2 || g.CanUndo) && !(i == 2 && won);
                int which = i;
                if (GUI.Button(new Rect(bx, by, widths[i], rowH), labels[i], st))
                {
                    Do(() =>
                    {
                        switch (which)
                        {
                            case 0: OpenPanel(); break;
                            case 1: skin.drawMode = skin.drawMode == 1 ? 3 : 1; Changed(); break;
                            case 2: Undo(); break;
                            case 3: Screen.fullScreen = !Screen.fullScreen; break;
                            case 4: NewGame(); break;
                        }
                    });
                }
                bx += widths[i] + bgap;
            }
            GUI.enabled = was;

            var hint = new GUIContent("Клик по карте отправляет её на лучшее место. Стопки можно перетаскивать. Ctrl+Z отменяет ход. Долгое нажатие или правый клик по открытой карте — её настройки.");
            float hy = bottom + 6 * sc;
            float hh = sNote.CalcHeight(hint, W - 2 * pad);
            GUI.Label(new Rect(pad, hy, W - 2 * pad, hh), hint, sNote);
            topH = hy + hh + 12 * sc;
        }

        // ---------- карты ----------

        int EffZ(int id, float now)
        {
            if (drag != null && drag.moved)
            {
                int k = drag.ids.IndexOf(id);
                if (k >= 0) return 3000 + k;
            }
            return z[id] + (now < flyUntil[id] ? 1000 : 0);
        }

        void SortDrawOrder()
        {
            float now = Time.unscaledTime;
            drawOrder.Clear();
            for (int i = 0; i < 52; i++) drawOrder.Add(i);
            drawOrder.Sort((a, b) => EffZ(a, now).CompareTo(EffZ(b, now)));
        }

        void DrawBoard()
        {
            SortDrawOrder();
            if (Event.current.type != EventType.Repaint) return;
            DrawSlot(stockRect, g.stock.Count == 0 && g.waste.Count > 0 ? "↻" : "");
            DrawSlot(wasteRect, "");
            foreach (var r in foundRects) DrawSlot(r, "A");
            foreach (var r in tabRects) DrawSlot(r, "K");
            float now = Time.unscaledTime;
            foreach (int id in drawOrder)
            {
                var r = new Rect(origin + pos[id], new Vector2(cw, ch));
                if (now < shakeUntil[id])
                {
                    float t = 1 - (shakeUntil[id] - now) / 0.32f;
                    r.x += Mathf.Sin(t * Mathf.PI * 4) * 4 * sc * (1 - t);
                }
                bool lifted = drag != null && drag.moved && drag.ids.Contains(id);
                DrawCard(r, id, g.up[id], lifted);
            }
        }

        static Rect Snap(Rect r) => new Rect(Mathf.Round(r.x), Mathf.Round(r.y), Mathf.Round(r.width), Mathf.Round(r.height));

        void DrawSlot(Rect r, string label)
        {
            GUI.DrawTexture(Snap(r), slotTex);
            if (label == "") return;
            sSlotLbl.fontSize = Mathf.Max(8, Mathf.RoundToInt(r.width * 0.34f));
            GUI.Label(r, label, sSlotLbl);
        }

        void DrawCard(Rect r, int id, bool faceUp, bool lifted = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            r = Snap(r);
            float m = r.width * 0.1f;
            var sh = new Rect(r.x - m, r.y - m + r.width * (lifted ? 0.1f : 0.025f), r.width + 2 * m, r.height + 2 * m);
            GUI.DrawTexture(sh, shadowTex);
            if (lifted) GUI.DrawTexture(sh, shadowTex);
            var tex = faceUp && faceTex[id] != null ? faceTex[id] : backTex; // пока лицо не нарисовано, видна рубашка
            if (tex != null) GUI.DrawTexture(r, tex);
            if (faceUp && faceTex[id] != null) DrawFaceText(r, id);
        }

        void DrawFaceText(Rect r, int id)
        {
            var info = faceInfo[id];
            int suit = id / 13, rank = id % 13 + 1;
            float w = r.width;
            int fs = Mathf.RoundToInt(w * 0.27f * skin.cornerScale);
            var old = GUI.color;
            if (fs >= 6)
            {
                sRank.font = CornerFont(skin.font) ?? fDisplay;
                sRank.fontSize = fs;
                sRank.fontStyle = skin.font == "russo" ? FontStyle.Normal : FontStyle.Bold;
                var rk = new GUIContent(CardArt.RANKS[rank]);
                var sz = sRank.CalcSize(rk);
                float x = r.x + w * 0.07f, y = r.y + r.height * 0.035f, icon = fs * 0.8f;
                if (info.cornerPlate)
                {
                    GUI.color = info.plate;
                    GUI.DrawTexture(new Rect(x - w * 0.035f, y - w * 0.02f, sz.x + icon + w * 0.06f, Mathf.Max(fs * 1.05f, icon) + w * 0.04f), plateTex);
                }
                GUI.color = info.ink;
                GUI.Label(new Rect(x, y - fs * 0.16f, sz.x + 4, sz.y), rk, sRank);
                GUI.DrawTexture(new Rect(x + sz.x * 0.94f, y + fs * 0.1f, icon, icon), suitTex[suit]);
                if (!info.cornerPlate)
                {
                    float mi = fs * 0.62f;
                    GUI.DrawTexture(new Rect(r.xMax - w * 0.07f - mi, y + fs * 0.22f, mi, mi), suitTex[suit]);
                }
            }
            if (info.courtLetter != null)
            {
                sLetter.font = CornerFont(skin.font) ?? fDisplay;
                sLetter.fontSize = Mathf.Max(8, Mathf.RoundToInt(w * 0.42f));
                float cy = r.y + info.letterY * r.height;
                GUI.color = info.ink;
                GUI.Label(new Rect(r.x, cy - sLetter.fontSize * 0.7f, w, sLetter.fontSize * 1.4f), info.courtLetter, sLetter);
            }
            if (!string.IsNullOrEmpty(info.caption))
            {
                int cfs = Mathf.Max(7, Mathf.RoundToInt(w * 0.11f));
                sCaption.fontSize = cfs;
                float ph = cfs * 1.5f;
                var pr = new Rect(r.x + w * 0.06f, r.yMax - r.height * 0.03f - ph, w * 0.88f, ph);
                GUI.color = info.plate;
                GUI.DrawTexture(pr, plateTex);
                GUI.color = info.ink;
                GUI.Label(pr, info.caption, sCaption);
            }
            GUI.color = old;
        }

        // ---------- ввод на столе ----------

        int HitCard(Vector2 p)
        {
            for (int k = drawOrder.Count - 1; k >= 0; k--)
            {
                int id = drawOrder[k];
                if (new Rect(origin + pos[id], new Vector2(cw, ch)).Contains(p)) return id;
            }
            return -1;
        }

        void BoardInput(Event e)
        {
            switch (e.type)
            {
                case EventType.MouseDown:
                {
                    int hit = HitCard(e.mousePosition);
                    if (e.button == 1)
                    {
                        if (hit >= 0 && g.up[hit] && !autoRunning) { int h = hit; Do(() => OpenEditor(h)); e.Use(); }
                        return;
                    }
                    if (e.button != 0 || autoRunning) return;
                    if (hit < 0)
                    {
                        if (stockRect.Contains(e.mousePosition)) { DrawStock(); e.Use(); }
                        return;
                    }
                    var loc = g.Locate(hit);
                    if (loc.pile == Pile.Stock) { DrawStock(); e.Use(); return; }
                    if (!g.up[hit]) return;
                    var list = g.Get(loc);
                    if (loc.pile != Pile.Tab && loc.pos != list.Count - 1) return;
                    drag = new Drag
                    {
                        ids = loc.pile == Pile.Tab ? list.GetRange(loc.pos, list.Count - loc.pos) : new List<int> { hit },
                        loc = loc,
                        start = e.mousePosition,
                        t0 = Time.unscaledTime,
                    };
                    e.Use();
                    break;
                }
                case EventType.MouseDrag:
                    if (drag == null) return;
                    drag.delta = e.mousePosition - drag.start;
                    if (!drag.moved && drag.delta.magnitude >= 6 * sc) drag.moved = true;
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (drag == null) return;
                    EndDrag();
                    e.Use();
                    break;
                default:
                    if (e.rawType == EventType.MouseUp && drag != null) EndDrag();
                    break;
            }
        }

        void EndDrag()
        {
            var d = drag;
            drag = null;
            if (!d.moved) { TapCard(d); return; }
            if (FindDrop(d, out var p, out int idx)) DoMove(d.ids, d.loc, p, idx);
        }

        // ---------- победа ----------

        void DrawWin()
        {
            float W = Screen.width, H = Screen.height, now = Time.unscaledTime;
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(10 / 255f, 12 / 255f, 11 / 255f, 0.72f);
                GUI.DrawTexture(new Rect(0, 0, W, H), whiteTex);
                GUI.color = Color.white;
                float s = 44 * sc;
                foreach (var v in rain)
                {
                    float t = now - winShownAt - v.z;
                    if (t < 0) continue;
                    float y = -s + t * v.y * (H + 2 * s);
                    if (y > H + s) continue;
                    var m = GUI.matrix;
                    var rect = new Rect(v.x * W - s / 2, y, s, s);
                    GUIUtility.RotateAroundPivot(v.w + t * 120, rect.center);
                    GUI.DrawTexture(rect, flexTex);
                    GUI.matrix = m;
                }
            }
            float bw = Mathf.Min(380 * sc, W - 32 * sc), bh = 210 * sc;
            var box = new Rect((W - bw) / 2, (H - bh) / 2, bw, bh);
            GUI.Box(box, GUIContent.none, sBox);
            GUI.Label(new Rect(box.x, box.y + 22 * sc, bw, 40 * sc), "Сет закрыт!", sWinTitle);
            string who = skin.art == "buff" ? "Все 52 качка стоят на пьедесталах." : "Все 52 карты разложены по мастям.";
            GUI.Label(new Rect(box.x + 20 * sc, box.y + 70 * sc, bw - 40 * sc, 60 * sc), who + " Время " + Fmt2(elapsed) + ", ходов " + g.moves + ".", sWinText);
            var bc = new GUIContent("Ещё подход");
            var bs = sBtnPrimary.CalcSize(bc);
            if (GUI.Button(new Rect(box.center.x - bs.x / 2, box.yMax - 58 * sc, bs.x, 38 * sc), bc, sBtnPrimary)) Do(NewGame);
        }
    }
}
