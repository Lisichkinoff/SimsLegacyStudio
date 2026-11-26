using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal sealed partial class MainWindow
    {
        private bool checkingUpdates, downloadingUpdate;
        private UpdateInstaller pendingUpdate;
        private string UpdateTokenPath { get { return Path.Combine(Path.GetDirectoryName(path), "update-access.dat"); } }

        private void ShowUpdatesMenu(Control anchor)
        {
            var menu = CreateContextMenu();
            menu.Items.Add("Версия " + AppUpdates.CurrentVersion.ToString(3)).Enabled = false;
            menu.Items.Add("Проверить обновления", null, (s, e) => CheckUpdates(true)).Enabled = !checkingUpdates;
            menu.Items.Add("Доступ к GitHub…", null, (s, e) =>
            {
                if (ConfigureUpdates()) CheckUpdates(true);
            }).Enabled = !checkingUpdates;
            menu.Closed += (s, e) =>
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(menu.Dispose));
                else menu.Dispose();
            };
            menu.Show(anchor, 0, anchor.Height);
        }

        private bool ConfigureUpdates()
        {
            using (var dialog = new Dialog("Доступ к GitHub", 585, 365))
            {
                dialog.Note("Для обновлений из закрытого репозитория " + AppUpdates.Repository +
                    " создайте fine-grained token. В Repository access выберите этот репозиторий, а в Permissions укажите Contents → Read-only.", 73);
                var link = new LinkLabel { Text = "Создать ключ на GitHub", Height = 25, Font = Theme.Font(14),
                    LinkColor = Theme.TreeAccent, VisitedLinkColor = Theme.TreeAccent, ActiveLinkColor = Theme.Green };
                link.LinkClicked += (s, e) =>
                {
                    try
                    {
                        using (Process.Start(new ProcessStartInfo("https://github.com/settings/personal-access-tokens/new" +
                            "?name=Sims+Legacy+Studio+Updates&target_name=" + AppUpdates.Repository.Split('/')[0] +
                            "&contents=read") { UseShellExecute = true })) { }
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        ShowUpdateError("Не удалось открыть GitHub в браузере.");
                    }
                };
                dialog.Add(link);
                var key = dialog.Field("Ключ GitHub", "");
                key.UseSystemPasswordChar = true;
                key.MaxLength = 512;
                dialog.Note("Ключ шифруется средствами Windows. Чтобы отключить проверки при запуске, сохраните пустое поле.", 48);
                bool enabled = false;
                dialog.ValidateAndSave = () =>
                {
                    string token = key.Text.Trim();
                    if (token.Length != 0 && !Regex.IsMatch(token, @"\Agithub_pat_[a-zA-Z0-9_]+\z"))
                        throw new InvalidDataException("Вставьте fine-grained token, начинающийся с github_pat_.");
                    try
                    {
                        enabled = token.Length != 0;
                        if (enabled) AppUpdates.SaveToken(UpdateTokenPath, token);
                        else File.Delete(UpdateTokenPath);
                    }
                    catch (Exception ex) { throw new InvalidDataException("Не удалось сохранить настройки. " + ex.Message); }
                };
                return dialog.ShowDialog(this) == DialogResult.OK && enabled;
            }
        }

        private async void CheckUpdates(bool manual)
        {
            if (checkingUpdates) return;
            UpdateInstaller installer = null;
            checkingUpdates = true;
            try
            {
                string token;
                try { token = AppUpdates.LoadToken(UpdateTokenPath); }
                catch (Exception)
                {
                    if (!manual) return;
                    ShowUpdateError("Не удалось прочитать сохранённый ключ. Укажите ключ снова.");
                    if (!ConfigureUpdates()) return;
                    token = AppUpdates.LoadToken(UpdateTokenPath);
                }
                if (string.IsNullOrEmpty(token))
                {
                    if (!manual || !ConfigureUpdates()) return;
                    token = AppUpdates.LoadToken(UpdateTokenPath);
                }
                if (manual) status.Text = "Проверяем обновления…";

                var update = await Task.Run(() => AppUpdates.Check(token));
                if (IsDisposed || Disposing) return;
                if (update == null)
                {
                    if (manual) status.Text = "Установлена последняя версия " + AppUpdates.CurrentVersion.ToString(3);
                    return;
                }
                string newVersion = update.Version.ToString(3);
                status.Text = "Доступна версия " + newVersion;
                if (AppNotice.Show(this, "Доступна версия " + newVersion +
                    ". Скачать обновление и перезапустить приложение?", "Обновления",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                downloadingUpdate = true;
                status.Text = "Скачиваем обновление " + newVersion + "…";
                installer = new UpdateInstaller(update);
                try { await Task.Run(() => installer.Download(token)); }
                finally { downloadingUpdate = false; }
                pendingUpdate = installer;
                Close();
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing) return;
                status.Text = "Не удалось проверить или скачать обновление";
                if (manual || installer != null) ShowUpdateError(ex.Message);
                else tips.SetToolTip(status, ex.Message);
            }
            finally
            {
                checkingUpdates = false;
                if (installer != null && !IsDisposed) installer.Discard();
                pendingUpdate = null;
            }
        }

        private bool StartUpdate()
        {
            if (dirty && !Save())
            {
                ShowUpdateError("Не удалось сохранить библиотеку. Установка отменена.");
                return false;
            }
            try
            {
                pendingUpdate.Start();
                return true;
            }
            catch (Exception ex)
            {
                status.Text = "Не удалось запустить установку обновления";
                ShowUpdateError(ex.Message);
                return false;
            }
        }

        private void ShowUpdateError(string message)
        {
            AppNotice.Show(this, message, "Обновления", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
