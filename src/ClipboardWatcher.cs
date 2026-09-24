using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Threading;

namespace PasteImageAsFile
{
    public class ClipboardListenerWindow : NativeWindow
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;
        public const int WM_SHOW_FLYOUT = 0x042A;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern uint RegisterWindowMessage(string lpString);

        public const string FLYOUT_MSG_NAME = "PasteImageAsFile_ShowFlyout_Message_v1";
        public static readonly uint WM_SHOW_FLYOUT_MSG = RegisterWindowMessage(FLYOUT_MSG_NAME);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public const string FlyoutEventName = "PasteImageAsFile_ShowFlyout_Event_v1";
        private EventWaitHandle flyoutEvent;
        private RegisteredWaitHandle registeredWait;

        public string CacheDirectory { get; private set; }
        private string lastAugmentedFile = null;
        private DateTime lastAugmentTime = DateTime.MinValue;
        private const int DebounceMs = 500;
        private FileSystemWatcher desktopWatcher;

        public ClipboardListenerWindow()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            CacheDirectory = Path.Combine(localApp, ShellIntegration.AppName, "Cache");
            Directory.CreateDirectory(CacheDirectory);

            CleanOldCache();

            CreateParams cp = new CreateParams();
            cp.Caption = "PasteImageAsFileListenerWindow";
            cp.Style = 0;
            cp.Parent = IntPtr.Zero;
            this.CreateHandle(cp);
            SetWindowText(this.Handle, "PasteImageAsFileListenerWindow");
            bool ok = AddClipboardFormatListener(this.Handle);
            Logger.Log("ClipboardListenerWindow handle created: " + this.Handle + ", listener added: " + ok);

            try
            {
                bool createdNew;
                flyoutEvent = new EventWaitHandle(false, EventResetMode.AutoReset, FlyoutEventName, out createdNew);
                registeredWait = ThreadPool.RegisterWaitForSingleObject(flyoutEvent, (state, timedOut) => {
                    if (!timedOut)
                    {
                        PostMessage(this.Handle, unchecked((uint)WM_SHOW_FLYOUT), IntPtr.Zero, IntPtr.Zero);
                    }
                }, null, -1, false);
                Logger.Log("Registered wait for " + FlyoutEventName);
            }
            catch (Exception ex)
            {
                Logger.Log("Error initializing flyoutEvent: " + ex.Message);
            }

            InitDesktopWatcher();

