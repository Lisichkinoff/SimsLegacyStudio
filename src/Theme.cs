using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal static class Theme
    {
        private const int DefaultTreeBackground = 0xEAF7FE;
        private const string DefaultTreeAccent = "#2B7EAB";
        public static bool Dark { get; set; }
        public static Color Ink { get { return Choose(0x183148, 0xE3EDF6); } }
        public static Color Muted { get { return Choose(0x51697D, 0xA8BDCE); } }
        public static Color Paper { get { return Choose(0xF0F7FC, 0x111C28); } }
        public static Color White { get { return Choose(0xFFFFFF, 0x1A2A39); } }
        public static Color Line { get { return Choose(0xD3E4F0, 0x354E62); } }
        public static Color Green { get { return Choose(0x106F4A, 0x8CDCB4); } }
        public static Color SoftGreen { get { return Choose(0xE2F8E9, 0x203C36); } }
        public static Color Sidebar { get { return Choose(0x0F2F4D, 0x0D1622); } }
        public static Color SidebarActive { get { return Choose(0x29516C, 0x23394C); } }
        public static Color SidebarText { get { return Choose(0xDBEAF5, 0xCEDDE9); } }
        public static Color Lime { get { return Color.FromArgb(193, 250, 107); } }
        public static Color Danger { get { return Choose(0xA7333F, 0xF5A2AE); } }
        public static Color TreeBackground { get { return Choose(DefaultTreeBackground, 0x172837); } }
        public static Color TreeAccent { get { return Choose(0x2B7EAB, 0x76BDE0); } }
        public static Color ProfileGradientTop { get { return Choose(0xD7F0FD, 0x243A4B); } }
        public static Color ProfileGradientBottom { get { return Choose(0xEAFBDF, 0x213E37); } }
        public static Color RailGradient { get { return Choose(0x114358, 0x162936); } }

        private static Color Rgb(int value) { return Color.FromArgb(unchecked((int)0xFF000000) | value); }
        private static Color Choose(int light, int dark) { return Rgb(Dark ? dark : light); }

        private static Color[] InterfaceColors()
        {
            return new[] { Ink, Muted, Paper, White, Line, Green, SoftGreen, Sidebar, SidebarActive, SidebarText, Danger };
        }

        internal static void Apply(Form form, bool dark)
        {
            if (Dark == dark) return;
            var previous = InterfaceColors();
            Dark = dark;
            var current = InterfaceColors();
            var map = new Dictionary<int, Color>();
            for (int i = 0; i < previous.Length; i++) map.Add(previous[i].ToArgb(), current[i]);
            form.SuspendLayout();
            try { Recolor(form, map); }
            finally { form.ResumeLayout(); }
            if (form.IsHandleCreated) StyleCaption(form);
            form.Invalidate(true);
        }

        private static void Recolor(Control control, Dictionary<int, Color> map)
        {
            Color color;
            if (map.TryGetValue(control.BackColor.ToArgb(), out color)) control.BackColor = color;
            if (map.TryGetValue(control.ForeColor.ToArgb(), out color)) control.ForeColor = color;
            var button = control as ActionButton;
            if (button != null)
            {
                if (map.TryGetValue(button.Surface.ToArgb(), out color)) button.Surface = color;
                if (map.TryGetValue(button.Stroke.ToArgb(), out color)) button.Stroke = color;
                if (map.TryGetValue(button.HoverSurface.ToArgb(), out color)) button.HoverSurface = color;
            }
            foreach (Control child in control.Controls) Recolor(child, map);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

        internal static void StyleCaption(Form form)
        {
            int value = Dark ? 1 : 0;
            if (DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
            int round = 2;
            DwmSetWindowAttribute(form.Handle, 33, ref round, sizeof(int));
            int border = ColorTranslator.ToWin32(Line);
            DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
        }

        internal static Color CanvasBackground(string value)
        {
            return CanvasBackground(ColorOr(value, TreeBackground));
        }

        internal static Color CanvasBackground(Color color)
        {
            if (color.ToArgb() == Rgb(DefaultTreeBackground).ToArgb()) return TreeBackground;
            if (!Dark) return color;

            const int maximumDarkLuminance = 66000;
            int luminance = Luminance(color);
            if (luminance <= maximumDarkLuminance) return color;
            double scale = (double)maximumDarkLuminance / luminance;
            return Color.FromArgb((int)Math.Round(color.R * scale), (int)Math.Round(color.G * scale),
                (int)Math.Round(color.B * scale));
        }

        internal static Color CanvasAccent(string value)
        {
            return String.Equals(value, DefaultTreeAccent, StringComparison.OrdinalIgnoreCase) ?
                TreeAccent : ColorOr(value, TreeAccent);
        }

        internal static bool IsDark(Color color)
        {
            return Luminance(color) < 140000;
        }

        private static int Luminance(Color color) { return color.R * 299 + color.G * 587 + color.B * 114; }

        internal static Color CanvasText(Color background, bool muted = false)
        {
            return IsDark(background) ? Rgb(muted ? 0xB3C8D9 : 0xF2F7FB) : Rgb(muted ? 0x51697D : 0x183148);
        }

        public static Font Font(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Pixel);
        }

        public static Font Display(float size)
        {
            return new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        public static Color ColorOr(string value, Color fallback)
        {
            if (String.IsNullOrWhiteSpace(value)) return fallback;
            try { return ColorTranslator.FromHtml(value); }
            catch (ArgumentException) { return fallback; }
        }

        public static GraphicsPath Rounded(RectangleF box, float radius)
        {
            var path = new GraphicsPath();
            radius = Math.Min(radius, Math.Min(box.Width, box.Height) / 2);
            float d = 2 * radius;
            if (d <= 0) { path.AddRectangle(box); return path; }
            path.AddArc(box.X, box.Y, d, d, 180, 90);
            path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
            path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Fill(Graphics g, Color color, RectangleF box, float radius)
        {
            using (var path = Rounded(box, radius))
            using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
        }

        public static void Border(Graphics g, Color color, RectangleF box, float radius, float width = 1)
        {
            using (var path = Rounded(box, radius))
            using (var pen = new Pen(color, width)) g.DrawPath(pen, path);
        }

        public static void Text(Graphics g, string text, Font font, Color color, RectangleF box,
            StringAlignment alignment = StringAlignment.Near)
        {
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = alignment, LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text ?? "", font, brush, box, format);
        }

        public static void Icon(Graphics g, string icon, RectangleF box, Color color)
        {
            var state = g.Save();
            g.TranslateTransform(box.X, box.Y);
            g.ScaleTransform(box.Width / 24, box.Height / 24);
            using (var p = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var b = new SolidBrush(color))
            {
                switch (icon)
                {
                    case "gem":
                        using (var light = new SolidBrush(Color.FromArgb(220, 255, 153)))
                        using (var dark = new SolidBrush(Color.FromArgb(49, 154, 82)))
                        {
                            g.FillPolygon(b, new[] { new PointF(12, 0), new PointF(19, 11), new PointF(12, 24), new PointF(5, 11) });
                            g.FillPolygon(light, new[] { new PointF(12, 0), new PointF(12, 11), new PointF(5, 11) });
                            g.FillPolygon(dark, new[] { new PointF(12, 11), new PointF(19, 11), new PointF(12, 24) });
                        }
                        break;
                    case "tree":
                        g.DrawRectangle(p, 9, 3, 6, 5); g.DrawRectangle(p, 3, 16, 6, 5); g.DrawRectangle(p, 15, 16, 6, 5);
                        g.DrawLines(p, new[] { new Point(6, 16), new Point(6, 12), new Point(18, 12), new Point(18, 16) }); g.DrawLine(p, 12, 8, 12, 12);
                        break;
                    case "people":
                        g.DrawEllipse(p, 5, 4, 7, 7); g.DrawArc(p, 1, 13, 15, 9, 180, 180);
                        g.DrawArc(p, 12, 4, 7, 7, 270, 180); g.DrawArc(p, 12, 13, 10, 9, 280, 80);
                        break;
                    case "person":
                        g.DrawEllipse(p, 8, 4, 8, 8); g.DrawArc(p, 4, 14, 16, 10, 180, 180); break;
                    case "paw":
                        g.FillEllipse(b, 6, 12, 12, 9);
                        g.FillEllipse(b, 2.7f, 8, 4, 5);
                        g.FillEllipse(b, 7, 3, 4, 6);
                        g.FillEllipse(b, 13, 3, 4, 6);
                        g.FillEllipse(b, 17.3f, 8, 4, 5);
                        break;
                    case "heart":
                    case "heartbreak":
                        using (var heart = new GraphicsPath())
                        {
                            heart.AddBezier(12, 20, 8, 17, 1, 12, 3, 7);
                            heart.AddBezier(3, 7, 5, 3, 9, 3, 12, 7);
                            heart.AddBezier(12, 7, 15, 3, 19, 3, 21, 7);
                            heart.AddBezier(21, 7, 23, 12, 16, 17, 12, 20);
                            g.DrawPath(p, heart);
                        }
                        if (icon == "heartbreak")
                            g.DrawLines(p, new[] { new Point(12, 7), new Point(10, 11), new Point(14, 13), new Point(12, 19) });
                        break;
                    case "ring":
                        g.DrawEllipse(p, 6, 10, 12, 11);
                        g.DrawPolygon(p, new[] { new Point(8, 6), new Point(10, 3), new Point(14, 3), new Point(16, 6), new Point(12, 10) });
                        break;
                    case "unlink":
                        g.DrawArc(p, 2, 8, 10, 8, 65, 235); g.DrawArc(p, 12, 8, 10, 8, 245, 235);
                        g.DrawLine(p, 13, 4, 11, 7); g.DrawLine(p, 13, 17, 11, 20); break;
                    case "memorial":
                        g.DrawLines(p, new[] { new Point(9, 20), new Point(9, 10), new Point(15, 10), new Point(15, 20) });
                        g.DrawLine(p, 5, 20, 19, 20);
                        g.DrawBezier(p, 12, 3, 13, 5, 15, 7, 12, 8); break;
                    case "spark":
                        g.DrawPolygon(p, new[] { new Point(12, 3), new Point(14, 10), new Point(21, 12), new Point(14, 14),
                            new Point(12, 21), new Point(10, 14), new Point(3, 12), new Point(10, 10) }); break;
                    case "folder":
                        g.DrawPolygon(p, new[] { new Point(3, 6), new Point(9, 6), new Point(12, 9), new Point(21, 9), new Point(21, 19), new Point(3, 19) }); break;
                    case "tune":
                        g.DrawLine(p, 3, 6, 21, 6); g.DrawLine(p, 3, 12, 21, 12); g.DrawLine(p, 3, 18, 21, 18);
                        g.FillEllipse(b, 7, 3.5f, 5, 5); g.FillEllipse(b, 14, 9.5f, 5, 5); g.FillEllipse(b, 8, 15.5f, 5, 5); break;
                    case "help":
                        g.DrawEllipse(p, 3, 3, 18, 18); g.DrawArc(p, 8.5f, 6.5f, 7, 6, 175, 230); g.DrawLine(p, 13, 12, 12, 14); g.FillEllipse(b, 11, 16, 2, 2); break;
                    case "plus":
                        g.DrawLine(p, 5, 12, 19, 12); g.DrawLine(p, 12, 5, 12, 19); break;
                    case "minus":
                        g.DrawLine(p, 5, 12, 19, 12); break;
                    case "import":
                    case "export":
                        g.DrawLines(p, new[] { new Point(4, 14), new Point(4, 20), new Point(20, 20), new Point(20, 14) });
                        g.DrawLine(p, 12, 3, 12, 15);
                        int y = icon == "import" ? 15 : 3;
                        int other = icon == "import" ? 10 : 8;
                        g.DrawLines(p, new[] { new Point(7, other), new Point(12, y), new Point(17, other) }); break;
                    case "eye":
                        using (var path = new GraphicsPath())
                        {
                            path.AddBezier(2, 12, 7, 4, 17, 4, 22, 12);
                            path.AddBezier(22, 12, 17, 20, 7, 20, 2, 12);
                            g.DrawPath(p, path);
                        }
                        g.DrawEllipse(p, 9, 9, 6, 6); break;
                    case "edit":
                        g.DrawPolygon(p, new[] { new Point(5, 15), new Point(16, 4), new Point(20, 8), new Point(9, 19), new Point(4, 20) }); g.DrawLine(p, 14, 6, 18, 10); break;
                    case "fit":
                        g.DrawLines(p, new[] { new Point(4, 9), new Point(4, 4), new Point(9, 4) });
                        g.DrawLines(p, new[] { new Point(15, 4), new Point(20, 4), new Point(20, 9) });
                        g.DrawLines(p, new[] { new Point(20, 15), new Point(20, 20), new Point(15, 20) });
                        g.DrawLines(p, new[] { new Point(9, 20), new Point(4, 20), new Point(4, 15) });
                        g.DrawRectangle(p, 9, 9, 6, 6); break;
                    case "undo":
                    case "redo":
                        if (icon == "redo") { g.TranslateTransform(24, 0); g.ScaleTransform(-1, 1); }
                        g.DrawLines(p, new[] { new Point(9, 5), new Point(4, 10), new Point(9, 15) });
                        g.DrawBezier(p, 4, 10, 18, 8, 21, 11, 20, 18); break;
                    case "link":
                        g.DrawArc(p, 3, 8, 12, 8, 45, 270); g.DrawArc(p, 9, 8, 12, 8, 225, 270);
                        g.DrawLine(p, 9, 12, 15, 12); break;
                    case "check":
                        g.DrawLines(p, new[] { new Point(5, 12), new Point(10, 17), new Point(19, 7) }); break;
                    case "search":
                        g.DrawEllipse(p, 4, 4, 11, 11); g.DrawLine(p, 14, 14, 20, 20); break;
                    case "close":
                        g.DrawLine(p, 6, 6, 18, 18); g.DrawLine(p, 18, 6, 6, 18); break;
                    case "maximize":
                        g.DrawRectangle(p, 5, 5, 14, 14); break;
                    case "restore":
                        g.DrawRectangle(p, 4, 8, 12, 12);
                        g.DrawLines(p, new[] { new Point(8, 8), new Point(8, 4), new Point(20, 4), new Point(20, 16), new Point(16, 16) }); break;
                    case "chevron":
                        g.DrawLines(p, new[] { new Point(6, 10), new Point(12, 16), new Point(18, 10) }); break;
                    case "dots":
                        for (int x = 5; x <= 19; x += 7) g.FillEllipse(b, x - 1.5f, 10.5f, 3, 3); break;
                    case "arrow":
                        g.DrawLine(p, 4, 12, 20, 12); g.DrawLines(p, new[] { new Point(14, 6), new Point(20, 12), new Point(14, 18) }); break;
                }
            }
            g.Restore(state);
        }
    }

    internal sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.White; } }
        public override Color ImageMarginGradientBegin { get { return Theme.White; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.White; } }
        public override Color ImageMarginGradientEnd { get { return Theme.White; } }
        public override Color MenuBorder { get { return Theme.Line; } }
        public override Color MenuItemBorder { get { return Theme.Line; } }
        public override Color MenuItemSelected { get { return Theme.SoftGreen; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.White; } }
    }

    internal class ActionButton : Button
    {
        private bool hover;
        public Color Surface { get; set; }
        public Color Stroke { get; set; }
        public Color HoverSurface { get; set; }
        public string Glyph;
        public string TrailingGlyph;
        public bool AlignLeft;

        public ActionButton(string text, string glyph = null)
        {
            Surface = Theme.White;
            Stroke = Theme.Line;
            Text = text; Glyph = glyph; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Font = Theme.Font(13, FontStyle.Bold); ForeColor = Theme.Ink; Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent == null ? Theme.Paper : Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(1, 1, Width - 2, Height - 2);
            Color fill = hover && Enabled
                ? !HoverSurface.IsEmpty ? HoverSurface :
                    Surface == Color.Transparent ? Theme.SidebarActive : ControlPaint.Light(Surface, .13f)
                : Surface;
            Color ink = Enabled ? ForeColor : Color.FromArgb(147, 156, 148);
            float radius = Math.Min(16f, Height / 2f);
            if (fill != Color.Transparent) Theme.Fill(g, fill, box, radius);
            if (Stroke != Color.Transparent) Theme.Border(g, Stroke, box, radius);
            if (Focused && ShowFocusCues) Theme.Border(g, Theme.Green, RectangleF.Inflate(box, -3, -3), radius - 3);
            if (String.IsNullOrEmpty(Glyph))
            {
                if (TrailingGlyph != null)
                    Theme.Icon(g, TrailingGlyph, new RectangleF(Width - 28, (Height - 16) / 2f, 16, 16), ink);
                Theme.Text(g, Text, Font, ink, AlignLeft ? new RectangleF(13, 0, Width - (TrailingGlyph == null ? 22 : 43), Height) : ClientRectangle,
                    AlignLeft ? StringAlignment.Near : StringAlignment.Center);
                return;
            }
            int textWidth = String.IsNullOrEmpty(Text) ? 0 : (int)Math.Ceiling(g.MeasureString(Text, Font).Width);
            int left = AlignLeft ? 13 : Math.Max(4, (Width - (textWidth > 0 ? textWidth + 24 : 18)) / 2);
            Theme.Icon(g, Glyph, new RectangleF(left, (Height - 18) / 2f, 18, 18), ink);
            if (textWidth > 0) Theme.Text(g, Text, Font, ink, new RectangleF(left + 25, 0, Width - left - 29, Height));
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        private readonly int radius;
        public RoundedPanel(int radius) { this.radius = radius; DoubleBuffered = true; }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 2 || Height < 2) return;
            var previous = Region;
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), radius))
                Region = new Region(path);
            if (previous != null) previous.Dispose();
        }
    }

    internal sealed class TraitChip : Control
    {
        public TraitChip(string text, int maxWidth)
        {
            Text = text; Font = Theme.Font(12, FontStyle.Bold); ForeColor = Theme.Green; DoubleBuffered = true;
            Size = new Size(Math.Min(maxWidth, TextRenderer.MeasureText(text, Font).Width + 25), 27);
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = text;
            Margin = new Padding(0, 0, 5, 5);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Fill(e.Graphics, Theme.SoftGreen, new RectangleF(0, 0, Width - 1, Height - 1), 13);
            Theme.Text(e.Graphics, Text, Font, ForeColor, new RectangleF(7, 0, Width - 14, Height), StringAlignment.Center);
        }
    }

    internal static class Photos
    {
        private static readonly Dictionary<int, Image> Embedded = new Dictionary<int, Image>();
        private static readonly Dictionary<string, Image> User = new Dictionary<string, Image>();

        public static Image Read(string value, bool keep = false)
        {
            if (String.IsNullOrEmpty(value) || value.Length > 5 * 1024 * 1024) return null;
            Image image;
            if (!keep && User.TryGetValue(value, out image)) return image;
            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(value)))
                using (var original = Image.FromStream(stream))
                    image = new Bitmap(original);
                if (keep) return image;
                if (User.Count >= 96)
                {
                    foreach (var cached in User.Values) cached.Dispose();
                    User.Clear();
                }
                User.Add(value, image);
                return image;
            }
            catch (ArgumentException) { return null; }
            catch (FormatException) { return null; }
            catch (OutOfMemoryException) { return null; }
        }

        public static Image Portrait(Sim sim)
        {
            if (sim.Photos != null && sim.Photos.Count > 0)
            {
                var custom = Read(sim.Photos[0]);
                if (custom != null) return custom;
            }
            if (sim.Portrait < 0 || sim.Portrait > 8) return null;
            Image image;
            if (Embedded.TryGetValue(sim.Portrait, out image)) return image;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("portrait-" + sim.Portrait + ".jpg"))
            {
                if (stream == null) return null;
                using (var original = Image.FromStream(stream)) image = new Bitmap(original);
            }
            Embedded.Add(sim.Portrait, image);
            return image;
        }

        public static PictureBox Thumbnail(string value, int size)
        {
            var image = Read(value, true);
            var picture = new PictureBox { Size = new Size(size, size), SizeMode = PictureBoxSizeMode.Zoom, Image = image, Cursor = Cursors.Hand };
            if (image != null) picture.Disposed += (s, e) => image.Dispose();
            return picture;
        }

        public static string Import(string path, int max)
        {
            if (new FileInfo(path).Length > 25 * 1024 * 1024) throw new InvalidDataException("Изображение превышает 25 МБ.");
            using (var original = Image.FromFile(path))
            {
                if ((long)original.Width * original.Height > 60000000) throw new InvalidDataException("Изображение слишком большое.");
                var ratio = Math.Min(1, (double)max / Math.Max(original.Width, original.Height));
                using (var result = new Bitmap(Math.Max(1, (int)(original.Width * ratio)), Math.Max(1, (int)(original.Height * ratio))))
                using (var g = Graphics.FromImage(result))
                using (var buffer = new MemoryStream())
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(original, 0, 0, result.Width, result.Height);
                    result.Save(buffer, ImageFormat.Jpeg);
                    return Convert.ToBase64String(buffer.ToArray());
                }
            }
        }

        public static void Draw(Graphics g, Sim sim, RectangleF rect, bool circle)
        {
            using (var shape = new GraphicsPath())
            {
                if (circle) shape.AddEllipse(rect);
                else using (var rounded = Theme.Rounded(rect, 9)) shape.AddPath(rounded, false);
                var state = g.Save();
                g.SetClip(shape, CombineMode.Intersect);
                var image = Portrait(sim);
                if (image != null) Cover(g, image, rect);
                else
                {
                    Theme.Fill(g, Theme.SoftGreen, rect, 0);
                    Theme.Icon(g, sim.IsAnimal() ? "paw" : "person",
                        RectangleF.Inflate(rect, -rect.Width * .25f, -rect.Height * .25f), Theme.Green);
                }
                g.Restore(state);
            }
        }

        public static void Cover(Graphics g, Image image, RectangleF rect)
        {
            float ratio = Math.Max(rect.Width / image.Width, rect.Height / image.Height);
            float w = rect.Width / ratio, h = rect.Height / ratio;
            g.DrawImage(image, rect, new RectangleF((image.Width - w) / 2, (image.Height - h) / 2, w, h), GraphicsUnit.Pixel);
        }
    }

    internal sealed class Avatar : Control
    {
        public Sim Sim;
        public Avatar(Sim sim) { Sim = sim; DoubleBuffered = true; }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 2 || Height < 2) return;
            var previous = Region;
            using (var circle = new GraphicsPath())
            {
                circle.AddEllipse(1, 1, Width - 2, Height - 2);
                Region = new Region(circle);
            }
            if (previous != null) previous.Dispose();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Photos.Draw(e.Graphics, Sim, new RectangleF(1, 1, Width - 2, Height - 2), true);
        }
    }
}
