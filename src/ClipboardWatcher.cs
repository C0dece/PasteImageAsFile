using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Specialized;
using System.Threading;

namespace PasteImageAsFile
{
    public class ClipboardListenerWindow : NativeWindow
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        public string CacheDirectory { get; private set; }
        private string lastAugmentedFile = null;
        private DateTime lastAugmentTime = DateTime.MinValue;
        private const int DebounceMs = 500;

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
            bool ok = AddClipboardFormatListener(this.Handle);
            Logger.Log("ClipboardListenerWindow handle created: " + this.Handle + ", listener added: " + ok);
        }

        protected override void WndProc(ref Message m)
        {
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

        private void OnClipboardUpdated()
        {
            Image img = null;
            IDataObject origData = null;

            for (int r = 0; r < 5; r++)
            {
                try
                {
                    if (!Clipboard.ContainsImage())
                    {
                        return;
                    }

                    if (Clipboard.ContainsFileDropList())
                    {
                        var files = Clipboard.GetFileDropList();
                        if (files.Count == 1 && string.Equals(files[0], lastAugmentedFile, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        // Файл скопирован пользователем в Проводнике, не вмешиваемся
                        return;
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

            if (img == null || origData == null) return;

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
                Logger.Log("Saved cache image: " + tempFilePath);

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

        private void CleanOldCache()
        {
            try
            {
                var dir = new DirectoryInfo(CacheDirectory);
                var files = dir.GetFiles("*.png");
                if (files.Length > 30)
                {
                    Array.Sort(files, (a, b) => a.CreationTime.CompareTo(b.CreationTime));
                    for (int i = 0; i < files.Length - 30; i++)
                    {
                        try { files[i].Delete(); } catch {}
                    }
                }
            }
            catch {}
        }

        public void Destroy()
        {
            RemoveClipboardFormatListener(this.Handle);
            this.DestroyHandle();
            Logger.Log("ClipboardListenerWindow destroyed");
        }
    }
}
