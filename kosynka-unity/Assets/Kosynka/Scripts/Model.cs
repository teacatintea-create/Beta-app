using System;
using System.IO;
using UnityEngine;

namespace Kosynka
{
    // Внешность качка на карте
    [Serializable]
    public class Look
    {
        public string pose = "double";
        public string hair = "buzz";
        public int skin;
        public string hairColor = "#24160d";
        public string trunks = "#d3203a";
        public bool mus, shades, beard, band, crown, tiara, belt, cape, gold;

        public Look Clone() => (Look)MemberwiseClone();

        public bool Get(string k)
        {
            switch (k)
            {
                case "mus": return mus;
                case "shades": return shades;
                case "beard": return beard;
                case "band": return band;
                case "crown": return crown;
                case "tiara": return tiara;
                case "belt": return belt;
                case "cape": return cape;
                case "gold": return gold;
            }
            return false;
        }

        public void Set(string k, bool v)
        {
            switch (k)
            {
                case "mus": mus = v; break;
                case "shades": shades = v; break;
                case "beard": beard = v; break;
                case "band": band = v; break;
                case "crown": crown = v; break;
                case "tiara": tiara = v; break;
                case "belt": belt = v; break;
                case "cape": cape = v; break;
                case "gold": gold = v; break;
            }
        }
    }

    // Личные настройки одной карты. Пустая строка или -1 — «как у колоды»
    [Serializable]
    public class CardOv
    {
        public string art = "";
        public string img = "";
        public string fit = "";
        public string area = "";
        public float zoom = 100;
        public float posX = -1;
        public float posY = -1;
        public string face = "";
        public string faceColor = "#fbf6ec";
        public bool hasColor;
        public string color = "#c8142f";
        public string caption = "";
        public bool hasLook;
        public Look look = new Look();

        public bool IsEmpty =>
            art == "" && img == "" && fit == "" && area == "" && Mathf.Approximately(zoom, 100) && posX < 0 && posY < 0 &&
            face == "" && !hasColor && caption == "" && !hasLook;
    }

    // Оформление всей колоды, стола и рубашки
    [Serializable]
    public class Skin
    {
        public string art = "buff";
        public string fallback = "buff";
        public string face = "cream";
        public string suitColors = "two";
        public string red = "#c8142f";
        public string black = "#17191b";
        public string font = "russo";
        public float cornerScale = 1f;
        public float radius = 8f;
        public bool courtFrame = true;
        public string fit = "cover";
        public string area = "below";
        public string focus = "center";
        public string back = "dumbbell";
        public string backColor = "#5a1a20";
        public string backInk = "#d9963a";
        public string backImage = "";
        public string table = "rubber";
        public string tableColor = "#2b4a3a";
        public string tableImage = "";
        public int drawMode = 1;
        public string[] assign = new string[52];
        public CardOv[] cards = new CardOv[52];

        public void Normalize()
        {
            if (assign == null || assign.Length != 52) Array.Resize(ref assign, 52);
            if (cards == null || cards.Length != 52) Array.Resize(ref cards, 52);
            for (int i = 0; i < 52; i++)
            {
                if (assign[i] == null) assign[i] = "";
                if (cards[i] == null) cards[i] = new CardOv();
                if (cards[i].look == null) cards[i].look = new Look();
            }
            if (drawMode != 3) drawMode = 1;
        }

        static string FilePath => Path.Combine(Application.persistentDataPath, "kosynka-skin.json");

        public static Skin Load()
        {
            var s = new Skin();
            try
            {
                if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), s);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Не удалось прочитать настройки: " + e.Message);
            }
            s.Normalize();
            return s;
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(this)); }
            catch (Exception e) { Debug.LogWarning("Не удалось сохранить настройки: " + e.Message); }
        }

        // Сброс оформления: играть по 1 или по 3 карты — не оформление, его сохраняем
        public static Skin Defaults(int drawMode)
        {
            var s = new Skin { drawMode = drawMode };
            s.Normalize();
            return s;
        }
    }
}
