using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal sealed class UpdateInstaller
    {
        private const string PayloadName = "download.exe";
        private const string HelperName = "installer.exe";
        private const string BackupName = "backup.exe";
        private readonly string executable = Application.ExecutablePath;
        private readonly Guid id = Guid.NewGuid();
        private readonly AppUpdates.Update update;
        private readonly string directory;

        internal UpdateInstaller(AppUpdates.Update update)
        {
            this.update = update;
            directory = StagingDirectory(executable, id);
        }

        internal void Download()
        {
            Directory.CreateDirectory(directory);
            AppUpdates.Download(update, Path.Combine(directory, PayloadName));
        }

        internal void Start()
        {
            string helper = Path.Combine(directory, HelperName);
            File.Copy(executable, helper);
            using (var parent = Process.GetCurrentProcess())
                Launch(helper, "--install-update " + parent.Id + " " + id.ToString("N") +
                    " " + update.Version + " " + update.Asset.digest + " \"" + Path.GetFileName(executable) + "\"");
        }

        internal void Discard()
        {
            DeleteStaging(directory);
        }

        internal static void Run(string[] args)
        {
            try
            {
                int parentId;
                Guid id;
                Version version;
                if (args.Length != 6 || !int.TryParse(args[1], out parentId) || parentId <= 0 ||
                    !Guid.TryParseExact(args[2], "N", out id) || !Version.TryParse(args[3], out version) ||
                    version <= AppUpdates.CurrentVersion || args[5] != Path.GetFileName(args[5]) ||
                    !string.Equals(Path.GetExtension(args[5]), ".exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Неверные параметры установки.");

                string directory = Path.GetDirectoryName(Application.ExecutablePath);
                string target = Path.Combine(Path.GetDirectoryName(directory), args[5]);
                if (!string.Equals(Application.ExecutablePath, Path.Combine(StagingDirectory(target, id), HelperName),
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Неверная папка установки.");

                if (!WaitForExit(parentId)) throw new IOException("Предыдущий процесс ещё работает.");
                using (var mutex = new Mutex(false, Program.MutexName))
                {
                    bool locked;
                    try { locked = mutex.WaitOne(10000); }
                    catch (AbandonedMutexException) { locked = true; }
                    if (!locked) throw new IOException("Закройте запущенный экземпляр приложения и повторите обновление.");
                    try { ReplaceAndRestart(directory, target, version, args[4], id); }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                AppNotice.Show("Не удалось установить обновление. " + ex.Message, "Sims Legacy Studio",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ReplaceAndRestart(string directory, string target, Version version, string digest, Guid id)
        {
            string payload = Path.Combine(directory, PayloadName);
            string backup = Path.Combine(directory, BackupName);
            if (System.Reflection.AssemblyName.GetAssemblyName(target).Version != AppUpdates.CurrentVersion)
                throw new InvalidDataException("Установленная версия изменилась. Повторите проверку обновлений.");

            AppUpdates.Verify(payload, version, digest);
            File.Replace(payload, target, backup);
            try { Restart(target, id); }
            catch
            {
                File.Replace(backup, target, null);
                Restart(target, id);
                throw new IOException("Не удалось запустить новую версию. Предыдущая версия восстановлена.");
            }
        }

        private static void Restart(string target, Guid id)
        {
            using (var installer = Process.GetCurrentProcess())
                Launch(target, "--finish-update " + id.ToString("N") + " " + installer.Id);
        }

        internal static void Finish(Guid id, int installerId)
        {
            string directory = StagingDirectory(Application.ExecutablePath, id);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (WaitForExit(installerId)) DeleteStaging(directory);
            });
        }

        private static bool WaitForExit(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId)) return process.WaitForExit(15000);
            }
            catch (ArgumentException) { return true; }
            catch (InvalidOperationException) { return true; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        private static string StagingDirectory(string target, Guid id)
        {
            return Path.Combine(Path.GetDirectoryName(target), "." + Path.GetFileName(target) + ".update-" + id.ToString("N"));
        }

        private static void Launch(string executable, string arguments)
        {
            var info = new ProcessStartInfo(executable, arguments) { UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable) };
            using (var process = Process.Start(info))
                if (process == null) throw new IOException("Не удалось запустить " + Path.GetFileName(executable) + ".");
        }

        private static void DeleteStaging(string directory)
        {
            try
            {
                if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                File.Delete(Path.Combine(directory, PayloadName));
                File.Delete(Path.Combine(directory, HelperName));
                File.Delete(Path.Combine(directory, BackupName));
                Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
