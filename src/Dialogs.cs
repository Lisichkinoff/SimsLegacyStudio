using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal class Dialog : Form
    {
        private readonly FlowLayoutPanel body;
        private readonly ActionButton save;
        private readonly int fieldWidth;
        public Action ValidateAndSave;

        public Dialog(string title, int width = 540, int height = 570, string accept = "Сохранить")
        {
            Text = title; BackColor = Theme.Paper; ForeColor = Theme.Ink; Font = Theme.Font(14);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(width, Math.Min(height, Screen.PrimaryScreen.WorkingArea.Height - 65));
            fieldWidth = width - 68;
            var heading = Ui.Label(title, 27, Theme.Ink, true);
            heading.SetBounds(25, 17, width - 50, 41);
            Controls.Add(heading);
            body = new FlowLayoutPanel {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
                Padding = new Padding(26, 4, 13, 12)
            };
            body.SetBounds(0, 65, width, ClientSize.Height - 133);
            Controls.Add(body);
            var cancel = Ui.Button("Отмена", null, 104, 38, () => Close());
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(width - 252, ClientSize.Height - 54, 102, 38);
            Controls.Add(cancel);
            save = Ui.Button(accept, "check", 120, 38, () =>
            {
                try
                {
                    if (ValidateAndSave != null) ValidateAndSave();
                    DialogResult = DialogResult.OK;
                }
                catch (InvalidDataException e) { MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
            }, true);
            save.SetBounds(width - 143, ClientSize.Height - 54, 119, 38);
            Controls.Add(save);
            CancelButton = cancel;
            AcceptButton = save;
        }

        public void Add(Control control, int bottom = 12)
        {
            control.Width = fieldWidth;
            control.Margin = new Padding(0, 0, 0, bottom);
            body.Controls.Add(control);
        }

        public void Note(string text, int height)
        {
            var label = Ui.Label(text, 13, Theme.Muted);
            label.Height = height;
            Add(label, 8);
        }

        public void Caption(string text)
        {
            var label = Ui.Label(text, 12, Theme.Ink, false, true);
            label.Height = 22;
            Add(label, 1);
        }

        public TextBox Field(string label, string value, int lines = 1)
        {
            Caption(label);
            var input = new TextBox {
                Text = value ?? "", Multiline = lines > 1, Height = lines > 1 ? lines * 21 + 8 : 27,
                ScrollBars = lines > 1 ? ScrollBars.Vertical : ScrollBars.None, MaxLength = lines > 1 ? 16000 : 180,
                Font = Theme.Font(15), ForeColor = Theme.Ink, BackColor = Theme.White, AccessibleName = label,
                BorderStyle = BorderStyle.None
            };
            var shell = new RoundedPanel(10) { Height = input.Height + (lines > 1 ? 18 : 13), BackColor = Theme.White };
            shell.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Theme.Border(e.Graphics, input.Focused ? Theme.TreeAccent : Theme.Line,
                    new RectangleF(1, 1, shell.Width - 3, shell.Height - 3), 10);
            };
            input.Location = new Point(10, lines > 1 ? 8 : 9);
            shell.Resize += (s, e) => input.Width = Math.Max(1, shell.Width - 20);
            input.GotFocus += (s, e) => shell.Invalidate();
            input.LostFocus += (s, e) => shell.Invalidate();
            shell.Controls.Add(input);
            Add(shell);
            return input;
        }

        public ComboBox Choice(string label, object[] options, int index)
        {
            Caption(label);
            var input = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Font(15), ForeColor = Theme.Ink,
                BackColor = Theme.White, FlatStyle = FlatStyle.Flat, AccessibleName = label
            };
            if (Theme.Dark)
            {
                input.DrawMode = DrawMode.OwnerDrawFixed;
                input.ItemHeight = 22;
                input.DrawItem += (s, e) =>
                {
                    bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                    using (var surface = new SolidBrush(selected ? Theme.SidebarActive : Theme.White))
                        e.Graphics.FillRectangle(surface, e.Bounds);
                    if (e.Index >= 0)
                        TextRenderer.DrawText(e.Graphics, input.GetItemText(input.Items[e.Index]), input.Font,
                            Rectangle.Inflate(e.Bounds, -4, 0), Theme.Ink,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    e.DrawFocusRectangle();
                };
            }
            input.Items.AddRange(options);
            if (index >= 0 && index < options.Length) input.SelectedIndex = index;
            Add(input);
            return input;
        }

        public CheckBox Toggle(string label, bool value)
        {
            var check = new CheckBox { Text = label, Checked = value, ForeColor = Theme.Ink, Height = 26, Font = Theme.Font(14) };
            Add(check, 4);
            return check;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.StyleCaption(this);
        }
    }

    internal static class Ui
    {
        public static Label Label(string text, int size, Color color, bool serif = false, bool bold = false)
        {
            return new Label { Text = text, ForeColor = color, Font = serif ? Theme.Display(size) :
                Theme.Font(size, bold ? FontStyle.Bold : FontStyle.Regular), AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        }

        public static ActionButton Button(string label, string icon, int w, int h, Action action, bool primary = false)
        {
            var button = new ActionButton(label, icon) { Size = new Size(w, h) };
            if (primary)
            {
                button.Surface = Theme.Lime; button.Stroke = Color.Transparent; button.ForeColor = Color.FromArgb(24, 62, 46);
            }
            button.Click += (s, e) => action();
            return button;
        }

        public static OpenFileDialog ImagePicker()
        {
            return new OpenFileDialog { Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif", Title = "Выбрать изображение" };
        }
    }

    internal static class Editors
    {
        public static Sim SimCard(Form owner, Sim source, Dynasty tree, List<Dynasty> library, bool newAnimal = false)
        {
            Sim draft = source == null ? new Sim() : Storage.Parse<Sim>(Storage.Json(source));
            bool animal = source == null ? newAnimal : source.IsAnimal();
            string title = animal
                ? (source == null ? "Новое животное" : "Карточка животного")
                : (source == null ? "Новый сим" : "Редактирование сима");
            using (var dialog = new Dialog(title, 545, 682))
            {
                var name = dialog.Field(animal ? "Кличка" : "Имя и фамилия", draft.Name);
                var species = animal ? dialog.Field("Вид или порода", draft.Species) : null;
                var age = dialog.Field("Возрастная стадия", draft.Age);
                var role = animal ? null : dialog.Field("Роль в династии", draft.Role);
                var traits = dialog.Field("Черты характера", draft.Traits, 2);
                var career = animal ? null : dialog.Field("Карьера", draft.Career);
                var goal = animal ? null : dialog.Field("Жизненная цель", draft.Aspiration);
                var birthplace = dialog.Field("Место рождения", draft.Birthplace);
                var education = animal ? null : dialog.Field("Образование", draft.Education);
                var bio = dialog.Field("Биография", draft.Bio, 3);
                var dead = dialog.Toggle(animal ? "Животное умерло" : "Сим умер", draft.Deceased);
                var available = library.Where(t => t.Id != tree.Id).ToList();
                var options = new List<object> { "Без ссылки" };
                options.AddRange(available.Select(t => (object)t.Title));
                var linked = dialog.Choice("Ссылка на другую династию", options.ToArray(), available.FindIndex(t => t.Id == draft.LinkedTree) + 1);
                dialog.Caption("Фотографии");
                var gallery = new FlowLayoutPanel { Height = 79, WrapContents = false, AutoScroll = true };
                dialog.Add(gallery, 3);
                var photos = new ListBox { Height = 85, Font = Theme.Font(14), BackColor = Theme.White,
                    ForeColor = Theme.Ink, AccessibleName = "Галерея фотографий" };
                dialog.Add(photos);
                Action refreshPhotos = () =>
                {
                    foreach (Control control in gallery.Controls.Cast<Control>().ToArray()) control.Dispose();
                    photos.Items.Clear();
                    for (int i = 0; i < draft.Photos.Count; i++)
                    {
                        var picture = Photos.Thumbnail(draft.Photos[i], 62);
                        int index = i;
                        picture.Click += (s, e) => { photos.SelectedIndex = index; ShowPhoto(dialog, draft.Photos[index]); };
                        gallery.Controls.Add(picture);
                        photos.Items.Add(i == 0 ? "Фото 1 · портрет на древе" : "Фото " + (i + 1));
                    }
                    if (photos.Items.Count > 0) photos.SelectedIndex = 0;
                };
                refreshPhotos();
                var actions = new FlowLayoutPanel { Height = 40, WrapContents = false };
                actions.Controls.Add(Ui.Button("Добавить фото", "plus", 136, 34, () =>
                {
                    if (draft.Photos.Count >= 20) { MessageBox.Show(dialog, "В одной карточке можно хранить до 20 фотографий."); return; }
                    using (var picker = Ui.ImagePicker())
                        if (picker.ShowDialog(dialog) == DialogResult.OK)
                            try { draft.Photos.Add(Photos.Import(picker.FileName, 1100)); refreshPhotos(); }
                            catch (Exception e) { MessageBox.Show(dialog, "Не удалось открыть изображение. " + e.Message); }
                }));
                actions.Controls.Add(Ui.Button("Сделать портретом", null, 148, 34, () =>
                {
                    if (photos.SelectedIndex < 1) return;
                    string photo = draft.Photos[photos.SelectedIndex];
                    draft.Photos.RemoveAt(photos.SelectedIndex);
                    draft.Photos.Insert(0, photo);
                    refreshPhotos();
                }));
                actions.Controls.Add(Ui.Button("Убрать", "close", 91, 34, () =>
                {
                    if (photos.SelectedIndex < 0) return;
                    draft.Photos.RemoveAt(photos.SelectedIndex);
                    refreshPhotos();
                }));
                dialog.Add(actions);
                dialog.ValidateAndSave = () =>
                {
                    if (String.IsNullOrWhiteSpace(name.Text)) throw new InvalidDataException(animal ? "Введите кличку." : "Введите имя сима.");
                    if (animal && String.IsNullOrWhiteSpace(species.Text)) throw new InvalidDataException("Укажите вид или породу животного.");
                    draft.Name = name.Text.Trim(); draft.Age = age.Text.Trim();
                    if (animal) draft.Species = species.Text.Trim();
                    else
                    {
                        draft.Role = role.Text.Trim(); draft.Career = career.Text.Trim();
                        draft.Aspiration = goal.Text.Trim(); draft.Education = education.Text.Trim();
                    }
                    draft.Traits = traits.Text.Trim(); draft.Birthplace = birthplace.Text.Trim(); draft.Bio = bio.Text.Trim();
                    draft.Deceased = dead.Checked; draft.LinkedTree = linked.SelectedIndex > 0 ? available[linked.SelectedIndex - 1].Id : null;
                };
                return dialog.ShowDialog(owner) == DialogResult.OK ? draft : null;
            }
        }

        public static void ShowPhoto(Form owner, string photo)
        {
            using (var image = Photos.Read(photo, true))
            using (var form = new Form { Text = "Фотография", StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Sidebar,
                ClientSize = new Size(Math.Min(800, Screen.PrimaryScreen.WorkingArea.Width - 60), Math.Min(650, Screen.PrimaryScreen.WorkingArea.Height - 75)),
                ShowInTaskbar = false, KeyPreview = true })
            {
                if (image == null) return;
                form.HandleCreated += (s, e) => Theme.StyleCaption(form);
                form.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Zoom });
                form.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };
                form.ShowDialog(owner);
            }
        }

        public static void Metadata(Form owner, Dynasty tree, Action<Action> change)
        {
            using (var dialog = new Dialog("О династии", 500, 410))
            {
                var name = dialog.Field("Название", tree.Title);
                var author = dialog.Field("Автор", tree.Author);
                var about = dialog.Field("История династии", tree.About, 5);
                dialog.ValidateAndSave = () =>
                {
                    if (String.IsNullOrWhiteSpace(name.Text)) throw new InvalidDataException("Введите название династии.");
                };
                if (dialog.ShowDialog(owner) == DialogResult.OK)
                    change(() => { tree.Title = name.Text.Trim(); tree.Author = author.Text.Trim(); tree.About = about.Text.Trim(); });
            }
        }

        public static void Appearance(Form owner, Dynasty tree, Action<Action> change)
        {
            using (var dialog = new Dialog("Оформление древа", 510, 505))
            {
                var layout = dialog.Choice("Вид карточек", new object[] { "Круглые портреты", "Квадратные портреты", "Компактные портреты" }, tree.Layout);
                var grid = dialog.Toggle("Точечная сетка на фоне", tree.Grid);
                var labels = dialog.Toggle("Имена и подписи", tree.Labels);
                dialog.Caption("Палитра фона");
                Color background = Theme.CanvasBackground(tree.Background), accent = Theme.CanvasAccent(tree.Accent);
                bool backgroundChanged = false, accentChanged = false;
                string picture = tree.Backdrop;
                var colors = new FlowLayoutPanel { Height = 42, WrapContents = false };
                var custom = Ui.Button("Свой цвет", null, 115, 34, () =>
                {
                    using (var picker = new ColorDialog { Color = background, FullOpen = true })
                        if (picker.ShowDialog(dialog) == DialogResult.OK) { background = picker.Color; backgroundChanged = true; }
                });
                string[] palette = Theme.Dark ? new[] { "#172837", "#1E3732", "#3D2B2A", "#322C44", "#35332B" } :
                    new[] { "#EAF7FE", "#E5F7ED", "#FBEFE9", "#F1EDFD", "#F5F4EC" };
                string[] names = { "Небо", "Мята", "Персик", "Лаванда", "Лён" };
                for (int i = 0; i < palette.Length; i++)
                {
                    Color color = ColorTranslator.FromHtml(palette[i]);
                    var swatch = Ui.Button("", null, 40, 34, () =>
                    {
                        background = color;
                        backgroundChanged = true;
                        custom.Surface = color;
                        custom.ForeColor = Theme.CanvasText(color);
                        custom.Invalidate();
                    });
                    swatch.Surface = color;
                    swatch.AccessibleName = names[i];
                    colors.Controls.Add(swatch);
                }
                custom.Surface = background;
                custom.ForeColor = Theme.CanvasText(background);
                custom.Click += (s, e) =>
                {
                    custom.Surface = background;
                    custom.ForeColor = Theme.CanvasText(background);
                    custom.Invalidate();
                };
                colors.Controls.Add(custom);
                dialog.Add(colors);
                var accentButton = Ui.Button("Цвет линий и акцента", "tune", 200, 36, () =>
                {
                    using (var picker = new ColorDialog { Color = accent, FullOpen = true })
                        if (picker.ShowDialog(dialog) == DialogResult.OK) { accent = picker.Color; accentChanged = true; }
                });
                dialog.Add(accentButton);
                var add = Ui.Button(String.IsNullOrEmpty(picture) ? "Выбрать фоновое изображение" : "Заменить фоновое изображение", "import", 270, 38, () =>
                {
                    using (var picker = Ui.ImagePicker())
                        if (picker.ShowDialog(dialog) == DialogResult.OK)
                            try { picture = Photos.Import(picker.FileName, 2000); }
                            catch (Exception e) { MessageBox.Show(dialog, "Не удалось открыть изображение. " + e.Message); }
                });
                add.Click += (s, e) => { if (picture != null) add.Text = "Заменить фоновое изображение"; };
                dialog.Add(add, 3);
                dialog.Add(Ui.Button("Убрать фоновое изображение", "close", 270, 36,
                    () => { picture = null; add.Text = "Выбрать фоновое изображение"; }));
                if (dialog.ShowDialog(owner) == DialogResult.OK)
                    change(() =>
                    {
                        tree.Layout = layout.SelectedIndex; tree.Grid = grid.Checked; tree.Labels = labels.Checked;
                        if (backgroundChanged) tree.Background = ColorTranslator.ToHtml(background);
                        if (accentChanged) tree.Accent = ColorTranslator.ToHtml(accent);
                        tree.Backdrop = picture;
                    });
            }
        }
    }
}
