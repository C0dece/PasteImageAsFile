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

        public static void SaveDirect(string targetFolder)
        {
            Logger.Log("SaveDirect called for: " + targetFolder);
            if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
            {
                targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

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

            if (img == null)
            {
                Logger.Log("SaveDirect: No image in clipboard");
                System.Media.SystemSounds.Beep.Play();
                return;
            }

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
                }
            }
            catch (Exception ex)
            {
                Logger.Log("SaveDirect save exception: " + ex.Message);
            }
        }
    }
}
