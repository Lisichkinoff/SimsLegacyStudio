using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal sealed class AppField : Panel
    {
        internal readonly TextBox Input;

        internal AppField(string label, string value = "", int lines = 1)
        {
            BackColor = Color.Transparent;
            DoubleBuffered = true;
            Input = new TextBox {
                Text = value ?? "", Multiline = lines > 1, Height = lines > 1 ? lines * 21 + 8 : 27,
                ScrollBars = lines > 1 ? ScrollBars.Vertical : ScrollBars.None, MaxLength = lines > 1 ? 16000 : 180,
                Font = Theme.Font(15), ForeColor = Theme.Ink, BackColor = Theme.White, AccessibleName = label,
                BorderStyle = BorderStyle.None
            };
            Height = lines > 1 ? Input.Height + 18 : 37;
            Input.Location = new Point(11, lines > 1 ? 8 : 9);
            Input.GotFocus += (s, e) => Invalidate();
            Input.LostFocus += (s, e) => Invalidate();
            Controls.Add(Input);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Input != null) Input.Width = Math.Max(1, Width - 22);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(1, 1, Width - 3, Height - 3);
            Theme.Fill(e.Graphics, Theme.White, bounds, 10);
            Theme.Border(e.Graphics, Input.Focused ? Theme.TreeAccent : Theme.Line, bounds, 10);
        }
    }

    internal sealed class AppList : ListBox
    {
        internal AppList()
        {
            BackColor = Theme.White;
            ForeColor = Theme.Ink;
            Font = Theme.Font(14);
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 26;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            using (var background = new SolidBrush(Theme.White)) e.Graphics.FillRectangle(background, e.Bounds);
            if ((e.State & DrawItemState.Selected) != 0)
                Theme.Fill(e.Graphics, Theme.SoftGreen, Rectangle.Inflate(e.Bounds, -2, -1), 7);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -10, 0),
                Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class AppMenu : ContextMenuStrip
    {
        internal AppMenu()
        {
            Font = Theme.Font(14);
            BackColor = Theme.White;
            ForeColor = Theme.Ink;
            Renderer = new AppMenuRenderer();
        }

        protected override void OnItemAdded(ToolStripItemEventArgs e)
        {
            base.OnItemAdded(e);
            e.Item.Padding = new Padding(5, 5, 10, 5);
        }
    }

    internal sealed class AppMenuRenderer : ToolStripProfessionalRenderer
    {
        internal AppMenuRenderer() : base(new MenuColors()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Fill(e.Graphics, Theme.SoftGreen, new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2), 8);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Ink : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Icon(e.Graphics, "check", e.ImageRectangle, Theme.Green);
        }
    }

    internal sealed class AppChoice : ActionButton
    {
        private readonly object[] options;
        private int selectedIndex = -1;
        private AppMenu menu;

        internal AppChoice(string caption, object[] options, int selectedIndex)
            : base("")
        {
            this.options = options;
            AccessibleName = caption;
            AccessibleRole = AccessibleRole.ComboBox;
            Height = 36;
            Font = Theme.Font(15);
            AlignLeft = true;
            TrailingGlyph = "chevron";
            HoverSurface = Theme.SoftGreen;
            if (selectedIndex >= 0 && selectedIndex < options.Length) SelectedIndex = selectedIndex;
        }

        internal event EventHandler SelectedIndexChanged;

        internal int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (value == selectedIndex || value < -1 || value >= options.Length) return;
                selectedIndex = value;
                Text = Convert.ToString(SelectedItem);
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        internal object SelectedItem { get { return selectedIndex < 0 ? null : options[selectedIndex]; } }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (menu != null || options.Length == 0) return;
            menu = new AppMenu { AutoSize = false, Width = Width };
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(Convert.ToString(options[i])) {
                    Checked = i == selectedIndex, AutoSize = false, Size = new Size(Width - 4, 33) };
                item.Click += (s, args) => SelectedIndex = index;
                menu.Items.Add(item);
            }
            var popup = menu;
            popup.Height = Math.Min(Screen.FromControl(this).WorkingArea.Height - 50, options.Length * 33 + 6);
            popup.Closed += (s, args) =>
            {
                menu = null;
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(popup.Dispose));
                else popup.Dispose();
            };
            popup.Show(this, new Point(0, Height + 4));
            if (selectedIndex >= 0) popup.Items[selectedIndex].Select();
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.F4 || keyData == (Keys.Alt | Keys.Down) || keyData == Keys.Enter)
            {
                PerformClick();
                return true;
            }
            if (keyData == Keys.Up || keyData == Keys.Down)
            {
                int next = selectedIndex + (keyData == Keys.Up ? -1 : 1);
                if (next >= 0 && next < options.Length) SelectedIndex = next;
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && menu != null) menu.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class AppCheckBox : CheckBox
    {
        internal AppCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        internal static void DrawMark(Graphics g, RectangleF box, bool check, bool enabled)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Fill(g, check && enabled ? Theme.SoftGreen : Theme.White, box, 5);
            Theme.Border(g, check && enabled ? Theme.Green : Theme.Line, box, 5);
            if (check) Theme.Icon(g, "check", RectangleF.Inflate(box, -1.5f, -1.5f), enabled ? Theme.Green : Theme.Muted);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Paper : Parent.BackColor);
            DrawMark(e.Graphics, new RectangleF(2, (Height - 18) / 2f, 18, 18), Checked, Enabled);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(27, 0, Width - 28, Height),
                Enabled ? Theme.Ink : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                Theme.Border(e.Graphics, Theme.Green, new RectangleF(0, 1, Width - 1, Height - 3), 7);
        }
    }

    internal sealed class AppChecklist : CheckedListBox
    {
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var background = new SolidBrush(Theme.White)) e.Graphics.FillRectangle(background, e.Bounds);
            if (selected)
                Theme.Fill(e.Graphics, Theme.SoftGreen, Rectangle.Inflate(e.Bounds, -2, 0), 7);
            AppCheckBox.DrawMark(e.Graphics, new RectangleF(e.Bounds.Left + 7, e.Bounds.Top + (e.Bounds.Height - 15) / 2f,
                15, 15), GetItemChecked(e.Index), Enabled);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font,
                new Rectangle(e.Bounds.Left + 30, e.Bounds.Top, e.Bounds.Width - 35, e.Bounds.Height), Theme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