            // Проверяем начальное состояние буфера для меню
            UpdateMenuStateForClipboard();
        }

        private void InitDesktopWatcher()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (Directory.Exists(desktopPath))
                {
                    desktopWatcher = new FileSystemWatcher(desktopPath);
                    desktopWatcher.Filter = "*.*";
                    desktopWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime;
                    desktopWatcher.Created += OnDesktopFileCreated;
                    desktopWatcher.EnableRaisingEvents = true;
                    Logger.Log("Desktop FileSystemWatcher initialized on " + desktopPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("InitDesktopWatcher error: " + ex.Message);
            }
        }

        private void OnDesktopFileCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (!Config.PlaceUnderCursor) return;

                string ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp") return;

                double elapsed = (DateTime.UtcNow - lastAugmentTime).TotalSeconds;
                bool isRecent = elapsed < 60.0;
                bool isOurFile = !string.IsNullOrEmpty(lastAugmentedFile) &&
                                 string.Equals(Path.GetFileNameWithoutExtension(e.FullPath), Path.GetFileNameWithoutExtension(lastAugmentedFile), StringComparison.OrdinalIgnoreCase);

                if (isRecent || isOurFile)
                {
                    Logger.Log("Desktop file created from paste detected: " + e.FullPath);
                    Thread.Sleep(120);
                    DesktopHelper.PositionFile(e.FullPath, true);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("OnDesktopFileCreated error: " + ex.Message);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)WM_SHOW_FLYOUT_MSG || m.Msg == WM_SHOW_FLYOUT)
            {
                Logger.Log("WM_SHOW_FLYOUT message received");
                try
                {
                    IntPtr prevFg = GetForegroundWindow();
                    DesktopHelper.POINT curPt;
                    DesktopHelper.TryGetCursorPosition(out curPt);
                    Point pt = new Point(curPt.x, curPt.y);
                    Screen scr = Screen.FromPoint(pt);
                    if (prevFg == IntPtr.Zero || Program.IsTaskbarOrTrayWindow(prevFg))
                    {
                        IntPtr userWnd = Program.FindLastActiveUserWindow(scr);
                        if (userWnd != IntPtr.Zero) prevFg = userWnd;
                    }
                    ClipboardFlyoutForm.ShowFlyout(pt, prevFg);
                }
                catch (Exception ex)
                {
                    Logger.Log("Error showing flyout from WM_SHOW_FLYOUT: " + ex.Message);
                }
                return;
            }

            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                // Debounce: подавление повторных событий после нашей же записи в буфер
                double elapsed = (DateTime.UtcNow - lastAugmentTime).TotalMilliseconds;
                if (elapsed < DebounceMs)
                {
                    Logger.Log("WM_CLIPBOARDUPDATE debounced (" + (int)elapsed + "ms)");
                    base.WndProc(ref m);
                    return;
                }
                Logger.Log("WM_CLIPBOARDUPDATE received");
                OnClipboardUpdated();
            }
            base.WndProc(ref m);
        }

        private void UpdateMenuStateForClipboard()
        {
            try
            {
                bool hasImage = false;
                if (Clipboard.ContainsImage())
                {
                    hasImage = true;
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files.Count == 1 && File.Exists(files[0]))
                    {
                        string ext = Path.GetExtension(files[0]).ToLowerInvariant();
                        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
                        {
                            hasImage = true;
                        }
                    }
                }

                if (hasImage)
                {
                    // В буфере сейчас картинка -> работает стандартный Ctrl+V, пункт меню скрываем
                    ShellIntegration.SetImagePasteMenuItem(false);
                }
                else
                {
                    // В буфере сейчас НЕ картинка -> если в кэше есть сохраненная картинка, показываем пункт
                    bool hasCached = ShellIntegration.HasAnyCachedImage();
                    ShellIntegration.SetImagePasteMenuItem(hasCached);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("UpdateMenuStateForClipboard error: " + ex.Message);
            }
        }

        private void OnClipboardUpdated()
        {
            try
            {
                // 1. Проверяем наличие изображения в буфере
                if (Clipboard.ContainsImage())
                {
                    HandleImageInClipboard();
                    return;
                }

                // 2. Проверяем наличие скопированных файлов/папок
                if (Clipboard.ContainsFileDropList() && Config.HistoryRememberFiles)
                {
                    var files = Clipboard.GetFileDropList();
                    if (files != null && files.Count > 0)
                    {
                        if (files.Count == 1 && string.Equals(files[0], lastAugmentedFile, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        string winTitle = NameHelper.GetActiveWindowTitle() ?? "Проводник";
                        var fileList = new List<string>();
                        foreach (string f in files) fileList.Add(f);

                        ClipboardHistoryManager.Instance.AddFiles(fileList, winTitle);
                        Logger.Log("History: added " + fileList.Count + " files from " + winTitle);
                        UpdateMenuStateForClipboard();
                        return;
                    }
                }

                // 3. Проверяем наличие текста
                if (Clipboard.ContainsText() && Config.HistoryRememberText)
                {
                    string text = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(text) && text.Trim().Length > 0)
                    {
                        string winTitle = NameHelper.GetActiveWindowTitle() ?? "";
                        ClipboardHistoryManager.Instance.AddText(text, winTitle);
                        Logger.Log("History: added text snippet (" + text.Length + " chars) from " + winTitle);
                        UpdateMenuStateForClipboard();
                        return;
                    }
                }

                UpdateMenuStateForClipboard();
            }
            catch (Exception ex)
            {
                Logger.Log("OnClipboardUpdated error: " + ex.Message);
            }
        }

        private void HandleImageInClipboard()
        {
            Image img = null;
            IDataObject origData = null;

            for (int r = 0; r < 5; r++)
            {
                try
                {
                    if (Clipboard.ContainsFileDropList())
                    {
                        var files = Clipboard.GetFileDropList();
                        if (files.Count == 1 && string.Equals(files[0], lastAugmentedFile, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }

                    origData = Clipboard.GetDataObject();
                    img = Clipboard.GetImage();
                    if (img != null && origData != null) break;
                }
                catch (Exception ex)
                {
                    Logger.Log("Read clipboard retry " + r + ": " + ex.Message);
                    Thread.Sleep(50);
                }
            }

            if (img == null || origData == null)
            {
                UpdateMenuStateForClipboard();
                return;
            }

            try
            {
                string bestName = NameHelper.GetBestImageName(origData);
                string ext = ".png";
                string tempFilePath = Path.Combine(CacheDirectory, bestName + ext);
                int count = 1;
                while (File.Exists(tempFilePath))
                {
                    tempFilePath = Path.Combine(CacheDirectory, bestName + " (" + count + ")" + ext);
                    count++;
                }

                img.Save(tempFilePath, ImageFormat.Png);
                long fSize = 0;
                try { fSize = new FileInfo(tempFilePath).Length; } catch {}
                Logger.Log("Saved cache image: " + tempFilePath);

                // Добавляем в менеджер истории
                ClipboardHistoryManager.Instance.AddImage(tempFilePath, bestName, img.Width, img.Height, fSize);

                DataObject aug = new DataObject();
                foreach (string fmt in origData.GetFormats())
                {
                    try
                    {
                        object val = origData.GetData(fmt);
                        if (val != null) aug.SetData(fmt, val);
                    }
                    catch {}
                }

                try { aug.SetImage(img); } catch {}

                StringCollection fl = new StringCollection();
                fl.Add(tempFilePath);
                aug.SetFileDropList(fl);

                byte[] dropEffect = new byte[] { 1, 0, 0, 0 };
                aug.SetData("Preferred DropEffect", new MemoryStream(dropEffect));

                lastAugmentedFile = tempFilePath;
                lastAugmentTime = DateTime.UtcNow;
                Clipboard.SetDataObject(aug, true);
                Logger.Log("Augmented clipboard with: " + tempFilePath);

                ShellIntegration.SetImagePasteMenuItem(false);
            }
            catch (Exception ex)
            {
                Logger.Log("Augmentation error: " + ex.Message);
            }
            finally
            {
                try { img.Dispose(); } catch {}
            }
        }

        public static void AugmentClipboardWithImage(string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var img = Image.FromStream(stream))
                {
                    DataObject data = new DataObject();
                    data.SetImage(new Bitmap(img));

                    StringCollection fl = new StringCollection();
                    fl.Add(filePath);
                    data.SetFileDropList(fl);

                    byte[] dropEffect = new byte[] { 1, 0, 0, 0 };
                    data.SetData("Preferred DropEffect", new MemoryStream(dropEffect));

                    Clipboard.SetDataObject(data, true);
                    Logger.Log("AugmentClipboardWithImage: set file and image to clipboard: " + filePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("AugmentClipboardWithImage error: " + ex.Message);
            }
        }

        private void CleanOldCache()
        {
            try
            {
                var dir = new DirectoryInfo(CacheDirectory);
                var files = dir.GetFiles("*.png");
                if (files.Length > 50)
                {
                    Array.Sort(files, (a, b) => a.CreationTime.CompareTo(b.CreationTime));
                    for (int i = 0; i < files.Length - 50; i++)
                    {
                        try { files[i].Delete(); } catch {}
                    }
                }
            }
            catch {}
        }

        public void Destroy()
        {
            if (desktopWatcher != null)
            {
                try
                {
                    desktopWatcher.EnableRaisingEvents = false;
                    desktopWatcher.Dispose();
                    desktopWatcher = null;
                }
                catch {}
            }
            if (registeredWait != null)
            {
                try { registeredWait.Unregister(null); } catch {}
                registeredWait = null;
            }
            if (flyoutEvent != null)
            {
                try { flyoutEvent.Close(); } catch {}
                flyoutEvent = null;
            }
            RemoveClipboardFormatListener(this.Handle);
            this.DestroyHandle();
            Logger.Log("ClipboardListenerWindow destroyed");
        }
    }
}
