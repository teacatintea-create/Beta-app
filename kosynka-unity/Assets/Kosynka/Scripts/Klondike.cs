using System.Collections.Generic;

namespace Kosynka
{
    public enum Pile { Stock, Waste, Found, Tab }

    public struct Loc
    {
        public Pile pile;
        public int idx, pos;
    }

    // Правила «Косынки» (Клондайк). Карта id: масть = id / 13 (♥ ♦ ♠ ♣), ранг = id % 13 + 1
    public class Klondike
    {
        public List<int> stock = new List<int>();
        public List<int> waste = new List<int>();
        public readonly List<int>[] found = new List<int>[4];
        public readonly List<int>[] tab = new List<int>[7];
        public readonly bool[] up = new bool[52];
        public int moves;

        class Snap
        {
            public int[][] piles;
            public bool[] up;
            public int moves;
        }

        readonly List<Snap> history = new List<Snap>();

        public Klondike()
        {
            for (int i = 0; i < 4; i++) found[i] = new List<int>();
            for (int i = 0; i < 7; i++) tab[i] = new List<int>();
        }

        public static int Suit(int id) => id / 13;
        public static int Rank(int id) => id % 13 + 1;
        public static bool Red(int id) => id / 13 < 2;

        public bool CanUndo => history.Count > 0;

        public bool Won
        {
            get
            {
                foreach (var f in found) if (f.Count != 13) return false;
                return true;
            }
        }

        public bool AutoReady
        {
            get
            {
                if (stock.Count > 0 || waste.Count > 0) return false;
                bool any = false;
                foreach (var t in tab)
                    foreach (int id in t)
                    {
                        if (!up[id]) return false;
                        any = true;
                    }
                return any;
            }
        }

        public void Deal(System.Random rnd)
        {
            var ids = new List<int>(52);
            for (int i = 0; i < 52; i++) ids.Add(i);
            for (int i = ids.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }
            stock.Clear();
            waste.Clear();
            foreach (var f in found) f.Clear();
            foreach (var t in tab) t.Clear();
            for (int i = 0; i < 52; i++) up[i] = false;
            int k = 0;
            for (int t = 0; t < 7; t++)
                for (int i = 0; i <= t; i++)
                {
                    int id = ids[k++];
                    tab[t].Add(id);
                    if (i == t) up[id] = true;
                }
            for (; k < 52; k++) stock.Add(ids[k]);
            moves = 0;
            history.Clear();
        }

        public List<int> Get(Pile p, int idx)
        {
            switch (p)
            {
                case Pile.Stock: return stock;
                case Pile.Waste: return waste;
                case Pile.Found: return found[idx];
                default: return tab[idx];
            }
        }

        public List<int> Get(Loc l) => Get(l.pile, l.idx);

        public Loc Locate(int id)
        {
            int i = stock.IndexOf(id);
            if (i >= 0) return new Loc { pile = Pile.Stock, pos = i };
            i = waste.IndexOf(id);
            if (i >= 0) return new Loc { pile = Pile.Waste, pos = i };
            for (int f = 0; f < 4; f++)
            {
                i = found[f].IndexOf(id);
                if (i >= 0) return new Loc { pile = Pile.Found, idx = f, pos = i };
            }
            for (int t = 0; t < 7; t++)
            {
                i = tab[t].IndexOf(id);
                if (i >= 0) return new Loc { pile = Pile.Tab, idx = t, pos = i };
            }
            return new Loc { pile = Pile.Stock, pos = -1 };
        }

        public static int Top(List<int> l) => l.Count > 0 ? l[l.Count - 1] : -1;

        public bool CanDrop(List<int> ids, Pile p, int idx)
        {
            int c = ids[0];
            if (p == Pile.Found)
            {
                if (ids.Count != 1) return false;
                int t = Top(found[idx]);
                return t >= 0 ? Suit(t) == Suit(c) && Rank(t) == Rank(c) - 1 : Rank(c) == 1;
            }
            if (p != Pile.Tab) return false;
            int top = Top(tab[idx]);
            return top >= 0 ? up[top] && Red(top) != Red(c) && Rank(top) == Rank(c) + 1 : Rank(c) == 13;
        }

        public void Move(List<int> ids, Loc from, Pile p, int idx, bool record = true)
        {
            if (record) Record();
            var moving = new List<int>(ids);
            var src = Get(from);
            src.RemoveRange(src.Count - moving.Count, moving.Count);
            Get(p, idx).AddRange(moving);
            if (from.pile == Pile.Tab)
            {
                int t = Top(src);
                if (t >= 0) up[t] = true;
            }
            moves++;
        }

        // Возвращает взятые из колоды карты (пусто, если колода перевёрнута обратно)
        public List<int> Draw(int n)
        {
            var drawn = new List<int>();
            if (stock.Count == 0 && waste.Count == 0) return null;
            Record();
            if (stock.Count == 0)
            {
                waste.Reverse();
                stock = waste;
                waste = new List<int>();
                foreach (int id in stock) up[id] = false;
            }
            else
            {
                for (int i = 0; i < n && stock.Count > 0; i++)
                {
                    int id = stock[stock.Count - 1];
                    stock.RemoveAt(stock.Count - 1);
                    up[id] = true;
                    waste.Add(id);
                    drawn.Add(id);
                }
            }
            moves++;
            return drawn;
        }

        void Record()
        {
            var s = new Snap { piles = new int[13][], up = (bool[])up.Clone(), moves = moves };
            s.piles[0] = stock.ToArray();
            s.piles[1] = waste.ToArray();
            for (int i = 0; i < 4; i++) s.piles[2 + i] = found[i].ToArray();
            for (int i = 0; i < 7; i++) s.piles[6 + i] = tab[i].ToArray();
            history.Add(s);
            if (history.Count > 400) history.RemoveAt(0);
        }

        public bool Undo()
        {
            if (history.Count == 0) return false;
            var s = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            stock = new List<int>(s.piles[0]);
            waste = new List<int>(s.piles[1]);
            for (int i = 0; i < 4; i++) { found[i].Clear(); found[i].AddRange(s.piles[2 + i]); }
            for (int i = 0; i < 7; i++) { tab[i].Clear(); tab[i].AddRange(s.piles[6 + i]); }
            System.Array.Copy(s.up, up, 52);
            moves = s.moves;
            return true;
        }
    }
}
