using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;

namespace PasteImageAsFile
{
    public static class Program
    {
        const string MutexName = "PasteImageAsFile_SingleInstance_Mutex_App_v1";
        static Mutex singleInstanceMutex;

        private static ClipboardListenerWindow watcherWindow;
        private static NotifyIcon trayIcon;
        private static MainForm mainForm;

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(DesktopHelper.POINT Point);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        const byte VK_LWIN = 0x5B;
        const byte VK_V = 0x56;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_SHOWWINDOW = 0x0040;

        public static bool IsWatcherRunningInCurrentProcess
        {
            get { return watcherWindow != null; }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) => Logger.Log("ThreadException: " + e.Exception.ToString());
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Logger.Log("UnhandledException: " + e.ExceptionObject.ToString());
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Logger.Log("ProcessExit event received");

            Logger.Log("Main started. Args: " + string.Join(" ", args));

            if (args.Length > 0)
            {
                string cmd = args[0].ToLowerInvariant();
                if (cmd == "--install" || cmd == "-i")
                {
                    ShellIntegration.Install(Config.AutoRun, Config.ContextMenu);
                    return;
                }
                if (cmd == "--uninstall" || cmd == "-u")
                {
                    ShellIntegration.Uninstall();
                    return;
                }
                if (cmd == "--history" || cmd == "-h" || cmd == "--clipboard" || cmd == "-v")
                {
                    ShowClipboardHistory();
                    return;
                }
                if (cmd == "--save" || cmd == "-s")
                {
                    string targetFolder = args.Length > 1 ? args[1] : "";
                    SaveDirect(targetFolder);
                    return;
                }
                if (Directory.Exists(args[0]))
                {
                    SaveDirect(args[0]);
                    return;
                }
                if (cmd == "--daemon" || cmd == "-d")
                {
                    RunDaemonMode();
                    return;
                }
            }

            RunInteractiveMode();
        }

        public static void ShowClipboardHistory()
        {
            Logger.Log("ShowClipboardHistory invoked");

            DesktopHelper.POINT pt;
            DesktopHelper.TryGetCursorPosition(out pt);
            Logger.Log("ShowClipboardHistory: target cursor point: " + pt.x + "," + pt.y);

            // Активируем окно под курсором
            try
            {
                IntPtr targetWin = WindowFromPoint(pt);
                if (targetWin != IntPtr.Zero)
                {
                    SetForegroundWindow(targetWin);
                }
            }
            catch {}

            // Небольшая задержка для завершения анимации закрытия контекстного меню
            Thread.Sleep(120);

            // Отправляем Win+V
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            // Отслеживаем появление окна истории буфера обмена и смещаем его к курсору
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(40);
                IntPtr foundHwnd = IntPtr.Zero;

                EnumWindows((hwnd, lParam) => {
                    if (!IsWindowVisible(hwnd)) return true;
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    try
                    {
                        var proc = Process.GetProcessById((int)pid);
                        string pName = proc.ProcessName.ToLowerInvariant();
                        if (pName == "textinputhost" || pName == "shellexperiencehost")
                        {
                            var sbClass = new System.Text.StringBuilder(256);
                            GetClassName(hwnd, sbClass, 256);
                            string cls = sbClass.ToString();

                            RECT rc;
                            GetWindowRect(hwnd, out rc);
                            int w = rc.Right - rc.Left;
                            int h = rc.Bottom - rc.Top;

                            if (w > 180 && h > 180)
                            {
                                foundHwnd = hwnd;
                                return false;
                            }
                        }
                    }
                    catch {}
                    return true;
                }, IntPtr.Zero);

                if (foundHwnd != IntPtr.Zero)
                {
                    RECT rc;
                    GetWindowRect(foundHwnd, out rc);
                    int w = rc.Right - rc.Left;
                    int h = rc.Bottom - rc.Top;

                    int targetX = pt.x + 8;
                    int targetY = pt.y + 8;
                    try
                    {
                        var screen = Screen.FromPoint(new Point(pt.x, pt.y)).WorkingArea;
                        if (targetX + w > screen.Right) targetX = pt.x - w - 8;
                        if (targetY + h > screen.Bottom) targetY = pt.y - h - 8;
                        if (targetX < screen.Left) targetX = screen.Left + 8;
                        if (targetY < screen.Top) targetY = screen.Top + 8;
                    }
                    catch {}

                    SetWindowPos(foundHwnd, IntPtr.Zero, targetX, targetY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
                    Logger.Log("Moved Clipboard History window to " + targetX + "," + targetY);
                    break;
                }
            }
        }

