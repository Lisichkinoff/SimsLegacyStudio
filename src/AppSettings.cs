using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal sealed partial class MainWindow
    {
        private readonly Timer autoImportTimer = new Timer { Interval = 4000 };
        private bool checkingSave, autoImportWarning;
        private Tuple<long, DateTime, DateTime> lastSaveStamp;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowEnabled(IntPtr window);

        private bool CanApplyAutoImport()
        {
            return IsHandleCreated && !IsDisposed && !Disposing && !importing && !canvas.Capture &&
                IsWindowEnabled(Handle);
        }

        private Dynasty AutoImportTarget(AutoImportSettings settings)
        {
            return settings == null ? null : library.Trees.FirstOrDefault(item => item.Id == settings.TreeId);
        }

        private void ChangeTheme(bool dark)
        {
            library.DarkTheme = dark;
            dirty = true;
            Theme.Apply(this, dark);
            profileTitle.ForeColor = Theme.Ink;
            foreach (var menu in new[] { simContextMenu, importContextMenu, exportContextMenu })
                if (menu != null)
                {
                    menu.BackColor = Theme.White;
                    menu.ForeColor = Theme.Ink;
                }
            RefreshTree(false);
        }

        private void ShowSettings()
        {
            var settings = library.AutoImport;
            var target = AutoImportTarget(settings);
            using (var dialog = new Dialog("Настройки", 615, 415))
            {
                var theme = dialog.Choice("Тема приложения", new object[] { "Светлая", "Тёмная" }, library.DarkTheme ? 1 : 0);
                var enabled = dialog.Toggle("Автоматически добавлять новых симов", target != null && settings.Enabled);
                dialog.Note(target == null ? "При включении выберите сохранение The Sims 4, семью и династию." :
                    Path.GetFileName(settings.FilePath) + " · " + settings.HouseholdName + " → " + target.Title, 38);
                dialog.Note("Новые карточки появятся после сохранения игры. Карточки, которые уже в династии, можно редактировать вручную.", 51);
                dialog.Add(Ui.Button("Выбрать сохранение и семью…", "folder", 271, 37, () =>
                {
                    dialog.DialogResult = DialogResult.Retry;
                }));

                var result = dialog.ShowDialog(this);
                if (result != DialogResult.OK && result != DialogResult.Retry) return;
                if (library.DarkTheme != (theme.SelectedIndex == 1)) ChangeTheme(theme.SelectedIndex == 1);
                if (result == DialogResult.Retry || (enabled.Checked && target == null))
                {
                    if (dirty) Save();
                    ConfigureAutoImport(result == DialogResult.OK || enabled.Checked);
                    return;
                }
                if (target != null && settings.Enabled != enabled.Checked)
                {
                    settings.Enabled = enabled.Checked;
                    lastSaveStamp = null;
                    autoImportWarning = false;
                    dirty = true;
                    if (Save()) status.Text = enabled.Checked ? "Автоимпорт включён" : "Автоимпорт выключен";
                    UpdateAutoImportTimer();
                }
                else if (dirty) Save();
            }
        }

        private async void ConfigureAutoImport(bool enable)
        {
            if (importing) return;
            var previous = library.AutoImport;
            string file = SelectGameSave(true, previous == null ? null : previous.FilePath);
            if (file == null) return;
            if (!String.Equals(Path.GetExtension(file), ".save", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Для автоимпорта выберите основной файл сохранения .save.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            importing = true;
            status.Text = "Читаем семьи из сохранения…";
            GameSave game;
            try { game = await Task.Run(() => GameSave.Open(file)); }
            catch (Exception ex)
            {
                status.Text = "Не удалось прочитать сохранение";
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally { importing = false; }

            var households = game.OrderedHouseholds();
            if (households.Length == 0)
            {
                status.Text = "В сохранении нет семей для автоимпорта.";
                return;
            }
            var targets = library.Trees.ToArray();
            using (var dialog = new Dialog("Автоимпорт симов", 590, 410))
            {
                dialog.Note("Сохранение «" + game.Title + "» · " + Path.GetFileName(file), 34);
                int familyIndex = previous == null ? -1 : Array.FindIndex(households, item => item.Id == previous.HouseholdId);
                var family = dialog.Choice("Семья в игре", households, Math.Max(0, familyIndex));
                var relatives = dialog.Toggle("Включать родственников из других семей", previous == null || previous.IncludeRelatives);
                int targetIndex = previous == null ? -1 : Array.FindIndex(targets, item => item.Id == previous.TreeId);
                var destination = dialog.Choice("Династия в приложении", new object[] { "Создать новую династию" }
                    .Concat(targets.Select(item => (object)item.Title)).ToArray(), targetIndex + 1);
                var enabled = dialog.Toggle("Включить автоимпорт", enable);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    status.Text = "Настройка автоимпорта отменена";
                    return;
                }

                var household = (SaveHousehold)family.SelectedItem;
                bool create = destination.SelectedIndex == 0;
                var chosen = create ? new Dynasty { Title = household.Name, About = household.Description } :
                    targets[destination.SelectedIndex - 1];
                bool sameSource = previous != null && previous.TreeId == chosen.Id &&
                    String.Equals(previous.FilePath, file, StringComparison.OrdinalIgnoreCase);
                library.AutoImport = new AutoImportSettings { Enabled = enabled.Checked, FilePath = file, TreeId = chosen.Id,
                    HouseholdId = household.Id, HouseholdName = household.Name, IncludeRelatives = relatives.Checked,
                    SeenGameIds = sameSource ? previous.SeenGameIds : null };
                lastSaveStamp = null;
                autoImportWarning = false;
                dirty = true;
                if (create)
                {
                    library.Trees.Add(chosen);
                    OpenTree(chosen);
                }
                else Save();
                if (!dirty) status.Text = enabled.Checked ? "Автоимпорт включён" : "Сохранение для автоимпорта выбрано";
                UpdateAutoImportTimer();
            }
        }

        private void UpdateAutoImportTimer()
        {
            var settings = library.AutoImport;
            autoImportTimer.Interval = 4000;
            autoImportTimer.Enabled = settings != null && settings.Enabled && AutoImportTarget(settings) != null;
            if (autoImportTimer.Enabled) CheckAutoImport();
        }

        private async void CheckAutoImport()
        {
            var settings = library.AutoImport;
            if (settings == null || !settings.Enabled || checkingSave || !CanApplyAutoImport()) return;
            var previous = lastSaveStamp;
            checkingSave = true;
            try
            {
                var read = await Task.Run(() =>
                {
                    var stamp = AutoImport.ReadStamp(settings.FilePath);
                    if (stamp.Equals(previous)) return null;
                    // Allow the game to save or swap the file while we read it. Only use an unchanged snapshot.
                    var game = GameSave.Open(settings.FilePath, FileShare.ReadWrite | FileShare.Delete);
                    if (!stamp.Equals(AutoImport.ReadStamp(settings.FilePath))) return null;
                    var household = game.Households.Find(item => item.Id == settings.HouseholdId);
                    if (household == null) throw new InvalidDataException("Выбранная семья больше не найдена в сохранении.");
                    return Tuple.Create(stamp, game.CreateTree(household, settings.IncludeRelatives));
                });
                if (!CanApplyAutoImport() || settings != library.AutoImport || !settings.Enabled) return;
                if (read == null) return;
                var target = AutoImportTarget(settings);
                if (target == null) { UpdateAutoImportTimer(); return; }

                var batch = AutoImport.Prepare(target, read.Item2, settings);
                int count = batch.NewSims.Count;
                if (count > 0 && target == tree)
                {
                    Remember(undoStack, Storage.Json(tree));
                    redoStack.Clear();
                }
                if (count > 0 || batch.Seen.Count != (settings.SeenGameIds == null ? 0 : settings.SeenGameIds.Count))
                {
                    target.Sims.AddRange(batch.NewSims);
                    target.Links.AddRange(batch.NewLinks);
                    settings.SeenGameIds = batch.Seen.ToList();
                    dirty = true;
                }
                bool saved = !dirty || Save();
                if (count > 0 && (target == tree || page == "library")) RefreshTree(false);
                if (!saved) return;
                if (count > 0)
                    status.Text = "Автоимпорт · добавлено " + Plural(count, "карточка", "карточки", "карточек");
                else if (autoImportWarning) status.Text = "Автоимпорт включён";
                autoImportWarning = false;
                autoImportTimer.Interval = 4000;
                lastSaveStamp = read.Item1;
            }
            catch (Exception ex)
            {
                if (!CanApplyAutoImport() || settings != library.AutoImport || !settings.Enabled) return;
                status.Text = "Автоимпорт ожидает доступное сохранение";
                tips.SetToolTip(status, ex.Message);
                autoImportWarning = true;
                autoImportTimer.Interval = 12000;
            }
            finally { checkingSave = false; }
        }
    }
}
