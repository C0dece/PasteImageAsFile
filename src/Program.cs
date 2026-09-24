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
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(DesktopHelper.POINT Point);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

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
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_SHOWWINDOW = 0x0040;
        const uint WM_NULL = 0x0000;

        private static int isShowingHistory = 0;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern uint RegisterWindowMessage(string lpString);

        const uint GA_ROOT = 2;
        const int ASFW_ANY = -1;

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
                    Thread.Sleep(600);
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

        public static bool IsTaskbarOrTrayWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                var sb = new System.Text.StringBuilder(128);
                GetClassName(hwnd, sb, 128);
                string cls = sb.ToString();
                if (cls == "Shell_TrayWnd" ||
                    cls == "Shell_SecondaryTrayWnd" ||
                    cls == "TopLevelWindowForOverflowXamlIsland" ||
                    cls == "NotifyIconOverflowWindow" ||
                    cls == "Windows.UI.Core.CoreWindow" ||
                    cls.StartsWith("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch {}
            return false;
        }

        public static IntPtr FindLastActiveUserWindowOnScreen(Screen screen)
        {
            IntPtr found = IntPtr.Zero;
            uint currentPid = (uint)Process.GetCurrentProcess().Id;

            EnumWindows((hwnd, lParam) => {
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == currentPid) return true;

                RECT rc;
                GetWindowRect(hwnd, out rc);
                int w = rc.Right - rc.Left;
                int h = rc.Bottom - rc.Top;
                if (w < 150 || h < 150) return true;

                if (IsTaskbarOrTrayWindow(hwnd)) return true;

                var sb = new System.Text.StringBuilder(128);
                GetClassName(hwnd, sb, 128);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW") return true;

                Screen s = Screen.FromHandle(hwnd);
                if (s != null && s.DeviceName == screen.DeviceName)
                {
                    found = hwnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        private static void ForceForegroundWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                AllowSetForegroundWindow(ASFW_ANY);
                uint foreThread = 0;
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fg, out foreThread);
                }
                uint appThread = GetCurrentThreadId();
                if (foreThread != 0 && foreThread != appThread)
                {
                    AttachThreadInput(appThread, foreThread, true);
                    SetForegroundWindow(hWnd);
                    AttachThreadInput(appThread, foreThread, false);
                }
                else
                {
                    SetForegroundWindow(hWnd);
                }
            }
            catch
            {
                SetForegroundWindow(hWnd);
            }
        }

        private static Form winVAnchor;

        public static void ShowClipboardHistory(bool fromTray = false)
        {
            Logger.Log("ShowClipboardHistory invoked (fromTray=" + fromTray + ")");
            try
            {
                // Если мы уже внутри работающего демона или формы:
                if (watcherWindow != null || (mainForm != null && !mainForm.IsDisposed))
                {
                    DesktopHelper.POINT curPt;
                    DesktopHelper.TryGetCursorPosition(out curPt);
                    Point pt = new Point(curPt.x, curPt.y);
                    Screen scr = Screen.FromPoint(pt);
                    IntPtr prevFg = GetForegroundWindow();
                    if (prevFg == IntPtr.Zero || IsTaskbarOrTrayWindow(prevFg))
                    {
                        IntPtr userWnd = FindLastActiveUserWindowOnScreen(scr);
                        if (userWnd != IntPtr.Zero) prevFg = userWnd;
                    }
                    ClipboardFlyoutForm.ShowFlyout(pt, prevFg);
                    return;
                }

                if (ShellIntegration.IsDaemonRunning())
                {
                    uint wmMsg = RegisterWindowMessage(ClipboardListenerWindow.FLYOUT_MSG_NAME);
                    if (wmMsg != 0)
                    {
                        PostMessage((IntPtr)0xffff, wmMsg, IntPtr.Zero, IntPtr.Zero);
                        Logger.Log("Broadcasted WM_SHOW_FLYOUT_MSG to running daemon");
                        return;
                    }
                }

                // Пытаемся разбудить работающий демон через именованный Event
                try
                {
                    using (var ev = EventWaitHandle.OpenExisting(ClipboardListenerWindow.FlyoutEventName))
                    {
                        ev.Set();
                        Logger.Log("Signaled " + ClipboardListenerWindow.FlyoutEventName + " successfully");
                        return;
                    }
                }
                catch (WaitHandleCannotBeOpenedException) {}
                catch (Exception exSignal)
                {
                    Logger.Log("Event open failed: " + exSignal.Message);
                }

                DesktopHelper.POINT cliPt;
                DesktopHelper.TryGetCursorPosition(out cliPt);
                IntPtr fg = GetForegroundWindow();
                ClipboardFlyoutForm.ShowFlyout(new Point(cliPt.x, cliPt.y), fg);
                Application.Run();
            }
            catch (Exception ex)
            {
                Logger.Log("ShowClipboardHistory error: " + ex.ToString());
            }
        }

        public static void SendNativeWinV(bool fromTray = false)
        {
            if (Interlocked.CompareExchange(ref isShowingHistory, 1, 0) != 0)
            {
                Logger.Log("SendNativeWinV: already in progress, debounced");
                return;
            }

            DesktopHelper.POINT pt;
            DesktopHelper.TryGetCursorPosition(out pt);
            Screen targetScreen = Screen.FromPoint(new Point(pt.x, pt.y));

            Logger.Log("SendNativeWinV invoked (fromTray=" + fromTray + ") cursor: " + pt.x + "," + pt.y + " targetScreen: " + targetScreen.DeviceName);

            ThreadPool.QueueUserWorkItem(_ => {
                try
                {
                    Thread.Sleep(fromTray ? 100 : 30);

                    IntPtr targetWin = FindLastActiveUserWindowOnScreen(targetScreen);
                    if (targetWin != IntPtr.Zero)
                    {
                        ForceForegroundWindow(targetWin);
                        Logger.Log("Focused target window on " + targetScreen.DeviceName + ": " + targetWin);
                        Thread.Sleep(40);
                    }
                    else
                    {
                        try
                        {
                            Action createAnchor = () => {
                                try
                                {
                                    if (winVAnchor != null && !winVAnchor.IsDisposed)
                                    {
                                        winVAnchor.Close();
                                        winVAnchor.Dispose();
                                    }
                                    winVAnchor = new Form
                                    {
                                        FormBorderStyle = FormBorderStyle.None,
                                        StartPosition = FormStartPosition.Manual,
                                        Location = new Point(targetScreen.WorkingArea.Right - 80, targetScreen.WorkingArea.Top + 40),
                                        Size = new Size(30, 30),
                                        BackColor = Color.FromArgb(28, 28, 28),
                                        ShowInTaskbar = false,
                                        TopMost = true
                                    };
                                    winVAnchor.Show();
                                    winVAnchor.BringToFront();
                                    ForceForegroundWindow(winVAnchor.Handle);
                                }
                                catch {}
                            };

                            if (mainForm != null && !mainForm.IsDisposed && mainForm.InvokeRequired)
                            {
                                mainForm.Invoke(createAnchor);
                            }
                            else
                            {
                                createAnchor();
                            }
                        }
                        catch {}

                        Thread.Sleep(80);
                        Logger.Log("Created anchor window on " + targetScreen.DeviceName + " for Win+V");
                    }

                    // Отправляем Win+V штатным системным образом
                    keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(30);
                    keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Logger.Log("Sent Win+V successfully to " + targetScreen.DeviceName);

                    if (winVAnchor != null)
                    {
                        Thread.Sleep(3000);
                        try
                        {
                            Action closeAnchor = () => {
                                try
                                {
                                    if (winVAnchor != null && !winVAnchor.IsDisposed)
                                    {
                                        winVAnchor.Close();
                                        winVAnchor.Dispose();
                                        winVAnchor = null;
                                    }
                                }
                                catch {}
                            };

                            if (mainForm != null && !mainForm.IsDisposed && mainForm.InvokeRequired)
                            {
                                mainForm.Invoke(closeAnchor);
                            }
                            else
                            {
                                closeAnchor();
                            }
                        }
                        catch {}
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("SendNativeWinV error: " + ex.Message);
                }
                finally
                {
                    Thread.Sleep(500);
                    Interlocked.Exchange(ref isShowingHistory, 0);
                }
            });
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

            try
            {
                var dock = SuperHubDockForm.Instance;
                dock.Show();
                Logger.Log("SuperHubDockForm initialized and shown");
            }
            catch (Exception ex)
            {
                Logger.Log("Error showing SuperHubDockForm: " + ex.Message);
            }

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
                new MenuItem("Буфер обмена", (s, e) => ShowClipboardHistory(true)),
                new MenuItem("Полка SuperHub", (s, e) => SuperHubDockForm.Instance.ExpandShelf()),
                new MenuItem("Системный буфер (Win+V)", (s, e) => SendNativeWinV(true)),
                new MenuItem("-"),
                new MenuItem("Сохранить картинку на Рабочий стол", (s, e) => SaveDirect(Environment.GetFolderPath(Environment.SpecialFolder.Desktop))),
                new MenuItem("Открыть папку кэша", (s, e) => {
                    string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShellIntegration.AppName, "Cache");
                    if (Directory.Exists(cache)) Process.Start("explorer.exe", cache);
                }),
                new MenuItem("Настройки и статус", (s, e) => ShowMainForm()),
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
                Text = ShellIntegration.AppName,
                Visible = true
            };

            trayIcon.MouseClick += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    if (Config.TrayClickOpensClipboard)
                    {
                        DesktopHelper.POINT curPt;
                        DesktopHelper.TryGetCursorPosition(out curPt);
                        Point pt = new Point(curPt.x, curPt.y);
                        Screen scr = Screen.FromPoint(pt);
                        IntPtr prevFg = GetForegroundWindow();
                        if (prevFg == IntPtr.Zero || IsTaskbarOrTrayWindow(prevFg))
                        {
                            IntPtr userWnd = FindLastActiveUserWindowOnScreen(scr);
                            if (userWnd != IntPtr.Zero) prevFg = userWnd;
                        }
                        ClipboardFlyoutForm.ShowFlyout(pt, prevFg);
                    }
                    else
                    {
                        ShowMainForm();
                    }
                }
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
