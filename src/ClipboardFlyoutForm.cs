using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Collections.Specialized;
using System.Text;

namespace PasteImageAsFile
{
    public class ClipboardFlyoutForm : Form
    {
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

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
        private Button btnClearAll;
        private Button btnPinWindow;
        private Panel pnlHeader;
        private Panel pnlTabs;
        private Panel pnlSearch;
        private TextBox txtSearch;
        private Panel pnlSuperHubZone;

        private ToolTip toolTip;
        private bool isWindowPinned = false;

        // Вкладки: 0=Все, 1=Снимки, 2=Текст, 3=Файлы, 4=SuperHub
        private int currentTabIndex = 0;
        private readonly string[] tabNames = new string[] { "Все", "Снимки", "Текст", "Файлы", "SuperHub" };
        private readonly List<Button> tabButtons = new List<Button>();

        public ClipboardFlyoutForm(IntPtr prevFg)
        {
            this.previousForegroundWindow = prevFg;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(420, 580);
            this.BackColor = Color.FromArgb(31, 31, 31);
            this.ForeColor = Color.White;
            this.DoubleBuffered = true;
            this.TopMost = true;
            this.KeyPreview = true;
            this.AllowDrop = true;

            toolTip = new ToolTip();

            this.Shown += (s, e) => {
                try
                {
                    int corner = DWMWCP_ROUND;
                    DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                    int dark = 1;
                    DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                }
                catch {}
            };

            BuildUI();

            this.Deactivate += (s, e) => {
                if (!isWindowPinned) CloseFlyout();
            };

            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape) CloseFlyout();
            };

