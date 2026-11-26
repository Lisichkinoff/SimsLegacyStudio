using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SimsLegacy
{
    internal static class GitHubSignIn
    {
        internal static string FindCredentialManager()
        {
            var roots = new[] {
                Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\GitForWindows", "InstallPath", null) as string,
                Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\GitForWindows", "InstallPath", null) as string,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git")
            };
            foreach (var root in roots)
            {
                if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) continue;
                foreach (var directory in new[] { @"mingw64\bin", @"mingw32\bin", @"mingwarm64\bin" })
                {
                    string path = Path.Combine(root, directory, "git-credential-manager.exe");
                    if (File.Exists(path)) return path;
                }
            }
            return null;
        }

        internal static string ReadExisting(string manager)
        {
            if (manager == null) return null;
            string owner = AppUpdates.Repository.Split('/')[0];
            var credential = ReadExisting(manager, "username=" + owner + "\n");
            return credential ?? ReadExisting(manager, "");
        }

        private static string ReadExisting(string manager, string username)
        {
            string output;
            try { output = Run(manager, "get", "protocol=https\nhost=github.com\n" + username + "\n", 8000); }
            catch (Win32Exception) { return null; }
            catch (IOException) { return null; }
            if (output == null) return null;
            using (var reader = new StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    if (line.StartsWith("password=", StringComparison.Ordinal) && line.Length > 9)
                        return line.Substring(9);
            }
            return null;
        }

        internal static bool OpenBrowserSignIn(string manager)
        {
            return Run(manager, "github login --url https://github.com --browser --force", null, 120000) != null;
        }

        private static string Run(string manager, string arguments, string input, int timeout)
        {
            var start = new ProcessStartInfo(manager, arguments) {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(manager),
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.EnvironmentVariables["GCM_INTERACTIVE"] = input == null ? "true" : "false";
            start.EnvironmentVariables["GCM_TRACE"] = "0";
            start.EnvironmentVariables["GCM_TRACE_SECRETS"] = "false";
            start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            using (var process = Process.Start(start))
            {
                if (process == null) return null;
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                if (input != null) process.StandardInput.Write(input);
                process.StandardInput.Close();
                if (!process.WaitForExit(timeout))
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    return null;
                }
                if (process.ExitCode != 0 || !output.Wait(1000) || !errors.Wait(1000)) return null;
                return output.Result;
            }
        }
    }
}