        private static void RunDaemonMode()
        {
            Logger.Log("RunDaemonMode started");
            bool createdNew;
            singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                Logger.Log("Daemon already running, exiting.");
                return;
            }

            InitTray();
            StartWatcher();

            Logger.Log("Entering Application.Run() for daemon");
            Application.Run();
            Logger.Log("Exited Application.Run() for daemon");

            GC.KeepAlive(singleInstanceMutex);
        }

        private static void RunInteractiveMode()
        {
            Logger.Log("RunInteractiveMode started");
            bool createdNew;
            singleInstanceMutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                Logger.Log("Interactive: stopping running daemon to take over UI");
                ShellIntegration.StopDaemon();
                Thread.Sleep(200);
                singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
            }

            InitTray();
            StartWatcher();

            mainForm = new MainForm();
            Logger.Log("Entering Application.Run(mainForm)");
            Application.Run(mainForm);
            Logger.Log("Exited Application.Run(mainForm)");

            GC.KeepAlive(singleInstanceMutex);
        }

        public static void StartWatcher()
        {
            if (watcherWindow == null)
            {
                watcherWindow = new ClipboardListenerWindow();
                Logger.Log("Watcher started, HWND: " + watcherWindow.Handle);
            }
        }

        public static void StopWatcher()
        {
            if (watcherWindow != null)
            {
                watcherWindow.Destroy();
                watcherWindow = null;
                Logger.Log("Watcher stopped");
            }
        }

        private static void InitTray()
        {
            if (trayIcon != null) return;

            MenuItem autoRunItem = new MenuItem("Автозапуск с Windows", (s, e) => {
                bool state = !Config.AutoRun;
                Config.AutoRun = state;
                ShellIntegration.SetAutoRun(state);
                ((MenuItem)s).Checked = state;
            });
            autoRunItem.Checked = Config.AutoRun;

            ContextMenu menu = new ContextMenu(new MenuItem[] {
                new MenuItem("Настройки и статус", (s, e) => ShowMainForm()),
                new MenuItem("-"),
                new MenuItem("Сохранить картинку на Рабочий стол", (s, e) => SaveDirect(Environment.GetFolderPath(Environment.SpecialFolder.Desktop))),
                new MenuItem("Буфер обмена (Win+V)", (s, e) => ShowClipboardHistory()),
                new MenuItem("Открыть папку кэша", (s, e) => {
                    string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShellIntegration.AppName, "Cache");
                    if (Directory.Exists(cache)) Process.Start("explorer.exe", cache);
                }),
                autoRunItem,
                new MenuItem("-"),
                new MenuItem("Выход", (s, e) => ExitApp())
            });

            Icon appIcon = null;
            try
            {
                string icoPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "app.ico");
                if (File.Exists(icoPath))
                {
                    appIcon = new Icon(icoPath);
                }
                else
                {
                    appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch {}
            if (appIcon == null) appIcon = SystemIcons.Application;

            trayIcon = new NotifyIcon
            {
                Icon = appIcon,
                ContextMenu = menu,
                Text = "PasteImageAsFile (Ctrl+V в Проводник)",
                Visible = true
            };

            trayIcon.DoubleClick += (s, e) => ShowMainForm();
            Logger.Log("InitTray complete, trayIcon visible: " + trayIcon.Visible);
        }

        public static void ShowMainForm()
        {
            if (mainForm == null || mainForm.IsDisposed)
            {
                mainForm = new MainForm();
            }
            mainForm.Show();
            mainForm.WindowState = FormWindowState.Normal;
            mainForm.BringToFront();
            mainForm.Activate();
            mainForm.UpdateStatus();
        }

        public static void ExitApp()
        {
            Logger.Log("ExitApp requested");
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
            StopWatcher();
            Application.Exit();
        }

        private static string GetLatestCacheFile()
        {
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string cacheDir = Path.Combine(localApp, ShellIntegration.AppName, "Cache");
                if (Directory.Exists(cacheDir))
                {
                    var dir = new DirectoryInfo(cacheDir);
                    var files = dir.GetFiles("*.png");
                    if (files.Length > 0)
                    {
                        Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                        return files[0].FullName;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("GetLatestCacheFile error: " + ex.Message);
            }
            return null;
        }

        public static void SaveDirect(string targetFolder)
        {
            Logger.Log("SaveDirect called for: " + targetFolder);
            if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
            {
                targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            // 1. Проверяем, есть ли в буфере готовый файл изображения
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files.Count == 1 && File.Exists(files[0]))
                    {
                        string srcExt = Path.GetExtension(files[0]).ToLowerInvariant();
                        if (srcExt == ".png" || srcExt == ".jpg" || srcExt == ".jpeg" || srcExt == ".bmp")
                        {
                            string leafName = Path.GetFileName(files[0]);
                            string destPath = Path.Combine(targetFolder, leafName);
                            int cnt = 1;
                            string nameWithoutExt = Path.GetFileNameWithoutExtension(leafName);
                            while (File.Exists(destPath))
                            {
                                destPath = Path.Combine(targetFolder, nameWithoutExt + " (" + cnt + ")" + srcExt);
                                cnt++;
                            }

                            File.Copy(files[0], destPath, true);
                            System.Media.SystemSounds.Asterisk.Play();
                            DesktopHelper.PositionFile(destPath, Config.PlaceUnderCursor);
                            Logger.Log("SaveDirect: Copied existing file to " + destPath);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("SaveDirect check file error: " + ex.Message);
            }

            // 2. Проверяем, содержит ли буфер картинку
            Image img = null;
            IDataObject origData = null;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (Clipboard.ContainsImage())
                    {
                        origData = Clipboard.GetDataObject();
                        img = Clipboard.GetImage();
                        if (img != null) break;
                    }
                }
                catch
                {
                    Thread.Sleep(50);
                }
            }

            if (img != null)
            {
                try
                {
                    using (img)
                    {
                        string bestName = NameHelper.GetBestImageName(origData);
                        string ext = ".png";
                        string targetFilePath = Path.Combine(targetFolder, bestName + ext);
                        int counter = 1;
                        while (File.Exists(targetFilePath))
                        {
                            targetFilePath = Path.Combine(targetFolder, bestName + " (" + counter + ")" + ext);
                            counter++;
                        }

                        img.Save(targetFilePath, ImageFormat.Png);
                        Logger.Log("SaveDirect: Saved bitmap to " + targetFilePath);
                        System.Media.SystemSounds.Asterisk.Play();
                        DesktopHelper.PositionFile(targetFilePath, Config.PlaceUnderCursor);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("SaveDirect save exception: " + ex.Message);
                }
            }

            // 3. Если в буфере сейчас не изображение (скопирован текст и т.д.), вставляем последнее сохраненное изображение из кэша
            string latestCached = GetLatestCacheFile();
            if (!string.IsNullOrEmpty(latestCached) && File.Exists(latestCached))
            {
                try
                {
                    string srcExt = Path.GetExtension(latestCached).ToLowerInvariant();
                    string leafName = Path.GetFileName(latestCached);
                    string destPath = Path.Combine(targetFolder, leafName);
                    int cnt = 1;
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(leafName);
                    while (File.Exists(destPath))
                    {
                        destPath = Path.Combine(targetFolder, nameWithoutExt + " (" + cnt + ")" + srcExt);
                        cnt++;
                    }

                    File.Copy(latestCached, destPath, true);
                    System.Media.SystemSounds.Asterisk.Play();
                    DesktopHelper.PositionFile(destPath, Config.PlaceUnderCursor);
                    Logger.Log("SaveDirect: Fallback copied latest cached image to " + destPath);
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Log("SaveDirect fallback copy error: " + ex.Message);
                }
            }

            Logger.Log("SaveDirect: No image in clipboard or cache");
            System.Media.SystemSounds.Beep.Play();
        }
    }
}
