using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace RetroSkin
{
    /// <summary>
    /// Запоминает исходные значения реестра (HKCU) перед первым изменением,
    /// чтобы кнопка «Вернуть как было» могла всё откатить.
    /// Повторное применение не затирает уже сохранённый оригинал.
    /// </summary>
    sealed class RegistryBackup
    {
        enum EntryType { Value, MissingValue, MissingKey }

        sealed class Entry
        {
            public EntryType Type;
            public string Key;
            public string Name;
            public RegistryValueKind Kind;
            public object Data;
        }

        public static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroSkin");

        static string FilePath => Path.Combine(Folder, "backup.txt");

        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static bool Exists => File.Exists(FilePath);

        public bool IsEmpty => _entries.Count == 0;

        public static RegistryBackup Load()
        {
            var backup = new RegistryBackup();
            if (!Exists) return backup;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var p = line.Split('\t');
                if (p.Length != 5) continue;
                var e = new Entry
                {
                    Type = (EntryType)Enum.Parse(typeof(EntryType), p[0]),
                    Key = Decode(p[1]),
                    Name = Decode(p[2]),
                    Kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), p[3]),
                };
                e.Data = DeserializeData(e.Kind, p[4]);
                backup._entries[Id(e.Type, e.Key, e.Name)] = e;
            }
            return backup;
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(FilePath, _entries.Values.Select(e => string.Join("\t",
                e.Type.ToString(), Encode(e.Key), Encode(e.Name ?? ""), e.Kind.ToString(), SerializeData(e.Kind, e.Data))));
        }

        public static void Delete()
        {
            if (Exists) File.Delete(FilePath);
        }

        /// <summary>Запомнить текущее значение (или его отсутствие).</summary>
        public void RememberValue(string key, string name)
        {
            if (_entries.ContainsKey(Id(EntryType.Value, key, name))) return;
            using (var k = Registry.CurrentUser.OpenSubKey(key))
            {
                object data = k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var e = data == null
                    ? new Entry { Type = EntryType.MissingValue, Key = key, Name = name, Kind = RegistryValueKind.Unknown }
                    : new Entry { Type = EntryType.Value, Key = key, Name = name, Kind = k.GetValueKind(name), Data = data };
                _entries[Id(e.Type, key, name)] = e;
            }
        }

        /// <summary>
        /// Запомнить самый верхний из отсутствующих разделов на пути к <paramref name="key"/>,
        /// чтобы при откате удалить его целиком вместе с вложенными.
        /// </summary>
        public void RememberKey(string key)
        {
            var parts = key.Split('\\');
            for (int i = 1; i <= parts.Length; i++)
            {
                string prefix = string.Join("\\", parts, 0, i);
                if (_entries.ContainsKey(Id(EntryType.MissingKey, prefix, null))) return;
                using (var k = Registry.CurrentUser.OpenSubKey(prefix))
                {
                    if (k != null) continue;
                }
                _entries[Id(EntryType.MissingKey, prefix, null)] =
                    new Entry { Type = EntryType.MissingKey, Key = prefix, Kind = RegistryValueKind.Unknown };
                return;
            }
        }

        public void RestoreAll(Action<string> log)
        {
            foreach (var e in _entries.Values.Where(e => e.Type != EntryType.MissingKey))
            {
                try
                {
                    if (e.Type == EntryType.Value)
                    {
                        using (var k = Registry.CurrentUser.CreateSubKey(e.Key))
                            k.SetValue(e.Name, e.Data, e.Kind);
                    }
                    else
                    {
                        using (var k = Registry.CurrentUser.OpenSubKey(e.Key, true))
                            k?.DeleteValue(e.Name, false);
                    }
                }
                catch (Exception ex)
                {
                    log($"  ! {e.Key}\\{e.Name}: {ex.Message}");
                }
            }

            // Сначала самые глубокие разделы
            foreach (var e in _entries.Values.Where(e => e.Type == EntryType.MissingKey).OrderByDescending(e => e.Key.Length))
            {
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(e.Key, false);
                }
                catch (Exception ex)
                {
                    log($"  ! {e.Key}: {ex.Message}");
                }
            }
        }

        /// <summary>Исходное значение, если оно было сохранено (нужно, например, для пути к обоям).</summary>
        public object GetOriginal(string key, string name) =>
            _entries.TryGetValue(Id(EntryType.Value, key, name), out var e) ? e.Data : null;

        static string Id(EntryType type, string key, string name) =>
            (type == EntryType.MissingKey ? "K|" : "V|") + key + "|" + (name ?? "");

        static string Encode(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        static string Decode(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));

        static string SerializeData(RegistryValueKind kind, object data)
        {
            switch (kind)
            {
                case RegistryValueKind.Unknown: return "";
                case RegistryValueKind.Binary: return Convert.ToBase64String((byte[])data);
                case RegistryValueKind.MultiString: return Encode(string.Join("\0", (string[])data));
                case RegistryValueKind.DWord: return ((int)data).ToString();
                case RegistryValueKind.QWord: return ((long)data).ToString();
                default: return Encode(Convert.ToString(data));
            }
        }

        static object DeserializeData(RegistryValueKind kind, string s)
        {
            switch (kind)
            {
                case RegistryValueKind.Unknown: return null;
                case RegistryValueKind.Binary: return Convert.FromBase64String(s);
                case RegistryValueKind.MultiString: return Decode(s).Split('\0');
                case RegistryValueKind.DWord: return int.Parse(s);
                case RegistryValueKind.QWord: return long.Parse(s);
                default: return Decode(s);
            }
        }
    }
}
