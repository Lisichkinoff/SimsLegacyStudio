using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal class AppWindow : Form
    {
        private readonly bool resizable;
        private readonly bool largeTitle;
        private readonly ActionButton restore;
        protected readonly int CaptionHeight;

        internal AppWindow(bool resizable = false, bool largeTitle = true)
        {
            this.resizable = resizable;
            this.largeTitle = largeTitle;
            CaptionHeight = largeTitle ? 61 : 38;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Paper;
            ForeColor = Theme.Ink;
            DoubleBuffered = true;
            AddCaptionButton("Закрыть", "close", 0, Close);
            if (resizable)
            {
                restore = AddCaptionButton("Развернуть", "maximize", 1, ToggleMaximized);
                AddCaptionButton("Свернуть", "minus", 2, () => WindowState = FormWindowState.Minimized);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                const int systemMenu = 0x00080000, thickFrame = 0x00040000, minMaxBoxes = 0x00030000;
                parameters.Style |= systemMenu;
                if (resizable) parameters.Style |= thickFrame | minMaxBoxes;
                return parameters;
            }
        }

        private ActionButton AddCaptionButton(string name, string icon, int position, Action action)
        {
            var button = new ActionButton("", icon) { Size = new Size(32, 28), AccessibleName = name,
                ForeColor = Theme.Muted, Surface = Color.Transparent, Stroke = Color.Transparent,
                HoverSurface = Theme.White, Cursor = Cursors.Default, TabStop = false };
            button.Location = new Point(ClientSize.Width - 42 - 35 * position, largeTitle ? 21 : 5);
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button.Click += (s, e) => action();
            Controls.Add(button);
            return button;
        }

        private void ToggleMaximized()
        {
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.StyleCaption(this);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (restore != null)
            {
                bool maximized = WindowState == FormWindowState.Maximized;
                restore.Glyph = maximized ? "restore" : "maximize";
                restore.AccessibleName = maximized ? "Восстановить размер" : "Развернуть";
                restore.Invalidate();
            }
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (WindowState != FormWindowState.Maximized)
                using (var frame = new Pen(Theme.Line)) g.DrawRectangle(frame, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            if (largeTitle)
            {
                using (var font = Theme.Display(26))
                    Theme.Text(g, Text, font, Theme.Ink, new RectangleF(25, 10, ClientSize.Width - 79, 48));
            }
            else
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Theme.Icon(g, "gem", new RectangleF(18, 11, 16, 16), Theme.Green);
                using (var font = Theme.Font(13, FontStyle.Bold))
                    Theme.Text(g, Text, font, Theme.Muted, new RectangleF(41, 0, ClientSize.Width - 165, CaptionHeight));
                using (var divider = new Pen(Theme.Line)) g.DrawLine(divider, 15, CaptionHeight - 1, ClientSize.Width - 15, CaptionHeight - 1);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
        }

        protected override void WndProc(ref Message message)
        {
            const int calculateFrame = 0x83, hitTest = 0x84, sizeLimits = 0x24;
            if (message.Msg == calculateFrame && message.WParam != IntPtr.Zero)
            {
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == hitTest && message.Result.ToInt32() == 1)
            {
                long position = message.LParam.ToInt64();
                var point = PointToClient(new Point(unchecked((short)position), unchecked((short)(position >> 16))));
                message.Result = (IntPtr)HitTest(point);
            }
            else if (message.Msg == sizeLimits && resizable && IsHandleCreated)
            {
                var screen = Screen.FromHandle(Handle);
                var bounds = screen.Bounds;
                var work = screen.WorkingArea;
                var limits = (MinMaxInfo)Marshal.PtrToStructure(message.LParam, typeof(MinMaxInfo));
                limits.MaxPosition = new Point(work.Left - bounds.Left, work.Top - bounds.Top);
                limits.MaxSize = new Point(work.Width, work.Height);
                limits.MinTrackSize = new Point(MinimumSize.Width, MinimumSize.Height);
                Marshal.StructureToPtr(limits, message.LParam, false);
            }
        }

        private int HitTest(Point point)
        {
            if (resizable && WindowState == FormWindowState.Normal)
            {
                const int edge = 6;
                bool left = point.X < edge, right = point.X >= ClientSize.Width - edge;
                if (point.Y < edge) return left ? 13 : right ? 14 : 12;
                if (point.Y >= ClientSize.Height - edge) return left ? 16 : right ? 17 : 15;
                if (left || right) return left ? 10 : 11;
            }
            int buttons = resizable ? 112 : 43;
            return point.Y < CaptionHeight && point.X < ClientSize.Width - buttons ? 2 : 1;
        }
    }
}
