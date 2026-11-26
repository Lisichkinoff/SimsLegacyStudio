using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal sealed partial class MainWindow : Form
    {
        private readonly Library library;
        private readonly string path;
        private readonly Panel rail, masthead, workspace, board, profile, alternative, statusBar;
        private readonly Label title, subtitle, count, status, profileTitle, boardTitle;
        private readonly ActionButton add, import, gameImport, export, previewButton, rename, undo, redo;
        private readonly ActionButton treeNav, animalNav, simNav, libraryNav, updatesNav;
        private readonly FamilyCanvas canvas;
        private readonly ToolTip tips = new ToolTip();
        private readonly List<string> undoStack = new List<string>(), redoStack = new List<string>();
        private ContextMenuStrip simContextMenu, importContextMenu, exportContextMenu;
        private Dynasty tree;
        private string selected, page = "tree";
        private bool preview, dirty, importing;
        private bool AnimalPage { get { return page == "animals"; } }

        public MainWindow(Library value, string storagePath)
        {
            library = value; path = storagePath;
            tree = library.Trees.FirstOrDefault(t => t.Id == library.Active) ?? library.Trees[0];
            selected = tree.Sims.FirstOrDefault(s => s.Role == "Наследница") != null
                ? tree.Sims.First(s => s.Role == "Наследница").Id : null;
            Text = "Sims Legacy Studio";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }
            BackColor = Theme.Paper; ForeColor = Theme.Ink; Font = Theme.Font(14);
            AutoScaleMode = AutoScaleMode.None; KeyPreview = true; MinimumSize = new Size(1050, 620);
            var screen = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1440, screen.Width - 28), Math.Min(845, screen.Height - 45));
            StartPosition = FormStartPosition.CenterScreen;
            SuspendLayout();
            rail = new RoundedPanel(24) { BackColor = Theme.Sidebar };
            rail.Paint += PaintRail;
            Controls.Add(rail);
            treeNav = Nav("Семейное древо", "tree", () => Navigate("tree"), 112);
            animalNav = Nav("Древо животных", "paw", () => Navigate("animals"), 161);
            simNav = Nav("Все симы", "people", () => Navigate("sims"), 210);
            libraryNav = Nav("Мои династии", "folder", () => Navigate("library"), 259);
            Nav("Оформление", "tune", () => Editors.Appearance(this, tree, a => Change(a)), 348);
            Nav("Справка", "help", Help, 397);
            updatesNav = Nav("Обновления", "import", () => ShowUpdatesMenu(updatesNav), 446);
            masthead = new Panel { BackColor = Theme.Paper };
            Controls.Add(masthead);
            title = Ui.Label("", 35, Theme.Ink, true);
            subtitle = Ui.Label("", 14, Theme.Muted);
            masthead.Controls.Add(title); masthead.Controls.Add(subtitle);
            rename = Header("", "edit", "О династии и её авторе", 39, () => Editors.Metadata(this, tree, a => Change(a)));
            import = Header("Импорт", "import", "Источники для импорта", 98, ImportMenu);
            gameImport = Header("Из игры", "gem", "Импорт сохранения The Sims 4", 105, ImportSave);
            gameImport.Surface = Theme.SoftGreen;
            gameImport.ForeColor = Theme.Green;
            gameImport.Stroke = Color.Transparent;
            export = Header("Экспорт", "export", "Экспорт династии или изображения", 104, ExportMenu);
            add = Header("Добавить сима", "plus", "Создать карточку сима", 148, () => Edit(null), true);
            workspace = new Panel { BackColor = Theme.Paper };
            Controls.Add(workspace);
            board = new RoundedPanel(23) { BackColor = Theme.White };
            workspace.Controls.Add(board);
            board.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, 0, 52, board.Width, 52);
            };
            var treeIcon = new Panel();
            treeIcon.SetBounds(17, 12, 32, 30);
            treeIcon.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.Fill(e.Graphics, Theme.Paper, treeIcon.ClientRectangle, 11);
                Theme.Icon(e.Graphics, AnimalPage ? "paw" : "tree", new RectangleF(6, 5, 20, 20), Theme.TreeAccent);
            };
            board.Controls.Add(treeIcon);
            boardTitle = Ui.Label("", 15, Theme.Ink, false, true);
            boardTitle.SetBounds(56, 9, 165, 36);
            board.Controls.Add(boardTitle);
            undo = BoardButton("", "undo", "Отменить (Ctrl+Z)", 36, () => History(false));
            redo = BoardButton("", "redo", "Повторить (Ctrl+Y)", 36, () => History(true));
            previewButton = BoardButton("Просмотр", "eye", "Режим просмотра дерева", 116, TogglePreview);
            canvas = new FamilyCanvas { SelectSim = Select, EditSim = id => Edit(id), ContextSim = SimMenu, OpenMember = OpenMember,
                MoveSim = (sim, old, next) => Change(() => { sim.OffsetX = next.X; sim.OffsetY = next.Y; }) };
            board.Controls.Add(canvas);
            profile = new RoundedPanel(23) { BackColor = Theme.White };
            workspace.Controls.Add(profile);
            alternative = new RoundedPanel(23) { BackColor = Theme.White, Visible = false };
            workspace.Controls.Add(alternative);
            profileTitle = Ui.Label("Карточка сима", 14, Theme.Ink, false, true);
            statusBar = new Panel { BackColor = Theme.Paper };
            Controls.Add(statusBar);
            status = Ui.Label("Изменения сохраняются автоматически", 12, Theme.Muted);
            count = Ui.Label("", 12, Theme.Muted);
            count.TextAlign = ContentAlignment.MiddleRight;
            statusBar.Controls.Add(status); statusBar.Controls.Add(count);
            Resize += (s, e) => LayoutWindow();
            FormClosing += (s, e) =>
            {
                if (downloadingUpdate) { e.Cancel = true; return; }
                if (importing) { e.Cancel = true; return; }
                if (pendingUpdate != null)
                {
                    e.Cancel = !StartUpdate();
                    return;
                }
                if (dirty && !Save() && MessageBox.Show(this, "Изменения не сохранены. Закрыть приложение?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) e.Cancel = true;
            };
            Shown += (s, e) => CheckUpdates(false);
            ResumeLayout();
            LayoutWindow();
            RefreshTree(true);
        }

        private void PaintRail(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (rail.Height > 489)
            {
                var lower = new Rectangle(0, 484, rail.Width, rail.Height - 484);
                using (var gradient = new LinearGradientBrush(lower, Theme.Sidebar, Color.FromArgb(17, 67, 88), 90f))
                    g.FillRectangle(gradient, lower);
            }
            Theme.Fill(g, Color.FromArgb(33, 77, 101), new RectangleF(16, 25, 45, 45), 15);
            Theme.Icon(g, "gem", new RectangleF(22, 31, 33, 33), Theme.Lime);
            using (var brand = Theme.Display(21))
            using (var minor = Theme.Font(11))
            using (var division = new Pen(Color.FromArgb(50, 93, 117)))
            {
                Theme.Text(g, "Sims Legacy", brand, Theme.White, new RectangleF(68, 22, 138, 30));
                Theme.Text(g, "THE SIMS 4", minor, Color.FromArgb(169, 210, 230), new RectangleF(70, 50, 130, 19));
                g.DrawLine(division, 20, 322, rail.Width - 20, 322);
                g.DrawLine(division, 20, rail.Height - 69, rail.Width - 20, rail.Height - 69);
                Theme.Icon(g, "check", new RectangleF(23, rail.Height - 49, 17, 17), Theme.Lime);
                Theme.Text(g, "Локальная библиотека", minor, Theme.SidebarText, new RectangleF(47, rail.Height - 54, 155, 26));
            }
        }

        private ActionButton Nav(string label, string icon, Action run, int y)
        {
            var button = Ui.Button(label, icon, 184, 43, run);
            button.Location = new Point(14, y);
            button.AlignLeft = true; button.Surface = Color.Transparent; button.Stroke = Color.Transparent; button.ForeColor = Theme.SidebarText;
            rail.Controls.Add(button);
            return button;
        }

        private ActionButton Header(string label, string icon, string hint, int width, Action action, bool primary = false)
        {
            var button = Ui.Button(label, icon, width, 40, action, primary);
            if (label.Length == 0) button.AccessibleName = hint;
            tips.SetToolTip(button, hint);
            masthead.Controls.Add(button);
            return button;
        }

        private ActionButton BoardButton(string label, string icon, string hint, int width, Action action)
        {
            var button = Ui.Button(label, icon, width, 35, action);
            if (label.Length == 0) button.AccessibleName = hint;
            tips.SetToolTip(button, hint);
            board.Controls.Add(button);
            return button;
        }

        private void LayoutWindow()
        {
            int left = 246, w = ClientSize.Width - left - 19;
            rail.SetBounds(15, 15, 212, ClientSize.Height - 30); rail.Invalidate();
            masthead.SetBounds(left, 6, w, 108);
            bool narrow = w < 920;
            int y = narrow ? 68 : 43;
            add.Location = new Point(w - add.Width, y);
            export.Location = new Point(add.Left - export.Width - 7, y);
            import.Location = new Point(export.Left - import.Width - 7, y);
            gameImport.Location = new Point(import.Left - gameImport.Width - 7, y);
            rename.Location = new Point(Math.Max(4, gameImport.Left - 46), y);
            int textWidth = page == "library" || narrow ? w : preview ? export.Left - 10 : rename.Left - 9;
            title.SetBounds(0, narrow ? 8 : 21, Math.Max(100, textWidth), 47);
            subtitle.SetBounds(2, narrow ? 53 : 69, Math.Max(180, rename.Left - 10), narrow ? 19 : 25);
            int top = narrow ? 133 : 114;
            masthead.Height = top - 10;
            int h = ClientSize.Height - top - 41;
            workspace.SetBounds(left, top, w, h);
            int profileWidth = Math.Min(310, Math.Max(255, w / 4));
            int boardWidth = preview ? w : w - profileWidth - 15;
            board.SetBounds(0, 0, boardWidth, h);
            previewButton.Location = new Point(boardWidth - 130, 9);
            redo.Location = new Point(previewButton.Left - 45, 9);
            undo.Location = new Point(redo.Left - 41, 9);
            canvas.SetBounds(1, 53, boardWidth - 2, Math.Max(1, h - 54));
            profile.SetBounds(boardWidth + 15, 0, profileWidth, h);
            alternative.SetBounds(0, 0, w, h);
            statusBar.SetBounds(left + 3, ClientSize.Height - 37, w - 6, 28);
            status.SetBounds(0, 0, Math.Max(1, w - 285), 28);
            count.SetBounds(Math.Max(0, w - 280), 0, 280, 28);
        }

        private void RefreshTree(bool fit)
        {
            bool animals = AnimalPage;
            if (!tree.Sims.Any(s => s.Id == selected && s.IsAnimal() == animals)) selected = null;
            bool isTree = page == "tree" || animals;
            boardTitle.Text = animals ? "Древо животных" : "Семейное древо";
            board.Invalidate(true);
            profileTitle.Text = animals ? "Карточка животного" : "Карточка сима";
            title.Text = page == "sims" ? "Все симы" : page == "library" ? "Мои династии" : tree.Title;
            subtitle.Text = page == "library" ? "Локальная коллекция семейных деревьев" :
                page == "sims" ? tree.Title : String.IsNullOrWhiteSpace(tree.Author) ? "Династия The Sims 4" : "Династия The Sims 4  ·  " + tree.Author;
            board.Visible = isTree; profile.Visible = isTree && !preview; alternative.Visible = !isTree;
            rename.Visible = isTree && !preview; add.Visible = page != "library" && !preview;
            add.Text = animals ? "Добавить животное" : "Добавить сима";
            add.Width = animals ? 190 : 148;
            tips.SetToolTip(add, animals ? "Создать карточку животного" : "Создать карточку сима");
            import.Visible = gameImport.Visible = page != "library" && !preview; export.Visible = page != "library";
            undo.Visible = redo.Visible = !preview;
            undo.Enabled = undoStack.Count > 0; redo.Enabled = redoStack.Count > 0;
            previewButton.Text = preview ? "Редактор" : "Просмотр";
            previewButton.Glyph = preview ? "edit" : "eye";
            foreach (var pair in new[] { Tuple.Create(treeNav, "tree"), Tuple.Create(animalNav, "animals"),
                Tuple.Create(simNav, "sims"), Tuple.Create(libraryNav, "library") })
            {
                pair.Item1.Surface = page == pair.Item2 ? Theme.SidebarActive : Color.Transparent;
                pair.Item1.ForeColor = page == pair.Item2 ? Theme.Lime : Theme.SidebarText;
                pair.Item1.Invalidate();
            }
            canvas.Preview = preview; canvas.Selected = selected;
            LayoutWindow();
            canvas.ShowTree(tree, fit, animals);
            int total = tree.Sims.Count(s => s.IsAnimal() == animals);
            count.Text = Plural(canvas.Generations, "поколение", "поколения", "поколений") + "  ·  " +
                (animals ? Plural(total, "животное", "животных", "животных") : Plural(total, "сим", "сима", "симов"));
            if (isTree && !preview) RefreshProfile();
            if (!isTree) RefreshAlternative();
        }

        private static string Plural(int count, string single, string few, string many)
        {
            int one = count % 10, two = count % 100;
            return count + " " + (two >= 11 && two <= 14 ? many : one == 1 ? single : one >= 2 && one <= 4 ? few : many);
        }

        private void Select(string id)
        {
            if (preview) return;
            selected = id; canvas.Selected = id; canvas.Invalidate(); RefreshProfile();
        }

        private void RefreshProfile()
        {
            foreach (Control c in profile.Controls.Cast<Control>().ToArray())
            {
                profile.Controls.Remove(c);
                if (c != profileTitle) c.Dispose();
            }
            profileTitle.SetBounds(18, 10, 210, 35);
            profile.Controls.Add(profileTitle);
            var separator = new Panel { BackColor = Theme.Line, Dock = DockStyle.None };
            separator.SetBounds(0, 52, profile.Width, 1);
            separator.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            profile.Controls.Add(separator);
            var sim = tree.Sims.FirstOrDefault(s => s.Id == selected);
            if (sim == null)
            {
                var label = Ui.Label(AnimalPage ? "Выберите животное на древе, чтобы открыть карточку." :
                    "Выберите сима на древе, чтобы открыть его карточку.", 14, Theme.Muted);
                label.SetBounds(18, 70, profile.Width - 37, 60);
                label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                profile.Controls.Add(label);
                return;
            }
            var dismiss = Ui.Button("", "close", 30, 31, () => Select(null));
            dismiss.AccessibleName = "Закрыть карточку";
            dismiss.Stroke = Color.Transparent;
            dismiss.Location = new Point(profile.Width - 43, 12);
            dismiss.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            profile.Controls.Add(dismiss);
            var scroll = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
                Padding = new Padding(18, 13, 0, 12), BackColor = Theme.White };
            scroll.SetBounds(0, 54, profile.Width, profile.Height - 55);
            scroll.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            profile.Controls.Add(scroll);
            int width = profile.Width - 42;
            Action<Control, int> row = (control, margin) =>
            {
                control.Width = width;
                control.Margin = new Padding(0, 0, 0, margin);
                scroll.Controls.Add(control);
            };
            var avatarRow = new RoundedPanel(19) { Height = 109, BackColor = Theme.Paper };
            avatarRow.Paint += (s, e) =>
            {
                if (avatarRow.ClientSize.Width == 0) return;
                using (var brush = new LinearGradientBrush(avatarRow.ClientRectangle, Color.FromArgb(215, 240, 253), Color.FromArgb(234, 251, 223), 25))
                    e.Graphics.FillRectangle(brush, avatarRow.ClientRectangle);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.Fill(e.Graphics, Theme.White, new RectangleF((width - 89) / 2f, 10, 89, 89), 45);
            };
            row(avatarRow, 8);
            var avatar = new Avatar(sim) { Size = new Size(81, 81), Location = new Point((width - 81) / 2, 14) };
            avatarRow.Controls.Add(avatar);
            var name = Ui.Label(sim.Name, 24, Theme.Ink, true);
            name.Height = 36; name.TextAlign = ContentAlignment.MiddleCenter;
            row(name, 2);
            bool animal = sim.IsAnimal();
            string caption = String.Join("  ·  ", new[] { animal ? sim.Species : sim.Role,
                sim.Deceased ? (animal ? "Умерло" : "Умерший сим") : sim.Age }.Where(t => !String.IsNullOrWhiteSpace(t)).ToArray());
            if (caption.Length > 0)
            {
                var role = Ui.Label(caption, 12, Theme.Green, false, true);
                role.Height = 24; role.TextAlign = ContentAlignment.MiddleCenter;
                row(role, 11);
            }
            var actions = new Panel { Height = 37, BackColor = Theme.White };
            int editWidth = (width + 18) / 2;
            var editProfile = Ui.Button("Изменить", "edit", editWidth, 37, () => Edit(sim.Id));
            editProfile.Surface = Theme.Paper;
            editProfile.AccessibleName = "Редактировать карточку";
            var relationButton = Ui.Button("Связи", "link", width - editWidth - 6, 37, () => Relationships(sim));
            relationButton.AccessibleName = "Связи персонажа";
            relationButton.Location = new Point(editWidth + 6, 0);
            actions.Controls.Add(editProfile);
            actions.Controls.Add(relationButton);
            row(actions, 15);
            var relations = tree.Links.Where(link => Family.Touches(link, sim.Id) && (Family.IsFormer(link.Kind) || link.Kind == "owner"))
                .ToLookup(link => Family.IsFormer(link.Kind) ? "Бывшие партнёры" : animal ? "Хозяева" : "Питомцы");
            var members = tree.Sims.ToDictionary(member => member.Id);
            foreach (string heading in new[] { "Бывшие партнёры", animal ? "Хозяева" : "Питомцы" })
            {
                if (!relations.Contains(heading)) continue;
                var label = Ui.Label(heading, 12, Theme.Muted, false, true);
                label.Height = 20; row(label, 2);
                foreach (var link in relations[heading])
                {
                    var other = members[Family.Other(link, sim.Id)];
                    var type = Family.Type(link, sim.Id);
                    var button = Ui.Button(other.Name, type.Icon, width, 31, () => OpenMember(other.Id));
                    button.AlignLeft = true;
                    bool former = Family.IsFormer(link.Kind);
                    row(button, former ? 0 : 4);
                    if (former)
                    {
                        var relation = Ui.Label(type.Label, 12, Theme.Danger);
                        relation.Padding = new Padding(13, 0, 0, 0);
                        relation.Height = 17;
                        row(relation, 6);
                    }
                }
            }
            if (!String.IsNullOrWhiteSpace(sim.Traits))
            {
                var label = Ui.Label("Черты характера", 12, Theme.Muted, false, true);
                label.Height = 22; row(label, 4);
                var chips = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MaximumSize = new Size(width, 0), WrapContents = true };
                foreach (string trait in sim.Traits.Split(',', ';', '\n').Select(t => t.Trim()).Where(t => t.Length > 0))
                {
                    chips.Controls.Add(new TraitChip(trait, width - 5));
                }
                row(chips, 8);
            }
            var fields = new[] { Tuple.Create("Карьера", sim.Career), Tuple.Create("Жизненная цель", sim.Aspiration),
                Tuple.Create("Место рождения", sim.Birthplace), Tuple.Create("Образование", sim.Education), Tuple.Create("Биография", sim.Bio) };
            foreach (var field in fields.Where(f => !String.IsNullOrWhiteSpace(f.Item2)))
            {
                var label = Ui.Label(field.Item1, 12, Theme.Muted, false, true);
                label.Height = 19; row(label, 0);
                var value = Ui.Label(field.Item2, 13, Theme.Ink);
                value.AutoSize = true; value.MaximumSize = new Size(width, 0);
                row(value, 9);
            }
            if (sim.Photos.Count > 0)
            {
                var gallery = new FlowLayoutPanel { Height = 68, WrapContents = false, AutoScroll = true };
                foreach (string photo in sim.Photos)
                {
                    var thumbnail = Photos.Thumbnail(photo, 47);
                    thumbnail.Click += (s, e) => Editors.ShowPhoto(this, photo);
                    gallery.Controls.Add(thumbnail);
                }
                row(gallery, 7);
            }
            var linked = library.Trees.FirstOrDefault(t => t.Id == sim.LinkedTree);
            if (linked != null) row(Ui.Button(linked.Title, "arrow", width, 35, () => OpenTree(linked)), 6);
            var remove = Ui.Button(animal ? "Удалить животное" : "Удалить сима", "close", width, 34, () => DeleteSim(sim));
            remove.ForeColor = Theme.Danger; remove.Stroke = Color.Transparent;
            row(remove, 0);
        }

        private void Navigate(string next)
        {
            page = next; preview = false; RefreshTree(next == "tree" || next == "animals");
        }

        private void OpenMember(string id)
        {
            if (preview) return;
            var member = tree.Sims.First(s => s.Id == id);
            string targetPage = member.IsAnimal() ? "animals" : "tree";
            if (page == targetPage) Select(id);
            else { selected = id; Navigate(targetPage); }
            canvas.FocusOn(id);
        }

        private void TogglePreview()
        {
            preview = !preview;
            RefreshTree(true);
        }

        private void Edit(string id, Sim related = null, string relation = null)
        {
            var existing = tree.Sims.FirstOrDefault(s => s.Id == id);
            var result = Editors.SimCard(this, existing, tree, library.Trees, AnimalPage);
            if (result == null) return;
            Change(() =>
            {
                if (existing == null) tree.Sims.Add(result);
                else tree.Sims[tree.Sims.IndexOf(existing)] = result;
                if (related != null)
                    tree.Links.Add(new Link { From = relation == "ancestor" ? result.Id : related.Id,
                        To = relation == "ancestor" ? related.Id : result.Id, Kind = relation == "ancestor" ? "parent" : relation });
                selected = result.Id;
            }, existing == null);
        }

        private void SimMenu(Point point, Sim sim)
        {
            Select(sim.Id);
            var menu = simContextMenu ?? (simContextMenu = CreateContextMenu());
            foreach (var item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
            menu.Items.Add("Редактировать карточку", null, (s, e) => Edit(sim.Id));
            menu.Items.Add("Добавить нового родителя", null, (s, e) => Edit(null, sim, "ancestor"));
            menu.Items.Add("Добавить нового ребёнка", null, (s, e) => Edit(null, sim, "parent"));
            if (!sim.IsAnimal())
                menu.Items.Add("Добавить нового партнёра", null, (s, e) => Edit(null, sim, "partner"));
            menu.Items.Add("Управлять связями…", null, (s, e) => Relationships(sim));
            if (sim.OffsetX != 0 || sim.OffsetY != 0)
                menu.Items.Add("Сбросить положение", null, (s, e) => Change(() => { sim.OffsetX = 0; sim.OffsetY = 0; }, true));
            menu.Items.Add(sim.IsAnimal() ? "Удалить животное" : "Удалить сима", null, (s, e) => DeleteSim(sim));
            menu.Show(point);
        }

        private ContextMenuStrip CreateContextMenu()
        {
            return new ContextMenuStrip { Font = Font, BackColor = Theme.White, ForeColor = Theme.Ink };
        }

        private void Relationships(Sim sim)
        {
            using (var dialog = new RelationshipPicker(sim, tree, library.Trees))
                if (dialog.ShowDialog(this) == DialogResult.OK && !dialog.Links.SequenceEqual(tree.Links))
                    Change(() =>
                    {
                        tree.Sims.AddRange(dialog.NewMembers.Where(member => dialog.Links.Any(link => Family.Touches(link, member.Id))));
                        tree.Links = dialog.Links;
                    }, true);
        }

        private void DeleteSim(Sim sim)
        {
            if (MessageBox.Show(this, "Удалить карточку «" + sim.Name + "» и все её семейные связи?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Change(() => { tree.Sims.Remove(sim); tree.Links.RemoveAll(l => l.From == sim.Id || l.To == sim.Id); });
        }

        private void Change(Action action, bool fit = false, string note = "Изменения сохранены")
        {
            Remember(undoStack, Storage.Json(tree)); redoStack.Clear();
            action();
            dirty = true;
            if (Save()) status.Text = note;
            RefreshTree(fit);
        }

        private static void Remember(List<string> history, string snapshot)
        {
            history.Add(snapshot);
            long total = history.Sum(s => (long)s.Length);
            while (history.Count > 1 && (history.Count > 20 || total > 20000000))
            { total -= history[0].Length; history.RemoveAt(0); }
        }

        private void History(bool forward)
        {
            if (preview) return;
            var previous = forward ? redoStack : undoStack;
            var next = forward ? undoStack : redoStack;
            if (previous.Count == 0) return;
            Remember(next, Storage.Json(tree));
            var restored = Storage.Parse<Dynasty>(previous[previous.Count - 1]);
            previous.RemoveAt(previous.Count - 1);
            library.Trees[library.Trees.IndexOf(tree)] = tree = restored;
            dirty = true;
            if (Save()) status.Text = forward ? "Действие повторено" : "Действие отменено";
            RefreshTree(true);
        }

        private bool Save()
        {
            try
            {
                Storage.Write(path, Storage.Json(library));
                dirty = false;
                return true;
            }
            catch (Exception e)
            {
                status.Text = "Не удалось сохранить библиотеку";
                tips.SetToolTip(status, e.Message);
                return false;
            }
        }

        private void OpenTree(Dynasty target)
        {
            tree = target; library.Active = target.Id; dirty = true; Save();
            undoStack.Clear(); redoStack.Clear(); selected = null;
            Navigate("tree");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z: History(false); return true;
                case Keys.Control | Keys.Y: History(true); return true;
                case Keys.Control | Keys.S: if (Save()) status.Text = "Изменения сохранены"; return true;
                case Keys.Control | Keys.I: if (!preview) ImportSave(); return true;
                case Keys.Escape: if (preview) { TogglePreview(); return true; } break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (simContextMenu != null) simContextMenu.Dispose();
                if (importContextMenu != null) importContextMenu.Dispose();
                if (exportContextMenu != null) exportContextMenu.Dispose();
                tips.Dispose();
                profileTitle.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
