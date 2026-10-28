using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kosynka
{
    // Панель «Колода»: оформление всей колоды и каждой карты отдельно
    public partial class KosynkaGame
    {
        bool panelOpen;
        string tab = "faces";
        Vector2 scroll;
        readonly List<int> sel = new List<int>();
        bool multi;
        string colorOpen;
        string panelMsg;
        string libSig = "";
        Texture2D circleTex;

        static readonly (string, string)[] OPT_ART = { ("buff", "Качки"), ("classic", "Классика"), ("custom", "Свои картинки") };
        static readonly (string, string)[] OPT_FALLBACK = { ("buff", "Качок"), ("classic", "Классика") };
        static readonly (string, string)[] OPT_FACE = { ("cream", "Кремовое"), ("white", "Белое"), ("vintage", "Старая бумага"), ("dark", "Тёмное") };
        static readonly (string, string)[] OPT_SUITS = { ("two", "Две краски"), ("four", "Четыре краски") };
        static readonly (string, string)[] OPT_FONT = { ("russo", "Рубленый"), ("rubik", "Обычный"), ("serif", "С засечками"), ("mono", "Моноширинный") };
        static readonly (string, string)[] OPT_FIT = { ("cover", "Заполнить"), ("contain", "Целиком") };
        static readonly (string, string)[] OPT_AREA = { ("below", "Под значком"), ("full", "На всю карту") };
        static readonly (string, string)[] OPT_FOCUS = { ("top", "Верх"), ("center", "Центр"), ("bottom", "Низ") };
        static readonly (string, string)[] OPT_BACK = { ("dumbbell", "Гантели"), ("stripes", "Полосы"), ("dots", "Горошек"), ("checks", "Клетка"), ("plain", "Без узора"), ("image", "Картинка") };
        static readonly (string, string)[] OPT_TABLE = { ("rubber", "Резина зала"), ("felt", "Зелёное сукно"), ("blue", "Синее сукно"), ("wood", "Дерево"), ("dark", "Тёмный"), ("color", "Свой цвет"), ("image", "Картинка") };
        static readonly (string, string)[] E_ART = { ("", "Как у колоды"), ("buff", "Качок"), ("classic", "Классика"), ("image", "Картинка") };
        static readonly (string, string)[] E_FIT = { ("", "Как у колоды"), ("cover", "Заполнить"), ("contain", "Целиком") };
        static readonly (string, string)[] E_AREA = { ("", "Как у колоды"), ("below", "Под значком"), ("full", "На всю карту") };
        static readonly (string, string)[] E_FACE = { ("", "Как у колоды"), ("cream", "Кремовый"), ("white", "Белый"), ("vintage", "Бумага"), ("dark", "Тёмный"), ("color", "Свой цвет") };
        static readonly (string, string)[] E_POSE = { ("double", "Двойной бицепс"), ("crab", "Краб"), ("lift", "Штанга") };
        static readonly (string, string)[] E_HAIR = { ("pomp", "Кок"), ("flat", "Площадка"), ("buzz", "Ёжик"), ("mohawk", "Ирокез"), ("bald", "Лысый"), ("long", "Длинные") };
        static readonly (string, string)[] TABS = { ("faces", "Карты"), ("pics", "Картинки"), ("cards", "По картам"), ("back", "Рубашка"), ("table", "Стол") };
        static readonly string[] PALETTE = { "#c8142f", "#e2557a", "#d9963a", "#f2c14e", "#16823a", "#2b4a3a", "#1f5fd1", "#3b2a63", "#5a1a20", "#17191b", "#fbf6ec", "#ffffff" };
        static readonly string[] RANK_FULL = { "", "Туз", "Двойка", "Тройка", "Четвёрка", "Пятёрка", "Шестёрка", "Семёрка", "Восьмёрка", "Девятка", "Десятка", "Валет", "Дама", "Король" };
        static readonly string[] SUIT_GEN = { "червей", "бубен", "пик", "треф" };
        static readonly string[] SUIT_NAME = { "черви", "бубны", "пики", "трефы" };
        static readonly int[] PREVIEW_IDS = { 0, 32, 24, 51 };

        // ---------- открытие и закрытие ----------

        void OpenPanel()
        {
            panelOpen = true;
            drag = null;
            panelMsg = null;
            string sig = lib.Signature();
            if (sig != libSig) ReloadImages();
        }

        void ClosePanel()
        {
            panelOpen = false;
            sel.Clear();
            colorOpen = null;
        }

        void OpenEditor(int id)
        {
            sel.Clear();
            sel.Add(id);
            multi = false;
            tab = "cards";
            scroll = Vector2.zero;
            OpenPanel();
        }

        void ReloadImages()
        {
            lib.Reload();
            libSig = lib.Signature();
            MarkAllFaces();
            backDirty = true;
            tableDirty = true;
        }

        // ---------- изменения ----------

        void SetSkin(Action change, bool faces = true, bool back = false, bool tbl = false, bool chrome = false)
        {
            change();
            if (faces) MarkAllFaces();
            if (back) backDirty = true;
            if (tbl) tableDirty = true;
            if (chrome) chromeDirty = true;
            Changed();
        }

        void SetOvNow(Action<CardOv> f)
        {
            foreach (int id in sel) f(skin.cards[id]);
            MarkFaces(sel);
            Changed();
        }

        void SetOv(Action<CardOv> f) => Do(() => SetOvNow(f));

        void SetLookNow(Action<Look> f)
        {
            foreach (int id in sel)
            {
                var o = skin.cards[id];
                if (!o.hasLook)
                {
                    o.look = CardArt.DefaultLook(id);
                    o.hasLook = true;
                }
                f(o.look);
                if (CardArt.Kind(skin, lib, id) != "buff") o.art = "buff";
            }
            MarkFaces(sel);
            Changed();
        }

        void SetLook(Action<Look> f) => Do(() => SetLookNow(f));

        void DealImages(string mode)
        {
            for (int i = 0; i < 52; i++) skin.assign[i] = "";
            int n = lib.list.Count;
            if (mode != "clear" && n > 0)
            {
                var pool = new List<string>();
                if (mode == "shuffle")
                {
                    while (pool.Count < 52)
                    {
                        var names = new List<string>();
                        foreach (var d in lib.list) names.Add(d.name);
                        Shuffle(names);
                        pool.AddRange(names);
                    }
                    pool = pool.GetRange(0, 52);
                    Shuffle(pool);
                }
                for (int id = 0; id < 52; id++)
                {
                    int suit = id / 13, rank = id % 13 + 1;
                    skin.assign[id] = mode == "shuffle" ? pool[id]
                        : mode == "rank" ? lib.list[(rank - 1) % n].name
                        : mode == "suit" ? lib.list[suit % n].name
                        : lib.list[id % n].name;
                }
                skin.art = "custom";
            }
            MarkAllFaces();
            Changed();
        }

        void Shuffle<T>(List<T> a)
        {
            for (int i = a.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
        }

        // ---------- элементы интерфейса ----------

        float S(float v) => v * sc;

        void Lab(string text)
        {
            GUILayout.Space(S(16));
            GUILayout.Label(text.ToUpperInvariant(), sLab);
            GUILayout.Space(S(4));
        }

        void Note(string text)
        {
            GUILayout.Space(S(4));
            GUILayout.Label(text, sNote);
        }

        string Seg(string cur, (string v, string t)[] opts, float maxW)
        {
            string res = cur;
            float x = 0, gp = S(6);
            GUILayout.BeginHorizontal();
            foreach (var (v, t) in opts)
            {
                var st = v == cur ? sSegOn : sBtn;
                float w = st.CalcSize(new GUIContent(t)).x;
                if (x > 0 && x + w > maxW)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(gp);
                    GUILayout.BeginHorizontal();
                    x = 0;
                }
                if (GUILayout.Button(t, st, GUILayout.Width(w), GUILayout.Height(S(34)))) res = v;
                GUILayout.Space(gp);
                x += w + gp;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return res;
        }

        // Кнопки-переключатели в несколько строк; возвращает индекс нажатой или -1
        int Flow(string[] labels, bool[] on, float maxW)
        {
            int res = -1;
            float x = 0, gp = S(6);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
            {
                var st = on[i] ? sSegOn : sBtn;
                float w = st.CalcSize(new GUIContent(labels[i])).x;
                if (x > 0 && x + w > maxW)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(gp);
                    GUILayout.BeginHorizontal();
                    x = 0;
                }
                if (GUILayout.Button(labels[i], st, GUILayout.Width(w), GUILayout.Height(S(34)))) res = i;
                GUILayout.Space(gp);
                x += w + gp;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return res;
        }

        bool Button(string text, bool primary = false)
        {
            var st = primary ? sBtnPrimary : sBtn;
            return GUILayout.Button(text, st, GUILayout.Width(st.CalcSize(new GUIContent(text)).x), GUILayout.Height(S(34)));
        }

        float SliderRow(string label, float value, float min, float max, string shown)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, sText, GUILayout.Width(S(130)));
            GUILayout.BeginVertical();
            GUILayout.Space(S(7));
            float v = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndVertical();
            GUILayout.Space(S(8));
            GUILayout.Label(shown, sMuted, GUILayout.Width(S(48)));
            GUILayout.EndHorizontal();
            return v;
        }

        // Цвет: кружок-образец, по нажатию раскрываются палитра и ползунки
        void ColorRow(string key, string label, string hex, Action<string> applyNow)
        {
            bool open = colorOpen == key;
            GUILayout.BeginHorizontal();
            var sr = GUILayoutUtility.GetRect(S(40), S(30), GUILayout.Width(S(40)), GUILayout.Height(S(30)));
            if (GUI.Button(sr, GUIContent.none, GUIStyle.none)) Do(() => colorOpen = open ? null : key);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = Pal.Hex(hex);
                GUI.DrawTexture(new Rect(sr.x + S(3), sr.y + S(3), sr.width - S(6), sr.height - S(6)), plateTex);
                GUI.color = Color.white;
                GUI.Box(sr, GUIContent.none, open ? sSwatchOn : sSwatch);
            }
            GUILayout.Space(S(10));
            GUILayout.BeginVertical();
            GUILayout.Space(S(5));
            GUILayout.Label(label, sText);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!open) return;

            GUILayout.Space(S(8));
            int pick = Swatches(PALETTE, Array.IndexOf(PALETTE, hex), S(400));
            if (pick >= 0) { string h = PALETTE[pick]; Do(() => applyNow(h)); }
            Color.RGBToHSV(Pal.Hex(hex), out float hh, out float ss, out float vv);
            float nh = SliderRow("Оттенок", hh, 0, 1, Mathf.RoundToInt(hh * 360) + "°");
            float ns = SliderRow("Насыщенность", ss, 0, 1, Mathf.RoundToInt(ss * 100) + "%");
            float nv = SliderRow("Яркость", vv, 0, 1, Mathf.RoundToInt(vv * 100) + "%");
            if (!Mathf.Approximately(nh, hh) || !Mathf.Approximately(ns, ss) || !Mathf.Approximately(nv, vv))
                applyNow(Pal.ToHex(Color.HSVToRGB(nh, ns, nv)));
        }

        // Ряд круглых образцов цвета; возвращает индекс нажатого или -1
        int Swatches(string[] colors, int current, float maxW)
        {
            int res = -1;
            float s = S(30), gp = S(7);
            int perRow = Mathf.Max(1, Mathf.FloorToInt((maxW + gp) / (s + gp)));
            for (int start = 0; start < colors.Length; start += perRow)
            {
                var row = GUILayoutUtility.GetRect(maxW, s, GUILayout.Height(s));
                for (int k = 0; k < perRow && start + k < colors.Length; k++)
                {
                    int i = start + k;
                    var r = new Rect(row.x + k * (s + gp), row.y, s, s);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) res = i;
                    if (Event.current.type != EventType.Repaint) continue;
                    GUI.color = Pal.Hex(colors[i]);
                    GUI.DrawTexture(new Rect(r.x + S(3), r.y + S(3), s - S(6), s - S(6)), circleTex);
                    GUI.color = Color.white;
                    GUI.Box(r, GUIContent.none, i == current ? sSwatchOn : sSwatch);
                }
                GUILayout.Space(gp);
            }
            return res;
        }

        // Сетка картинок из библиотеки; onPick == null — только просмотр
        void ImageGrid(string current, float w, Action<string> onPick)
        {
            if (lib.list.Count == 0)
            {
                Note("Библиотека пуста. Добавь картинки во вкладке «Картинки».");
                return;
            }
            float gp = S(6);
            int cols = Mathf.Max(3, Mathf.FloorToInt((w + gp) / (S(64) + gp)));
            float cell = (w - (cols - 1) * gp) / cols, th = cell * 1.4f;
            for (int start = 0; start < lib.list.Count; start += cols)
            {
                var row = GUILayoutUtility.GetRect(w, th, GUILayout.Height(th));
                for (int k = 0; k < cols && start + k < lib.list.Count; k++)
                {
                    var d = lib.list[start + k];
                    var r = new Rect(row.x + k * (cell + gp), row.y, cell, th);
                    if (onPick != null && GUI.Button(r, GUIContent.none, GUIStyle.none))
                    {
                        string name = d.name;
                        Do(() => onPick(name));
                    }
                    if (Event.current.type != EventType.Repaint) continue;
                    GUI.DrawTexture(r, d.thumb, ScaleMode.ScaleAndCrop);
                    if (d.name == current) GUI.Box(r, GUIContent.none, sSwatchOn);
                }
                GUILayout.Space(gp);
            }
        }

        // ---------- панель ----------

        void DrawPanel()
        {
            if (circleTex == null) circleTex = CardArt.RenderRound(Color.white, new Color(1, 1, 1, 0), 32, 16, 0);
            var ev = Event.current;
            float W = Screen.width, H = Screen.height;
            float sw = Mathf.Min(W, S(480));
            var sheet = new Rect(W - sw, 0, sw, H);
            if (ev.type == EventType.Repaint)
            {
                GUI.color = new Color(8 / 255f, 10 / 255f, 9 / 255f, 0.55f);
                GUI.DrawTexture(new Rect(0, 0, W - sw, H), whiteTex);
                GUI.color = Color.white;
            }
            if (ev.type == EventType.MouseDown && !sheet.Contains(ev.mousePosition))
            {
                Do(ClosePanel);
                ev.Use();
                return;
            }
            GUI.Box(sheet, GUIContent.none, sSheet);

            float pad = S(16), innerW = sw - 2 * pad - S(20);
            GUILayout.BeginArea(new Rect(sheet.x + pad, S(12), sw - 2 * pad, H - S(24)));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Колода", sTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", sBtn, GUILayout.Width(S(38)), GUILayout.Height(S(38)))) Do(ClosePanel);
            GUILayout.EndHorizontal();

            // превью
            float pw = Mathf.Min(S(62), (sw - 2 * pad - S(32)) / 5), ph = pw * 1.4f;
            var pr = GUILayoutUtility.GetRect(sw - 2 * pad, ph + S(16), GUILayout.Height(ph + S(16)));
            float px = pr.x + (pr.width - (pw * 5 + S(8) * 4)) / 2;
            for (int i = 0; i < 4; i++) DrawCard(new Rect(px + i * (pw + S(8)), pr.y + S(8), pw, ph), PREVIEW_IDS[i], true);
            DrawCard(new Rect(px + 4 * (pw + S(8)), pr.y + S(8), pw, ph), 0, false);

            // вкладки
            GUILayout.BeginHorizontal();
            foreach (var (key, label) in TABS)
            {
                bool on = tab == key;
                if (GUILayout.Button(label, on ? sTabOn : sTab))
                {
                    string k = key;
                    Do(() => { tab = k; scroll = Vector2.zero; colorOpen = null; panelMsg = null; });
                }
                if (on && ev.type == EventType.Repaint)
                {
                    var lr = GUILayoutUtility.GetLastRect();
                    GUI.color = Pal.Bronze;
                    GUI.DrawTexture(new Rect(lr.x, lr.yMax - S(2), lr.width, S(2)), whiteTex);
                    GUI.color = Color.white;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            var line = GUILayoutUtility.GetRect(sw - 2 * pad, 1, GUILayout.Height(1));
            if (ev.type == EventType.Repaint)
            {
                GUI.color = Pal.SlotLine;
                GUI.DrawTexture(line, whiteTex);
                GUI.color = Color.white;
            }

            scroll = GUILayout.BeginScrollView(scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (!string.IsNullOrEmpty(panelMsg))
            {
                GUILayout.Space(S(10));
                GUILayout.Label(panelMsg, sNote);
            }
            switch (tab)
            {
                case "faces": TabFaces(innerW); break;
                case "pics": TabPics(innerW); break;
                case "cards": TabCards(innerW); break;
                case "back": TabBack(innerW); break;
                default: TabTable(innerW); break;
            }
            GUILayout.Space(S(24));
            GUILayout.EndScrollView();

            GUILayout.Space(S(10));
            GUILayout.BeginHorizontal();
            if (Button("Сбросить оформление"))
                Do(() =>
                {
                    skin = Skin.Defaults(skin.drawMode);
                    MarkAllFaces();
                    backDirty = tableDirty = chromeDirty = true;
                    Changed();
                });
            GUILayout.FlexibleSpace();
            if (Button("Готово", true)) Do(ClosePanel);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        // ---------- вкладка «Карты» ----------

        void TabFaces(float w)
        {
            Lab("Рисунок");
            var art = Seg(skin.art, OPT_ART, w);
            if (art != skin.art) Do(() => SetSkin(() => skin.art = art));
            if (skin.art == "custom")
            {
                Lab("Если у карты нет своей картинки");
                var fb = Seg(skin.fallback, OPT_FALLBACK, w);
                if (fb != skin.fallback) Do(() => SetSkin(() => skin.fallback = fb));
            }
            Lab("Лицо карты");
            var face = Seg(skin.face, OPT_FACE, w);
            if (face != skin.face) Do(() => SetSkin(() => skin.face = face));
            Lab("Масти");
            var suits = Seg(skin.suitColors, OPT_SUITS, w);
            if (suits != skin.suitColors) Do(() => SetSkin(() => skin.suitColors = suits));
            GUILayout.Space(S(8));
            ColorRow("red", "Красные", skin.red, h => SetSkin(() => skin.red = h));
            GUILayout.Space(S(6));
            ColorRow("black", "Чёрные", skin.black, h => SetSkin(() => skin.black = h));
            Lab("Шрифт значка в углу");
            var font = Seg(skin.font, OPT_FONT, w);
            if (font != skin.font) Do(() => SetSkin(() => skin.font = font, false));
            Lab("Размер значка");
            float cs = SliderRow("Масштаб", skin.cornerScale, 0.7f, 1.4f, Mathf.RoundToInt(skin.cornerScale * 100) + "%");
            if (!Mathf.Approximately(cs, skin.cornerScale)) SetSkin(() => skin.cornerScale = Mathf.Round(cs * 20) / 20, false);
            Lab("Скругление углов");
            float rad = SliderRow("Радиус", skin.radius, 0, 20, Mathf.RoundToInt(skin.radius) + "%");
            if (Mathf.Round(rad) != skin.radius) SetSkin(() => skin.radius = Mathf.Round(rad), true, true, false, true);
            GUILayout.Space(S(12));
            bool frame = GUILayout.Toggle(skin.courtFrame, "  Золотая рамка у J, Q, K");
            if (frame != skin.courtFrame) Do(() => SetSkin(() => skin.courtFrame = frame));
        }

        // ---------- вкладка «Картинки» ----------

        void TabPics(float w)
        {
            Note("Положи картинки PNG или JPG в папку «Картинки» рядом с игрой и нажми «Обновить». Картинки никуда не отправляются.");
            Note("Папка: " + lib.Dir);
            GUILayout.Space(S(8));
            GUILayout.BeginHorizontal();
            if (Button("Открыть папку", true)) Application.OpenURL("file:///" + lib.Dir.Replace('\\', '/'));
            GUILayout.Space(S(6));
            if (Button("Обновить")) Do(ReloadImages);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (lib.Failed > 0) Note("Не получилось открыть файлов: " + lib.Failed + ". Подходят только PNG и JPG.");

            Lab("Библиотека (" + lib.list.Count + ")");
            ImageGrid(null, w, null);

            Lab("Разложить по колоде");
            int deal = Flow(new[] { "По порядку", "Вперемешку", "По рангам", "По мастям", "Убрать со всех" }, new bool[5], w);
            if (deal >= 0)
            {
                string mode = new[] { "order", "shuffle", "rank", "suit", "clear" }[deal];
                Do(() =>
                {
                    if (mode != "clear" && lib.list.Count == 0) { panelMsg = "Сначала добавь картинки в папку и нажми «Обновить»."; return; }
                    DealImages(mode);
                });
            }
            Note("Поставить картинку на конкретную карту, приблизить её или сдвинуть кадр можно во вкладке «По картам».");

            Lab("Как вписать");
            var fit = Seg(skin.fit, OPT_FIT, w);
            if (fit != skin.fit) Do(() => SetSkin(() => skin.fit = fit));
            Lab("Где на карте");
            var area = Seg(skin.area, OPT_AREA, w);
            if (area != skin.area) Do(() => SetSkin(() => skin.area = area));
            Lab("Какую часть показывать");
            var focus = Seg(skin.focus, OPT_FOCUS, w);
            if (focus != skin.focus) Do(() => SetSkin(() => skin.focus = focus));
        }

        // ---------- вкладка «Рубашка» ----------

        void TabBack(float w)
        {
            Lab("Узор");
            var back = Seg(skin.back, OPT_BACK, w);
            if (back != skin.back) Do(() => SetSkin(() => skin.back = back, false, true));
            if (skin.back == "image")
            {
                Lab("Картинка для рубашки");
                ImageGrid(skin.backImage, w, name => SetSkin(() => skin.backImage = name, false, true));
            }
            else
            {
                GUILayout.Space(S(12));
                ColorRow("backColor", "Фон", skin.backColor, h => SetSkin(() => skin.backColor = h, false, true));
                if (skin.back != "plain")
                {
                    GUILayout.Space(S(6));
                    ColorRow("backInk", "Узор", skin.backInk, h => SetSkin(() => skin.backInk = h, false, true));
                }
            }
        }

        // ---------- вкладка «Стол» ----------

        void TabTable(float w)
        {
            Lab("Покрытие");
            var t = Seg(skin.table, OPT_TABLE, w);
            if (t != skin.table) Do(() => SetSkin(() => skin.table = t, false, false, true));
            if (skin.table == "color")
            {
                GUILayout.Space(S(12));
                ColorRow("tableColor", "Цвет стола", skin.tableColor, h => SetSkin(() => skin.tableColor = h, false, false, true));
            }
            if (skin.table == "image")
            {
                Lab("Картинка для стола");
                ImageGrid(skin.tableImage, w, name => SetSkin(() => skin.tableImage = name, false, false, true));
            }
        }

        // ---------- вкладка «По картам» ----------

        void ChipClick(int id)
        {
            if (multi)
            {
                if (sel.Contains(id)) sel.Remove(id);
                else sel.Add(id);
            }
            else if (sel.Count == 1 && sel[0] == id) sel.Clear();
            else
            {
                sel.Clear();
                sel.Add(id);
            }
            colorOpen = null;
        }

        void SuitClick(int suit)
        {
            multi = true;
            bool all = true;
            for (int r = 0; r < 13; r++) if (!sel.Contains(suit * 13 + r)) all = false;
            for (int r = 0; r < 13; r++)
            {
                int id = suit * 13 + r;
                if (all) sel.Remove(id);
                else if (!sel.Contains(id)) sel.Add(id);
            }
        }

        void Chips(float w)
        {
            float suitW = S(26), gp = S(3);
            float cellW = (w - suitW - 13 * gp) / 13, cellH = cellW * 1.4f;
            var ev = Event.current;
            for (int s = 0; s < 4; s++)
            {
                var row = GUILayoutUtility.GetRect(w, cellH, GUILayout.Height(cellH));
                var sr = new Rect(row.x, row.y + (cellH - suitW) / 2, suitW, suitW);
                int suit = s;
                if (GUI.Button(sr, GUIContent.none, sBtn)) Do(() => SuitClick(suit));
                if (ev.type == EventType.Repaint)
                {
                    GUI.color = s < 2 ? Pal.Hex("#e0475e") : Pal.Chalk;
                    GUI.DrawTexture(new Rect(sr.x + S(5), sr.y + S(5), sr.width - S(10), sr.height - S(10)), suitTex[s]);
                    GUI.color = Color.white;
                }
                for (int r = 0; r < 13; r++)
                {
                    int id = s * 13 + r;
                    var cr = new Rect(row.x + suitW + gp + r * (cellW + gp), row.y, cellW, cellH);
                    if (GUI.Button(cr, GUIContent.none, GUIStyle.none)) Do(() => ChipClick(id));
                    if (ev.type != EventType.Repaint) continue;
                    var img = CardArt.ImgFor(skin, lib, id);
                    if (img != null) GUI.DrawTexture(cr, img.thumb, ScaleMode.ScaleAndCrop);
                    else
                    {
                        GUI.color = Pal.Hex("#f3ecdc");
                        GUI.DrawTexture(cr, plateTex);
                        GUI.color = Color.white;
                        sChip.normal.textColor = s < 2 ? Pal.Hex("#c8142f") : Pal.Ink;
                        GUI.Label(cr, CardArt.RANKS[r + 1], sChip);
                    }
                    if (!skin.cards[id].IsEmpty)
                    {
                        float d = Mathf.Max(5, cellW * 0.28f);
                        GUI.color = Pal.Ink;
                        GUI.DrawTexture(new Rect(cr.xMax - d - 1, cr.y + 1, d, d), circleTex);
                        GUI.color = Pal.BronzeHi;
                        GUI.DrawTexture(new Rect(cr.xMax - d, cr.y + 2, d - 2, d - 2), circleTex);
                        GUI.color = Color.white;
                    }
                    if (sel.Contains(id)) GUI.Box(cr, GUIContent.none, sSwatchOn);
                }
                GUILayout.Space(gp);
            }
        }

        void TabCards(float w)
        {
            Lab("Какие карты настраиваем");
            Chips(w);
            GUILayout.Space(S(8));
            bool m = GUILayout.Toggle(multi, "  Выбирать несколько");
            if (m != multi) Do(() => multi = m);
            GUILayout.Space(S(8));
            GUILayout.BeginHorizontal();
            if (Button("Выбрать все")) Do(() => { multi = true; sel.Clear(); for (int i = 0; i < 52; i++) sel.Add(i); });
            GUILayout.Space(S(6));
            if (Button("Снять выбор")) Do(() => sel.Clear());
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Note(sel.Count > 1
                ? "Выбрано карт: " + sel.Count + ". Изменения ниже ставятся на все выбранные."
                : "Нажми на карту в сетке. Значок масти слева выделяет всю масть. Точкой отмечены карты со своими настройками. На столе карту можно открыть долгим нажатием или правым кликом.");
            if (sel.Count > 0) CardEditor(w);
        }

        void CardEditor(float w)
        {
            int id = sel[0];
            int suit = id / 13, rank = id % 13 + 1;
            var ov = skin.cards[id];
            var look = CardArt.LookOf(skin, id);
            string kind = CardArt.Kind(skin, lib, id);

            GUILayout.Space(S(14));
            var line = GUILayoutUtility.GetRect(w, 1, GUILayout.Height(1));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = Pal.SlotLine;
                GUI.DrawTexture(line, whiteTex);
                GUI.color = Color.white;
            }
            GUILayout.Space(S(14));
            GUILayout.BeginHorizontal();
            float pw = S(92);
            var pr = GUILayoutUtility.GetRect(pw, pw * 1.4f, GUILayout.Width(pw), GUILayout.Height(pw * 1.4f));
            DrawCard(pr, id, true);
            GUILayout.Space(S(14));
            GUILayout.BeginVertical();
            GUILayout.Space(S(20));
            GUILayout.Label(RANK_FULL[rank] + " " + SUIT_GEN[suit], sTitle);
            GUILayout.Label(sel.Count > 1 ? "и ещё " + (sel.Count - 1) + ". Изменения ставятся на все выбранные карты." : "Эти настройки только для этой карты.", sNote, GUILayout.Width(w - pw - S(20)));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            Lab("Рисунок");
            var art = Seg(ov.art, E_ART, w);
            if (art != ov.art)
            {
                if (art == "image" && lib.list.Count == 0) Do(() => panelMsg = "В библиотеке нет картинок. Добавь их во вкладке «Картинки».");
                SetOv(o => o.art = art);
            }

            string chosen = ov.art != "" ? ov.art : (skin.art == "custom" ? "image" : skin.art);
            if (chosen == "image" || kind == "image")
            {
                Lab("Картинка");
                var cur = CardArt.ImgFor(skin, lib, id);
                ImageGrid(cur?.name, w, name => SetOvNow(o => { o.img = name; o.art = "image"; }));
                Lab("Как вписать");
                var fit = Seg(ov.fit, E_FIT, w);
                if (fit != ov.fit) SetOv(o => o.fit = fit);
                Lab("Где на карте");
                var area = Seg(ov.area, E_AREA, w);
                if (area != ov.area) SetOv(o => o.area = area);
                Lab("Кадр");
                float zoom = ov.zoom;
                float pxv = ov.posX >= 0 ? ov.posX : 50;
                float pyv = ov.posY >= 0 ? ov.posY : (skin.focus == "top" ? 0 : skin.focus == "bottom" ? 100 : 50);
                float nz = Mathf.Round(SliderRow("Масштаб", zoom, 100, 300, Mathf.RoundToInt(zoom) + "%") / 5) * 5;
                if (!Mathf.Approximately(nz, zoom)) SetOvNow(o => o.zoom = nz);
                float nx = Mathf.Round(SliderRow("Влево–вправо", pxv, 0, 100, Mathf.RoundToInt(pxv) + "%"));
                if (!Mathf.Approximately(nx, pxv)) SetOvNow(o => o.posX = nx);
                float ny = Mathf.Round(SliderRow("Вверх–вниз", pyv, 0, 100, Mathf.RoundToInt(pyv) + "%"));
                if (!Mathf.Approximately(ny, pyv)) SetOvNow(o => o.posY = ny);
            }

            if (kind == "buff")
            {
                Lab("Поза");
                var pose = Seg(look.pose, E_POSE, w);
                if (pose != look.pose) SetLook(L => L.pose = pose);
                Lab("Причёска");
                var hair = Seg(look.hair, E_HAIR, w);
                if (hair != look.hair) SetLook(L => L.hair = hair);
                Lab("Кожа");
                var skins = new string[CardArt.SKINS.Length];
                for (int i = 0; i < skins.Length; i++) skins[i] = CardArt.SKINS[i][0];
                int sk = Swatches(skins, look.skin, w);
                if (sk >= 0) SetLook(L => L.skin = sk);
                Lab("Цвет волос");
                int hc = Swatches(CardArt.HAIRS, Array.IndexOf(CardArt.HAIRS, look.hairColor), w);
                if (hc >= 0) SetLook(L => L.hairColor = CardArt.HAIRS[hc]);
                GUILayout.Space(S(8));
                ColorRow("trunks", "Плавки", look.trunks, h => SetLookNow(L => L.trunks = h));
                Lab("Аксессуары");
                var labels = new string[CardArt.EXTRAS.Length];
                var on = new bool[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    labels[i] = CardArt.EXTRAS[i].label;
                    on[i] = look.Get(CardArt.EXTRAS[i].key);
                }
                int t = Flow(labels, on, w);
                if (t >= 0)
                {
                    string key = CardArt.EXTRAS[t].key;
                    bool val = !on[t];
                    SetLook(L => L.Set(key, val));
                }
            }

            Lab("Фон карты");
            var face = Seg(ov.face, E_FACE, w);
            if (face != ov.face) SetOv(o => o.face = face);
            if (ov.face == "color")
            {
                GUILayout.Space(S(8));
                ColorRow("faceColor", "Свой цвет фона", ov.faceColor, h => SetOvNow(o => o.faceColor = h));
            }
            GUILayout.Space(S(14));
            bool hasColor = GUILayout.Toggle(ov.hasColor, "  Свой цвет масти");
            if (hasColor != ov.hasColor)
            {
                string start = Pal.ToHex(CardArt.SuitColor(skin, suit, false));
                SetOv(o => { if (hasColor && !o.hasColor) o.color = start; o.hasColor = hasColor; });
            }
            if (ov.hasColor)
            {
                GUILayout.Space(S(8));
                ColorRow("suitColor", "Цвет масти", ov.color, h => SetOvNow(o => o.color = h));
            }
            Lab("Подпись внизу карты");
            string cap = GUILayout.TextField(ov.caption ?? "", 24, sField);
            if (cap != (ov.caption ?? "")) SetOvNow(o => o.caption = cap);
            GUILayout.Space(S(16));
            if (Button("Сбросить выбранные карты"))
                Do(() =>
                {
                    foreach (int i in sel) skin.cards[i] = new CardOv();
                    MarkFaces(sel);
                    Changed();
                });
        }
    }
}
