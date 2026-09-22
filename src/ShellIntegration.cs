using System;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PasteImageAsFile
{
    public static class ShellIntegration
    {
        public const string AppName = "PasteImageAsFile";
        public const string MenuText = "Вставить изображение из буфера";
        public const string AppVersion = "1.1.0";

        public static string InstallDir
        {
            get
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localApp, "Programs", AppName);
            }
        }

        public static string InstalledExePath
        {
            get { return Path.Combine(InstallDir, AppName + ".exe"); }
        }

        public static bool IsInstalled
        {
            get { return File.Exists(InstalledExePath) && (IsAutoRunRegistered || IsContextMenuRegistered); }
        }

        public static bool IsAutoRunRegistered
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                    {
                        return key != null && key.GetValue(AppName) != null;
                    }
                }
                catch { return false; }
            }
        }

        public static bool IsContextMenuRegistered
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\Background\shell\PasteImageAsFile", false))
                    {
                        return key != null;
                    }
                }
                catch { return false; }
            }
        }

        public static bool IsDaemonRunning()
        {
            int currentId = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName(AppName))
            {
                if (p.Id != currentId) return true;
            }
            return false;
        }

        public static void Install(bool autoRun, bool contextMenu)
        {
            Directory.CreateDirectory(InstallDir);
            string currentExe = Application.ExecutablePath;

            if (!string.Equals(currentExe, InstalledExePath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentExe, InstalledExePath, true);
            }

            SetContextMenu(contextMenu);
            SetAutoRun(autoRun);

            Config.AutoRun = autoRun;
            Config.ContextMenu = contextMenu;
        }

        public static void Uninstall()
        {
            StopDaemon();

            SetContextMenu(false);
            SetAutoRun(false);

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software", true))
                {
                    if (key != null) key.DeleteSubKeyTree("PasteImageAsFile", false);
                }
            }
            catch {}
        }

        public static void SetContextMenu(bool enable)
        {
            string quotedExe = "\"" + InstalledExePath + "\"";

            if (enable)
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\Background\shell\PasteImageAsFile"))
                {
                    if (key != null)
                    {
                        key.SetValue("", MenuText);
                        key.SetValue("Icon", "imageres.dll,-70");
                        using (var cmd = key.CreateSubKey("command"))
                        {
                            if (cmd != null) cmd.SetValue("", quotedExe + " --save \"%V\"");
                        }
                    }
                }

                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\DesktopBackground\shell\PasteImageAsFile"))
                {
                    if (key != null)
                    {
                        key.SetValue("", MenuText);
                        key.SetValue("Icon", "imageres.dll,-70");
                        using (var cmd = key.CreateSubKey("command"))
                        {
                            if (cmd != null) cmd.SetValue("", quotedExe + " --save \"%V\"");
                        }
                    }
                }
            }
            else
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\PasteImageAsFile", false); } catch {}
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\DesktopBackground\shell\PasteImageAsFile", false); } catch {}
            }
        }

        public static void SetAutoRun(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue(AppName, "\"" + InstalledExePath + "\" --daemon");
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                        }
                    }
                }
            }
            catch {}
        }

        public static void StopDaemon()
        {
            int currentId = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName(AppName))
            {
                if (p.Id != currentId)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(1000);
                    }
                    catch {}
                }
            }
        }

        public static void StartDaemon()
        {
            if (IsDaemonRunning()) return;

            string target = File.Exists(InstalledExePath) ? InstalledExePath : Application.ExecutablePath;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    Arguments = "--daemon",
                    UseShellExecute = true
                });
            }
            catch {}
        }

        public static void RestartDaemon()
        {
            StopDaemon();
            StartDaemon();
        }
    }
}
