using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace RetroSkin
{
    enum Theme { Win95, Win7 }

    sealed class Tweak
    {
        public string Title;
        public string Hint;
        public Func<Theme, bool> DefaultOn;
        public Action<TweakContext> Apply;
    }

    sealed class TweakContext
    {
        public Theme Theme;
        public RegistryBackup Backup;
        public Action<string> Log;

        /// <summary>Пишет значение в HKCU, предварительно сохранив оригинал.</summary>
        public bool Set(string key, string name, object value, RegistryValueKind kind = RegistryValueKind.DWord)
        {
            Backup.RememberKey(key);
            Backup.RememberValue(key, name);
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(key))
                    k.SetValue(name, value, kind);
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                Log($"  · {name}: Windows не разрешает менять это значение, пропускаю");
                return false;
            }
        }
    }

    static class Tweaks
    {
        const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string Dwm = @"Software\Microsoft\Windows\DWM";
        const string Accent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        const string Desktop = @"Control Panel\Desktop";
        const string DesktopIcons = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
        const string ClassicMenuKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        const string OpenShellSettings = @"Software\OpenShell\StartMenu\Settings";

        public static readonly Tweak[] All =
        {
            new Tweak
            {
                Title = "Классическое меню «Пуск» (Open-Shell)",
                Hint = "Устанавливает бесплатный Open-Shell через winget и включает стиль меню:\n" +
                       "Windows 95 — классическое меню в один столбец, Windows 7 — двухколоночное меню с поиском.",
                DefaultOn = _ => true,
                Apply = ApplyOpenShell,
            },
            new Tweak
            {
                Title = "Панель задач слева, как раньше",
                Hint = "Кнопка «Пуск» и значки прижаты к левому краю.",
                DefaultOn = _ => true,
                Apply = c => c.Set(Advanced, "TaskbarAl", 0),
            },
            new Tweak
            {
                Title = "Кнопки панели задач с подписями",
                Hint = "Окна не группируются, у каждой кнопки есть название — как в Windows 95.",
                DefaultOn = t => t == Theme.Win95,
                Apply = c =>
                {
                    c.Set(Advanced, "TaskbarGlomLevel", 2);
                    c.Set(Advanced, "MMTaskbarGlomLevel", 2);
                },
            },
            new Tweak
            {
                Title = "Убрать виджеты, поиск и «Представление задач»",
                Hint = "Скрывает новые кнопки, которых не было в старых Windows.",
                DefaultOn = _ => true,
                Apply = c =>
                {
                    c.Set(Advanced, "ShowTaskViewButton", 0);
                    c.Set(Advanced, "TaskbarDa", 0);
                    c.Set(Advanced, "TaskbarMn", 0);
                    c.Set(@"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0);
                },
            },
            new Tweak
            {
                Title = "Старое контекстное меню (правый клик)",
                Hint = "Сразу полное меню без пункта «Показать дополнительные параметры».",
                DefaultOn = _ => true,
                Apply = c => c.Set(ClassicMenuKey + @"\InprocServer32", "", "", RegistryValueKind.String),
            },
            new Tweak
            {
                Title = "Цвета окон и панели задач",
                Hint = "Windows 95 — тёмно-синие заголовки, серые неактивные окна, без прозрачности.\n" +
                       "Windows 7 — голубые заголовки и тёмная полупрозрачная панель задач.",
                DefaultOn = _ => true,
                Apply = ApplyColors,
            },
            new Tweak
            {
                Title = "Обои рабочего стола",
                Hint = "Windows 95 — бирюзовый фон, Windows 7 — синий фон со световыми лентами.",
                DefaultOn = _ => true,
                Apply = ApplyWallpaper,
            },
            new Tweak
            {
                Title = "Значки «Компьютер», «Корзина», «Сеть» на рабочем столе",
                Hint = "Для Windows 7 ещё и папка пользователя.",
                DefaultOn = _ => true,
                Apply = c =>
                {
                    c.Set(DesktopIcons, "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0); // Этот компьютер
                    c.Set(DesktopIcons, "{645FF040-5081-101B-9F08-00AA002F954E}", 0); // Корзина
                    c.Set(DesktopIcons, "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", 0); // Сеть
                    if (c.Theme == Theme.Win7)
                        c.Set(DesktopIcons, "{59031a47-3f72-44a7-89c5-5595fe6b30ee}", 0); // Папка пользователя
                },
            },
            new Tweak
            {
                Title = "Проводник открывается на «Этот компьютер»",
                Hint = "Вместо «Главной» с рекомендациями.",
                DefaultOn = _ => true,
                Apply = c => c.Set(Advanced, "LaunchTo", 1),
            },
            new Tweak
            {
                Title = "Отключить анимации окон",
                Hint = "Окна открываются и сворачиваются мгновенно, как в Windows 95.",
                DefaultOn = t => t == Theme.Win95,
                Apply = c =>
                {
                    c.Set(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", RegistryValueKind.String);
                    c.Set(Advanced, "TaskbarAnimations", 0);
                    Native.SetAnimations(false);
                },
            },
            new Tweak
            {
                Title = "Панель задач в стиле Windows 10/7 (ExplorerPatcher)",
                Hint = "Устанавливает ExplorerPatcher через winget. После установки: правый клик по панели задач →\n" +
                       "«Свойства» → Taskbar style → Windows 10. Это возвращает маленькие значки и старый трей.\n" +
                       "Некоторые антивирусы ругаются на эту программу — ставьте на своё усмотрение.",
                DefaultOn = _ => false,
                Apply = c => WingetInstall(c, "valinet.ExplorerPatcher", "ExplorerPatcher"),
            },
        };

        // ---------------- Цвета ----------------

        static void ApplyColors(TweakContext c)
        {
            bool is95 = c.Theme == Theme.Win95;
            Color accent = is95 ? Artwork.Win95Navy : Color.FromArgb(58, 110, 175);
            Color inactive = is95 ? Color.FromArgb(128, 128, 128) : Color.FromArgb(160, 185, 215);

            // Win95: всё светлое, как серый интерфейс; Win7: тёмная «стеклянная» панель задач
            c.Set(Personalize, "AppsUseLightTheme", 1);
            c.Set(Personalize, "SystemUsesLightTheme", is95 ? 1 : 0);
            c.Set(Personalize, "EnableTransparency", is95 ? 0 : 1);
            c.Set(Personalize, "ColorPrevalence", is95 ? 0 : 1);
            c.Set(Desktop, "AutoColorization", "0", RegistryValueKind.String);

            var palette = BuildPalette(accent);
            byte[] bytes = palette.SelectMany(p => new[] { p.R, p.G, p.B, (byte)0 }).ToArray();
            c.Set(Accent, "AccentPalette", bytes, RegistryValueKind.Binary);
            c.Set(Accent, "AccentColorMenu", Abgr(palette[3]));
            c.Set(Accent, "StartColorMenu", Abgr(palette[4]));

            c.Set(Dwm, "AccentColor", Abgr(accent));
            c.Set(Dwm, "AccentColorInactive", Abgr(inactive));
            c.Set(Dwm, "ColorizationColor", Argb(accent, 0xC4));
            c.Set(Dwm, "ColorizationAfterglow", Argb(accent, 0xC4));
            c.Set(Dwm, "ColorPrevalence", 1);
            c.Set(Dwm, "EnableWindowColorization", 1);
        }

        /// <summary>Палитра акцента, как её хранит Windows: 3 светлых, базовый, 3 тёмных и контрастный.</summary>
        static Color[] BuildPalette(Color a) => new[]
        {
            Mix(a, Color.White, 0.6), Mix(a, Color.White, 0.4), Mix(a, Color.White, 0.2), a,
            Mix(a, Color.Black, 0.25), Mix(a, Color.Black, 0.45), Mix(a, Color.Black, 0.65),
            Color.FromArgb(255 - a.R, 255 - a.G, 255 - a.B),
        };

        static Color Mix(Color a, Color b, double t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        static int Abgr(Color c) => unchecked((int)(0xFF000000u | (uint)c.B << 16 | (uint)c.G << 8 | c.R));
        static int Argb(Color c, byte alpha) => unchecked((int)((uint)alpha << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B));

        // ---------------- Обои ----------------

        static void ApplyWallpaper(TweakContext c)
        {
            string path = Artwork.CreateWallpaper(c.Theme);
            c.Set(Desktop, "WallpaperStyle", c.Theme == Theme.Win95 ? "2" : "10", RegistryValueKind.String);
            c.Set(Desktop, "TileWallpaper", "0", RegistryValueKind.String);
            c.Set(Desktop, "Wallpaper", path, RegistryValueKind.String);
            c.Set(@"Control Panel\Colors", "Background", c.Theme == Theme.Win95 ? "0 128 128" : "10 60 120", RegistryValueKind.String);
            if (!Native.SetWallpaper(path))
                c.Log("  · не удалось поставить обои, файл лежит здесь: " + path);
        }

        // ---------------- Open-Shell ----------------

        static string OpenShellExe => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Open-Shell", "StartMenu.exe");

        static void ApplyOpenShell(TweakContext c)
        {
            bool is95 = c.Theme == Theme.Win95;
            c.Set(OpenShellSettings, "MenuStyle", is95 ? "Classic1" : "Win7", RegistryValueKind.String);
            c.Set(OpenShellSettings, "SkinC1", "Classic Skin", RegistryValueKind.String);
            c.Set(OpenShellSettings, "SkinW7", "Windows Aero", RegistryValueKind.String);
            c.Set(OpenShellSettings, "EnableStartButton", 1);
            c.Set(OpenShellSettings, "StartButtonType", is95 ? "ClassicButton" : "AeroButton", RegistryValueKind.String);

            if (!File.Exists(OpenShellExe) && !WingetInstall(c, "Open-Shell.Open-Shell-Menu", "Open-Shell"))
                return;

            // Перезапуск меню, чтобы оно перечитало настройки
            if (File.Exists(OpenShellExe))
            {
                RunAndWait(OpenShellExe, "-exit", 10000);
                Process.Start(OpenShellExe);
            }
        }

        // ---------------- Общее ----------------

        static bool WingetInstall(TweakContext c, string id, string name)
        {
            c.Log($"  · устанавливаю {name} через winget (может появиться запрос прав администратора)…");
            int code;
            try
            {
                code = RunAndWait("winget",
                    $"install --id {id} -e --silent --accept-package-agreements --accept-source-agreements", 15 * 60 * 1000);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                c.Log($"  ! winget не найден. Установите «Установщик приложений» из Microsoft Store или скачайте {name} вручную.");
                return false;
            }

            // -1978335189 (0x8A15002B) — «уже установлено, обновлений нет»
            if (code == 0 || code == -1978335189 || code == -1978335135)
            {
                c.Log($"  · {name} установлен");
                return true;
            }
            c.Log($"  ! winget завершился с кодом 0x{code:X8}, {name} не установлен");
            return false;
        }

        static int RunAndWait(string file, string args, int timeoutMs)
        {
            var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                if (!p.WaitForExit(timeoutMs)) return -1;
                return p.ExitCode;
            }
        }

        public static void RestartExplorer(Action<string> log)
        {
            log("Перезапускаю Проводник…");
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); p.WaitForExit(5000); }
                catch (Exception) { /* процесс уже завершился */ }
                finally { p.Dispose(); }
            }

            // Обычно Windows сама поднимает оболочку; если нет — запускаем вручную
            for (int i = 0; i < 20 && Process.GetProcessesByName("explorer").Length == 0; i++)
                Thread.Sleep(250);
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
        }

        // ---------------- Применение и откат ----------------

        public static void ApplySelected(Theme theme, IEnumerable<Tweak> selected, bool restartExplorer, Action<string> log)
        {
            var backup = RegistryBackup.Load();
            var ctx = new TweakContext { Theme = theme, Backup = backup, Log = log };
            log($"Применяю стиль {(theme == Theme.Win95 ? "Windows 95" : "Windows 7")}");

            foreach (var t in selected)
            {
                log("• " + t.Title);
                try
                {
                    t.Apply(ctx);
                }
                catch (Exception ex)
                {
                    log("  ! ошибка: " + ex.Message);
                }
                backup.Save(); // сохраняем после каждого шага, чтобы откат работал даже после сбоя
            }

            Native.BroadcastSettingsChange();
            if (restartExplorer) RestartExplorer(log);
            log("Готово! Если цвета заголовков не обновились — выйдите из системы и войдите снова.");
        }

        public static void RestoreOriginal(bool restartExplorer, Action<string> log)
        {
            if (!RegistryBackup.Exists)
            {
                log("Резервной копии нет — RetroSkin ещё ничего не менял.");
                return;
            }

            log("Возвращаю исходные настройки Windows 11…");
            var backup = RegistryBackup.Load();
            backup.RestoreAll(log);

            var wallpaper = backup.GetOriginal(Desktop, "Wallpaper") as string;
            Native.SetWallpaper(wallpaper ?? "");

            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics"))
                Native.SetAnimations(k?.GetValue("MinAnimate") as string != "0");

            if (File.Exists(OpenShellExe))
                log("Open-Shell остался установленным. Удалить его можно в «Параметры → Приложения».");

            RegistryBackup.Delete();
            Native.BroadcastSettingsChange();
            if (restartExplorer) RestartExplorer(log);
            log("Готово! Настройки возвращены.");
        }
    }
}
