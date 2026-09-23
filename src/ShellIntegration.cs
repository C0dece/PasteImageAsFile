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
        public const string AppVersion = "1.3.0";

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
                        if (key != null) return true;
                    }
                    using (var key2 = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\Background\shell\PasteImageAsFile_History", false))
                    {
                        return key2 != null;
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

        public static void SetImagePasteMenuItem(bool visible)
        {
            if (!Config.ContextMenu)
            {
                // Если общая настройка меню выключена, пункт должен быть удален
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\PasteImageAsFile", false); } catch {}
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\DesktopBackground\shell\PasteImageAsFile", false); } catch {}
                return;
            }

            string exePath = File.Exists(InstalledExePath) ? InstalledExePath : Application.ExecutablePath;
            string quotedExe = "\"" + exePath + "\"";
            string iconRef = "\"" + exePath + "\",0";

            if (visible)
            {
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
            }
            else
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\PasteImageAsFile", false); } catch {}
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\DesktopBackground\shell\PasteImageAsFile", false); } catch {}
            }
        }

        public static void SetHistoryMenuItem(bool visible)
        {
            if (!Config.ContextMenu || !visible)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\PasteImageAsFile_History", false); } catch {}
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\DesktopBackground\shell\PasteImageAsFile_History", false); } catch {}
                return;
            }

            string exePath = File.Exists(InstalledExePath) ? InstalledExePath : Application.ExecutablePath;
            string quotedExe = "\"" + exePath + "\"";
            string iconRef = "\"" + exePath + "\",0";

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

        public static void SetContextMenu(bool enable)
        {
            if (enable)
            {
                SetHistoryMenuItem(Config.ShowHistoryMenu);
                // По умолчанию показываем пункт вставки, если в кэше есть сохраненные изображения
                bool hasCached = HasAnyCachedImage();
                SetImagePasteMenuItem(hasCached);
            }
            else
            {
                SetImagePasteMenuItem(false);
                SetHistoryMenuItem(false);
            }

            RefreshShellIcons();
        }

        public static bool HasAnyCachedImage()
        {
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string cacheDir = Path.Combine(localApp, AppName, "Cache");
                if (Directory.Exists(cacheDir))
                {
                    return Directory.GetFiles(cacheDir, "*.png").Length > 0;
                }
            }
            catch {}
            return false;
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
