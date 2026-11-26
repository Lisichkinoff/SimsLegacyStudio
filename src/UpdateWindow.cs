using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal sealed partial class MainWindow
    {
        private bool checkingUpdates, downloadingUpdate;
        private UpdateInstaller pendingUpdate;
        private string LegacyUpdateTokenPath { get { return Path.Combine(Path.GetDirectoryName(path), "update-access.dat"); } }

        private void ShowUpdatesMenu(Control anchor)
        {
            var menu = CreateContextMenu();
            menu.Items.Add("Версия " + AppUpdates.CurrentVersion.ToString(3)).Enabled = false;
            menu.Items.Add("Проверить обновления", null, (s, e) => CheckUpdates(true)).Enabled = !checkingUpdates;
            menu.Closed += (s, e) =>
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(menu.Dispose));
                else menu.Dispose();
            };
            menu.Show(anchor, 0, anchor.Height);
        }

        private Tuple<AppUpdates.Update, string> ReadRelease(string manager)
        {
            var credential = GitHubSignIn.ReadExisting(manager);
            var release = TryReadRelease(credential);
            if (release != null) return release;

            // Older installations may already have a working credential saved for this repository.
            string legacy;
            try { legacy = AppUpdates.LoadLegacyToken(LegacyUpdateTokenPath); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (FormatException) { return null; }
            catch (CryptographicException) { return null; }
            return legacy == credential ? null : TryReadRelease(legacy);
        }

        private static Tuple<AppUpdates.Update, string> TryReadRelease(string credential)
        {
            if (String.IsNullOrEmpty(credential)) return null;
            try { return Tuple.Create(AppUpdates.Check(credential), credential); }
            catch (AppUpdates.AccessException) { return null; }
        }

        private async void CheckUpdates(bool manual)
        {
            if (checkingUpdates) return;
            UpdateInstaller installer = null;
            checkingUpdates = true;
            try
            {
                if (manual) status.Text = "Проверяем обновления…";
                string manager = GitHubSignIn.FindCredentialManager();
                var release = await Task.Run(() => ReadRelease(manager));
                if (IsDisposed || Disposing) return;
                if (release == null)
                {
                    if (!manual) return;
                    status.Text = "Для обновлений нужен доступ к GitHub";
                    if (manager == null)
                    {
                        ShowUpdateError("Установите Git for Windows с Git Credential Manager, затем повторите проверку обновлений.");
                        return;
                    }
                    if (AppNotice.Show(this, "Войти в GitHub через браузер для скачивания закрытых обновлений?",
                        "Обновления", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    status.Text = "Ожидаем вход в GitHub…";
                    bool signedIn = await Task.Run(() => GitHubSignIn.OpenBrowserSignIn(manager));
                    if (IsDisposed || Disposing) return;
                    if (!signedIn)
                    {
                        status.Text = "Вход в GitHub не завершён";
                        return;
                    }
                    release = await Task.Run(() => ReadRelease(manager));
                    if (IsDisposed || Disposing) return;
                    if (release == null) throw new AppUpdates.AccessException();
                }
                var update = release.Item1;
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
                try { await Task.Run(() => installer.Download(release.Item2)); }
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
