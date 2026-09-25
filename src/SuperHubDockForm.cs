using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PasteImageAsFile
{
    public class SuperHubDockForm : Form
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out DesktopHelper.POINT lpPoint);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        const int DWMWA_NCRENDERING_POLICY = 2;
        const int DWMNCRP_DISABLED = 1;
        const int DWMNCRP_ENABLED = 2;

        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        const int VK_LBUTTON = 0x01;
        const byte VK_CONTROL = 0x11;
        const byte VK_V = 0x56;
        const uint KEYEVENTF_KEYUP = 0x0002;

        // Размеры вертикального режима (из настроек)
        private int ExpandedWidthVertical { get { return Config.SuperHubWidthVertical; } }
        private int ShelfHeightVertical { get { return Config.SuperHubHeightVertical; } }

        // Размеры горизонтального режима (сверху / снизу, из настроек)
        private int ExpandedWidthHorizontal { get { return Config.SuperHubWidthHorizontal; } }
        private int ShelfHeightHorizontal { get { return Config.SuperHubHeightHorizontal; } }

        // Размеры видимого ярлычка в свернутом виде (акцентный маркер без серой подложки)
        private const int CollapsedBarThickness = 4;
        private const int CollapsedBarLength = 80;

        private static SuperHubDockForm instance;
        private static readonly object instanceLock = new object();

        private bool isExpanded = false;
        private bool isPinned = false;
        private System.Windows.Forms.Timer pollTimer;
        private DateTime lastPointerInside = DateTime.MinValue;
        private int hoverExpandCounter = 0;

        // Плавная анимация выезда и сворачивания (Slide-in / Slide-out)
        private System.Windows.Forms.Timer animTimer;
        private bool isAnimating = false;
        private bool isExpandingTarget = false;
        private int animCurrentStep = 0;
        private const int AnimTotalSteps = 12;
        private Rectangle animStartBounds;
        private Rectangle animTargetBounds;

        private Panel pnlHeader;
        private Label lblTitle;
        private Button btnDragMode;
        private Button btnSettings;
        private Button btnPin;
        private Button btnDragAll;
        private Button btnClear;
        private Button btnCollapse;

        // Навигационные вкладки режима "Буфер обмена и SuperHub"
        private Panel pnlTabs;
        private Button btnMainClipboard;
        private Button btnMainSuperHub;
        private int currentMainTab = 1; // 0 = Буфер обмена, 1 = SuperHub

        private Panel pnlSubTabs;
        private readonly string[] filterNames = new string[] { "Все", "Снимки", "Текст", "Файлы" };
        private List<Button> filterButtons = new List<Button>();
        private int currentFilterIndex = 0; // 0 = Все, 1 = Снимки, 2 = Текст, 3 = Файлы

        private Panel pnlContent;
        private Panel cardsList;
        private Panel dropHint;
        private Label lblDropHint;
        private ToolTip toolTip;

        // Fluent скроллбар для полки
        private int scrollOffset = 0;
        private int maxScrollOffset = 0;

        public static SuperHubDockForm Instance
        {
            get
            {
                lock (instanceLock)
                {
                    if (instance == null || instance.IsDisposed)
                    {
                        instance = new SuperHubDockForm();
                    }
                    return instance;
                }
            }
        }

        public SuperHubDockForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(CollapsedBarThickness, CollapsedBarLength);
            this.TopMost = true;
            this.AllowDrop = true;
            this.DoubleBuffered = true;
            this.MinimumSize = new Size(1, 1);

            this.Click += (s, e) => {
                if (!isExpanded)
                {
                    ExpandShelf();
                }
            };

            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 6000;
            toolTip.InitialDelay = 250;
            toolTip.ReshowDelay = 100;
            toolTip.ShowAlways = true;

            this.Shown += (s, e) => ApplyWindowStyles();

            ThemeHelper.ThemeChanged += () => {
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke((Action)(() => {
                        ApplyTheme();
                        RefreshItems();
                    }));
                }
            };

            // Синхронизация в реальном времени с менеджером истории
            ClipboardHistoryManager.Instance.HistoryChanged += () => {
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke((Action)(() => {
                        if (isExpanded)
                        {
                            RefreshItems();
                        }
                    }));
                }
            };

            BuildUI();
            ApplyTheme();
            PositionCollapsed();

            // Таймер отслеживания приближения курсора и перетаскивания файлов
            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Interval = 50;
            pollTimer.Tick += OnPollTick;
            pollTimer.Start();

            // Таймер плавной анимации выдвижения/скрытия полки (Slide-in / Slide-out)
            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 12;
            animTimer.Tick += OnAnimTick;

            // Настройка сплошного приема Drop на всё окно и дочерние контролы
            RegisterDropTarget(this);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE - не перехватывает фокус у других окон
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW - не создаёт стандартную рамку DWM
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTLEFT = 10;
            const int HTRIGHT = 11;
            const int HTTOP = 12;
            const int HTTOPLEFT = 13;
            const int HTTOPRIGHT = 14;
            const int HTBOTTOM = 15;
            const int HTBOTTOMLEFT = 16;
            const int HTBOTTOMRIGHT = 17;

            // Интерактивный ресайз перетаскиванием свободных краев окна
            if (m.Msg == WM_NCHITTEST && isExpanded && !isAnimating)
            {
                Point pt = this.PointToClient(Cursor.Position);
                int b = 6;
                bool l = pt.X <= b;
                bool r = pt.X >= this.ClientSize.Width - b;
                bool t = pt.Y <= b;
                bool d = pt.Y >= this.ClientSize.Height - b;

                bool isHoriz = IsHorizontalPosition();
                bool isLeftDock = IsPositionOnLeft();
                bool isTopDock = IsPositionTop();
                bool isBottomDock = IsPositionBottom();

                if (isHoriz)
                {
                    if (isTopDock)
                    {
                        if (d && l) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                        if (d && r) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                        if (d) { m.Result = (IntPtr)HTBOTTOM; return; }
                        if (l) { m.Result = (IntPtr)HTLEFT; return; }
                        if (r) { m.Result = (IntPtr)HTRIGHT; return; }
                    }
                    else if (isBottomDock)
                    {
                        if (t && l) { m.Result = (IntPtr)HTTOPLEFT; return; }
                        if (t && r) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                        if (t) { m.Result = (IntPtr)HTTOP; return; }
                        if (l) { m.Result = (IntPtr)HTLEFT; return; }
                        if (r) { m.Result = (IntPtr)HTRIGHT; return; }
                    }
                }
                else
                {
                    if (!isLeftDock) // Прикреплен справа экрана
                    {
                        if (l && t) { m.Result = (IntPtr)HTTOPLEFT; return; }
                        if (l && d) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                        if (l) { m.Result = (IntPtr)HTLEFT; return; }
                        if (t) { m.Result = (IntPtr)HTTOP; return; }
                        if (d) { m.Result = (IntPtr)HTBOTTOM; return; }
                    }
                    else // Прикреплен слева экрана
                    {
                        if (r && t) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                        if (r && d) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                        if (r) { m.Result = (IntPtr)HTRIGHT; return; }
                        if (t) { m.Result = (IntPtr)HTTOP; return; }
                        if (d) { m.Result = (IntPtr)HTBOTTOM; return; }
                    }
                }
            }

            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (isExpanded && !isAnimating)
            {
                if (IsHorizontalPosition())
                {
                    Config.SuperHubWidthHorizontal = Math.Max(380, Math.Min(1600, this.Width));
                    Config.SuperHubHeightHorizontal = Math.Max(160, Math.Min(800, this.Height));
                }
                else
                {
                    Config.SuperHubWidthVertical = Math.Max(260, Math.Min(900, this.Width));
                    Config.SuperHubHeightVertical = Math.Max(300, Math.Min(1200, this.Height));
                }
                LayoutShelfContent();
                RefreshItems();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!isExpanded)
            {
                // В свернутом состоянии заливаем фон исключительно чистым цветом акцента без серого подслоя
                using (var b = new SolidBrush(ThemeHelper.Accent))
                {
                    e.Graphics.FillRectangle(b, 0, 0, this.Width, this.Height);
                }
                return;
            }
            base.OnPaintBackground(e);
        }

        private void ApplyWindowStyles()
        {
            try
            {
                int corner = isExpanded ? DWMWCP_ROUND : 1;
                DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                int dark = ThemeHelper.IsDarkTheme() ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                int ncPolicy = isExpanded ? DWMNCRP_ENABLED : DWMNCRP_DISABLED;
                DwmSetWindowAttribute(this.Handle, DWMWA_NCRENDERING_POLICY, ref ncPolicy, sizeof(int));
            }
            catch {}
        }

        private void ApplyTheme()
        {
            ApplyWindowStyles();
            this.BackColor = ThemeHelper.Background;
            this.ForeColor = ThemeHelper.TextPrimary;

            if (pnlHeader != null)
            {
                pnlHeader.BackColor = ThemeHelper.HeaderBackground;
                lblTitle.ForeColor = ThemeHelper.TextPrimary;
            }

            if (pnlTabs != null)
            {
                pnlTabs.BackColor = ThemeHelper.HeaderBackground;
                pnlTabs.Invalidate();
            }

            if (pnlSubTabs != null)
            {
                pnlSubTabs.BackColor = ThemeHelper.HeaderBackground;
                pnlSubTabs.Invalidate();
            }

            UpdateTabsVisual();

            if (dropHint != null)
            {
                dropHint.BackColor = ThemeHelper.CardBackground;
                if (lblDropHint != null) lblDropHint.ForeColor = ThemeHelper.TextSecondary;
            }

            if (pnlContent != null)
            {
                pnlContent.BackColor = ThemeHelper.Background;
            }

            UpdateDragModeButtonVisual();
            this.Invalidate();
        }

        private void RegisterDropTarget(Control parent)
        {
            parent.AllowDrop = true;
            parent.DragEnter -= OnShelfDragEnter;
            parent.DragOver -= OnShelfDragOver;
            parent.DragDrop -= OnShelfDragDrop;

            parent.DragEnter += OnShelfDragEnter;
            parent.DragOver += OnShelfDragOver;
            parent.DragDrop += OnShelfDragDrop;

            foreach (Control child in parent.Controls)
            {
                RegisterDropTarget(child);
            }
        }

        public bool IsHorizontalPosition()
        {
            string pos = Config.SuperHubPosition;
            return string.Equals(pos, "TopCenter", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(pos, "BottomCenter", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPositionTop()
        {
            return string.Equals(Config.SuperHubPosition, "TopCenter", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPositionBottom()
        {
            return string.Equals(Config.SuperHubPosition, "BottomCenter", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsPositionOnLeft()
        {
            string pos = Config.SuperHubPosition;
            return pos.StartsWith("Left", StringComparison.OrdinalIgnoreCase);
        }

        private int CalculateShelfY(Screen scr)
        {
            string pos = Config.SuperHubPosition;
            if (pos.EndsWith("Top", StringComparison.OrdinalIgnoreCase))
            {
                return scr.WorkingArea.Top + 24;
            }
            if (pos.EndsWith("Bottom", StringComparison.OrdinalIgnoreCase))
            {
                return scr.WorkingArea.Bottom - ShelfHeightVertical - 24;
            }
            // Center
            return scr.WorkingArea.Top + (scr.WorkingArea.Height - ShelfHeightVertical) / 2;
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            // 1. Шапка полки SuperHub - 38px
            pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ExpandedWidthVertical, 38),
                BackColor = Color.FromArgb(24, 24, 24),
                Visible = false
            };

            Font titleFont;
            try { titleFont = new Font("Segoe UI Emoji", 9.5f, FontStyle.Bold); }
            catch { titleFont = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold); }

            lblTitle = new Label
            {
                Text = "SuperHub",
                Font = titleFont,
                ForeColor = Color.White,
                Location = new Point(8, 9),
                Size = new Size(110, 20),
                AutoEllipsis = true
            };
            pnlHeader.Controls.Add(lblTitle);

            // Кнопка сворачивания [› / ‹ / ▲ / ▼]
            btnCollapse = CreateToolButton("›", 260, 7, 24, 24, "Свернуть полку SuperHub");
            btnCollapse.Click += (s, e) => { isPinned = false; CollapseShelf(); };
            pnlHeader.Controls.Add(btnCollapse);

            // Кнопка закрепить [📌]
            btnPin = CreateToolButton("📌", 234, 7, 24, 24, "Закрепить полку на экране (не скрывать при уходе мыши)");
            btnPin.Click += (s, e) => {
                isPinned = !isPinned;
                btnPin.ForeColor = isPinned ? ThemeHelper.Accent : ThemeHelper.TextSecondary;
                toolTip.SetToolTip(btnPin, isPinned ? "Полка закреплена (кликните, чтобы открепить)" : "Закрепить полку на экране");
            };
            pnlHeader.Controls.Add(btnPin);

            // Кнопка шестеренки [⚙] (Настройки)
            btnSettings = CreateToolButton("⚙", 208, 7, 24, 24, "Открыть настройки PasteImageAsFile и SuperHub");
            btnSettings.Click += (s, e) => { Program.ShowMainForm(); };
            pnlHeader.Controls.Add(btnSettings);

            // Кнопка "Перетащить все" [📦]
            btnDragAll = CreateToolButton("📦", 182, 7, 24, 24, "Зажать левой кнопкой мыши и перетащить все файлы полки наружу");
            btnDragAll.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) DragAllFilesOut();
            };
            pnlHeader.Controls.Add(btnDragAll);

            // Кнопка очистки [🗑]
            btnClear = CreateToolButton("🗑", 156, 7, 24, 24, "Очистить полку SuperHub (удалить все элементы)");
            btnClear.Click += (s, e) => {
                bool showTabs = string.Equals(Config.SuperHubViewMode, "Tabs", StringComparison.OrdinalIgnoreCase);
                bool isHub = (!showTabs || currentMainTab == 1);
                if (isHub)
                {
                    if (MessageBox.Show("Очистить все файлы на полке SuperHub?", "SuperHub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        ClipboardHistoryManager.Instance.ClearAll(null, true);
                        RefreshItems();
                    }
                }
                else
                {
                    ClipboardItemType? filter = null;
                    if (currentFilterIndex == 1) filter = ClipboardItemType.Image;
                    else if (currentFilterIndex == 2) filter = ClipboardItemType.Text;
                    else if (currentFilterIndex == 3) filter = ClipboardItemType.Files;

                    string cat = filterNames[currentFilterIndex];
                    if (MessageBox.Show("Очистить раздел \"" + cat + "\" журнала буфера обмена?", "Буфер обмена", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        ClipboardHistoryManager.Instance.ClearAll(filter, false);
                        RefreshItems();
                    }
                }
            };
            pnlHeader.Controls.Add(btnClear);

            // Кнопка режима Drag-and-Drop: [Копия / Перенос]
            btnDragMode = new Button
            {
                Location = new Point(88, 7),
                Size = new Size(64, 24),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnDragMode.FlatAppearance.BorderSize = 0;
            btnDragMode.Click += (s, e) => {
                bool isCopy = string.Equals(Config.SuperHubDragMode, "Copy", StringComparison.OrdinalIgnoreCase);
                Config.SuperHubDragMode = isCopy ? "Move" : "Copy";
                UpdateDragModeButtonVisual();
            };
            UpdateDragModeButtonVisual();
            pnlHeader.Controls.Add(btnDragMode);

            pnlHeader.Resize += (s, e) => LayoutHeaderControls();
            this.Controls.Add(pnlHeader);

            // 2. Верхний уровень вкладок: [📋 Буфер] и [📦 SuperHub] - 28px
            pnlTabs = new Panel
            {
                Location = new Point(0, 38),
                Size = new Size(ExpandedWidthVertical, 28),
                BackColor = Color.FromArgb(24, 24, 24),
                Visible = false
            };

            btnMainClipboard = new Button
            {
                Text = "Буфер",
                Location = new Point(8, 2),
                Size = new Size(86, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = (currentMainTab == 0) ? ThemeHelper.TextPrimary : ThemeHelper.TextSecondary,
                Font = new Font("Segoe UI", 8f, (currentMainTab == 0) ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnMainClipboard.FlatAppearance.BorderSize = 0;
            btnMainClipboard.Click += (s, e) => SwitchMainTab(0);
            pnlTabs.Controls.Add(btnMainClipboard);

            btnMainSuperHub = new Button
            {
                Text = "SuperHub",
                Location = new Point(98, 2),
                Size = new Size(116, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = (currentMainTab == 1) ? ThemeHelper.TextPrimary : ThemeHelper.TextSecondary,
                Font = new Font("Segoe UI", 8f, (currentMainTab == 1) ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnMainSuperHub.FlatAppearance.BorderSize = 0;
            btnMainSuperHub.Click += (s, e) => SwitchMainTab(1);
            pnlTabs.Controls.Add(btnMainSuperHub);

            pnlTabs.Paint += (s, e) => {
                Button actBtn = (currentMainTab == 0) ? btnMainClipboard : btnMainSuperHub;
                if (actBtn != null)
                {
                    int barW = Math.Min(36, actBtn.Width - 8);
                    int barX = actBtn.Left + (actBtn.Width - barW) / 2;
                    using (var brush = new SolidBrush(ThemeHelper.Accent))
                    {
                        e.Graphics.FillRectangle(brush, barX, pnlTabs.Height - 2, barW, 2);
                    }
                }
            };
            this.Controls.Add(pnlTabs);

            // Нижний уровень подвкладок буфера: [Все] [Снимки] [Текст] [Файлы] - 26px
            pnlSubTabs = new Panel
            {
                Location = new Point(0, 66),
                Size = new Size(ExpandedWidthVertical, 26),
                BackColor = Color.FromArgb(28, 28, 28),
                Visible = false
            };

            filterButtons.Clear();
            int subX = 8;
            for (int i = 0; i < filterNames.Length; i++)
            {
                int fIdx = i;
                Button fBtn = new Button
                {
                    Text = filterNames[i],
                    Location = new Point(subX, 2),
                    Size = new Size(i == 0 ? 36 : 52, 22),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = (i == currentFilterIndex) ? ThemeHelper.AccentBackground : Color.Transparent,
                    ForeColor = (i == currentFilterIndex) ? ThemeHelper.Accent : ThemeHelper.TextSecondary,
                    Font = new Font("Segoe UI", 7.5f, (i == currentFilterIndex) ? FontStyle.Bold : FontStyle.Regular),
                    Cursor = Cursors.Hand,
                    TabStop = false
                };
                fBtn.FlatAppearance.BorderSize = 0;
                fBtn.Click += (s, e) => SwitchFilter(fIdx);
                filterButtons.Add(fBtn);
                pnlSubTabs.Controls.Add(fBtn);
                subX += fBtn.Width + 4;
            }
            this.Controls.Add(pnlSubTabs);

            // 3. Зона подсказки сброса файлов (Drop Hint) - 32px
            dropHint = new Panel
            {
                Location = new Point(8, 72),
                Size = new Size(ExpandedWidthVertical - 16, 32),
                BackColor = Color.FromArgb(36, 36, 36),
                Visible = false,
                AllowDrop = true
            };
            dropHint.Paint += (s, e) => {
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    pen.DashStyle = DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen, 0, 0, dropHint.Width - 1, dropHint.Height - 1);
                }
            };

            lblDropHint = new Label
            {
                Text = "Перетащите файлы или текст сюда",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(160, 160, 160),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            dropHint.Controls.Add(lblDropHint);
            this.Controls.Add(dropHint);

            // 4. Область контента карточек
            pnlContent = new Panel
            {
                Location = new Point(0, 108),
                Size = new Size(ExpandedWidthVertical, ShelfHeightVertical - 110),
                BackColor = Color.FromArgb(28, 28, 28),
                Visible = false,
                AllowDrop = true
            };

            cardsList = new Panel
            {
                Location = new Point(8, 0),
                Width = ExpandedWidthVertical - 18,
                BackColor = Color.Transparent,
                AllowDrop = true
            };
            pnlContent.Controls.Add(cardsList);

            pnlContent.MouseWheel += (s, e) => {
                if (maxScrollOffset <= 0) return;
                int step = 45;
                int newOffset = scrollOffset + (e.Delta > 0 ? -step : step);
                if (newOffset < 0) newOffset = 0;
                if (newOffset > maxScrollOffset) newOffset = maxScrollOffset;
                if (newOffset != scrollOffset)
                {
                    scrollOffset = newOffset;
                    if (IsHorizontalPosition())
                    {
                        cardsList.Left = -scrollOffset + 8;
                    }
                    else
                    {
                        cardsList.Top = -scrollOffset;
                    }
                    pnlContent.Invalidate();
                }
            };

            pnlContent.Paint += RenderScrollbar;
            this.Controls.Add(pnlContent);

            this.Paint += (s, e) => {
                if (!isExpanded)
                {
                    using (var b = new SolidBrush(ThemeHelper.Accent))
                    {
                        e.Graphics.FillRectangle(b, 0, 0, this.Width, this.Height);
                    }
                }
                else
                {
                    using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                    {
                        e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                    }
                }
            };

            this.ResumeLayout(false);
        }

        public void SwitchMainTab(int mainTab)
        {
            currentMainTab = mainTab;
            UpdateTabsVisual();
            LayoutShelfContent();
            RefreshItems();
        }

        public void SwitchFilter(int filterIndex)
        {
            currentFilterIndex = filterIndex;
            UpdateTabsVisual();
            RefreshItems();
        }

        public void SwitchTab(int index)
        {
            if (index == 4) SwitchMainTab(1);
            else
            {
                SwitchMainTab(0);
                SwitchFilter(index);
            }
        }

        private void UpdateTabsVisual()
        {
            if (btnMainClipboard != null)
            {
                bool active = (currentMainTab == 0);
                btnMainClipboard.Font = new Font("Segoe UI", 8f, active ? FontStyle.Bold : FontStyle.Regular);
                btnMainClipboard.ForeColor = active ? ThemeHelper.TextPrimary : ThemeHelper.TextSecondary;
            }

            if (btnMainSuperHub != null)
            {
                bool active = (currentMainTab == 1);
                var superList = ClipboardHistoryManager.Instance.GetItems(null, true);
                string title = superList.Count > 0 ? ("SuperHub [" + superList.Count + "]") : "SuperHub";
                btnMainSuperHub.Text = title;
                btnMainSuperHub.Font = new Font("Segoe UI", 8f, active ? FontStyle.Bold : FontStyle.Regular);
                btnMainSuperHub.ForeColor = active ? ThemeHelper.TextPrimary : ThemeHelper.TextSecondary;
            }

            if (pnlTabs != null) pnlTabs.Invalidate();

            for (int i = 0; i < filterButtons.Count; i++)
            {
                bool active = (i == currentFilterIndex);
                filterButtons[i].BackColor = active ? ThemeHelper.AccentBackground : Color.Transparent;
                filterButtons[i].ForeColor = active ? ThemeHelper.Accent : ThemeHelper.TextSecondary;
                filterButtons[i].Font = new Font("Segoe UI", 7.5f, active ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        private void LayoutHeaderControls()
        {
            if (pnlHeader == null) return;
            int r = pnlHeader.Width - 4;

            // Кнопки справа налево:
            btnCollapse.Location = new Point(r - 24, 7);
            btnCollapse.Size = new Size(24, 24);
            r -= 26;

            btnPin.Location = new Point(r - 24, 7);
            btnPin.Size = new Size(24, 24);
            r -= 26;

            btnSettings.Location = new Point(r - 24, 7);
            btnSettings.Size = new Size(24, 24);
            r -= 26;

            if (btnDragAll.Visible)
            {
                btnDragAll.Location = new Point(r - 24, 7);
                btnDragAll.Size = new Size(24, 24);
                r -= 26;
            }

            btnClear.Location = new Point(r - 24, 7);
            btnClear.Size = new Size(24, 24);
            r -= 28;

            if (btnDragMode.Visible)
            {
                btnDragMode.Location = new Point(r - 64, 7);
                btnDragMode.Size = new Size(64, 24);
                r -= 68;
            }

            // Заголовок занимает пространство слева
            lblTitle.Location = new Point(8, 9);
            lblTitle.Size = new Size(Math.Max(50, r - 12), 20);
        }

        private void ApplySlideOffset()
        {
            if (IsHorizontalPosition())
            {
                int h = ShelfHeightHorizontal;
                int offsetY = IsPositionTop() ? 0 : (this.ClientSize.Height - h);
                if (pnlHeader != null) pnlHeader.Top = offsetY;
                int topY = offsetY + 38;
                if (pnlTabs != null && pnlTabs.Visible)
                {
                    pnlTabs.Top = topY;
                    topY += pnlTabs.Height;
                }
                if (pnlSubTabs != null && pnlSubTabs.Visible)
                {
                    pnlSubTabs.Top = topY;
                    topY += pnlSubTabs.Height;
                }
                if (dropHint != null && dropHint.Visible)
                {
                    dropHint.Top = topY + 3;
                    topY += 38;
                }
                if (pnlContent != null && pnlContent.Visible)
                {
                    pnlContent.Top = topY;
                }
            }
            else
            {
                int w = ExpandedWidthVertical;
                int offsetX = IsPositionOnLeft() ? 0 : (this.ClientSize.Width - w);
                if (pnlHeader != null) pnlHeader.Left = offsetX;
                if (pnlTabs != null) pnlTabs.Left = offsetX;
                if (pnlSubTabs != null) pnlSubTabs.Left = offsetX;
                if (dropHint != null) dropHint.Left = offsetX + 8;
                if (pnlContent != null) pnlContent.Left = offsetX;
            }
        }

        private void LayoutShelfContent()
        {
            int w = IsHorizontalPosition() ? ExpandedWidthHorizontal : ExpandedWidthVertical;
            int h = IsHorizontalPosition() ? ShelfHeightHorizontal : ShelfHeightVertical;

            if (pnlHeader != null)
            {
                pnlHeader.Visible = isExpanded;
                pnlHeader.Bounds = new Rectangle(0, 0, w, 38);
                pnlHeader.BringToFront();
                LayoutHeaderControls();
            }

            bool showTabs = string.Equals(Config.SuperHubViewMode, "Tabs", StringComparison.OrdinalIgnoreCase);

            int topY = 38;
            if (pnlTabs != null)
            {
                pnlTabs.Visible = showTabs && isExpanded;
                if (pnlTabs.Visible)
                {
                    pnlTabs.Location = new Point(0, topY);
                    pnlTabs.Size = new Size(w, 28);
                    pnlTabs.BringToFront();
                    topY += pnlTabs.Height;
                }
            }

            bool isHubTab = (!showTabs || currentMainTab == 1);

            if (pnlSubTabs != null)
            {
                pnlSubTabs.Visible = showTabs && isExpanded && !isHubTab;
                if (pnlSubTabs.Visible)
                {
                    pnlSubTabs.Location = new Point(0, topY);
                    pnlSubTabs.Size = new Size(w, 28);
                    pnlSubTabs.BringToFront();
                    topY += pnlSubTabs.Height;
                }
            }

            if (dropHint != null)
            {
                dropHint.Visible = isHubTab && isExpanded;
                if (dropHint.Visible)
                {
                    dropHint.Location = new Point(8, topY + 3);
                    dropHint.Size = new Size(Math.Max(100, w - 16), 32);
                    topY += 38;
                }
            }

            if (pnlContent != null)
            {
                pnlContent.Visible = isExpanded;
                pnlContent.Location = new Point(0, topY);
                pnlContent.Size = new Size(w, Math.Max(40, h - topY));
            }

            ApplySlideOffset();
        }

        private void UpdateDragModeButtonVisual()
        {
            if (btnDragMode == null) return;
            bool isCopy = string.Equals(Config.SuperHubDragMode, "Copy", StringComparison.OrdinalIgnoreCase);
            if (isCopy)
            {
                btnDragMode.Text = "Копия";
                btnDragMode.BackColor = ThemeHelper.AccentBackground;
                btnDragMode.ForeColor = ThemeHelper.Accent;
                toolTip.SetToolTip(btnDragMode, "Режим: КОПИРОВАНИЕ файлов.\nПри перетаскивании наружу исходные файлы остаются на месте.\nКликните для переключения в режим Переноса.");
            }
            else
            {
                btnDragMode.Text = "Перенос";
                btnDragMode.BackColor = Color.FromArgb(80, 50, 20);
                btnDragMode.ForeColor = Color.FromArgb(255, 170, 60);
                toolTip.SetToolTip(btnDragMode, "Режим: ПЕРЕМЕЩЕНИЕ файлов.\nПри перетаскивании наружу файлы вырезаются и переносятся.\nКликните для переключения в режим Копирования.");
            }
        }

        private Button CreateToolButton(string text, int x, int y, int w, int h, string tip)
        {
            Font btnFont;
            try { btnFont = new Font("Segoe UI Emoji", 9f); }
            catch { btnFont = new Font("Segoe UI", 9f); }

            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ThemeHelper.TextSecondary,
                Font = btnFont,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
            btn.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
            toolTip.SetToolTip(btn, tip);
            return btn;
        }

        private void RenderScrollbar(object sender, PaintEventArgs e)
        {
            if (maxScrollOffset <= 0) return;

            using (var b = new SolidBrush(ThemeHelper.ScrollBarThumb))
            {
                if (IsHorizontalPosition())
                {
                    int trackW = pnlContent.Width - 16;
                    int thumbW = Math.Max(24, (int)((float)pnlContent.Width / cardsList.Width * trackW));
                    int thumbX = 8 + (int)((float)scrollOffset / maxScrollOffset * (trackW - thumbW));
                    e.Graphics.FillRectangle(b, thumbX, pnlContent.Height - 5, thumbW, 3);
                }
                else
                {
                    int trackH = pnlContent.Height - 16;
                    int thumbH = Math.Max(24, (int)((float)pnlContent.Height / cardsList.Height * trackH));
                    int thumbY = 6 + (int)((float)scrollOffset / maxScrollOffset * (trackH - thumbH));
                    e.Graphics.FillRectangle(b, pnlContent.Width - 5, thumbY, 3, thumbH);
                }
            }
        }

        private Rectangle GetCollapsedBounds()
        {
            Screen scr = Screen.PrimaryScreen;
            if (IsHorizontalPosition())
            {
                int x = scr.WorkingArea.Left + (scr.WorkingArea.Width - CollapsedBarLength) / 2;
                int y = IsPositionTop() ? scr.WorkingArea.Top : (scr.WorkingArea.Bottom - CollapsedBarThickness);
                return new Rectangle(x, y, CollapsedBarLength, CollapsedBarThickness);
            }
            else
            {
                int y = CalculateShelfY(scr) + (ShelfHeightVertical - CollapsedBarLength) / 2;
                int x = IsPositionOnLeft() ? scr.WorkingArea.Left : (scr.WorkingArea.Right - CollapsedBarThickness);
                return new Rectangle(x, y, CollapsedBarThickness, CollapsedBarLength);
            }
        }

        public void PositionCollapsed()
        {
            if (!Config.SuperHubEnabled)
            {
                this.Visible = false;
                return;
            }

            if (animTimer != null) animTimer.Stop();
            isAnimating = false;
            this.isExpanded = false;
            pnlHeader.Visible = false;
            if (pnlTabs != null) pnlTabs.Visible = false;
            if (pnlSubTabs != null) pnlSubTabs.Visible = false;
            dropHint.Visible = false;
            pnlContent.Visible = false;

            Rectangle b = GetCollapsedBounds();
            this.SetBounds(b.X, b.Y, b.Width, b.Height);
            if (IsHorizontalPosition())
            {
                btnCollapse.Text = IsPositionTop() ? "▲" : "▼";
            }
            else
            {
                btnCollapse.Text = IsPositionOnLeft() ? "‹" : "›";
            }

            ApplyWindowStyles();
            this.Visible = true;
            this.Invalidate();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            animCurrentStep++;
            float t = (float)animCurrentStep / AnimTotalSteps;
            if (t > 1.0f) t = 1.0f;

            // Кубическая функция сглаживания Ease-Out (быстрый старт, плавное замедление)
            float ease = (float)(1.0 - Math.Pow(1.0 - t, 3));

            int curX = (int)(animStartBounds.X + (animTargetBounds.X - animStartBounds.X) * ease);
            int curY = (int)(animStartBounds.Y + (animTargetBounds.Y - animStartBounds.Y) * ease);
            int curW = (int)(animStartBounds.Width + (animTargetBounds.Width - animStartBounds.Width) * ease);
            int curH = (int)(animStartBounds.Height + (animTargetBounds.Height - animStartBounds.Height) * ease);

            this.SetBounds(curX, curY, Math.Max(1, curW), Math.Max(1, curH));
            ApplySlideOffset();

            if (animCurrentStep >= AnimTotalSteps)
            {
                animTimer.Stop();
                isAnimating = false;
                this.SetBounds(animTargetBounds.X, animTargetBounds.Y, animTargetBounds.Width, animTargetBounds.Height);

                if (!isExpandingTarget)
                {
                    PositionCollapsed();
                }
                else
                {
                    this.isExpanded = true;
                    ApplySlideOffset();
                    ApplyWindowStyles();
                    this.BringToFront();
                    this.Invalidate();
                }
            }
        }

        public void ExpandShelf(bool animate = true)
        {
            if (!Config.SuperHubEnabled) return;
            if (isExpanded && !isAnimating) return;
            if (isAnimating && isExpandingTarget) return;

            Screen scr = Screen.PrimaryScreen;
            Rectangle target;
            if (IsHorizontalPosition())
            {
                int w = ExpandedWidthHorizontal;
                int h = ShelfHeightHorizontal;
                int x = scr.WorkingArea.Left + (scr.WorkingArea.Width - w) / 2;
                int y = IsPositionTop() ? scr.WorkingArea.Top : (scr.WorkingArea.Bottom - h);
                target = new Rectangle(x, y, w, h);
                btnCollapse.Text = IsPositionTop() ? "▲" : "▼";
            }
            else
            {
                int w = ExpandedWidthVertical;
                int h = ShelfHeightVertical;
                int y = CalculateShelfY(scr);
                int x = IsPositionOnLeft() ? scr.WorkingArea.Left : (scr.WorkingArea.Right - w);
                target = new Rectangle(x, y, w, h);
                btnCollapse.Text = IsPositionOnLeft() ? "‹" : "›";
            }

            this.isExpanded = true;
            LayoutShelfContent();
            RefreshItems();

            ApplyWindowStyles();

            if (!animate)
            {
                this.SetBounds(target.X, target.Y, target.Width, target.Height);
                ApplySlideOffset();
                this.BringToFront();
                this.Invalidate();
                return;
            }

            animStartBounds = this.Bounds;
            animTargetBounds = target;
            isExpandingTarget = true;
            isAnimating = true;
            animCurrentStep = 0;
            ApplySlideOffset();
            animTimer.Start();
        }

        public void CollapseShelf(bool animate = true)
        {
            if (isPinned) return;
            if (!isExpanded && !isAnimating) return;
            if (isAnimating && !isExpandingTarget) return;

            Rectangle collapsed = GetCollapsedBounds();

            if (!animate)
            {
                PositionCollapsed();
                return;
            }

            animStartBounds = this.Bounds;
            animTargetBounds = collapsed;
            isExpandingTarget = false;
            isAnimating = true;
            animCurrentStep = 0;
            animTimer.Start();
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            if (!Config.SuperHubEnabled)
            {
                if (this.Visible) this.Visible = false;
                return;
            }

            DesktopHelper.POINT pt;
            if (!GetCursorPos(out pt)) return;

            Point cur = new Point(pt.x, pt.y);

            // Проверяем нахождение курсора над областью окна SuperHub
            Rectangle hitArea = this.Bounds;
            if (!isExpanded)
            {
                if (IsHorizontalPosition())
                {
                    hitArea.Inflate(8, 4);
                }
                else
                {
                    hitArea.Inflate(4, 8);
                }
            }

            bool isOverUs = hitArea.Contains(cur);

            if (isOverUs)
            {
                lastPointerInside = DateTime.UtcNow;
            }

            if (!isExpanded)
            {
                if (isOverUs)
                {
                    hoverExpandCounter++;
                    if (hoverExpandCounter >= 2)
                    {
                        hoverExpandCounter = 0;
                        ExpandShelf();
                    }
                }
                else
                {
                    hoverExpandCounter = 0;
                }
            }
            else
            {
                bool isLeftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                if (!isPinned && !isOverUs && !isLeftDown)
                {
                    if ((DateTime.UtcNow - lastPointerInside).TotalMilliseconds > 650)
                    {
                        CollapseShelf();
                    }
                }
            }
        }

        private void OnShelfDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                e.Effect = DragDropEffects.Copy;
                dropHint.BackColor = ThemeHelper.AccentBackground;
                if (lblDropHint != null) lblDropHint.ForeColor = ThemeHelper.Accent;

                if (!isExpanded)
                {
                    ExpandShelf();
                }
            }
        }

        private void OnShelfDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                e.Effect = DragDropEffects.Copy;
                if (!isExpanded)
                {
                    ExpandShelf();
                }
            }
        }

        private void OnShelfDragDrop(object sender, DragEventArgs e)
        {
            dropHint.BackColor = ThemeHelper.CardBackground;
            if (lblDropHint != null) lblDropHint.ForeColor = ThemeHelper.TextSecondary;

            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        ClipboardHistoryManager.Instance.AddCustomSuperHubItem(files, null);
                    }
                }
                else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
                {
                    string txt = (string)e.Data.GetData(DataFormats.UnicodeText);
                    if (!string.IsNullOrEmpty(txt))
                    {
                        ClipboardHistoryManager.Instance.AddCustomSuperHubItem(null, txt);
                    }
                }

                bool showTabs = string.Equals(Config.SuperHubViewMode, "Tabs", StringComparison.OrdinalIgnoreCase);
                if (showTabs && currentMainTab != 1)
                {
                    SwitchMainTab(1);
                }
                else
                {
                    RefreshItems();
                }
            }
            catch (Exception ex)
            {
                Logger.Log("OnShelfDragDrop error: " + ex.Message);
            }
        }

        private static void SafeDisposeControls(Control parent)
        {
            if (parent == null) return;
            for (int i = parent.Controls.Count - 1; i >= 0; i--)
            {
                try
                {
                    Control c = parent.Controls[i];
                    parent.Controls.RemoveAt(i);
                    var pic = c as PictureBox;
                    if (pic != null && pic.Image != null)
                    {
                        try { pic.Image.Dispose(); pic.Image = null; } catch {}
                    }
                    c.Dispose();
                }
                catch {}
            }
        }

        public void RefreshItems()
        {
            if (!isExpanded) return;

            bool showTabs = string.Equals(Config.SuperHubViewMode, "Tabs", StringComparison.OrdinalIgnoreCase);
            bool isHubTab = (!showTabs || currentMainTab == 1);

            List<ClipboardItem> items;
            if (isHubTab)
            {
                items = ClipboardHistoryManager.Instance.GetItems(null, true);
                lblTitle.Text = string.Format("SuperHub [{0}]", items.Count);
                btnDragAll.Visible = true;
                btnDragMode.Visible = true;
                btnClear.Visible = true;
                toolTip.SetToolTip(btnClear, "Очистить полку SuperHub (удалить все элементы)");
            }
            else
            {
                ClipboardItemType? filter = null;
                if (currentFilterIndex == 1) filter = ClipboardItemType.Image;
                else if (currentFilterIndex == 2) filter = ClipboardItemType.Text;
                else if (currentFilterIndex == 3) filter = ClipboardItemType.Files;

                items = ClipboardHistoryManager.Instance.GetItems(filter, false);
                string cat = filterNames[currentFilterIndex];
                lblTitle.Text = string.Format("{0} [{1}]", cat, items.Count);
                btnDragAll.Visible = false;
                btnDragMode.Visible = false;
                btnClear.Visible = true;
                toolTip.SetToolTip(btnClear, "Очистить журнал буфера обмена");
            }
            UpdateTabsVisual();

            LayoutHeaderControls();
            cardsList.SuspendLayout();
            SafeDisposeControls(cardsList);
            scrollOffset = 0;

            bool isHorizontal = IsHorizontalPosition();
            int curPos = 0;

            if (isHorizontal)
            {
                cardsList.Top = 4;
                cardsList.Left = 8;
                cardsList.Height = pnlContent.Height - 12;

                foreach (var item in items)
                {
                    Panel card = isHubTab 
                        ? CreateCardControlHorizontal(item, curPos, 0, 190, cardsList.Height - 8)
                        : CreateClipboardCardControlHorizontal(item, curPos, 0, 190, cardsList.Height - 8);
                    cardsList.Controls.Add(card);
                    curPos += card.Width + 8;
                }

                cardsList.Width = Math.Max(pnlContent.Width - 16, curPos);
                maxScrollOffset = Math.Max(0, cardsList.Width - pnlContent.Width + 16);
            }
            else
            {
                cardsList.Left = 8;
                cardsList.Top = 0;
                cardsList.Width = pnlContent.Width - 18;

                foreach (var item in items)
                {
                    Panel card = isHubTab
                        ? CreateCardControlVertical(item, 0, curPos, cardsList.Width, 58)
                        : CreateClipboardCardControlVertical(item, 0, curPos, cardsList.Width, 58);
                    cardsList.Controls.Add(card);
                    curPos += card.Height + 6;
                }

                cardsList.Height = Math.Max(pnlContent.Height, curPos);
                maxScrollOffset = Math.Max(0, cardsList.Height - pnlContent.Height);
            }

            cardsList.ResumeLayout(true);
            pnlContent.Invalidate();
        }

        // Карточка SuperHub (вертикальный список)
        private Panel CreateCardControlVertical(ClipboardItem item, int x, int y, int w, int h)
        {
            Panel card = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = ThemeHelper.CardBackground,
                Cursor = Cursors.Hand,
                AllowDrop = true
            };

            card.Paint += (s, e) => {
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                    using (var path = CreateRoundedPath(rect, 4))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };

            string title = "";
            string sub = "";
            Image iconImg = null;
            string targetPath = null;

            if (item.Type == ClipboardItemType.Image)
            {
                title = SafeGetFileName(item.ImagePath ?? "Снимок");
                sub = "Изображение";
                targetPath = item.ImagePath;
                if (SafeFileExists(item.ImagePath)) iconImg = FileIconHelper.GetIconForPath(item.ImagePath);
            }
            else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
            {
                targetPath = item.FilePaths[0];
                title = SafeGetFileName(targetPath);
                sub = (item.FilePaths.Count > 1) ? ("Файлов: " + item.FilePaths.Count) : "Файл";
                if (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)) iconImg = FileIconHelper.GetIconForPath(targetPath);
            }
            else
            {
                title = (item.TextContent ?? "").Replace("\r", "").Replace("\n", " ");
                sub = "Текст";
            }

            // Иконка 32x32
            PictureBox pic = new PictureBox
            {
                Location = new Point(8, 13),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            if (iconImg != null) pic.Image = iconImg;
            else pic.Image = SystemIcons.Application.ToBitmap();
            card.Controls.Add(pic);

            // Сетка кнопок действий 2x2 справа (24x24)
            // Верх: [👁] и [✕]
            Button btnView = CreateToolButton("👁", card.Width - 54, 4, 24, 24, "Просмотр содержимого файла или текста");
            btnView.Click += (s, e) => ItemViewerForm.ShowViewer(item, targetPath);
            card.Controls.Add(btnView);

            Button btnRemove = CreateToolButton("✕", card.Width - 26, 4, 24, 24, "Убрать файл с полки SuperHub");
            btnRemove.MouseEnter += (s, e) => { btnRemove.BackColor = Color.FromArgb(196, 43, 28); btnRemove.ForeColor = Color.White; };
            btnRemove.MouseLeave += (s, e) => { btnRemove.BackColor = Color.Transparent; btnRemove.ForeColor = ThemeHelper.TextSecondary; };
            btnRemove.Click += (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnRemove);

            // Низ: [📥] и [▷] или [···]
            Button btnPaste = CreateToolButton("📥", card.Width - 54, 30, 24, 24, "Вставить в активное окно (Ctrl+V)");
            btnPaste.Click += (s, e) => PasteItem(item);
            card.Controls.Add(btnPaste);

            Button btnAction;
            if (!string.IsNullOrEmpty(targetPath) && (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)))
            {
                btnAction = CreateToolButton("▷", card.Width - 26, 30, 24, 24, "Открыть в ассоциированной программе");
                btnAction.Click += (s, e) => LaunchFile(targetPath);
            }
            else
            {
                btnAction = CreateToolButton("···", card.Width - 26, 30, 24, 24, "Меню действий");
                btnAction.Click += (s, e) => {
                    if (card.ContextMenu != null) card.ContextMenu.Show(btnAction, new Point(0, btnAction.Height));
                };
            }
            card.Controls.Add(btnAction);

            // Заголовок
            Label lblName = new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                ForeColor = ThemeHelper.TextPrimary,
                Location = new Point(46, 9),
                Size = new Size(card.Width - 110, 18),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblName);

            // Подзаголовок
            Label lblSub = new Label
            {
                Text = sub,
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = ThemeHelper.TextSecondary,
                Location = new Point(46, 31),
                Size = new Size(card.Width - 110, 16),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblSub);

            // Контекстное меню
            ContextMenu cardMenu = new ContextMenu();
            cardMenu.MenuItems.Add(new MenuItem("Просмотр / Детали", (s, e) => ItemViewerForm.ShowViewer(item, targetPath)));
            if (!string.IsNullOrEmpty(targetPath) && (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)))
            {
                cardMenu.MenuItems.Add(new MenuItem("Открыть в системе", (s, e) => LaunchFile(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("Показать в Проводнике", (s, e) => ShowInExplorer(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("-"));
            }
            cardMenu.MenuItems.Add(new MenuItem("Вставить в активное окно (Ctrl+V)", (s, e) => PasteItem(item)));
            cardMenu.MenuItems.Add(new MenuItem("Скопировать в буфер", (s, e) => CopyItem(item)));
            cardMenu.MenuItems.Add(new MenuItem("Удалить с полки", (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            }));
            card.ContextMenu = cardMenu;

            Point dragStartPt = Point.Empty;
            bool isPotentialDrag = false;

            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                if (c is Button) return;

                c.MouseEnter += (s, e) => card.BackColor = ThemeHelper.CardHover;
                c.MouseLeave += (s, e) => card.BackColor = ThemeHelper.CardBackground;

                c.Click += (s, e) => {
                    CopyItem(item);
                };

                c.DoubleClick += (s, e) => {
                    if (item.Type == ClipboardItemType.Files && !string.IsNullOrEmpty(targetPath))
                    {
                        LaunchFile(targetPath);
                    }
                    else
                    {
                        ItemViewerForm.ShowViewer(item, targetPath);
                    }
                };

                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left && e.Clicks == 1)
                    {
                        dragStartPt = e.Location;
                        isPotentialDrag = true;
                    }
                };

                c.MouseMove += (s, e) => {
                    if (isPotentialDrag && e.Button == MouseButtons.Left)
                    {
                        int dx = Math.Abs(e.X - dragStartPt.X);
                        int dy = Math.Abs(e.Y - dragStartPt.Y);
                        if (dx >= SystemInformation.DragSize.Width || dy >= SystemInformation.DragSize.Height)
                        {
                            isPotentialDrag = false;
                            StartDragOut(item, card);
                        }
                    }
                };

                c.MouseUp += (s, e) => {
                    isPotentialDrag = false;
                };

                foreach (Control subCtrl in c.Controls) attachEvents(subCtrl);
            };
            attachEvents(card);

            string cardTip = "Клик: скопировать в буфер\nДвойной клик: открыть\nЗажать и потянуть: перетащить наружу (" + (Config.SuperHubDragMode == "Move" ? "Перемещение" : "Копирование") + ")";
            toolTip.SetToolTip(lblName, cardTip);
            toolTip.SetToolTip(lblSub, cardTip);
            toolTip.SetToolTip(pic, cardTip);
            return card;
        }

        // Карточка SuperHub (горизонтальный ряд)
        private Panel CreateCardControlHorizontal(ClipboardItem item, int x, int y, int w, int h)
        {
            Panel card = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = ThemeHelper.CardBackground,
                Cursor = Cursors.Hand,
                AllowDrop = true
            };

            card.Paint += (s, e) => {
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                    using (var path = CreateRoundedPath(rect, 4))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };

            string title = "";
            string sub = "";
            Image iconImg = null;
            string targetPath = null;

            if (item.Type == ClipboardItemType.Image)
            {
                title = SafeGetFileName(item.ImagePath ?? "Снимок");
                sub = "Изображение";
                targetPath = item.ImagePath;
                if (SafeFileExists(item.ImagePath)) iconImg = FileIconHelper.GetIconForPath(item.ImagePath);
            }
            else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
            {
                targetPath = item.FilePaths[0];
                title = SafeGetFileName(targetPath);
                sub = (item.FilePaths.Count > 1) ? ("Файлов: " + item.FilePaths.Count) : "Файл";
                if (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)) iconImg = FileIconHelper.GetIconForPath(targetPath);
            }
            else
            {
                title = (item.TextContent ?? "").Replace("\r", "").Replace("\n", " ");
                sub = "Текст";
            }

            // Иконка по центру вверху
            PictureBox pic = new PictureBox
            {
                Location = new Point((card.Width - 36) / 2, 8),
                Size = new Size(36, 36),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            if (iconImg != null) pic.Image = iconImg;
            else pic.Image = SystemIcons.Application.ToBitmap();
            card.Controls.Add(pic);

            // Кнопка удаления [✕] (24x24) в правом верхнем углу
            Button btnRemove = CreateToolButton("✕", card.Width - 28, 4, 24, 24, "Убрать файл с полки");
            btnRemove.MouseEnter += (s, e) => { btnRemove.BackColor = Color.FromArgb(196, 43, 28); btnRemove.ForeColor = Color.White; };
            btnRemove.MouseLeave += (s, e) => { btnRemove.BackColor = Color.Transparent; btnRemove.ForeColor = ThemeHelper.TextSecondary; };
            btnRemove.Click += (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnRemove);

            // Кнопка предпросмотра [👁] (24x24) в левом верхнем углу
            Button btnView = CreateToolButton("👁", 4, 4, 24, 24, "Просмотр содержимого");
            btnView.Click += (s, e) => ItemViewerForm.ShowViewer(item, targetPath);
            card.Controls.Add(btnView);

            // Заголовок
            Label lblName = new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold),
                ForeColor = ThemeHelper.TextPrimary,
                Location = new Point(6, 48),
                Size = new Size(card.Width - 12, 18),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblName);

            // Подзаголовок
            Label lblSub = new Label
            {
                Text = sub,
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = ThemeHelper.TextSecondary,
                Location = new Point(6, 68),
                Size = new Size(card.Width - 12, 16),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblSub);

            // Контекстное меню
            ContextMenu cardMenu = new ContextMenu();
            cardMenu.MenuItems.Add(new MenuItem("👁 Просмотр / Детали", (s, e) => ItemViewerForm.ShowViewer(item, targetPath)));
            if (!string.IsNullOrEmpty(targetPath) && (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)))
            {
                cardMenu.MenuItems.Add(new MenuItem("Открыть в системе", (s, e) => LaunchFile(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("Показать в Проводнике", (s, e) => ShowInExplorer(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("-"));
            }
            cardMenu.MenuItems.Add(new MenuItem("Скопировать в буфер", (s, e) => CopyItem(item)));
            cardMenu.MenuItems.Add(new MenuItem("Удалить с полки", (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            }));
            card.ContextMenu = cardMenu;

            Point dragStartPt = Point.Empty;
            bool isPotentialDrag = false;

            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                if (c is Button) return;

                c.MouseEnter += (s, e) => card.BackColor = ThemeHelper.CardHover;
                c.MouseLeave += (s, e) => card.BackColor = ThemeHelper.CardBackground;

                c.Click += (s, e) => {
                    CopyItem(item);
                };

                c.DoubleClick += (s, e) => {
                    if (item.Type == ClipboardItemType.Files && !string.IsNullOrEmpty(targetPath))
                    {
                        LaunchFile(targetPath);
                    }
                    else
                    {
                        ItemViewerForm.ShowViewer(item, targetPath);
                    }
                };

                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left && e.Clicks == 1)
                    {
                        dragStartPt = e.Location;
                        isPotentialDrag = true;
                    }
                };

                c.MouseMove += (s, e) => {
                    if (isPotentialDrag && e.Button == MouseButtons.Left)
                    {
                        int dx = Math.Abs(e.X - dragStartPt.X);
                        int dy = Math.Abs(e.Y - dragStartPt.Y);
                        if (dx >= SystemInformation.DragSize.Width || dy >= SystemInformation.DragSize.Height)
                        {
                            isPotentialDrag = false;
                            StartDragOut(item, card);
                        }
                    }
                };

                c.MouseUp += (s, e) => {
                    isPotentialDrag = false;
                };

                foreach (Control subCtrl in c.Controls) attachEvents(subCtrl);
            };
            attachEvents(card);

            string cardTip = "Клик: скопировать в буфер\nДвойной клик: открыть\nЗажать и потянуть: перетащить наружу (" + (Config.SuperHubDragMode == "Move" ? "Перемещение" : "Копирование") + ")";
            toolTip.SetToolTip(lblName, cardTip);
            toolTip.SetToolTip(lblSub, cardTip);
            return card;
        }

        // Карточка буфера обмена (вертикальный список)
        private Panel CreateClipboardCardControlVertical(ClipboardItem item, int x, int y, int w, int h)
        {
            Panel card = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = ThemeHelper.CardBackground,
                Cursor = Cursors.Hand,
                AllowDrop = true
            };

            card.Paint += (s, e) => {
                using (var pen = new Pen(item.IsPinned ? ThemeHelper.Accent : ThemeHelper.CardBorder, item.IsPinned ? 1.5f : 1f))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                    using (var path = CreateRoundedPath(rect, 4))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };

            string title = "";
            string sub = "";
            Image iconImg = null;
            string targetPath = null;

            if (item.Type == ClipboardItemType.Image)
            {
                title = SafeGetFileName(item.ImagePath ?? "Снимок");
                sub = string.IsNullOrEmpty(item.SourceApp) ? "Изображение" : item.SourceApp;
                targetPath = item.ImagePath;
                if (SafeFileExists(item.ImagePath)) iconImg = FileIconHelper.GetIconForPath(item.ImagePath);
            }
            else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
            {
                targetPath = item.FilePaths[0];
                title = SafeGetFileName(targetPath);
                sub = (item.FilePaths.Count > 1) ? ("Файлов: " + item.FilePaths.Count) : "Файл";
                if (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)) iconImg = FileIconHelper.GetIconForPath(targetPath);
            }
            else
            {
                title = (item.TextContent ?? "").Replace("\r", "").Replace("\n", " ");
                sub = string.IsNullOrEmpty(item.SourceApp) ? "Текст" : item.SourceApp;
            }

            // Иконка
            PictureBox pic = new PictureBox
            {
                Location = new Point(8, 13),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            if (iconImg != null) pic.Image = iconImg;
            else pic.Image = SystemIcons.Application.ToBitmap();
            card.Controls.Add(pic);

            // Сетка кнопок действий 2x2 справа (24x24)
            // Верх: [📥] и [✕]
            Button btnPaste = CreateToolButton("📥", card.Width - 58, 6, 24, 24, "Вставить в активное окно (Ctrl+V)");
            btnPaste.Click += (s, e) => PasteItem(item);
            card.Controls.Add(btnPaste);

            Button btnDelete = CreateToolButton("✕", card.Width - 30, 6, 24, 24, "Удалить элемент из истории буфера");
            btnDelete.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
            btnDelete.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 30, 20);
            btnDelete.MouseEnter += (s, e) => { btnDelete.ForeColor = Color.White; };
            btnDelete.MouseLeave += (s, e) => { btnDelete.ForeColor = ThemeHelper.TextSecondary; };
            btnDelete.Click += (s, e) => {
                ClipboardHistoryManager.Instance.DeleteItem(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnDelete);

            // Низ: [👁] и [📌]
            Button btnView = CreateToolButton("👁", card.Width - 58, 34, 24, 24, "Просмотр содержимого");
            btnView.Click += (s, e) => ItemViewerForm.ShowViewer(item, targetPath);
            card.Controls.Add(btnView);

            Button btnPinItem = CreateToolButton("📌", card.Width - 30, 34, 24, 24, item.IsPinned ? "Открепить" : "Закрепить вверху");
            btnPinItem.ForeColor = item.IsPinned ? ThemeHelper.Accent : ThemeHelper.TextSecondary;
            btnPinItem.Click += (s, e) => {
                ClipboardHistoryManager.Instance.TogglePin(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnPinItem);

            // Заголовок
            Label lblName = new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                ForeColor = ThemeHelper.TextPrimary,
                Location = new Point(46, 9),
                Size = new Size(card.Width - 110, 18),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblName);

            // Подзаголовок
            Label lblSub = new Label
            {
                Text = sub,
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = ThemeHelper.TextSecondary,
                Location = new Point(46, 31),
                Size = new Size(card.Width - 110, 16),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblSub);

            // Контекстное меню
            ContextMenu cardMenu = new ContextMenu();
            cardMenu.MenuItems.Add(new MenuItem("Просмотр / Детали", (s, e) => ItemViewerForm.ShowViewer(item, targetPath)));
            if (!string.IsNullOrEmpty(targetPath) && (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)))
            {
                cardMenu.MenuItems.Add(new MenuItem("Открыть в системе", (s, e) => LaunchFile(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("Показать в Проводнике", (s, e) => ShowInExplorer(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("-"));
            }
            cardMenu.MenuItems.Add(new MenuItem("Вставить в каретку (Ctrl+V)", (s, e) => PasteItem(item)));
            cardMenu.MenuItems.Add(new MenuItem("Скопировать в буфер", (s, e) => CopyItem(item)));
            cardMenu.MenuItems.Add(new MenuItem(item.IsPinned ? "Открепить" : "Закрепить вверху", (s, e) => {
                ClipboardHistoryManager.Instance.TogglePin(item.Id);
                RefreshItems();
            }));
            cardMenu.MenuItems.Add(new MenuItem(item.IsInSuperHub ? "Убрать из SuperHub" : "Добавить в SuperHub", (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            }));
            cardMenu.MenuItems.Add(new MenuItem("-"));
            cardMenu.MenuItems.Add(new MenuItem("Удалить из истории", (s, e) => {
                ClipboardHistoryManager.Instance.DeleteItem(item.Id);
                RefreshItems();
            }));
            card.ContextMenu = cardMenu;

            Point dragStartPt = Point.Empty;
            bool isPotentialDrag = false;

            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                if (c is Button) return;

                c.MouseEnter += (s, e) => card.BackColor = ThemeHelper.CardHover;
                c.MouseLeave += (s, e) => card.BackColor = ThemeHelper.CardBackground;

                c.Click += (s, e) => {
                    bool isPasteAction = string.Equals(Config.ClipboardClickAction, "Paste", StringComparison.OrdinalIgnoreCase);
                    if (isPasteAction) PasteItem(item);
                    else CopyItem(item);
                };

                c.DoubleClick += (s, e) => {
                    if (item.Type == ClipboardItemType.Files && !string.IsNullOrEmpty(targetPath))
                    {
                        LaunchFile(targetPath);
                    }
                    else
                    {
                        ItemViewerForm.ShowViewer(item, targetPath);
                    }
                };

                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left && e.Clicks == 1)
                    {
                        dragStartPt = e.Location;
                        isPotentialDrag = true;
                    }
                };

                c.MouseMove += (s, e) => {
                    if (isPotentialDrag && e.Button == MouseButtons.Left)
                    {
                        int dx = Math.Abs(e.X - dragStartPt.X);
                        int dy = Math.Abs(e.Y - dragStartPt.Y);
                        if (dx >= SystemInformation.DragSize.Width || dy >= SystemInformation.DragSize.Height)
                        {
                            isPotentialDrag = false;
                            StartDragOut(item, card);
                        }
                    }
                };

                c.MouseUp += (s, e) => {
                    isPotentialDrag = false;
                };

                foreach (Control subCtrl in c.Controls) attachEvents(subCtrl);
            };
            attachEvents(card);

            string cardTip = "Клик: " + (Config.ClipboardClickAction == "Paste" ? "вставить" : "скопировать") + "\nДвойной клик: открыть\nЗажать и потянуть: перетащить (Drag & Drop)";
            toolTip.SetToolTip(lblName, cardTip);
            toolTip.SetToolTip(lblSub, cardTip);
            toolTip.SetToolTip(pic, cardTip);
            return card;
        }

        // Карточка буфера обмена (горизонтальный ряд)
        private Panel CreateClipboardCardControlHorizontal(ClipboardItem item, int x, int y, int w, int h)
        {
            Panel card = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = ThemeHelper.CardBackground,
                Cursor = Cursors.Hand,
                AllowDrop = true
            };

            card.Paint += (s, e) => {
                using (var pen = new Pen(item.IsPinned ? ThemeHelper.Accent : ThemeHelper.CardBorder, item.IsPinned ? 1.5f : 1f))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                    using (var path = CreateRoundedPath(rect, 4))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };

            string title = "";
            string sub = "";
            Image iconImg = null;
            string targetPath = null;

            if (item.Type == ClipboardItemType.Image)
            {
                title = SafeGetFileName(item.ImagePath ?? "Снимок");
                sub = string.IsNullOrEmpty(item.SourceApp) ? "Изображение" : item.SourceApp;
                targetPath = item.ImagePath;
                if (SafeFileExists(item.ImagePath)) iconImg = FileIconHelper.GetIconForPath(item.ImagePath);
            }
            else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
            {
                targetPath = item.FilePaths[0];
                title = SafeGetFileName(targetPath);
                sub = (item.FilePaths.Count > 1) ? ("Файлов: " + item.FilePaths.Count) : "Файл";
                if (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)) iconImg = FileIconHelper.GetIconForPath(targetPath);
            }
            else
            {
                title = (item.TextContent ?? "").Replace("\r", "").Replace("\n", " ");
                sub = string.IsNullOrEmpty(item.SourceApp) ? "Текст" : item.SourceApp;
            }

            // Иконка по центру вверху
            PictureBox pic = new PictureBox
            {
                Location = new Point((card.Width - 36) / 2, 8),
                Size = new Size(36, 36),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            if (iconImg != null) pic.Image = iconImg;
            else pic.Image = SystemIcons.Application.ToBitmap();
            card.Controls.Add(pic);

            // Кнопка удаления [✕] (24x24) в правом верхнем углу
            Button btnDelete = CreateToolButton("✕", card.Width - 30, 4, 24, 24, "Удалить запись из истории буфера");
            btnDelete.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
            btnDelete.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 30, 20);
            btnDelete.MouseEnter += (s, e) => { btnDelete.ForeColor = Color.White; };
            btnDelete.MouseLeave += (s, e) => { btnDelete.ForeColor = ThemeHelper.TextSecondary; };
            btnDelete.Click += (s, e) => {
                ClipboardHistoryManager.Instance.DeleteItem(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnDelete);

            // Кнопка закрепления [📌] (24x24) слева от кнопки удаления
            Button btnPinItem = CreateToolButton("📌", card.Width - 58, 4, 24, 24, item.IsPinned ? "Открепить" : "Закрепить");
            btnPinItem.ForeColor = item.IsPinned ? ThemeHelper.Accent : ThemeHelper.TextSecondary;
            btnPinItem.Click += (s, e) => {
                ClipboardHistoryManager.Instance.TogglePin(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnPinItem);

            // Кнопка предпросмотра [👁] (24x24) в левом верхнем углу
            Button btnView = CreateToolButton("👁", 4, 4, 24, 24, "Просмотр содержимого");
            btnView.Click += (s, e) => ItemViewerForm.ShowViewer(item, targetPath);
            card.Controls.Add(btnView);

            // Заголовок
            Label lblName = new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold),
                ForeColor = ThemeHelper.TextPrimary,
                Location = new Point(6, 48),
                Size = new Size(card.Width - 12, 18),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblName);

            // Подзаголовок
            Label lblSub = new Label
            {
                Text = sub,
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = ThemeHelper.TextSecondary,
                Location = new Point(6, 68),
                Size = new Size(card.Width - 12, 16),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblSub);

            Point dragStartPt = Point.Empty;
            bool isPotentialDrag = false;

            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                if (c is Button) return;

                c.MouseEnter += (s, e) => card.BackColor = ThemeHelper.CardHover;
                c.MouseLeave += (s, e) => card.BackColor = ThemeHelper.CardBackground;

                c.Click += (s, e) => {
                    bool isPasteAction = string.Equals(Config.ClipboardClickAction, "Paste", StringComparison.OrdinalIgnoreCase);
                    if (isPasteAction) PasteItem(item);
                    else CopyItem(item);
                };

                c.DoubleClick += (s, e) => {
                    if (item.Type == ClipboardItemType.Files && !string.IsNullOrEmpty(targetPath))
                    {
                        LaunchFile(targetPath);
                    }
                    else
                    {
                        ItemViewerForm.ShowViewer(item, targetPath);
                    }
                };

                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left && e.Clicks == 1)
                    {
                        dragStartPt = e.Location;
                        isPotentialDrag = true;
                    }
                };

                c.MouseMove += (s, e) => {
                    if (isPotentialDrag && e.Button == MouseButtons.Left)
                    {
                        int dx = Math.Abs(e.X - dragStartPt.X);
                        int dy = Math.Abs(e.Y - dragStartPt.Y);
                        if (dx >= SystemInformation.DragSize.Width || dy >= SystemInformation.DragSize.Height)
                        {
                            isPotentialDrag = false;
                            StartDragOut(item, card);
                        }
                    }
                };

                c.MouseUp += (s, e) => {
                    isPotentialDrag = false;
                };

                foreach (Control subCtrl in c.Controls) attachEvents(subCtrl);
            };
            attachEvents(card);

            string cardTip = "Клик: " + (Config.ClipboardClickAction == "Paste" ? "вставить" : "скопировать") + "\nДвойной клик: открыть\nЗажать и потянуть: перетащить (Drag & Drop)";
            toolTip.SetToolTip(lblName, cardTip);
            toolTip.SetToolTip(lblSub, cardTip);
            return card;
        }

        private void LaunchFile(string path)
        {
            try
            {
                if (SafeFileExists(path) || SafeDirectoryExists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось запустить файл:\n" + ex.Message, "SuperHub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowInExplorer(string path)
        {
            try
            {
                if (SafeFileExists(path) || SafeDirectoryExists(path))
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
            }
            catch {}
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
                else if (item.Type == ClipboardItemType.Files && item.FilePaths != null)
                {
                    var sc = new StringCollection();
                    foreach (var f in item.FilePaths) if (SafeFileExists(f) || SafeDirectoryExists(f)) sc.Add(f);
                    if (sc.Count > 0) Clipboard.SetFileDropList(sc);
                }
            }
            catch {}
        }

        private void PasteItem(ClipboardItem item)
        {
            try
            {
                CopyItem(item);
                if (!isPinned)
                {
                    CollapseShelf();
                }

                System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                    try
                    {
                        System.Threading.Thread.Sleep(isPinned ? 80 : 150);

                        keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Alt
                        keybd_event(0x10, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Shift
                        keybd_event(0x5B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Win

                        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(20);
                        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(30);
                        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(20);
                        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("SuperHub PasteItem simulated input error: " + ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Log("SuperHub PasteItem error: " + ex.Message);
            }
        }

        private void StartDragOut(ClipboardItem item, Control src)
        {
            try
            {
                DataObject data = new DataObject();
                bool isMove = string.Equals(Config.SuperHubDragMode, "Move", StringComparison.OrdinalIgnoreCase);

                byte dropEffectVal = (byte)(isMove ? 2 : 1);
                var dropEffectStream = new MemoryStream(new byte[] { dropEffectVal, 0, 0, 0 });
                data.SetData("Preferred DropEffect", dropEffectStream);

                if (item.Type == ClipboardItemType.Files && item.FilePaths != null)
                {
                    var sc = new StringCollection();
                    foreach (var f in item.FilePaths) if (SafeFileExists(f) || SafeDirectoryExists(f)) sc.Add(f);
                    if (sc.Count > 0) data.SetFileDropList(sc);
                }
                else if (item.Type == ClipboardItemType.Image && SafeFileExists(item.ImagePath))
                {
                    data.SetFileDropList(new StringCollection { item.ImagePath });
                }
                else if (item.Type == ClipboardItemType.Text)
                {
                    data.SetText(item.TextContent ?? "");
                }

                DragDropEffects allowedEffects = isMove ? (DragDropEffects.Move | DragDropEffects.Copy) : DragDropEffects.Copy;
                src.DoDragDrop(data, allowedEffects);
            }
            catch (Exception ex)
            {
                Logger.Log("StartDragOut error: " + ex.Message);
            }
        }

        private void DragAllFilesOut()
        {
            try
            {
                var list = ClipboardHistoryManager.Instance.GetItems(null, true);
                var sc = new StringCollection();
                foreach (var it in list)
                {
                    if (it.Type == ClipboardItemType.Files && it.FilePaths != null)
                    {
                        foreach (var f in it.FilePaths) if (SafeFileExists(f) || SafeDirectoryExists(f)) sc.Add(f);
                    }
                    else if (it.Type == ClipboardItemType.Image && SafeFileExists(it.ImagePath))
                    {
                        sc.Add(it.ImagePath);
                    }
                }

                if (sc.Count > 0)
                {
                    DataObject data = new DataObject();
                    bool isMove = string.Equals(Config.SuperHubDragMode, "Move", StringComparison.OrdinalIgnoreCase);
                    byte dropEffectVal = (byte)(isMove ? 2 : 1);
                    var dropEffectStream = new MemoryStream(new byte[] { dropEffectVal, 0, 0, 0 });
                    data.SetData("Preferred DropEffect", dropEffectStream);
                    data.SetFileDropList(sc);

                    DragDropEffects allowedEffects = isMove ? (DragDropEffects.Move | DragDropEffects.Copy) : DragDropEffects.Copy;
                    this.DoDragDrop(data, allowedEffects);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("DragAllFilesOut error: " + ex.Message);
            }
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

        private static bool SafeFileExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path); }
            catch { return false; }
        }

        private static bool SafeDirectoryExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && Directory.Exists(path); }
            catch { return false; }
        }

        private static string SafeGetFileName(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return "";
                return Path.GetFileName(path);
            }
            catch
            {
                if (string.IsNullOrEmpty(path)) return "";
                int idx = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
                if (idx >= 0 && idx < path.Length - 1) return path.Substring(idx + 1);
                return path;
            }
        }
    }
}