            // Drag and drop для всего окна
            this.DragEnter += OnFormDragEnter;
            this.DragDrop += OnFormDragDrop;
        }

        public void CloseFlyout()
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
            // 1. Верхняя шапка (Header)
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(24, 24, 24),
                Padding = new Padding(12, 0, 10, 0)
            };

            lblTitle = new Label
            {
                Text = "Буфер обмена",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(14, 11)
            };
            pnlHeader.Controls.Add(lblTitle);

            // Кнопка закрытия ✕
            Button btnClose = CreateHeaderButton("✕", 380, 8, 28, 28);
            btnClose.Font = new Font("Segoe UI", 9.5f);
            btnClose.Click += (s, e) => CloseFlyout();
            toolTip.SetToolTip(btnClose, "Закрыть (Esc)");
            pnlHeader.Controls.Add(btnClose);

            // Кнопка закрепить окно на экране (Pin Window)
            btnPinWindow = CreateHeaderButton("📌", 346, 8, 28, 28);
            btnPinWindow.Font = new Font("Segoe UI", 9f);
            btnPinWindow.Click += (s, e) => {
                isWindowPinned = !isWindowPinned;
                btnPinWindow.BackColor = isWindowPinned ? Color.FromArgb(0, 120, 215) : Color.FromArgb(38, 38, 38);
                toolTip.SetToolTip(btnPinWindow, isWindowPinned ? "Окно закреплено на экране (кликните, чтобы открепить)" : "Закрепить окно на экране (чтобы не закрывалось при перетаскивании файлов)");
            };
            toolTip.SetToolTip(btnPinWindow, "Закрепить окно на экране (не закрывать при потере фокуса)");
            pnlHeader.Controls.Add(btnPinWindow);

            // Кнопка системного буфера (Win+V)
            Button btnWinV = CreateHeaderButton("Win+V", 290, 9, 50, 26);
            btnWinV.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnWinV.Click += (s, e) => {
                CloseFlyout();
                Program.SendNativeWinV(false);
            };
            toolTip.SetToolTip(btnWinV, "Открыть системный буфер Windows (Win+V)");
            pnlHeader.Controls.Add(btnWinV);

            // Кнопка Очистить все
            btnClearAll = CreateHeaderButton("Очистить все", 192, 9, 92, 26);
            btnClearAll.Font = new Font("Segoe UI", 8f);
            btnClearAll.Click += (s, e) => {
                string msg = currentTabIndex == 4
                    ? "Очистить все незакрепленные элементы в SuperHub?"
                    : "Очистить все незакрепленные элементы в этой вкладке?";
                if (MessageBox.Show(msg, "Буфер обмена", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    ClipboardItemType? filter = null;
                    if (currentTabIndex == 1) filter = ClipboardItemType.Image;
                    else if (currentTabIndex == 2) filter = ClipboardItemType.Text;
                    else if (currentTabIndex == 3) filter = ClipboardItemType.Files;

                    ClipboardHistoryManager.Instance.ClearAll(filter, currentTabIndex == 4);
                    RefreshItems();
                }
            };
            toolTip.SetToolTip(btnClearAll, "Очистить все незакрепленные элементы");
            pnlHeader.Controls.Add(btnClearAll);

            this.Controls.Add(pnlHeader);

            // 2. Панель навигационных вкладок (Tabs)
            pnlTabs = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = Color.FromArgb(24, 24, 24),
                Padding = new Padding(10, 0, 10, 0)
            };

            int tabX = 10;
            for (int i = 0; i < tabNames.Length; i++)
            {
                int tabIdx = i;
                Button tabBtn = new Button
                {
                    Text = tabNames[i],
                    Location = new Point(tabX, 4),
                    Size = new Size(i == 4 ? 86 : (i == 0 ? 46 : 64), 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = (i == currentTabIndex) ? Color.FromArgb(42, 42, 42) : Color.Transparent,
                    ForeColor = (i == currentTabIndex) ? Color.White : Color.FromArgb(160, 160, 160),
                    Font = new Font("Segoe UI", 8.5f, (i == currentTabIndex) ? FontStyle.Bold : FontStyle.Regular),
                    Cursor = Cursors.Hand
                };
                tabBtn.FlatAppearance.BorderSize = 0;
                tabBtn.Click += (s, e) => SwitchTab(tabIdx);

                tabButtons.Add(tabBtn);
                pnlTabs.Controls.Add(tabBtn);
                tabX += tabBtn.Width + 4;
            }

            pnlTabs.Paint += (s, e) => {
                // Синяя линия Windows 11 под активной вкладкой
                if (currentTabIndex >= 0 && currentTabIndex < tabButtons.Count)
                {
                    Button actBtn = tabButtons[currentTabIndex];
                    using (var brush = new SolidBrush(Color.FromArgb(96, 205, 255)))
                    {
                        e.Graphics.FillRectangle(brush, actBtn.Left + 6, pnlTabs.Height - 3, actBtn.Width - 12, 3);
                    }
                }
            };

            this.Controls.Add(pnlTabs);

            // 3. Строка поиска (Search Bar)
            pnlSearch = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(28, 28, 28),
                Padding = new Padding(12, 5, 12, 5)
            };

            Panel searchBoxBg = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(38, 38, 38),
                Padding = new Padding(8, 4, 8, 4)
            };

            Label searchIcon = new Label
            {
                Text = "🔍",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(140, 140, 140),
                Location = new Point(6, 4),
                Size = new Size(20, 16)
            };
            searchBoxBg.Controls.Add(searchIcon);

            txtSearch = new TextBox
            {
                Location = new Point(28, 3),
                Width = 350,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(38, 38, 38),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f)
            };
            txtSearch.TextChanged += (s, e) => RefreshItems();
            searchBoxBg.Controls.Add(txtSearch);

            pnlSearch.Controls.Add(searchBoxBg);
            this.Controls.Add(pnlSearch);

            // 4. Дроп-зона и действия SuperHub (показывается только на вкладке SuperHub)
            pnlSuperHubZone = new Panel
            {
                Dock = DockStyle.Top,
                Height = 100,
                BackColor = Color.FromArgb(28, 28, 28),
                Padding = new Padding(10, 6, 10, 6),
                Visible = false,
                AllowDrop = true
            };

            Panel dropBox = new Panel
            {
                Location = new Point(10, 6),
                Size = new Size(396, 56),
                BackColor = Color.FromArgb(34, 34, 34),
                AllowDrop = true,
                Cursor = Cursors.Hand
            };

            dropBox.Paint += (s, e) => {
                using (var pen = new Pen(Color.FromArgb(70, 70, 70), 1))
                {
                    pen.DashStyle = DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen, 1, 1, dropBox.Width - 3, dropBox.Height - 3);
                }
            };

            Label lblDropPrompt = new Label
            {
                Text = "📥 Перетащите сюда любые файлы, папки или текст для сбора",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(180, 180, 180),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            dropBox.Controls.Add(lblDropPrompt);

            // Drag drop на саму область
            Action<DragEventArgs> handleDrop = (e) => {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    ClipboardHistoryManager.Instance.AddCustomSuperHubItem(files, null);
                    RefreshItems();
                }
                else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
                {
                    string t = (string)e.Data.GetData(DataFormats.UnicodeText);
                    ClipboardHistoryManager.Instance.AddCustomSuperHubItem(null, t);
                    RefreshItems();
                }
            };

            dropBox.DragEnter += (s, e) => {
                if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText))
                {
                    e.Effect = DragDropEffects.Copy;
                    dropBox.BackColor = Color.FromArgb(48, 54, 62);
                }
            };
            dropBox.DragLeave += (s, e) => dropBox.BackColor = Color.FromArgb(34, 34, 34);
            dropBox.DragDrop += (s, e) => {
                dropBox.BackColor = Color.FromArgb(34, 34, 34);
                handleDrop(e);
            };

            // Нижние кнопки действий SuperHub
            Button btnDragAll = new Button
            {
                Text = "📦 Перетащить все",
                Location = new Point(10, 68),
                Size = new Size(130, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 42, 42),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand
            };
            btnDragAll.FlatAppearance.BorderSize = 0;
            btnDragAll.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    StartDragAllSuperHub();
                }
            };
            toolTip.SetToolTip(btnDragAll, "Зажмите и перетащите в любую программу или папку!");

            Button btnCopyAll = new Button
            {
                Text = "📋 Скопировать все",
                Location = new Point(146, 68),
                Size = new Size(130, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 42, 42),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand
            };
            btnCopyAll.FlatAppearance.BorderSize = 0;
            btnCopyAll.Click += (s, e) => CopyAllSuperHub();

            Button btnClearSuperHub = new Button
            {
                Text = "Очистить полку",
                Location = new Point(282, 68),
                Size = new Size(124, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 42, 42),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand
            };
            btnClearSuperHub.FlatAppearance.BorderSize = 0;
            btnClearSuperHub.Click += (s, e) => {
                ClipboardHistoryManager.Instance.ClearAll(null, true);
                RefreshItems();
            };

            pnlSuperHubZone.Controls.Add(dropBox);
            pnlSuperHubZone.Controls.Add(btnDragAll);
            pnlSuperHubZone.Controls.Add(btnCopyAll);
            pnlSuperHubZone.Controls.Add(btnClearSuperHub);

            this.Controls.Add(pnlSuperHubZone);

            // 5. Основная панель карточек со скроллом
            itemsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(31, 31, 31),
                Padding = new Padding(10, 8, 10, 8),
                AllowDrop = true
            };
            itemsPanel.DragEnter += OnFormDragEnter;
            itemsPanel.DragDrop += OnFormDragDrop;
            this.Controls.Add(itemsPanel);

            // Рамка 1px вокруг формы
            this.Paint += (s, e) => {
                using (var pen = new Pen(Color.FromArgb(55, 55, 55), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };
        }

        private void OnFormDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void OnFormDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                ClipboardHistoryManager.Instance.AddCustomSuperHubItem(files, null);
                SwitchTab(4); // Переключаемся на SuperHub
            }
            else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                string t = (string)e.Data.GetData(DataFormats.UnicodeText);
                ClipboardHistoryManager.Instance.AddCustomSuperHubItem(null, t);
                SwitchTab(4);
            }
        }

        private void SwitchTab(int tabIndex)
        {
            currentTabIndex = tabIndex;
            for (int i = 0; i < tabButtons.Count; i++)
            {
                bool active = (i == currentTabIndex);
                tabButtons[i].BackColor = active ? Color.FromArgb(42, 42, 42) : Color.Transparent;
                tabButtons[i].ForeColor = active ? Color.White : Color.FromArgb(160, 160, 160);
                tabButtons[i].Font = new Font("Segoe UI", 8.5f, active ? FontStyle.Bold : FontStyle.Regular);
            }

            pnlTabs.Invalidate();
            pnlSuperHubZone.Visible = (currentTabIndex == 4);

            RefreshItems();
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
            btn.MouseEnter += (s, e) => {
                if (btn != btnPinWindow || !isWindowPinned) btn.BackColor = Color.FromArgb(52, 52, 52);
            };
            btn.MouseLeave += (s, e) => {
                if (btn != btnPinWindow || !isWindowPinned) btn.BackColor = Color.FromArgb(38, 38, 38);
            };
            return btn;
        }

        public void RefreshItems()
        {
            itemsPanel.Controls.Clear();

            ClipboardItemType? filter = null;
            bool superHubOnly = (currentTabIndex == 4);
            if (currentTabIndex == 1) filter = ClipboardItemType.Image;
            else if (currentTabIndex == 2) filter = ClipboardItemType.Text;
            else if (currentTabIndex == 3) filter = ClipboardItemType.Files;

            string search = txtSearch != null ? txtSearch.Text.Trim() : null;
            var list = ClipboardHistoryManager.Instance.GetItems(filter, superHubOnly, search);

            // Обновляем бейдж SuperHub на вкладке
            var superList = ClipboardHistoryManager.Instance.GetItems(null, true);
            tabButtons[4].Text = superList.Count > 0 ? ("SuperHub [" + superList.Count + "]") : "SuperHub";

            if (list.Count == 0)
            {
                Label empty = new Label
                {
                    Text = superHubOnly
                        ? "Полка SuperHub пуста.\nПеретащите сюда файлы или добавьте элементы из буфера кнопкой [📥]."
                        : "История пуста.\nСкопируйте текст, снимок или файл,\nи они появятся здесь.",
                    ForeColor = Color.FromArgb(140, 140, 140),
                    Font = new Font("Segoe UI", 9.5f),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(380, 140),
                    Margin = new Padding(10, 40, 10, 10)
                };
                itemsPanel.Controls.Add(empty);
                return;
            }

            foreach (var item in list)
            {
                var card = CreateItemCard(item);
                itemsPanel.Controls.Add(card);
            }
        }

        private Control CreateItemCard(ClipboardItem item)
        {
            Panel card = new Panel
            {
                Size = new Size(382, 82),
                BackColor = Color.FromArgb(40, 40, 40),
                Margin = new Padding(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };

            // Рамка карточки с эффектом закрепления
            card.Paint += (s, e) => {
                Color bColor = item.IsPinned ? Color.FromArgb(0, 120, 215) : Color.FromArgb(55, 55, 55);
                using (var pen = new Pen(bColor, item.IsPinned ? 1.5f : 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            // Левая часть: миниатюра/иконка
            Control visualPreview = CreatePreviewControl(item);
            visualPreview.Location = new Point(8, 8);
            card.Controls.Add(visualPreview);

            // Кнопки действий карточки в правом верхнем углу:
            // 1. Delete (✕)
            Button btnDel = CreateCardActionButton("✕", card.Width - 28, 6, 22, 22, "Удалить из истории");
            btnDel.Click += (s, e) => {
                ClipboardHistoryManager.Instance.DeleteItem(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnDel);

            // 2. Pin (📌)
            Button btnPin = CreateCardActionButton(item.IsPinned ? "📌" : "📍", card.Width - 54, 6, 24, 22, item.IsPinned ? "Открепить" : "Закрепить вверху");
            if (item.IsPinned) btnPin.ForeColor = Color.FromArgb(96, 205, 255);
            btnPin.Click += (s, e) => {
                ClipboardHistoryManager.Instance.TogglePin(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnPin);

            // 3. SuperHub (📥/📤)
            string hubText = item.IsInSuperHub ? "📤" : "📥";
            string hubTip = item.IsInSuperHub ? "Убрать из SuperHub" : "Добавить в карман SuperHub";
            Button btnHub = CreateCardActionButton(hubText, card.Width - 80, 6, 24, 22, hubTip);
            btnHub.Click += (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnHub);

            // Центральная информационная часть
            Label lblMain = new Label
            {
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(80, 8),
                Size = new Size(215, 18),
                AutoEllipsis = true
            };

            Label lblSub = new Label
            {
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(160, 160, 160),
                Location = new Point(80, 28),
                Size = new Size(215, 34),
                AutoEllipsis = true
            };

            Label lblMeta = new Label
            {
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(130, 130, 130),
                Location = new Point(80, 62),
                Size = new Size(295, 16),
                AutoEllipsis = true
            };

            // Заполнение текстов по типу
            if (item.Type == ClipboardItemType.Image)
            {
                lblMain.Text = string.IsNullOrEmpty(item.SourceApp) ? "Изображение" : item.SourceApp;
                string dim = (item.ImageWidth > 0 && item.ImageHeight > 0) ? (item.ImageWidth + "×" + item.ImageHeight + " • ") : "";
                string sz = item.FileSizeBytes > 0 ? ((item.FileSizeBytes / 1024) + " КБ • ") : "";
                lblSub.Text = dim + sz + FormatDate(item.Timestamp);
                lblMeta.Text = SafeGetFileName(item.ImagePath);
            }
            else if (item.Type == ClipboardItemType.Text)
            {
                string txtPreview = item.TextContent ?? "";
                txtPreview = txtPreview.Replace("\r", " ").Replace("\n", " ");
                if (txtPreview.Length > 80) txtPreview = txtPreview.Substring(0, 80) + "...";
                lblMain.Text = txtPreview;

                string src = string.IsNullOrEmpty(item.SourceApp) ? "Текст" : item.SourceApp;
                lblSub.Text = src + " • " + (item.TextContent != null ? item.TextContent.Length : 0) + " симв.";
                lblMeta.Text = FormatDate(item.Timestamp);
            }
            else if (item.Type == ClipboardItemType.Files)
            {
                string firstFile = (item.FilePaths != null && item.FilePaths.Count > 0) ? item.FilePaths[0] : "";
                lblMain.Text = SafeGetFileName(firstFile);
                if (item.FilePaths != null && item.FilePaths.Count > 1)
                {
                    lblSub.Text = "и еще " + (item.FilePaths.Count - 1) + " файлов • " + item.SourceApp;
                }
                else
                {
                    lblSub.Text = (SafeDirectoryExists(firstFile) ? "Папка" : "Файл") + " • " + item.SourceApp;
                }
                lblMeta.Text = FormatDate(item.Timestamp);
            }

            card.Controls.Add(lblMain);
            card.Controls.Add(lblSub);
            card.Controls.Add(lblMeta);

            // Контекстное меню по правому клику
            card.ContextMenu = CreateCardContextMenu(item);

            // Обработка клика, наведения и перетаскивания (Drag out)
            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                c.MouseEnter += (s, e) => card.BackColor = Color.FromArgb(50, 50, 50);
                c.MouseLeave += (s, e) => card.BackColor = Color.FromArgb(40, 40, 40);

                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left && e.Clicks == 1)
                    {
                        // Запуск Drag and Drop наружу, если пользователь начинает тянуть
                        StartDragItem(item, card);
                    }
                };

                c.Click += (s, e) => {
                    var me = e as MouseEventArgs;
                    if (me == null || me.Button == MouseButtons.Left)
                    {
                        CopyItem(item);
                        ShowCopyFeedback(lblMain);
                    }
                };
                foreach (Control sub in c.Controls) attachEvents(sub);
            };
            attachEvents(card);

            return card;
        }

        private static string SafeGetFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                return Path.GetFileName(path);
            }
            catch
            {
                try
                {
                    int idx = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
                    return idx >= 0 && idx < path.Length - 1 ? path.Substring(idx + 1) : path;
                }
                catch { return path; }
            }
        }

        private static bool SafeDirectoryExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return Directory.Exists(path); } catch { return false; }
        }

        private static bool SafeFileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return File.Exists(path); } catch { return false; }
        }

        private Control CreatePreviewControl(ClipboardItem item)
        {
            if (item.Type == ClipboardItemType.Image && SafeFileExists(item.ImagePath))
            {
                PictureBox pic = new PictureBox
                {
                    Size = new Size(66, 66),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(24, 24, 24)
                };
                try
                {
                    using (var stream = new FileStream(item.ImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var src = Image.FromStream(stream))
                    {
                        pic.Image = new Bitmap(src);
                    }
                }
                catch {}
                return pic;
            }
            else if (item.Type == ClipboardItemType.Files)
            {
                string firstFile = (item.FilePaths != null && item.FilePaths.Count > 0) ? item.FilePaths[0] : "";
                Image ico = FileIconHelper.GetIconForPath(firstFile);
                PictureBox pic = new PictureBox
                {
                    Size = new Size(66, 66),
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    BackColor = Color.FromArgb(28, 28, 28),
                    Image = ico
                };
                return pic;
            }
            else
            {
                // Текстовая иконка
                Label lbl = new Label
                {
                    Size = new Size(66, 66),
                    BackColor = Color.FromArgb(28, 28, 28),
                    ForeColor = Color.FromArgb(96, 205, 255),
                    Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                    Text = "T",
                    TextAlign = ContentAlignment.MiddleCenter
                };
                return lbl;
            }
        }

        private Button CreateCardActionButton(string text, int x, int y, int w, int h, string tip)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 32, 32),
                ForeColor = Color.FromArgb(190, 190, 190),
                Font = new Font("Segoe UI", 7.5f),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(60, 60, 60);
            btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(32, 32, 32);
            toolTip.SetToolTip(btn, tip);
            return btn;
        }

        private ContextMenu CreateCardContextMenu(ClipboardItem item)
        {
            var menu = new ContextMenu();
            menu.MenuItems.Add(new MenuItem("Скопировать в буфер", (s, e) => CopyItem(item)));
            menu.MenuItems.Add(new MenuItem("Вставить в активное окно", (s, e) => PasteItem(item)));
            menu.MenuItems.Add(new MenuItem(item.IsPinned ? "Открепить" : "Закрепить вверху", (s, e) => {
                ClipboardHistoryManager.Instance.TogglePin(item.Id);
                RefreshItems();
            }));
            menu.MenuItems.Add(new MenuItem(item.IsInSuperHub ? "Убрать из SuperHub" : "Добавить в SuperHub", (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            }));

            if (item.Type == ClipboardItemType.Image || item.Type == ClipboardItemType.Files)
            {
                menu.MenuItems.Add(new MenuItem("-"));
                menu.MenuItems.Add(new MenuItem("Сохранить на Рабочий стол", (s, e) => SaveItemToDesktop(item)));
                menu.MenuItems.Add(new MenuItem("Показать в Проводнике", (s, e) => ShowItemInExplorer(item)));
            }

            menu.MenuItems.Add(new MenuItem("-"));
            menu.MenuItems.Add(new MenuItem("Удалить", (s, e) => {
                ClipboardHistoryManager.Instance.DeleteItem(item.Id);
                RefreshItems();
            }));
            return menu;
        }

        private void CopyItem(ClipboardItem item)
        {
            try
            {
                if (item.Type == ClipboardItemType.Image && File.Exists(item.ImagePath))
                {
                    ClipboardListenerWindow.AugmentClipboardWithImage(item.ImagePath);
                }
                else if (item.Type == ClipboardItemType.Text)
                {
                    Clipboard.SetText(item.TextContent ?? "", TextDataFormat.UnicodeText);
                }
                else if (item.Type == ClipboardItemType.Files && item.FilePaths != null)
                {
                    var sc = new StringCollection();
                    foreach (var f in item.FilePaths) if (File.Exists(f) || Directory.Exists(f)) sc.Add(f);
                    if (sc.Count > 0) Clipboard.SetFileDropList(sc);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Flyout: CopyItem error: " + ex.Message);
            }
        }

        private void PasteItem(ClipboardItem item)
        {
            CopyItem(item);
            if (!isWindowPinned) CloseFlyout();

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

        private void StartDragItem(ClipboardItem item, Control src)
        {
            try
            {
                DataObject data = new DataObject();
                if (item.Type == ClipboardItemType.Image && File.Exists(item.ImagePath))
                {
                    var sc = new StringCollection();
                    sc.Add(item.ImagePath);
                    data.SetFileDropList(sc);
                }
                else if (item.Type == ClipboardItemType.Files && item.FilePaths != null)
                {
                    var sc = new StringCollection();
                    foreach (var f in item.FilePaths) sc.Add(f);
                    data.SetFileDropList(sc);
                }
                else if (item.Type == ClipboardItemType.Text)
                {
                    data.SetText(item.TextContent ?? "", TextDataFormat.UnicodeText);
                }

                src.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            catch {}
        }

        private void StartDragAllSuperHub()
        {
            try
            {
                var hubItems = ClipboardHistoryManager.Instance.GetItems(null, true);
                var sc = new StringCollection();
                StringBuilder sbText = new StringBuilder();

                foreach (var it in hubItems)
                {
                    if (it.Type == ClipboardItemType.Image && File.Exists(it.ImagePath))
                    {
                        sc.Add(it.ImagePath);
                    }
                    else if (it.Type == ClipboardItemType.Files && it.FilePaths != null)
                    {
                        foreach (var f in it.FilePaths) sc.Add(f);
                    }
                    else if (it.Type == ClipboardItemType.Text)
                    {
                        sbText.AppendLine(it.TextContent);
                    }
                }

                DataObject data = new DataObject();
                if (sc.Count > 0) data.SetFileDropList(sc);
                if (sbText.Length > 0) data.SetText(sbText.ToString(), TextDataFormat.UnicodeText);

                this.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            catch {}
        }

        private void CopyAllSuperHub()
        {
            try
            {
                var hubItems = ClipboardHistoryManager.Instance.GetItems(null, true);
                var sc = new StringCollection();
                StringBuilder sbText = new StringBuilder();

                foreach (var it in hubItems)
                {
                    if (it.Type == ClipboardItemType.Image && File.Exists(it.ImagePath))
                    {
                        sc.Add(it.ImagePath);
                    }
                    else if (it.Type == ClipboardItemType.Files && it.FilePaths != null)
                    {
                        foreach (var f in it.FilePaths) sc.Add(f);
                    }
                    else if (it.Type == ClipboardItemType.Text)
                    {
                        sbText.AppendLine(it.TextContent);
                    }
                }

                DataObject data = new DataObject();
                if (sc.Count > 0) data.SetFileDropList(sc);
                if (sbText.Length > 0) data.SetText(sbText.ToString(), TextDataFormat.UnicodeText);

                Clipboard.SetDataObject(data, true);
                toolTip.Show("✓ Все элементы SuperHub скопированы!", this, this.Width / 2 - 100, this.Height / 2, 1500);
            }
            catch {}
        }

        private void SaveItemToDesktop(ClipboardItem item)
        {
            try
            {
                string desk = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (item.Type == ClipboardItemType.Image && File.Exists(item.ImagePath))
                {
                    string dest = Path.Combine(desk, Path.GetFileName(item.ImagePath));
                    File.Copy(item.ImagePath, dest, true);
                }
                else if (item.Type == ClipboardItemType.Files && item.FilePaths != null)
                {
                    foreach (var f in item.FilePaths)
                    {
                        if (File.Exists(f))
                        {
                            string dest = Path.Combine(desk, Path.GetFileName(f));
                            File.Copy(f, dest, true);
                        }
                    }
                }
                toolTip.Show("Сохранено на Рабочий стол!", this, this.Width / 2 - 80, this.Height / 2, 1500);
            }
            catch {}
        }

        private void ShowItemInExplorer(ClipboardItem item)
        {
            try
            {
                string target = null;
                if (item.Type == ClipboardItemType.Image) target = item.ImagePath;
                else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0) target = item.FilePaths[0];

                if (!string.IsNullOrEmpty(target) && (File.Exists(target) || Directory.Exists(target)))
                {
                    Process.Start("explorer.exe", "/select,\"" + target + "\"");
                }
            }
            catch {}
        }

        private void ShowCopyFeedback(Label lblTitle)
        {
            string old = lblTitle.Text;
            lblTitle.Text = "✓ Скопировано в буфер!";
            lblTitle.ForeColor = Color.FromArgb(96, 205, 255);

            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 280;
            timer.Tick += (s, e) => {
                timer.Stop();
                timer.Dispose();
                if (!isWindowPinned) CloseFlyout();
                else
                {
                    lblTitle.Text = old;
                    lblTitle.ForeColor = Color.White;
                }
            };
            timer.Start();
        }

        private string FormatDate(DateTime dt)
        {
            DateTime now = DateTime.Now;
            if (dt.Date == now.Date) return dt.ToString("HH:mm:ss");
            if (dt.Date == now.Date.AddDays(-1)) return "Вчера " + dt.ToString("HH:mm");
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
                currentInstance.RefreshItems();

                Screen scr = Screen.FromPoint(cursor);
                int x = cursor.X - currentInstance.Width / 2;

                // Учет вертикального положения панели задач
                int y;
                if (scr.WorkingArea.Top > scr.Bounds.Top + 10)
                {
                    // Панель задач расположена сверху
                    y = scr.WorkingArea.Top + 8;
                }
                else if (scr.WorkingArea.Bottom < scr.Bounds.Bottom - 10)
                {
                    // Панель задач расположена снизу
                    y = scr.WorkingArea.Bottom - currentInstance.Height - 8;
                }
                else
                {
                    // Панель автоскрыта или сбоку: ориентируемся на положение курсора
                    if (cursor.Y < scr.Bounds.Top + scr.Bounds.Height / 2)
                    {
                        y = scr.WorkingArea.Top + 8;
                    }
                    else
                    {
                        y = scr.WorkingArea.Bottom - currentInstance.Height - 8;
                    }
                }

                // Учет боковой панели задач
                if (scr.WorkingArea.Left > scr.Bounds.Left + 10)
                {
                    x = scr.WorkingArea.Left + 8;
                }
                else if (scr.WorkingArea.Right < scr.Bounds.Right - 10)
                {
                    x = scr.WorkingArea.Right - currentInstance.Width - 8;
                }
                else
                {
                    if (x < scr.WorkingArea.Left + 8) x = scr.WorkingArea.Left + 8;
                    if (x + currentInstance.Width > scr.WorkingArea.Right - 8) x = scr.WorkingArea.Right - currentInstance.Width - 8;
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
