using System;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PasteImageAsFile
{
    public static class ShellIntegration
    {
        public const string AppName = "PasteImageAsFile";
        public const string MenuText = "Вставить изображение из буфера";
        public const string HistoryMenuText = "Буфер обмена";
        public const string AppVersion = "1.2.0";

        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        const uint SHCNE_ASSOCCHANGED = 0x08000000;
        const uint SHCNF_IDLIST = 0x0000;

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

            string currentIco = Path.Combine(Path.GetDirectoryName(currentExe), "app.ico");
            string installedIco = Path.Combine(InstallDir, "app.ico");
            if (File.Exists(currentIco) && !string.Equals(currentIco, installedIco, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Copy(currentIco, installedIco, true); } catch {}
            }

            SetContextMenu(contextMenu);
            SetAutoRun(autoRun);

            Config.AutoRun = autoRun;
            Config.ContextMenu = contextMenu;

            RefreshShellIcons();
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

            RefreshShellIcons();
        }

        public static void SetContextMenu(bool enable)
        {
            string exePath = File.Exists(InstalledExePath) ? InstalledExePath : Application.ExecutablePath;
            string quotedExe = "\"" + exePath + "\"";
            string iconRef = "\"" + exePath + "\",0";

            if (enable)
            {
                // 1. Пункт "Вставить изображение из буфера" в папке
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\Background\shell\PasteImageAsFile"))
                {
                    if (key != null)
                    {
                        key.SetValue("", MenuText);
                        key.SetValue("Icon", iconRef);
                        using (var cmd = key.CreateSubKey("command"))
                        {
                            if (cmd != null) cmd.SetValue("", quotedExe + " --save \"%V\"");
                        }
                    }
                }

                // 2. Пункт "Вставить изображение из буфера" на Рабочем столе
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\DesktopBackground\shell\PasteImageAsFile"))
                {
                    if (key != null)
                    {
                        key.SetValue("", MenuText);
                        key.SetValue("Icon", iconRef);
                        using (var cmd = key.CreateSubKey("command"))
                        {
                            if (cmd != null) cmd.SetValue("", quotedExe + " --save \"%V\"");
                        }
                    }
                }

                // 3. Пункт "Буфер обмена" (вызов Win+V) в папке
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\Background\shell\PasteImageAsFile_History"))
                {
                    if (key != null)
                    {
                        key.SetValue("", HistoryMenuText);
                        key.SetValue("Icon", iconRef);
                        using (var cmd = key.CreateSubKey("command"))
                        {
                            if (cmd != null) cmd.SetValue("", quotedExe + " --history");
                        }
                    }
                }

                // 4. Пункт "Буфер обмена" (вызов Win+V) на Рабочем столе
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\DesktopBackground\shell\PasteImageAsFile_History"))
                {
                    if (key != null)
                    {
                        key.SetValue("", HistoryMenuText);
                        key.SetValue("Icon", iconRef);
                        using (var cmd = key.CreateSubKey("command"))
                        {
                            if (cmd != null) cmd.SetValue("", quotedExe + " --history");
                        }
                    }
                }
            }
            else
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\PasteImageAsFile", false); } catch {}
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\DesktopBackground\shell\PasteImageAsFile", false); } catch {}
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\PasteImageAsFile_History", false); } catch {}
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\DesktopBackground\shell\PasteImageAsFile_History", false); } catch {}
            }

            RefreshShellIcons();
        }

        public static void SetAutoRun(bool enable)
        {
            string exePath = File.Exists(InstalledExePath) ? InstalledExePath : Application.ExecutablePath;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue(AppName, "\"" + exePath + "\" --daemon");
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

        public static void RefreshShellIcons()
        {
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
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
