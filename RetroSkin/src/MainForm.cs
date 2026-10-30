using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RetroSkin
{
    sealed class MainForm : Form
    {
        Theme _theme = Theme.Win95;

        readonly HeaderBar _header = new HeaderBar { Dock = DockStyle.Fill, Text = "RetroSkin" };
        readonly ThemeCard _card95 = new ThemeCard(Theme.Win95, "Windows 95") { Dock = DockStyle.Fill };
        readonly ThemeCard _card7 = new ThemeCard(Theme.Win7, "Windows 7") { Dock = DockStyle.Fill };
        readonly GroupBox _group = new GroupBox { Text = "Что изменить", Dock = DockStyle.Fill, FlatStyle = FlatStyle.System, Padding = new Padding(10, 6, 10, 6) };
        readonly List<(Tweak tweak, CheckBox box)> _checks = new List<(Tweak, CheckBox)>();
        readonly CheckBox _restartExplorer = new CheckBox { Text = "Перезапустить Проводник", Checked = true, AutoSize = true, FlatStyle = FlatStyle.System, Anchor = AnchorStyles.Left };
        readonly RetroButton _apply = new RetroButton { Text = "Применить", Size = new Size(130, 30) };
        readonly RetroButton _restore = new RetroButton { Text = "Вернуть Windows 11", Size = new Size(160, 30) };
        readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.Fixed3D };
        readonly ToolTip _tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };

        public MainForm()
        {
            Text = "RetroSkin — Windows 95 / 7 для Windows 11";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(860, 640);
            MinimumSize = new Size(760, 600);
            StartPosition = FormStartPosition.CenterScreen;

            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8, 6, 8, 0) };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cards.Controls.Add(_card95, 0, 0);
            cards.Controls.Add(_card7, 1, 0);
            _card95.Click += (s, e) => SelectTheme(Theme.Win95);
            _card7.Click += (s, e) => SelectTheme(Theme.Win7);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            int half = (Tweaks.All.Length + 1) / 2;
            for (int i = 0; i < Tweaks.All.Length; i++)
            {
                var t = Tweaks.All[i];
                var box = new CheckBox { Text = t.Title, AutoSize = true, FlatStyle = FlatStyle.System, Margin = new Padding(3, 4, 3, 4) };
                _tips.SetToolTip(box, t.Hint);
                _checks.Add((t, box));
                grid.Controls.Add(box, i < half ? 0 : 1, i < half ? i : i - half);
            }
            _group.Controls.Add(grid);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(6, 6, 6, 0), WrapContents = false };
            _restartExplorer.Margin = new Padding(16, 9, 3, 3);
            buttons.Controls.AddRange(new Control[] { _apply, _restore, _restartExplorer });
            _apply.Click += async (s, e) => await ApplyAsync();
            _restore.Click += async (s, e) => await RestoreAsync();
            _tips.SetToolTip(_restore, "Откатывает всё, что изменил RetroSkin, по сохранённой резервной копии.");

            var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 10) };
            logPanel.Controls.Add(_log);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var groupPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 0) };
            groupPanel.Controls.Add(_group);
            root.Controls.Add(_header, 0, 0);
            root.Controls.Add(cards, 0, 1);
            root.Controls.Add(groupPanel, 0, 2);
            root.Controls.Add(buttons, 0, 3);
            root.Controls.Add(logPanel, 0, 4);
            foreach (Control c in root.Controls) c.Margin = Padding.Empty;
            Controls.Add(root);

            Log("Выберите стиль, отметьте нужные пункты и нажмите «Применить».");
            Log("Наведите курсор на пункт, чтобы увидеть подробности. Перед изменениями создаётся резервная копия.");
            if (RegistryBackup.Exists)
                Log("Найдена резервная копия прошлых изменений — «Вернуть Windows 11» откатит всё.");

            SelectTheme(Theme.Win95);
        }

        void SelectTheme(Theme theme)
        {
            _theme = theme;
            foreach (var (tweak, box) in _checks) box.Checked = tweak.DefaultOn(theme);
            ApplyUiTheme();
        }

        /// <summary>Само окно программы тоже переодевается в выбранный стиль.</summary>
        void ApplyUiTheme()
        {
            bool is95 = _theme == Theme.Win95;
            SuspendLayout();
            BackColor = is95 ? Artwork.Win95Gray : Color.FromArgb(240, 244, 249);
            Font = is95 ? new Font("Microsoft Sans Serif", 9f) : new Font("Segoe UI", 9f);
            _header.Theme = _theme;
            _header.Subtitle = is95 ? "Серые окна, бирюзовый рабочий стол и кнопка «Пуск»" : "Голубое «стекло», круглая кнопка «Пуск» и синие обои";
            foreach (var card in new[] { _card95, _card7 })
            {
                card.UiTheme = _theme;
                card.Selected = card.CardTheme == _theme;
            }
            _apply.Theme = _restore.Theme = _theme;

            // В стиле 95 отключаем визуальные стили — Windows сама нарисует классические флажки и рамки
            var themed = _checks.Select(c => (Control)c.box).Concat(new Control[] { _group, _restartExplorer, _log });
            foreach (var c in themed)
            {
                Native.SetClassicLook(c.Handle, is95);
                c.Invalidate();
            }
            ResumeLayout(true);
            Invalidate(true);
        }

        async Task ApplyAsync()
        {
            var selected = _checks.Where(c => c.box.Checked).Select(c => c.tweak).ToList();
            if (selected.Count == 0)
            {
                Log("Ничего не отмечено.");
                return;
            }

            string name = _theme == Theme.Win95 ? "Windows 95" : "Windows 7";
            string warn = _restartExplorer.Checked ? "\n\nПанель задач на пару секунд исчезнет — Проводник перезапустится." : "";
            if (MessageBox.Show(this, $"Применить стиль {name}?\nИсходные настройки сохранятся, их можно вернуть кнопкой «Вернуть Windows 11».{warn}",
                    "RetroSkin", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            var theme = _theme;
            bool restart = _restartExplorer.Checked;
            await RunBusy(() => Tweaks.ApplySelected(theme, selected, restart, LogFromWorker));
        }

        async Task RestoreAsync()
        {
            if (MessageBox.Show(this, "Вернуть настройки, которые были до RetroSkin?", "RetroSkin",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            bool restart = _restartExplorer.Checked;
            await RunBusy(() => Tweaks.RestoreOriginal(restart, LogFromWorker));
        }

        async Task RunBusy(Action work)
        {
            _apply.Enabled = _restore.Enabled = _card95.Enabled = _card7.Enabled = false;
            UseWaitCursor = true;
            try
            {
                await Task.Run(work);
            }
            catch (Exception ex)
            {
                Log("Ошибка: " + ex.Message);
            }
            finally
            {
                UseWaitCursor = false;
                _apply.Enabled = _restore.Enabled = _card95.Enabled = _card7.Enabled = true;
            }
        }

        void LogFromWorker(string line)
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() => Log(line)));
        }

        void Log(string line) => _log.AppendText(line + Environment.NewLine);
    }
}
