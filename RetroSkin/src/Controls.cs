using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RetroSkin
{
    /// <summary>Кнопка, которая рисуется как в Windows 95 (объёмная рамка) или Windows 7 (глянец).</summary>
    sealed class RetroButton : Control
    {
        bool _hover, _pressed;

        public Theme Theme { get; set; }

        public RetroButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Default;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            Color textColor = Enabled ? Color.Black : Color.Gray;

            if (Theme == Theme.Win95)
            {
                g.Clear(Artwork.Win95Gray);
                var face = r;
                if (Focused)
                {
                    g.DrawRectangle(Pens.Black, 0, 0, r.Width - 1, r.Height - 1);
                    face.Inflate(-1, -1);
                }
                ControlPaint.DrawBorder3D(g, face, _pressed ? Border3DStyle.Sunken : Border3DStyle.Raised);
                var text = r;
                if (_pressed) text.Offset(1, 1);
                TextRenderer.DrawText(g, Text, Font, text, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (Focused)
                {
                    var f = Rectangle.Inflate(r, -5, -5);
                    ControlPaint.DrawFocusRectangle(g, f);
                }
                return;
            }

            g.Clear(Parent?.BackColor ?? SystemColors.Control);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new Rectangle(0, 0, r.Width - 1, r.Height - 1);
            using (var path = Artwork.RoundRect(box, 3))
            {
                Color top = _pressed ? Color.FromArgb(229, 244, 252) : _hover ? Color.FromArgb(234, 246, 253) : Color.FromArgb(242, 242, 242);
                Color bottom = _pressed ? Color.FromArgb(152, 209, 239) : _hover ? Color.FromArgb(167, 217, 245) : Color.FromArgb(207, 207, 207);
                using (var b = new LinearGradientBrush(box, top, bottom, 90f))
                    g.FillPath(b, path);

                // Глянцевая верхняя половина
                var upper = new Rectangle(box.X + 1, box.Y + 1, box.Width - 2, box.Height / 2);
                using (var shine = new SolidBrush(Color.FromArgb(_pressed ? 40 : 110, 255, 255, 255)))
                    g.FillRectangle(shine, upper);

                Color border = _hover || _pressed || Focused ? Color.FromArgb(60, 127, 177) : Color.FromArgb(112, 112, 112);
                using (var pen = new Pen(border)) g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, r, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Карточка выбора темы с нарисованным превью рабочего стола.</summary>
    sealed class ThemeCard : Control
    {
        public Theme CardTheme { get; }
        public Theme UiTheme { get; set; }
        public bool Selected { get; set; }
        bool _hover;

        public ThemeCard(Theme theme, string title)
        {
            CardTheme = theme;
            Text = title;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            g.Clear(Parent?.BackColor ?? SystemColors.Control);

            int labelHeight = Font.Height + 10;
            var preview = new Rectangle(8, 8, r.Width - 16, r.Height - labelHeight - 12);

            if (UiTheme == Theme.Win95)
            {
                var frame = Rectangle.Inflate(preview, 2, 2);
                ControlPaint.DrawBorder3D(g, frame, Border3DStyle.Sunken);
                Artwork.DrawPreview(g, preview, CardTheme);
                g.SmoothingMode = SmoothingMode.None;
                if (Selected)
                    using (var pen = new Pen(Color.Black) { DashStyle = DashStyle.Dot })
                        g.DrawRectangle(pen, 1, 1, r.Width - 3, r.Height - 3);
            }
            else
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (Selected || _hover)
                {
                    using (var path = Artwork.RoundRect(new Rectangle(1, 1, r.Width - 3, r.Height - 3), 4))
                    {
                        using (var b = new LinearGradientBrush(r, Color.FromArgb(Selected ? 235 : 245, 245, 253),
                                   Color.FromArgb(Selected ? 205 : 230, 230, 248), 90f))
                            g.FillPath(b, path);
                        using (var pen = new Pen(Color.FromArgb(Selected ? 125 : 185, Selected ? 162 : 210, Selected ? 206 : 240)))
                            g.DrawPath(pen, path);
                    }
                }
                using (var clip = Artwork.RoundRect(preview, 4))
                {
                    var state = g.Save();
                    g.SetClip(clip);
                    Artwork.DrawPreview(g, preview, CardTheme);
                    g.Restore(state);
                }
            }

            var label = new Rectangle(0, preview.Bottom + 4, r.Width, labelHeight);
            using (var bold = new Font(Font, Selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, (Selected ? "● " : "○ ") + Text, bold, label, Color.Black,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            if (_hover && UiTheme == Theme.Win95 && !Selected)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(label, -60, -2));
        }
    }

    /// <summary>Полоса-«заголовок» сверху окна.</summary>
    sealed class HeaderBar : Control
    {
        public Theme Theme { get; set; }
        public string Subtitle { get; set; }

        public HeaderBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            if (Theme == Theme.Win95)
            {
                using (var b = new SolidBrush(Artwork.Win95Navy)) g.FillRectangle(b, r);
            }
            else
            {
                using (var b = new LinearGradientBrush(r, Color.FromArgb(70, 140, 210), Color.FromArgb(20, 70, 140), 90f))
                    g.FillRectangle(b, r);
                using (var shine = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                    g.FillRectangle(shine, 0, 0, r.Width, r.Height / 2);
            }

            using (var big = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold))
            {
                var size = TextRenderer.MeasureText(g, Text, big);
                int y = (r.Height - size.Height - Font.Height) / 2;
                TextRenderer.DrawText(g, Text, big, new Point(14, y), Color.White);
                TextRenderer.DrawText(g, Subtitle, Font, new Point(17, y + size.Height), Color.FromArgb(220, 235, 255));
            }
        }
    }
}
