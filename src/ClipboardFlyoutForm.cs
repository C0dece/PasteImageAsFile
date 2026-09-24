using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

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

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

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
        private Panel pnlTopBar;
        private Panel pnlTabs;
        private Panel pnlActionHeader;
        private Panel pnlSearch;
        private TextBox txtSearch;
        private Panel pnlViewport;
        private Panel cardsContainer;
        private Button btnPinWindow;
        private Button btnClearAll;

        private ToolTip toolTip;
        private bool isWindowPinned = false;

        // Вкладки: 0=Все, 1=Снимки, 2=Текст, 3=Файлы, 4=SuperHub
        private int currentTabIndex = 0;
        private readonly string[] tabNames = new string[] { "Все", "Снимки", "Текст", "Файлы", "SuperHub" };
        private readonly List<Button> tabButtons = new List<Button>();

        // Кастомный Fluent скроллбар
        private int scrollY = 0;
        private int maxScrollY = 0;
        private bool isDraggingScroll = false;
        private int dragStartMouseY = 0;
        private int dragStartScrollY = 0;
        private bool isHoveringScroll = false;

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

        private void DragWindow()
        {
            try
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
            }
            catch {}
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            // 1. Верхний бар (TopBar) - 34px: заголовок и кнопки закрепа/закрытия
            pnlTopBar = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(this.Width, 34),
                BackColor = Color.FromArgb(24, 24, 24),
                Cursor = Cursors.SizeAll
            };
            pnlTopBar.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };

            Label lblAppHeader = new Label
            {
                Text = "Буфер обмена",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(160, 160, 160),
                AutoSize = true,
                Location = new Point(14, 8),
                Cursor = Cursors.SizeAll
            };
            lblAppHeader.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };
            pnlTopBar.Controls.Add(lblAppHeader);

            // Кнопка закрытия ✕
            Button btnClose = new Button
            {
                Text = "✕",
                Location = new Point(this.Width - 34, 4),
                Size = new Size(26, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(190, 190, 190),
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.MouseEnter += (s, e) => { btnClose.BackColor = Color.FromArgb(196, 43, 28); btnClose.ForeColor = Color.White; };
            btnClose.MouseLeave += (s, e) => { btnClose.BackColor = Color.Transparent; btnClose.ForeColor = Color.FromArgb(190, 190, 190); };
            btnClose.Click += (s, e) => CloseFlyout();
            toolTip.SetToolTip(btnClose, "Закрыть (Esc)");
            pnlTopBar.Controls.Add(btnClose);

            // Кнопка закрепить окно на экране (Pin Window)
            btnPinWindow = new Button
            {
                Text = "📌",
                Location = new Point(this.Width - 64, 4),
                Size = new Size(26, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand
            };
            btnPinWindow.FlatAppearance.BorderSize = 0;
            btnPinWindow.MouseEnter += (s, e) => { if (!isWindowPinned) btnPinWindow.BackColor = Color.FromArgb(45, 45, 45); };
            btnPinWindow.MouseLeave += (s, e) => { if (!isWindowPinned) btnPinWindow.BackColor = Color.Transparent; };
            btnPinWindow.Click += (s, e) => {
                isWindowPinned = !isWindowPinned;
                btnPinWindow.BackColor = isWindowPinned ? Color.FromArgb(0, 120, 215) : Color.Transparent;
                btnPinWindow.ForeColor = isWindowPinned ? Color.White : Color.FromArgb(180, 180, 180);
                toolTip.SetToolTip(btnPinWindow, isWindowPinned ? "Окно закреплено (кликните, чтобы открепить)" : "Закрепить окно на экране (перетаскивайте за заголовок)");
            };
            toolTip.SetToolTip(btnPinWindow, "Закрепить окно на экране");
            pnlTopBar.Controls.Add(btnPinWindow);

            this.Controls.Add(pnlTopBar);

            // 2. Панель навигационных вкладок (Tabs) - 34px
            pnlTabs = new Panel
            {
                Location = new Point(0, 34),
                Size = new Size(this.Width, 34),
                BackColor = Color.FromArgb(24, 24, 24),
                Cursor = Cursors.SizeAll
            };
            pnlTabs.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };

            int tabX = 12;
            for (int i = 0; i < tabNames.Length; i++)
            {
                int tabIdx = i;
                Button tabBtn = new Button
                {
                    Text = tabNames[i],
                    Location = new Point(tabX, 3),
                    Size = new Size(i == 4 ? 86 : (i == 0 ? 46 : 64), 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
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
                // Синяя акцентная полоска Windows 11 под активной вкладкой
                if (currentTabIndex >= 0 && currentTabIndex < tabButtons.Count)
                {
                    Button actBtn = tabButtons[currentTabIndex];
                    int barW = 20;
                    int barX = actBtn.Left + (actBtn.Width - barW) / 2;
                    using (var brush = new SolidBrush(Color.FromArgb(96, 205, 255)))
                    using (var path = CreateRoundedPath(new Rectangle(barX, pnlTabs.Height - 3, barW, 3), 1))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.FillPath(brush, path);
                    }
                }
            };
            this.Controls.Add(pnlTabs);

            // 3. Строка заголовка раздела и кнопки "Очистить все" - 38px
            pnlActionHeader = new Panel
            {
                Location = new Point(0, 68),
                Size = new Size(this.Width, 38),
                BackColor = Color.FromArgb(31, 31, 31)
            };

            Label lblSection = new Label
            {
                Text = "Буфер обмена",
                Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(14, 6)
            };
            pnlActionHeader.Controls.Add(lblSection);

            btnClearAll = new Button
            {
                Text = "Очистить все",
                Location = new Point(this.Width - 116, 6),
                Size = new Size(102, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(44, 44, 44),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand
            };
            btnClearAll.FlatAppearance.BorderSize = 0;
            btnClearAll.MouseEnter += (s, e) => btnClearAll.BackColor = Color.FromArgb(56, 56, 56);
            btnClearAll.MouseLeave += (s, e) => btnClearAll.BackColor = Color.FromArgb(44, 44, 44);
            btnClearAll.Click += (s, e) => {
                string msg = currentTabIndex == 4
                    ? "Очистить все незакрепленные элементы в SuperHub?"
                    : "Очистить все незакрепленные элементы?";
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
            pnlActionHeader.Controls.Add(btnClearAll);

            this.Controls.Add(pnlActionHeader);

            // 4. Строка поиска (Search Bar) - 34px
            pnlSearch = new Panel
            {
                Location = new Point(0, 106),
                Size = new Size(this.Width, 34),
                BackColor = Color.FromArgb(31, 31, 31),
                Padding = new Padding(12, 2, 12, 4)
            };

            Panel searchBoxBg = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(38, 38, 38),
                Padding = new Padding(8, 4, 8, 4)
            };
            searchBoxBg.Paint += (s, e) => {
                using (var pen = new Pen(Color.FromArgb(60, 60, 60), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, searchBoxBg.Width - 1, searchBoxBg.Height - 1);
                }
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
                Location = new Point(28, 4),
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

            // 5. Область просмотра карточек с кастомным Fluent-скроллбаром
            int contentTop = 142;
            pnlViewport = new Panel
            {
                Location = new Point(0, contentTop),
                Size = new Size(this.Width, this.ClientSize.Height - contentTop - 4),
                BackColor = Color.FromArgb(31, 31, 31),
                AllowDrop = true
            };

            cardsContainer = new Panel
            {
                Location = new Point(12, 0),
                Width = pnlViewport.Width - 24,
                BackColor = Color.Transparent,
                AutoSize = false
            };
            pnlViewport.Controls.Add(cardsContainer);

            // Обработка прокрутки колесиком мыши
            pnlViewport.MouseWheel += OnViewportMouseWheel;
            cardsContainer.MouseWheel += OnViewportMouseWheel;

            // Отрисовка темного Fluent Scrollbar
            pnlViewport.Paint += RenderFluentScrollbar;
            pnlViewport.MouseMove += OnScrollbarMouseMove;
            pnlViewport.MouseDown += OnScrollbarMouseDown;
            pnlViewport.MouseUp += OnScrollbarMouseUp;
            pnlViewport.MouseLeave += (s, e) => {
                if (isHoveringScroll) { isHoveringScroll = false; pnlViewport.Invalidate(); }
            };

            this.Controls.Add(pnlViewport);

            // Внешняя тонкая рамка 1px вокруг всей формы
            this.Paint += (s, e) => {
                using (var pen = new Pen(Color.FromArgb(50, 50, 50), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            this.ResumeLayout(false);
        }

        private void OnViewportMouseWheel(object sender, MouseEventArgs e)
        {
            if (maxScrollY <= 0) return;
            int step = 50;
            int delta = (e.Delta > 0) ? -step : step;
            SetScrollY(scrollY + delta);
        }

        private void SetScrollY(int newY)
        {
            if (newY < 0) newY = 0;
            if (newY > maxScrollY) newY = maxScrollY;
            if (newY != scrollY)
            {
                scrollY = newY;
                cardsContainer.Top = -scrollY;
                pnlViewport.Invalidate();
            }
        }

        private Rectangle GetScrollbarThumbRect()
        {
            if (maxScrollY <= 0) return Rectangle.Empty;

            int trackTop = 6;
            int trackBottom = pnlViewport.Height - 14;
            int trackHeight = trackBottom - trackTop;
            if (trackHeight <= 20) return Rectangle.Empty;

            int totalContentHeight = cardsContainer.Height;
            int thumbHeight = Math.Max(28, (int)((float)pnlViewport.Height / totalContentHeight * trackHeight));
            int availableMove = trackHeight - thumbHeight;

            int thumbY = trackTop + (int)((float)scrollY / maxScrollY * availableMove);
            int thumbW = isHoveringScroll || isDraggingScroll ? 6 : 4;
            int thumbX = pnlViewport.Width - thumbW - 3;

            return new Rectangle(thumbX, thumbY, thumbW, thumbHeight);
        }

        private void RenderFluentScrollbar(object sender, PaintEventArgs e)
        {
            if (maxScrollY <= 0) return;

            Rectangle thumb = GetScrollbarThumbRect();
            if (thumb.IsEmpty) return;

            Color thumbColor = isDraggingScroll
                ? Color.FromArgb(145, 145, 145)
                : (isHoveringScroll ? Color.FromArgb(120, 120, 120) : Color.FromArgb(85, 85, 85));

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(thumbColor))
            using (var path = CreateRoundedPath(thumb, 2))
            {
                e.Graphics.FillPath(brush, path);
            }
        }

        private void OnScrollbarMouseMove(object sender, MouseEventArgs e)
        {
            if (maxScrollY <= 0) return;

            if (isDraggingScroll)
            {
                int trackHeight = pnlViewport.Height - 20;
                Rectangle thumb = GetScrollbarThumbRect();
                int availableMove = Math.Max(1, trackHeight - thumb.Height);

                int deltaMouse = e.Y - dragStartMouseY;
                int deltaScroll = (int)((float)deltaMouse / availableMove * maxScrollY);
                SetScrollY(dragStartScrollY + deltaScroll);
                return;
            }

            Rectangle scrollArea = new Rectangle(pnlViewport.Width - 12, 0, 12, pnlViewport.Height);
            bool hover = scrollArea.Contains(e.Location);
            if (hover != isHoveringScroll)
            {
                isHoveringScroll = hover;
                pnlViewport.Invalidate();
            }
        }

        private void OnScrollbarMouseDown(object sender, MouseEventArgs e)
        {
            if (maxScrollY <= 0 || e.Button != MouseButtons.Left) return;

            Rectangle thumb = GetScrollbarThumbRect();
            if (thumb.Contains(e.Location))
            {
                isDraggingScroll = true;
                dragStartMouseY = e.Y;
                dragStartScrollY = scrollY;
            }
            else if (e.X >= pnlViewport.Width - 12)
            {
                // Клик по треку скролла
                if (e.Y < thumb.Y) SetScrollY(scrollY - pnlViewport.Height / 2);
                else SetScrollY(scrollY + pnlViewport.Height / 2);
            }
        }

        private void OnScrollbarMouseUp(object sender, MouseEventArgs e)
        {
            if (isDraggingScroll)
            {
                isDraggingScroll = false;
                pnlViewport.Invalidate();
            }
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

        public void SwitchTab(int tabIndex)
        {
            currentTabIndex = tabIndex;
            for (int i = 0; i < tabButtons.Count; i++)
            {
                bool active = (i == tabIndex);
                tabButtons[i].Font = new Font("Segoe UI", 8.5f, active ? FontStyle.Bold : FontStyle.Regular);
                tabButtons[i].ForeColor = active ? Color.White : Color.FromArgb(160, 160, 160);
            }
            pnlTabs.Invalidate();
            RefreshItems();
        }

        public void RefreshItems()
        {
            cardsContainer.SuspendLayout();
            cardsContainer.Controls.Clear();

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

            int cardY = 4;
            int cardW = cardsContainer.Width;

            if (list.Count == 0)
            {
                Label empty = new Label
                {
                    Text = superHubOnly
                        ? "Полка SuperHub пуста.\nПеретащите сюда файлы или добавьте через меню [···]."
                        : "История пуста.\nСкопируйте текст, снимок или файл,\nи они появятся здесь.",
                    ForeColor = Color.FromArgb(140, 140, 140),
                    Font = new Font("Segoe UI", 9.5f),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(cardW, 140),
                    Location = new Point(0, 30)
                };
                cardsContainer.Controls.Add(empty);
                cardsContainer.Height = 200;
            }
            else
            {
                foreach (var item in list)
                {
                    var card = CreateItemCard(item, cardW);
                    card.Location = new Point(0, cardY);
                    cardsContainer.Controls.Add(card);
                    cardY += card.Height + 8;
                }
                cardsContainer.Height = cardY + 10;
            }

            maxScrollY = Math.Max(0, cardsContainer.Height - pnlViewport.Height);
            if (scrollY > maxScrollY) scrollY = maxScrollY;
            cardsContainer.Top = -scrollY;

            cardsContainer.ResumeLayout(true);
            pnlViewport.Invalidate();
        }

        private Control CreateItemCard(ClipboardItem item, int width)
        {
            int cardH = item.Type == ClipboardItemType.Image ? 76 : 68;

            Panel card = new Panel
            {
                Size = new Size(width, cardH),
                BackColor = Color.FromArgb(43, 43, 43),
                Cursor = Cursors.Hand
            };

            // Рамка карточки со скруглением 6px (как в Windows 11)
            card.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color bColor = item.IsPinned ? Color.FromArgb(0, 120, 215) : Color.FromArgb(55, 55, 55);
                using (var pen = new Pen(bColor, item.IsPinned ? 1.5f : 1f))
                using (var path = CreateRoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 6))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            };

            // Правые кнопки управления: в точности по скриншоту Windows 11
            // 1. Вверху справа: кнопка действий [···]
            Button btnMenu = new Button
            {
                Text = "···",
                Location = new Point(card.Width - 32, 6),
                Size = new Size(24, 22),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(160, 160, 160),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnMenu.FlatAppearance.BorderSize = 0;
            btnMenu.MouseEnter += (s, e) => { btnMenu.BackColor = Color.FromArgb(58, 58, 58); btnMenu.ForeColor = Color.White; };
            btnMenu.MouseLeave += (s, e) => { btnMenu.BackColor = Color.Transparent; btnMenu.ForeColor = Color.FromArgb(160, 160, 160); };
            btnMenu.Click += (s, e) => {
                ContextMenu cm = CreateCardContextMenu(item);
                cm.Show(btnMenu, new Point(0, btnMenu.Height));
            };
            toolTip.SetToolTip(btnMenu, "Действия");
            card.Controls.Add(btnMenu);

            // 2. Внизу справа: кнопка закрепления [📌]
            Button btnPin = new Button
            {
                Text = "📌",
                Location = new Point(card.Width - 32, card.Height - 28),
                Size = new Size(24, 22),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = item.IsPinned ? Color.FromArgb(96, 205, 255) : Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand
            };
            btnPin.FlatAppearance.BorderSize = 0;
            btnPin.MouseEnter += (s, e) => { btnPin.BackColor = Color.FromArgb(58, 58, 58); btnPin.ForeColor = Color.White; };
            btnPin.MouseLeave += (s, e) => {
                btnPin.BackColor = Color.Transparent;
                btnPin.ForeColor = item.IsPinned ? Color.FromArgb(96, 205, 255) : Color.FromArgb(120, 120, 120);
            };
            btnPin.Click += (s, e) => {
                ClipboardHistoryManager.Instance.TogglePin(item.Id);
                RefreshItems();
            };
            toolTip.SetToolTip(btnPin, item.IsPinned ? "Открепить" : "Закрепить вверху");
            card.Controls.Add(btnPin);

            // Контентная часть карточки
            int textLeft = 14;
            int textWidth = card.Width - 52;

            if (item.Type == ClipboardItemType.Image && SafeFileExists(item.ImagePath))
            {
                // Для картинок: миниатюра слева 56x56
                PictureBox pic = new PictureBox
                {
                    Location = new Point(8, (cardH - 58) / 2),
                    Size = new Size(58, 58),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(24, 24, 24),
                    Cursor = Cursors.Hand
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
                card.Controls.Add(pic);

                textLeft = 74;
                textWidth = card.Width - 110;

                Label lblImgTitle = new Label
                {
                    Text = string.IsNullOrEmpty(item.SourceApp) ? SafeGetFileName(item.ImagePath) : item.SourceApp,
                    Font = new Font("Segoe UI Semibold", 9.5f),
                    ForeColor = Color.White,
                    Location = new Point(textLeft, 14),
                    Size = new Size(textWidth, 20),
                    AutoEllipsis = true
                };

                string dim = (item.ImageWidth > 0 && item.ImageHeight > 0) ? (item.ImageWidth + "×" + item.ImageHeight + " • ") : "";
                string sz = item.FileSizeBytes > 0 ? ((item.FileSizeBytes / 1024) + " КБ • ") : "";
                Label lblImgSub = new Label
                {
                    Text = dim + sz + FormatDate(item.Timestamp),
                    Font = new Font("Segoe UI", 8f),
                    ForeColor = Color.FromArgb(150, 150, 150),
                    Location = new Point(textLeft, 38),
                    Size = new Size(textWidth, 18),
                    AutoEllipsis = true
                };

                card.Controls.Add(lblImgTitle);
                card.Controls.Add(lblImgSub);
            }
            else if (item.Type == ClipboardItemType.Files)
            {
                // Для файлов: системная иконка файла слева 36x36
                string firstFile = (item.FilePaths != null && item.FilePaths.Count > 0) ? item.FilePaths[0] : "";
                Image ico = FileIconHelper.GetIconForPath(firstFile);

                PictureBox pic = new PictureBox
                {
                    Location = new Point(10, (cardH - 36) / 2),
                    Size = new Size(36, 36),
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    BackColor = Color.Transparent,
                    Image = ico,
                    Cursor = Cursors.Hand
                };
                card.Controls.Add(pic);

                textLeft = 54;
                textWidth = card.Width - 92;

                Label lblFileTitle = new Label
                {
                    Text = SafeGetFileName(firstFile),
                    Font = new Font("Segoe UI Semibold", 9.5f),
                    ForeColor = Color.White,
                    Location = new Point(textLeft, 12),
                    Size = new Size(textWidth, 20),
                    AutoEllipsis = true
                };

                string sub = (item.FilePaths != null && item.FilePaths.Count > 1)
                    ? ("и еще " + (item.FilePaths.Count - 1) + " файлов • " + item.SourceApp)
                    : ((SafeDirectoryExists(firstFile) ? "Папка" : "Файл") + " • " + item.SourceApp);

                Label lblFileSub = new Label
                {
                    Text = sub + " • " + FormatDate(item.Timestamp),
                    Font = new Font("Segoe UI", 8f),
                    ForeColor = Color.FromArgb(150, 150, 150),
                    Location = new Point(textLeft, 36),
                    Size = new Size(textWidth, 18),
                    AutoEllipsis = true
                };

                card.Controls.Add(lblFileTitle);
                card.Controls.Add(lblFileSub);
            }
            else
            {
                // Для текста: читаемый текст прямо с левого края без лишних значков (как в Windows 11)
                string raw = item.TextContent ?? "";
                string line1 = raw.Replace("\r", " ").Replace("\n", " ").Trim();
                if (line1.Length > 90) line1 = line1.Substring(0, 90) + "...";

                Label lblTxtMain = new Label
                {
                    Text = line1,
                    Font = new Font("Segoe UI", 9.5f),
                    ForeColor = Color.White,
                    Location = new Point(textLeft, 12),
                    Size = new Size(textWidth, 22),
                    AutoEllipsis = true
                };

                string src = string.IsNullOrEmpty(item.SourceApp) ? "Текст" : item.SourceApp;
                Label lblTxtSub = new Label
                {
                    Text = src + " • " + raw.Length + " симв. • " + FormatDate(item.Timestamp),
                    Font = new Font("Segoe UI", 8f),
                    ForeColor = Color.FromArgb(145, 145, 145),
                    Location = new Point(textLeft, 38),
                    Size = new Size(textWidth, 18),
                    AutoEllipsis = true
                };

                card.Controls.Add(lblTxtMain);
                card.Controls.Add(lblTxtSub);
            }

            // Наведение и клик по карточке
            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                c.MouseEnter += (s, e) => { card.BackColor = Color.FromArgb(52, 52, 52); };
                c.MouseLeave += (s, e) => { card.BackColor = Color.FromArgb(43, 43, 43); };

                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left && e.Clicks == 1 && c != btnMenu && c != btnPin)
                    {
                        StartDragItem(item, card);
                    }
                };

                c.Click += (s, e) => {
                    if (c == btnMenu || c == btnPin) return;
                    var me = e as MouseEventArgs;
                    if (me == null || me.Button == MouseButtons.Left)
                    {
                        PasteItem(item);
                    }
                };

                foreach (Control sub in c.Controls) attachEvents(sub);
            };
            attachEvents(card);

            return card;
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private ContextMenu CreateCardContextMenu(ClipboardItem item)
        {
            var menu = new ContextMenu();
            menu.MenuItems.Add(new MenuItem("Вставить в активное окно", (s, e) => PasteItem(item)));
            menu.MenuItems.Add(new MenuItem("Скопировать в буфер", (s, e) => CopyItem(item)));
            menu.MenuItems.Add(new MenuItem(item.IsPinned ? "Открепить" : "Закрепить вверху", (s, e) => {
                ClipboardHistoryManager.Instance.TogglePin(item.Id);
                RefreshItems();
            }));
            menu.MenuItems.Add(new MenuItem(item.IsInSuperHub ? "Убрать из SuperHub" : "📥 Добавить в SuperHub", (s, e) => {
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
                if (item.Type == ClipboardItemType.Image && SafeFileExists(item.ImagePath))
                {
                    ClipboardListenerWindow.AugmentClipboardWithImage(item.ImagePath);
                }
                else if (item.Type == ClipboardItemType.Text)
                {
                    Clipboard.SetText(item.TextContent ?? "");
                }
                else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
                {
                    var sc = new StringCollection();
                    foreach (var f in item.FilePaths) if (SafeFileExists(f) || SafeDirectoryExists(f)) sc.Add(f);
                    if (sc.Count > 0) Clipboard.SetFileDropList(sc);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("CopyItem error: " + ex.Message);
            }
        }

        private void PasteItem(ClipboardItem item)
        {
            CopyItem(item);
            CloseFlyout();

            ThreadPool.QueueUserWorkItem(_ => {
                Thread.Sleep(80);
                try
                {
                    if (previousForegroundWindow != IntPtr.Zero)
                    {
                        SetForegroundWindow(previousForegroundWindow);
                        Thread.Sleep(50);
                    }
                    keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(20);
                    keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
                catch (Exception ex)
                {
                    Logger.Log("PasteItem simulated input error: " + ex.Message);
                }
            });
        }

        private void StartDragItem(ClipboardItem item, Control sourceControl)
        {
            try
            {
                DataObject data = new DataObject();
                if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
                {
                    var sc = new StringCollection();
                    foreach (var f in item.FilePaths) if (SafeFileExists(f) || SafeDirectoryExists(f)) sc.Add(f);
                    if (sc.Count > 0) data.SetFileDropList(sc);
                }
                else if (item.Type == ClipboardItemType.Image && SafeFileExists(item.ImagePath))
                {
                    var sc = new StringCollection { item.ImagePath };
                    data.SetFileDropList(sc);
                }
                else if (item.Type == ClipboardItemType.Text)
                {
                    data.SetText(item.TextContent ?? "");
                }

                sourceControl.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            catch (Exception ex)
            {
                Logger.Log("StartDragItem error: " + ex.Message);
            }
        }

        private void SaveItemToDesktop(ClipboardItem item)
        {
            try
            {
                string dt = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (item.Type == ClipboardItemType.Image && SafeFileExists(item.ImagePath))
                {
                    string target = Path.Combine(dt, SafeGetFileName(item.ImagePath));
                    File.Copy(item.ImagePath, target, true);
                    DesktopHelper.PositionFile(target, true);
                }
                else if (item.Type == ClipboardItemType.Files && item.FilePaths != null)
                {
                    foreach (var f in item.FilePaths)
                    {
                        if (SafeFileExists(f))
                        {
                            string target = Path.Combine(dt, SafeGetFileName(f));
                            File.Copy(f, target, true);
                            DesktopHelper.PositionFile(target, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("SaveItemToDesktop error: " + ex.Message);
            }
        }

        private void ShowItemInExplorer(ClipboardItem item)
        {
            try
            {
                string path = null;
                if (item.Type == ClipboardItemType.Image) path = item.ImagePath;
                else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0) path = item.FilePaths[0];

                if (!string.IsNullOrEmpty(path) && (SafeFileExists(path) || SafeDirectoryExists(path)))
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
            }
            catch {}
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
