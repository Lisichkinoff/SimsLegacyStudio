using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Sims Legacy Studio")]
[assembly: System.Reflection.AssemblyDescription("Семейные древа для The Sims 4")]
[assembly: System.Reflection.AssemblyProduct("Sims Legacy Studio")]

namespace SimsLegacy
{
    internal static class Program
    {
        internal const string MutexName = @"Local\SimsLegacyStudio.Library.v1";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--install-update")
            {
                UpdateInstaller.Run(args);
                return;
            }

            Guid updateId = Guid.Empty;
            int installerId = 0;
            bool restarting = args.Length == 3 && args[0] == "--finish-update" &&
                Guid.TryParseExact(args[1], "N", out updateId) && int.TryParse(args[2], out installerId) && installerId > 0;
            using (var mutex = new Mutex(false, MutexName))
            {
                bool locked;
                try { locked = mutex.WaitOne(restarting ? 15000 : 0); }
                catch (AbandonedMutexException) { locked = true; }
                if (!locked)
                {
                    AppNotice.Show("Sims Legacy Studio уже открыта.", "Sims Legacy Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    if (restarting) UpdateInstaller.Finish(updateId, installerId);
                    string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimsLegacyStudio");
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, "library.json");
                    var library = Load(path);
                    if (library.Trees.Count == 0) library.Trees.Add(Dynasty.Example());
                    Application.Run(new MainWindow(library, path));
                }
                catch (Exception ex)
                {
                    AppNotice.Show("Не удалось открыть приложение. " + ex.Message, "Sims Legacy Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static Library Load(string path)
        {
            if (!File.Exists(path)) return new Library();
            try
            {
                var library = Storage.Parse<Library>(File.ReadAllText(path));
                if (library == null || library.Trees == null) throw new InvalidDataException("Не удалось прочитать библиотеку.");
                foreach (var tree in library.Trees) Storage.Validate(tree);
                return library;
            }
            catch (Exception)
            {
                string backup = path + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                File.Copy(path, backup);
                AppNotice.Show("Библиотека не открылась. Резервная копия сохранена:\n" + backup,
                    "Sims Legacy Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return new Library();
            }
        }
    }
}
