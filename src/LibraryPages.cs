using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal sealed partial class MainWindow
    {
        private void RefreshAlternative()
        {
            foreach (Control control in alternative.Controls.Cast<Control>().ToArray()) control.Dispose();
            if (page == "sims") SimDirectory();
            else DynastyDirectory();
        }

        private void SimDirectory()
        {
            var label = Ui.Label("Поиск по имени", 13, Theme.Muted, false, true);
            label.SetBounds(23, 15, 180, 24);
            alternative.Controls.Add(label);
            var field = new AppField("Поиск по имени");
            field.SetBounds(23, 43, 286, field.Height);
            alternative.Controls.Add(field);
            var search = field.Input;
            var list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false,
                BorderStyle = BorderStyle.None, Font = Theme.Font(15), BackColor = Theme.White, ForeColor = Theme.Ink, OwnerDraw = true };
            list.Columns.Add("Имя", 265); list.Columns.Add("Возраст", 155); list.Columns.Add("Роль в династии", 195);
            list.DrawColumnHeader += (s, e) =>
            {
                using (var surface = new SolidBrush(Theme.Paper)) e.Graphics.FillRectangle(surface, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, Rectangle.Inflate(e.Bounds, -9, 0), Theme.Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            list.DrawSubItem += (s, e) =>
            {
                using (var surface = new SolidBrush(e.Item.Selected ? Theme.SoftGreen : Theme.White))
                    e.Graphics.FillRectangle(surface, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, Rectangle.Inflate(e.Bounds, -9, 0),
                    e.ColumnIndex == 0 ? Theme.Ink : Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            list.SetBounds(23, 94, alternative.Width - 46, alternative.Height - 164);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            alternative.Controls.Add(list);
            Action update = () =>
            {
                list.BeginUpdate(); list.Items.Clear();
                foreach (var sim in tree.Sims.Where(s => !s.IsAnimal() &&
                    s.Name.IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0).OrderBy(s => s.Name))
                {
                    var item = new ListViewItem(new[] { sim.Name, sim.Age ?? "", sim.Role ?? "" }) { Tag = sim.Id };
                    list.Items.Add(item);
                }
                list.EndUpdate();
            };
            search.TextChanged += (s, e) => update();
            Action open = () =>
            {
                if (list.SelectedItems.Count == 0) return;
                OpenMember((string)list.SelectedItems[0].Tag);
            };
            list.DoubleClick += (s, e) => open();
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) open(); };
            var show = Ui.Button("Открыть на древе", "arrow", 168, 38, open, true);
            show.Location = new Point(22, alternative.Height - 51);
            show.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            alternative.Controls.Add(show);
            var edit = Ui.Button("Редактировать", "edit", 155, 38, () =>
            {
                if (list.SelectedItems.Count != 0) Edit((string)list.SelectedItems[0].Tag);
            });
            edit.Location = new Point(196, alternative.Height - 51);
            edit.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            alternative.Controls.Add(edit);
            update();
        }

        private void DynastyDirectory()
        {
            var create = Ui.Button("Новая династия", "plus", 160, 40, CreateTree, true);
            create.Location = new Point(22, 17);
            alternative.Controls.Add(create);
            var read = Ui.Button("Открыть файл проекта", "folder", 196, 40, OpenProject);
            read.Location = new Point(191, 17);
            alternative.Controls.Add(read);
            var loadSave = Ui.Button("Из игры (.save)", "gem", 165, 40, ImportSave);
            loadSave.Location = new Point(395, 17);
            loadSave.Surface = Theme.SoftGreen;
            alternative.Controls.Add(loadSave);
            var list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.White };
            list.SetBounds(22, 80, alternative.Width - 44, alternative.Height - 93);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            alternative.Controls.Add(list);
            foreach (var entry in library.Trees)
            {
                var card = new RoundedPanel(17) { BackColor = Theme.Paper, Width = Math.Max(500, alternative.Width - 75), Height = 86, Margin = new Padding(0, 0, 0, 10) };
                var heading = Ui.Label(entry.Title, 22, Theme.Ink, true);
                heading.SetBounds(15, 10, card.Width - 310, 31);
                card.Controls.Add(heading);
                int animals = entry.Sims.Count(s => s.IsAnimal());
                var description = Ui.Label(Plural(entry.Sims.Count - animals, "сим", "сима", "симов") +
                    (animals > 0 ? "  ·  " + Plural(animals, "животное", "животных", "животных") : "") +
                    (String.IsNullOrWhiteSpace(entry.Author) ? "" : "  ·  " + entry.Author), 13, Theme.Muted);
                description.SetBounds(16, 45, card.Width - 310, 24);
                card.Controls.Add(description);
                var open = Ui.Button("Открыть", "arrow", 104, 37, () => OpenTree(entry));
                open.Location = new Point(card.Width - 119, 23);
                card.Controls.Add(open);
                var remove = Ui.Button("Удалить", "close", 94, 37, () => DeleteTree(entry));
                remove.Location = new Point(card.Width - 219, 23); remove.ForeColor = Theme.Danger;
                card.Controls.Add(remove);
                var copy = Ui.Button("Файл", "export", 75, 37, () => SaveProject(entry));
                copy.Location = new Point(card.Width - 296, 23);
                card.Controls.Add(copy);
                list.Controls.Add(card);
            }
        }

        private void CreateTree()
        {
            var draft = new Dynasty();
            using (var dialog = new Dialog("Новая династия", 500, 411, "Создать"))
            {
                var name = dialog.Field("Название", "");
                var author = dialog.Field("Автор", "");
                var story = dialog.Field("История династии", "", 4);
                dialog.ValidateAndSave = () =>
                {
                    if (String.IsNullOrWhiteSpace(name.Text)) throw new InvalidDataException("Введите название династии.");
                    draft.Title = name.Text.Trim(); draft.Author = author.Text.Trim(); draft.About = story.Text.Trim();
                };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
            }
            library.Trees.Add(draft); OpenTree(draft);
        }

        private void DeleteTree(Dynasty entry)
        {
            if (AppNotice.Show(this, "Удалить «" + entry.Title + "» из локальной библиотеки?", Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
            library.Trees.Remove(entry);
            if (library.AutoImport != null && library.AutoImport.TreeId == entry.Id)
            {
                library.AutoImport = null;
                UpdateAutoImportTimer();
            }
            foreach (var other in library.Trees)
                foreach (var sim in other.Sims.Where(s => s.LinkedTree == entry.Id)) sim.LinkedTree = null;
            if (library.Trees.Count == 0) library.Trees.Add(new Dynasty());
            if (tree == entry)
            {
                tree = library.Trees[0]; library.Active = tree.Id; selected = null;
            }
            undoStack.Clear(); redoStack.Clear();
            dirty = true;
            if (Save()) status.Text = "Династия удалена";
            RefreshTree(false);
        }

        private void OpenProject()
        {
            using (var picker = new OpenFileDialog { Filter = "Династия Sims Legacy|*.simslegacy;*.json", Title = "Открыть файл проекта" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (new FileInfo(picker.FileName).Length > 120 * 1024 * 1024) throw new InvalidDataException("Размер проекта превышает 120 МБ.");
                    string data = File.ReadAllText(picker.FileName);
                    var treeCopy = Storage.Parse<Dynasty>(data);
                    Storage.Validate(treeCopy);
                    if (library.Trees.Any(t => t.Id == treeCopy.Id))
                    {
                        treeCopy.Title += " (копия)";
                        treeCopy.Id = Guid.NewGuid().ToString("N");
                    }
                    else if (String.IsNullOrWhiteSpace(treeCopy.Id)) treeCopy.Id = Guid.NewGuid().ToString("N");
                    library.Trees.Add(treeCopy); OpenTree(treeCopy);
                }
                catch (Exception ex) { AppNotice.Show(this, "Не удалось открыть проект. " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        private static string SafeName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Trim().TrimEnd('.');
        }

        private void SaveProject(Dynasty entry)
        {
            using (var picker = new SaveFileDialog { Filter = "Династия Sims Legacy|*.simslegacy", FileName = SafeName(entry.Title),
                DefaultExt = "simslegacy", AddExtension = true, Title = "Сохранить файл проекта" })
                if (picker.ShowDialog(this) == DialogResult.OK)
                    try { Storage.Write(picker.FileName, Storage.Json(entry)); status.Text = "Файл проекта сохранён"; }
                    catch (Exception ex) { AppNotice.Show(this, "Не удалось сохранить проект. " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void ExportMenu()
        {
            if (exportContextMenu == null)
            {
                exportContextMenu = CreateContextMenu();
                exportContextMenu.Items.Add("Изображение PNG", null, (s, e) =>
                {
                    using (var picker = new SaveFileDialog { Filter = "Изображение PNG|*.png",
                        FileName = SafeName(tree.Title + (AnimalPage ? " — древо животных" : "")),
                        DefaultExt = "png", AddExtension = true, Title = AnimalPage ? "Экспорт древа животных" : "Экспорт семейного древа" })
                        if (picker.ShowDialog(this) == DialogResult.OK)
                            try { canvas.ExportPng(picker.FileName); status.Text = "Изображение сохранено"; }
                            catch (Exception ex) { AppNotice.Show(this, "Не удалось сохранить изображение. " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                });
                exportContextMenu.Items.Add("Файл проекта .simslegacy", null, (s, e) => SaveProject(tree));
            }
            exportContextMenu.Show(export, new Point(0, export.Height + 3));
        }

        private void ImportMenu()
        {
            if (importing) return;
            if (importContextMenu == null)
            {
                importContextMenu = CreateContextMenu();
                importContextMenu.Items.Add("Из сохранения The Sims 4 (.save)", null, (s, e) => ImportSave());
                importContextMenu.Items.Add("Отчёты MCCC и GEDCOM", null, (s, e) => ImportMccc());
            }
            importContextMenu.Show(import, new Point(0, import.Height + 5));
        }

        private string SelectGameSave(bool currentSlotOnly, string previous = null)
        {
            using (var picker = new OpenFileDialog { Filter = currentSlotOnly ? "Сохранения The Sims 4|*.save" :
                "Сохранения The Sims 4|*.save;*.save.ver0;*.save.ver1;*.save.ver2;*.save.ver3;*.save.ver4",
                Title = "Выбрать сохранение The Sims 4" })
            {
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string[] locations = { Path.Combine(documents, "Electronic Arts", "The Sims 4", "saves"),
                    Path.Combine(documents, "Electronic Arts", "Les Sims 4", "saves"),
                    Path.Combine(documents, "Electronic Arts", "Die Sims 4", "saves") };
                picker.InitialDirectory = previous == null ? locations.FirstOrDefault(Directory.Exists) ?? documents :
                    Path.GetDirectoryName(previous);
                if (previous != null) picker.FileName = Path.GetFileName(previous);
                return picker.ShowDialog(this) == DialogResult.OK ? picker.FileName : null;
            }
        }

        private async void ImportSave()
        {
            if (importing) return;
            string file = SelectGameSave(false);
            if (file == null) return;

            var target = tree;
            GameSave game;
            importing = true;
            import.Enabled = false;
            status.Text = "Читаем сохранение The Sims 4…";
            try { game = await Task.Run(() => GameSave.Open(file)); }
            catch (Exception ex)
            {
                status.Text = "Не удалось прочитать сохранение";
                AppNotice.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally { importing = false; import.Enabled = true; }
            if (tree != target) { status.Text = "Импорт отменён после смены династии"; return; }

            var households = game.OrderedHouseholds();
            Dynasty incoming = null;
            bool newTree = true;
            using (var dialog = new Dialog("Импорт из The Sims 4", 550, 405, "Продолжить"))
            {
                dialog.Note("Сохранение «" + game.Title + "» · " + Plural(game.People.Count, "сим", "сима", "симов"), 42);
                var options = households.Cast<object>().Concat(new object[] { "Все симы в сохранении" }).ToArray();
                var group = dialog.Choice("Семья", options, 0);
                var relatives = dialog.Toggle("Включить родственников из других семей", true);
                relatives.Enabled = group.SelectedIndex < households.Length;
                group.SelectedIndexChanged += (s, e) => relatives.Enabled = group.SelectedIndex < households.Length;
                var destination = dialog.Choice("Куда импортировать", new object[] { "Новая династия", "Текущая династия · " + target.Title }, 0);
                dialog.Note("На следующем шаге можно убрать отдельных симов.", 34);
                if (dialog.ShowDialog(this) != DialogResult.OK) { status.Text = "Импорт отменён"; return; }
                var household = group.SelectedIndex < households.Length ? households[group.SelectedIndex] : null;
                incoming = game.CreateTree(household, relatives.Checked);
                newTree = destination.SelectedIndex == 0;
            }
            var chosen = ReviewImport(incoming, newTree ? incoming : target, "Импорт из The Sims 4");
            if (chosen == null) { status.Text = "Импорт отменён"; return; }
            if (!newTree)
                Change(() => Mccc.Merge(target, incoming, chosen), true, "Симы из сохранения добавлены");
            else
            {
                var created = new Dynasty { Title = incoming.Title, About = incoming.About };
                Mccc.Merge(created, incoming, chosen);
                library.Trees.Add(created);
                OpenTree(created);
                if (!dirty) status.Text = "Династия импортирована из сохранения";
            }
        }

        private async void ImportMccc()
        {
            if (importing) return;
            string[] files;
            using (var picker = new OpenFileDialog { Filter = "Файлы MCCC|*.txt;*.log;*.ged;*.gedcom|Все файлы|*.*",
                Title = "Выбрать отчёты MCCC или файлы GEDCOM", Multiselect = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                files = picker.FileNames;
            }
            var target = tree;
            Dynasty incoming;
            importing = true; import.Enabled = false;
            status.Text = "Чтение отчёта MCCC…";
            try
            {
                incoming = await Task.Run(() =>
                {
                    var combined = new Dynasty();
                    foreach (string file in files)
                    {
                        var parsed = Mccc.Parse(Mccc.Read(file), file);
                        Mccc.Merge(combined, parsed, new HashSet<string>(parsed.Sims.Select(s => s.Id)));
                    }
                    return combined;
                });
            }
            catch (Exception ex)
            {
                status.Text = "Не удалось прочитать отчёт";
                AppNotice.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally { importing = false; import.Enabled = true; }
            if (incoming.Sims.Count == 0)
            {
                status.Text = "Карточки симов в файле не найдены";
                AppNotice.Show(this, "Проверьте, что в файле есть имена симов. Подходят подробный отчёт Log Sim Details, отчёт get_pop с именами и экспорт MC Gedcom.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (tree != target) { status.Text = "Импорт отменён после смены династии"; return; }
            var chosen = ReviewImport(incoming, target);
            if (chosen == null) { status.Text = "Импорт отменён"; return; }
            Change(() => Mccc.Merge(target, incoming, chosen), true, "Импорт MCCC завершён");
        }

        private HashSet<string> ReviewImport(Dynasty incoming, Dynasty target, string heading = "Импорт MCCC")
        {
            var entries = incoming.Sims.OrderBy(s => s.Name).ToList();
            using (var dialog = new Dialog(heading, 590, 635, "Импорт"))
            {
                dialog.Note("Династия «" + target.Title + "». Найдено " + Plural(entries.Count, "сим", "сима", "симов") +
                    ", с чертами характера — " + entries.Count(s => !String.IsNullOrEmpty(s.Traits)) + ". Отметьте карточки для импорта.", 60);
                var list = new AppChecklist { Height = 254, BackColor = Theme.White, ForeColor = Theme.Ink, Font = Theme.Font(15),
                    CheckOnClick = true, HorizontalScrollbar = true, AccessibleName = "Карточки для импорта" };
                var search = dialog.Field("Поиск симов", "");
                var selectedIds = new HashSet<string>(entries.Select(s => s.Id));
                var visible = entries;
                bool updating = false;
                Action showResults = () =>
                {
                    updating = true;
                    list.BeginUpdate();
                    list.Items.Clear();
                    visible = entries.Where(s => s.Name.IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
                    foreach (var sim in visible)
                        list.Items.Add(sim.Name + (String.IsNullOrWhiteSpace(sim.Traits) ? "" : "  ·  " + sim.Traits), selectedIds.Contains(sim.Id));
                    list.EndUpdate();
                    updating = false;
                };
                list.ItemCheck += (s, e) =>
                {
                    if (updating) return;
                    if (e.NewValue == CheckState.Checked) selectedIds.Add(visible[e.Index].Id);
                    else selectedIds.Remove(visible[e.Index].Id);
                };
                search.TextChanged += (s, e) => showResults();
                showResults();
                dialog.Add(list);
                var buttons = new FlowLayoutPanel { Height = 40, WrapContents = false };
                buttons.Controls.Add(Ui.Button("Выбрать всех", "check", 135, 34, () =>
                { selectedIds.UnionWith(entries.Select(s => s.Id)); showResults(); }));
                buttons.Controls.Add(Ui.Button("Снять выбор", null, 122, 34, () => { selectedIds.Clear(); showResults(); }));
                dialog.Add(buttons, 1);
                dialog.ValidateAndSave = () =>
                {
                    if (selectedIds.Count == 0) throw new InvalidDataException("Отметьте хотя бы одного сима.");
                };
                return dialog.ShowDialog(this) == DialogResult.OK ? selectedIds : null;
            }
        }

        private void Help()
        {
            using (var dialog = new Dialog("Справка", 600, 590, "Закрыть"))
            {
                dialog.Caption("Работа с деревом");
                dialog.Note("Щёлкните по портрету, чтобы открыть карточку. Двойной щелчок открывает редактор, правый — меню родственников. Перетаскивайте портреты и фон, меняйте масштаб колесом мыши. Ctrl+Z и Ctrl+Y отменяют и повторяют изменения.", 81);
                dialog.Caption("Импорт из игры без модов");
                dialog.Note("Сохраните игру, нажмите «Из игры» и выберите Slot_*.save из папки Electronic Arts\\The Sims 4\\saves. Выберите семью и при необходимости добавьте родственников из других семей.", 85);
                dialog.Caption("Импорт имён и черт из MCCC");
                dialog.Note("Нажмите на сима и выберите MC Command Center → Sim Commands → Logging Commands → Log Sim Details. Включите Traits и создайте отчёт. Для списка симов на участке введите get_pop s m lot в консоли. В меню «Импорт → Отчёты MCCC и GEDCOM» можно выбрать несколько файлов сразу.", 108);
                dialog.Caption("Импорт семейных связей");
                dialog.Note("С модулем MC Gedcom введите gedcom Имя Фамилия. Откройте созданный .ged через «Отчёты MCCC и GEDCOM», чтобы добавить симов и родственные связи в открытую династию.", 72);
                dialog.Caption("Обмен династиями");
                dialog.Note("Экспортируйте дерево как PNG или сохраните файл .simslegacy вместе с портретами и биографиями. Файлы других игроков открываются в разделе «Мои династии».", 62);
                dialog.ShowDialog(this);
            }
        }
    }
}
