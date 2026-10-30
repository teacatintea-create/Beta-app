using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace RetroSkin
{
    /// <summary>Рисует обои и мини-превью тем. Всё процедурное — никаких чужих картинок.</summary>
    static class Artwork
    {
        public static readonly Color Win95Teal = Color.FromArgb(0, 128, 128);
        public static readonly Color Win95Gray = Color.FromArgb(192, 192, 192);
        public static readonly Color Win95Navy = Color.FromArgb(0, 0, 128);

        /// <summary>Создаёт файл обоев под размер основного экрана и возвращает путь к нему.</summary>
        public static string CreateWallpaper(Theme theme)
        {
            var screen = Screen.PrimaryScreen.Bounds.Size;
            int w = Math.Max(screen.Width, 800), h = Math.Max(screen.Height, 600);
            Directory.CreateDirectory(RegistryBackup.Folder);

            string path = Path.Combine(RegistryBackup.Folder, theme == Theme.Win95 ? "wallpaper-95.png" : "wallpaper-7.jpg");
            using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    if (theme == Theme.Win95)
                        g.Clear(Win95Teal);
                    else
                        DrawAeroBackground(g, new Rectangle(0, 0, w, h));
                }

                if (theme == Theme.Win95)
                    bmp.Save(path, ImageFormat.Png);
                else
                    SaveJpeg(bmp, path, 92);
            }
            return path;
        }

        static void SaveJpeg(Bitmap bmp, string path, long quality)
        {
            var codec = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatID == ImageFormat.Jpeg.Guid);
            using (var p = new EncoderParameters(1))
            {
                p.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                bmp.Save(path, codec, p);
            }
        }

        /// <summary>Глубокий синий градиент с мягким свечением и световыми лентами — в духе «Аэро».</summary>
        public static void DrawAeroBackground(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new LinearGradientBrush(r, Color.FromArgb(18, 92, 170), Color.FromArgb(4, 30, 74), 70f))
                g.FillRectangle(bg, r);

            // Свечение в центре
            using (var glowPath = new GraphicsPath())
            {
                var glow = new RectangleF(r.X + r.Width * 0.15f, r.Y + r.Height * 0.05f, r.Width * 0.75f, r.Height * 0.8f);
                glowPath.AddEllipse(glow);
                using (var gb = new PathGradientBrush(glowPath)
                {
                    CenterColor = Color.FromArgb(150, 90, 190, 255),
                    SurroundColors = new[] { Color.FromArgb(0, 90, 190, 255) },
                })
                    g.FillPath(gb, glowPath);
            }

            // Изогнутые полупрозрачные ленты
            float w = r.Width, h = r.Height;
            DrawRibbon(g, r, new PointF(-0.1f * w, 0.85f * h), new PointF(0.3f * w, 0.35f * h),
                new PointF(0.7f * w, 0.95f * h), new PointF(1.1f * w, 0.30f * h), 0.10f * h, 70);
            DrawRibbon(g, r, new PointF(-0.1f * w, 0.70f * h), new PointF(0.35f * w, 0.20f * h),
                new PointF(0.65f * w, 0.85f * h), new PointF(1.1f * w, 0.15f * h), 0.05f * h, 90);
            DrawRibbon(g, r, new PointF(-0.1f * w, 0.95f * h), new PointF(0.4f * w, 0.55f * h),
                new PointF(0.75f * w, 1.05f * h), new PointF(1.1f * w, 0.55f * h), 0.03f * h, 110);

            // Блики
            var rnd = new Random(7);
            for (int i = 0; i < 14; i++)
            {
                float size = (float)(rnd.NextDouble() * 0.05 + 0.01) * h;
                float x = (float)rnd.NextDouble() * w, y = (float)(rnd.NextDouble() * 0.7 + 0.1) * h;
                using (var p = new GraphicsPath())
                {
                    p.AddEllipse(x - size, y - size, size * 2, size * 2);
                    using (var b = new PathGradientBrush(p)
                    {
                        CenterColor = Color.FromArgb(60 + rnd.Next(60), 200, 235, 255),
                        SurroundColors = new[] { Color.FromArgb(0, 200, 235, 255) },
                    })
                        g.FillPath(b, p);
                }
            }
        }

        static void DrawRibbon(Graphics g, Rectangle r, PointF a, PointF c1, PointF c2, PointF b, float thickness, int alpha)
        {
            using (var path = new GraphicsPath())
            {
                path.AddBezier(a, c1, c2, b);
                path.AddLine(b, new PointF(b.X, b.Y + thickness));
                path.AddBezier(new PointF(b.X, b.Y + thickness), new PointF(c2.X, c2.Y + thickness * 1.6f),
                    new PointF(c1.X, c1.Y + thickness * 0.4f), new PointF(a.X, a.Y + thickness));
                path.CloseFigure();
                using (var brush = new LinearGradientBrush(r, Color.FromArgb(alpha, 160, 225, 255),
                           Color.FromArgb(alpha / 4, 60, 160, 240), 0f))
                    g.FillPath(brush, path);
            }
        }

        // ---------- Мини-превью для карточек выбора темы ----------

        public static void DrawPreview(Graphics g, Rectangle r, Theme theme)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (theme == Theme.Win95) DrawPreview95(g, r);
            else DrawPreview7(g, r);
        }

        static void DrawPreview95(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.None;
            using (var teal = new SolidBrush(Win95Teal)) g.FillRectangle(teal, r);

            // Значки на рабочем столе
            for (int i = 0; i < 3; i++)
            {
                var icon = new Rectangle(r.X + 8, r.Y + 8 + i * 26, 14, 12);
                g.FillRectangle(Brushes.Silver, icon);
                g.DrawRectangle(Pens.Black, icon);
                g.FillRectangle(Brushes.White, r.X + 5, r.Y + 22 + i * 26, 20, 3);
            }

            // Окно
            var win = new Rectangle(r.X + r.Width / 4, r.Y + 12, r.Width / 2 + 10, r.Height / 2 + 6);
            using (var gray = new SolidBrush(Win95Gray)) g.FillRectangle(gray, win);
            ControlPaint.DrawBorder3D(g, win, Border3DStyle.Raised);
            var title = new Rectangle(win.X + 3, win.Y + 3, win.Width - 6, 11);
            using (var navy = new SolidBrush(Win95Navy)) g.FillRectangle(navy, title);
            using (var font = new Font("Microsoft Sans Serif", 6.5f, FontStyle.Bold))
                g.DrawString("Мой компьютер", font, Brushes.White, title.X + 2, title.Y - 1);
            var box = new Rectangle(win.Right - 13, title.Y + 2, 9, 7);
            using (var gray = new SolidBrush(Win95Gray)) g.FillRectangle(gray, box);
            ControlPaint.DrawBorder3D(g, box, Border3DStyle.RaisedInner);
            var client = new Rectangle(win.X + 5, title.Bottom + 3, win.Width - 10, win.Bottom - title.Bottom - 8);
            g.FillRectangle(Brushes.White, client);
            ControlPaint.DrawBorder3D(g, client, Border3DStyle.SunkenOuter);

            // Панель задач
            var bar = new Rectangle(r.X, r.Bottom - 16, r.Width, 16);
            using (var gray = new SolidBrush(Win95Gray)) g.FillRectangle(gray, bar);
            g.DrawLine(Pens.White, bar.X, bar.Y + 1, bar.Right, bar.Y + 1);
            var start = new Rectangle(bar.X + 2, bar.Y + 3, 40, 12);
            ControlPaint.DrawBorder3D(g, start, Border3DStyle.Raised);
            using (var font = new Font("Microsoft Sans Serif", 6.5f, FontStyle.Bold))
                g.DrawString("Пуск", font, Brushes.Black, start.X + 9, start.Y);
            using (var red = new SolidBrush(Color.FromArgb(220, 40, 30))) g.FillRectangle(red, start.X + 3, start.Y + 3, 3, 3);
            using (var green = new SolidBrush(Color.FromArgb(40, 170, 40))) g.FillRectangle(green, start.X + 6, start.Y + 3, 3, 3);
            using (var blue = new SolidBrush(Color.FromArgb(30, 80, 220))) g.FillRectangle(blue, start.X + 3, start.Y + 6, 3, 3);
            using (var yellow = new SolidBrush(Color.FromArgb(240, 200, 30))) g.FillRectangle(yellow, start.X + 6, start.Y + 6, 3, 3);
            var task = new Rectangle(start.Right + 4, start.Y, 60, 12);
            ControlPaint.DrawBorder3D(g, task, Border3DStyle.Sunken);
            var tray = new Rectangle(bar.Right - 34, start.Y, 32, 12);
            ControlPaint.DrawBorder3D(g, tray, Border3DStyle.SunkenOuter);
            using (var font = new Font("Microsoft Sans Serif", 6f))
                g.DrawString("12:00", font, Brushes.Black, tray.X + 3, tray.Y + 1);
        }

        static void DrawPreview7(Graphics g, Rectangle r)
        {
            DrawAeroBackground(g, r);

            // Окно со «стеклянной» рамкой
            var win = new Rectangle(r.X + r.Width / 4, r.Y + 10, r.Width / 2 + 14, r.Height / 2 + 10);
            using (var path = RoundRect(win, 5))
            {
                using (var glass = new LinearGradientBrush(win, Color.FromArgb(210, 160, 200, 240), Color.FromArgb(200, 90, 140, 200), 90f))
                    g.FillPath(glass, path);
                using (var pen = new Pen(Color.FromArgb(200, 20, 40, 70))) g.DrawPath(pen, path);
            }
            var client = new Rectangle(win.X + 5, win.Y + 16, win.Width - 10, win.Height - 21);
            g.FillRectangle(Brushes.White, client);
            using (var pen = new Pen(Color.FromArgb(120, 40, 60, 90))) g.DrawRectangle(pen, client);
            var close = new Rectangle(win.Right - 22, win.Y + 1, 18, 9);
            using (var path = RoundRect(close, 3))
            using (var red = new LinearGradientBrush(close, Color.FromArgb(235, 140, 120), Color.FromArgb(190, 40, 20), 90f))
                g.FillPath(red, path);

            // Панель задач
            var bar = new Rectangle(r.X, r.Bottom - 18, r.Width, 18);
            using (var b = new LinearGradientBrush(bar, Color.FromArgb(170, 30, 60, 90), Color.FromArgb(220, 10, 25, 45), 90f))
                g.FillRectangle(b, bar);
            using (var pen = new Pen(Color.FromArgb(120, 200, 230, 255))) g.DrawLine(pen, bar.X, bar.Y, bar.Right, bar.Y);

            // Круглая кнопка «Пуск»
            var orb = new Rectangle(bar.X + 4, bar.Y - 1, 19, 19);
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(orb);
                using (var ob = new PathGradientBrush(p)
                {
                    CenterPoint = new PointF(orb.X + orb.Width / 2f, orb.Bottom - 3),
                    CenterColor = Color.FromArgb(120, 230, 255),
                    SurroundColors = new[] { Color.FromArgb(20, 60, 120) },
                })
                    g.FillPath(ob, p);
                using (var pen = new Pen(Color.FromArgb(180, 10, 20, 40))) g.DrawEllipse(pen, orb);
            }
            using (var shine = new SolidBrush(Color.FromArgb(110, 255, 255, 255)))
                g.FillEllipse(shine, orb.X + 3, orb.Y + 2, orb.Width - 6, orb.Height / 2 - 1);

            // Значки приложений на панели
            for (int i = 0; i < 3; i++)
            {
                var btn = new Rectangle(orb.Right + 6 + i * 22, bar.Y + 2, 20, 14);
                using (var path = RoundRect(btn, 3))
                using (var b = new SolidBrush(Color.FromArgb(i == 0 ? 90 : 40, 200, 230, 255)))
                    g.FillPath(b, path);
            }
            using (var font = new Font("Segoe UI", 6f))
                g.DrawString("12:00", font, Brushes.White, bar.Right - 30, bar.Y + 3);
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
