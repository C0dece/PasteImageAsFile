using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;

namespace PasteImageAsFile
{
    public class ClipboardFlyoutForm : Form
    {
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        const byte VK_CONTROL = 0x11;
        const byte VK_V = 0x56;
        const uint KEYEVENTF_KEYUP = 0x0002;

        private static ClipboardFlyoutForm currentInstance;
        private static readonly object instanceLock = new object();

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        private IntPtr previousForegroundWindow = IntPtr.Zero;
        private FlowLayoutPanel itemsPanel;
        private Label lblTitle;
        private Label lblCount;
        private ToolTip toolTip;
        private Panel pnlHeader;

        public ClipboardFlyoutForm(IntPtr prevFg)
        {
            this.previousForegroundWindow = prevFg;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(390, 530);
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.DoubleBuffered = true;
            this.TopMost = true;
            this.KeyPreview = true;

            toolTip = new ToolTip();
            BuildUI();

            this.Deactivate += (s, e) => CloseFlyout();
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape) CloseFlyout();
            };
        }

        private void CloseFlyout()
        {
            try
            {
                this.Close();
                this.Dispose();
            }
            catch {}
            finally
            {
                lock (instanceLock)
                {
                    if (currentInstance == this) currentInstance = null;
                }
            }
        }

        private void BuildUI()
        {
            // Панель заголовка
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = Color.FromArgb(22, 22, 22),
                Padding = new Padding(12, 0, 10, 0)
            };

            lblTitle = new Label
            {
                Text = "Буфер обмена",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(14, 13)
            };
            pnlHeader.Controls.Add(lblTitle);

            lblCount = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(150, 150, 150),
                AutoSize = true,
                Location = new Point(136, 16)
            };
            pnlHeader.Controls.Add(lblCount);

            // Кнопка закрытия ✕
            Button btnClose = CreateHeaderButton("✕", 352, 10, 28, 28);
            btnClose.Font = new Font("Segoe UI", 9.5f);
            btnClose.Click += (s, e) => CloseFlyout();
            toolTip.SetToolTip(btnClose, "Закрыть (Esc)");
            pnlHeader.Controls.Add(btnClose);

            // Кнопка Win+V (системный буфер)
            Button btnWinV = CreateHeaderButton("Win+V", 294, 11, 52, 26);
            btnWinV.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnWinV.Click += (s, e) => {
                CloseFlyout();
                Program.ShowClipboardHistory(false);
            };
            toolTip.SetToolTip(btnWinV, "Открыть системный буфер Windows (Win+V)");
            pnlHeader.Controls.Add(btnWinV);

            // Кнопка папки кэша
            Button btnFolder = CreateHeaderButton("Папка", 236, 11, 52, 26);
            btnFolder.Font = new Font("Segoe UI", 8f);
            btnFolder.Click += (s, e) => {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShellIntegration.AppName, "Cache");
                if (Directory.Exists(cache)) Process.Start("explorer.exe", cache);
                CloseFlyout();
            };
            toolTip.SetToolTip(btnFolder, "Открыть папку со снимками");
            pnlHeader.Controls.Add(btnFolder);

            // Кнопка очистки
            Button btnClear = CreateHeaderButton("Очистить", 166, 11, 64, 26);
            btnClear.Font = new Font("Segoe UI", 8f);
            btnClear.Click += (s, e) => {
                if (MessageBox.Show("Очистить историю кэша изображений?", "Буфер обмена", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    ClearCache();
                    PopulateItems();
                }
            };
            toolTip.SetToolTip(btnClear, "Очистить кэш снимков");
            pnlHeader.Controls.Add(btnClear);

            this.Controls.Add(pnlHeader);

            // Основная панель карточек со скроллом
            itemsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(28, 28, 28),
                Padding = new Padding(8, 8, 8, 8)
            };
            this.Controls.Add(itemsPanel);

            // Рамка 1px вокруг формы
            this.Paint += (s, e) => {
                using (var pen = new Pen(Color.FromArgb(55, 55, 55), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };
        }

        private Button CreateHeaderButton(string text, int x, int y, int w, int h)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(38, 38, 38),
                ForeColor = Color.FromArgb(210, 210, 210),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(52, 52, 52);
            btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(38, 38, 38);
            return btn;
        }

        public void PopulateItems()
        {
            itemsPanel.Controls.Clear();
            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShellIntegration.AppName, "Cache");
            if (!Directory.Exists(cacheDir)) return;

            var files = new DirectoryInfo(cacheDir).GetFiles("*.png");
            Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

            lblCount.Text = "(" + files.Length + ")";

            if (files.Length == 0)
            {
                Label empty = new Label
                {
                    Text = "История пуста.\nСкопируйте или сделайте скриншот,\nи он появится здесь.",
                    ForeColor = Color.FromArgb(140, 140, 140),
                    Font = new Font("Segoe UI", 9.5f),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(350, 120),
                    Margin = new Padding(10, 60, 10, 10)
                };
                itemsPanel.Controls.Add(empty);
                return;
            }

            int count = Math.Min(files.Length, 30);
            for (int i = 0; i < count; i++)
            {
                var card = CreateItemCard(files[i]);
                itemsPanel.Controls.Add(card);
            }
        }

        private Control CreateItemCard(FileInfo fi)
        {
            Panel card = new Panel
            {
                Size = new Size(354, 76),
                BackColor = Color.FromArgb(36, 36, 36),
                Margin = new Padding(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };

            // Превью изображения
            PictureBox pic = new PictureBox
            {
                Size = new Size(76, 60),
                Location = new Point(8, 8),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(20, 20, 20)
            };

            int imgWidth = 0;
            int imgHeight = 0;
            try
            {
                using (var stream = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var src = Image.FromStream(stream))
                {
                    imgWidth = src.Width;
                    imgHeight = src.Height;
                    pic.Image = new Bitmap(src);
                }
            }
            catch {}
            card.Controls.Add(pic);

            // Заголовок карточки
            string title = Path.GetFileNameWithoutExtension(fi.Name);
            int dateIdx = title.LastIndexOf("_202");
            if (dateIdx > 0) title = title.Substring(0, dateIdx);

            Label lblName = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(92, 10),
                Size = new Size(250, 18),
                AutoEllipsis = true
            };
            card.Controls.Add(lblName);

            // Метаданные (время, размер, разрешение)
            string timeStr = FormatDate(fi.LastWriteTime);
            string sizeStr = (fi.Length / 1024) + " КБ";
            string dimStr = (imgWidth > 0 && imgHeight > 0) ? (imgWidth + "×" + imgHeight + " • ") : "";

            Label lblInfo = new Label
            {
                Text = dimStr + sizeStr + " • " + timeStr,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(160, 160, 160),
                Location = new Point(92, 34),
                Size = new Size(250, 16),
                AutoEllipsis = true
            };
            card.Controls.Add(lblInfo);

            // Контекстное меню для карточки
            ContextMenu cardMenu = new ContextMenu(new MenuItem[] {
                new MenuItem("Скопировать в буфер", (s, e) => CopyItemToClipboard(fi.FullName)),
                new MenuItem("Вставить в активное окно", (s, e) => PasteItemToActiveWindow(fi.FullName)),
                new MenuItem("Сохранить на Рабочий стол", (s, e) => SaveItemToDesktop(fi.FullName)),
                new MenuItem("-"),
                new MenuItem("Открыть в Просмотре", (s, e) => Process.Start(fi.FullName)),
                new MenuItem("Показать в Проводнике", (s, e) => Process.Start("explorer.exe", "/select,\"" + fi.FullName + "\"")),
                new MenuItem("-"),
                new MenuItem("Удалить", (s, e) => DeleteItem(fi.FullName))
            });
            card.ContextMenu = cardMenu;

            // Обработка клика и подсветки
            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                c.MouseEnter += (s, e) => card.BackColor = Color.FromArgb(48, 48, 48);
                c.MouseLeave += (s, e) => card.BackColor = Color.FromArgb(36, 36, 36);
                c.Click += (s, e) => {
                    var me = e as MouseEventArgs;
                    if (me == null || me.Button == MouseButtons.Left)
                    {
                        CopyItemToClipboard(fi.FullName);
                        ShowCopyFeedback(lblName);
                    }
                };
                foreach (Control sub in c.Controls) attachEvents(sub);
            };
            attachEvents(card);

            return card;
        }

        private void ShowCopyFeedback(Label lblName)
        {
            string oldText = lblName.Text;
            lblName.Text = "✓ Скопировано в буфер!";
            lblName.ForeColor = Color.FromArgb(70, 205, 110);

            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 280;
            timer.Tick += (s, e) => {
                timer.Stop();
                timer.Dispose();
                CloseFlyout();
            };
            timer.Start();
        }

        private void CopyItemToClipboard(string filePath)
        {
            try
            {
                ClipboardListenerWindow.AugmentClipboardWithImage(filePath);
                Logger.Log("Flyout: copied item to clipboard: " + filePath);
            }
            catch (Exception ex)
            {
                Logger.Log("Flyout: copy error: " + ex.Message);
            }
        }

        private void PasteItemToActiveWindow(string filePath)
        {
            CopyItemToClipboard(filePath);
            CloseFlyout();

            if (previousForegroundWindow != IntPtr.Zero)
            {
                ThreadPool.QueueUserWorkItem(_ => {
                    Thread.Sleep(120);
                    SetForegroundWindow(previousForegroundWindow);
                    Thread.Sleep(80);
                    keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(30);
                    keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                });
            }
        }

        private void SaveItemToDesktop(string filePath)
        {
            try
            {
                string desk = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string dest = Path.Combine(desk, Path.GetFileName(filePath));
                File.Copy(filePath, dest, true);
                toolTip.Show("Сохранено на Рабочий стол!", this, this.Width / 2, this.Height / 2, 1500);
            }
            catch (Exception ex)
            {
                Logger.Log("Flyout: SaveItemToDesktop error: " + ex.Message);
            }
        }

        private void DeleteItem(string filePath)
        {
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
                PopulateItems();
            }
            catch (Exception ex)
            {
                Logger.Log("Flyout: DeleteItem error: " + ex.Message);
            }
        }

        private void ClearCache()
        {
            try
            {
                string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShellIntegration.AppName, "Cache");
                if (Directory.Exists(cacheDir))
                {
                    foreach (var f in Directory.GetFiles(cacheDir, "*.png"))
                    {
                        try { File.Delete(f); } catch {}
                    }
                }
            }
            catch {}
        }

        private string FormatDate(DateTime dt)
        {
            DateTime now = DateTime.Now;
            if (dt.Date == now.Date)
            {
                return dt.ToString("HH:mm:ss");
            }
            if (dt.Date == now.Date.AddDays(-1))
            {
                return "Вчера " + dt.ToString("HH:mm");
            }
            return dt.ToString("dd.MM.yyyy HH:mm");
        }

        public static void ShowFlyout(Point cursor, IntPtr prevFg)
        {
            lock (instanceLock)
            {
                if (currentInstance != null && !currentInstance.IsDisposed)
                {
                    currentInstance.CloseFlyout();
                    return;
                }

                currentInstance = new ClipboardFlyoutForm(prevFg);
                currentInstance.PopulateItems();

                Screen scr = Screen.FromPoint(cursor);
                int x = cursor.X - currentInstance.Width / 2;

                // Ограничиваем окно по рабочей области экрана
                if (x < scr.WorkingArea.Left + 8) x = scr.WorkingArea.Left + 8;
                if (x + currentInstance.Width > scr.WorkingArea.Right - 8) x = scr.WorkingArea.Right - currentInstance.Width - 8;

                int y;
                // Панель задач сверху (пользовательский сценарий)
                if (scr.WorkingArea.Top > scr.Bounds.Top + 10)
                {
                    y = scr.WorkingArea.Top + 4;
                }
                else
                {
                    y = scr.WorkingArea.Bottom - currentInstance.Height - 4;
                }

                currentInstance.Location = new Point(x, y);
                currentInstance.Show();
                currentInstance.BringToFront();
                SetForegroundWindow(currentInstance.Handle);
                currentInstance.Activate();

                Logger.Log(string.Format("Flyout shown at ({0},{1}) on {2}", x, y, scr.DeviceName));
            }
        }
    }
}
