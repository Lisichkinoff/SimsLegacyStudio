using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal sealed partial class MainWindow
    {
        private bool checkingUpdates, downloadingUpdate;
        private UpdateInstaller pendingUpdate;

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

        private async void CheckUpdates(bool manual)
        {
            if (checkingUpdates) return;
            UpdateInstaller installer = null;
            checkingUpdates = true;
            try
            {
                if (manual) status.Text = "Проверяем обновления…";
                var update = await Task.Run(() => AppUpdates.Check());
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
                try { await Task.Run(() => installer.Download()); }
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
