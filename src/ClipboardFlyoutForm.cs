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

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        const byte VK_CONTROL = 0x11;
        const byte VK_V = 0x56;
        const uint KEYEVENTF_KEYUP = 0x0002;

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public Point ptReserved;
            public Point ptMaxSize;
            public Point ptMaxPosition;
            public Point ptMinTrackSize;
            public Point ptMaxTrackSize;
        }

        private class FlyoutViewportPanel : Panel
        {
            private const int WM_NCHITTEST = 0x0084;
            private const int HTTRANSPARENT = -1;

            public FlyoutViewportPanel()
            {
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_NCHITTEST)
                {
                    Point pt = this.PointToClient(new Point(m.LParam.ToInt32()));
                    int border = 8;
                    if (pt.X >= this.Width - border || pt.Y >= this.Height - border || pt.X <= border)
                    {
                        m.Result = (IntPtr)HTTRANSPARENT;
                        return;
                    }
                }
                base.WndProc(ref m);
            }
        }

        private class FlyoutTopBarPanel : Panel
        {
            private const int WM_NCHITTEST = 0x0084;
            private const int HTTRANSPARENT = -1;

            public FlyoutTopBarPanel()
            {
                this.DoubleBuffered = true;
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_NCHITTEST)
                {
                    Point pt = this.PointToClient(new Point(m.LParam.ToInt32()));
                    int border = 8;
                    if (pt.Y <= border || pt.X <= border || pt.X >= this.Width - border)
                    {
                        m.Result = (IntPtr)HTTRANSPARENT;
                        return;
                    }
                }
                base.WndProc(ref m);
            }
        }

        private static bool IsOurAppWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            return pid == (uint)Process.GetCurrentProcess().Id;
        }

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
        private FlyoutTopBarPanel pnlTopBar;
        private Label lblAppHeader;
        private Button btnOpenSettings;
        private Panel pnlMainTabs;
        private Button btnMainClipboard;
        private Button btnMainSuperHub;
        private int currentMainTab = 0; // 0 = Буфер обмена, 1 = SuperHub

        private Panel pnlSubBar;
        private int currentFilterIndex = 0; // 0 = Все, 1 = Снимки, 2 = Текст, 3 = Файлы
        private readonly string[] filterNames = new string[] { "Все", "Снимки", "Текст", "Файлы" };
        private readonly List<Button> filterButtons = new List<Button>();

        private Panel pnlDropHint;
        private Label lblDropHint;

        private Panel pnlSearch;
        private Panel searchBoxBg;
        private TextBox txtSearch;
        private FlyoutViewportPanel pnlViewport;
        private Panel cardsContainer;
        private Button btnPinWindow;
        private Button btnClose;
        private Button btnClearAll;
        private Button btnDragMode;
        private ToolTip toolTip;
        private bool isWindowPinned = false;

        // Кастомный Fluent скроллбар
        private int scrollY = 0;
        private int maxScrollY = 0;
        private bool isDraggingScroll = false;
        private int dragStartMouseY = 0;
        private int dragStartScrollY = 0;
        private bool isHoveringScroll = false;

        // Анимация плавного появления окна (Fade-in + Slide)
        private System.Windows.Forms.Timer openAnimTimer;
        private int openAnimStep = 0;
        private const int OpenAnimTotalSteps = 8;
        private Point openTargetLocation;
        private int openStartY;

        public ClipboardFlyoutForm(IntPtr prevFg)
        {
            this.previousForegroundWindow = prevFg;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(Config.ClipboardFlyoutWidth, Config.ClipboardFlyoutHeight);
            this.MinimumSize = new Size(360, 380);
            this.DoubleBuffered = true;
            this.TopMost = true;
            this.KeyPreview = true;
            this.AllowDrop = true;

            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 6000;
            toolTip.InitialDelay = 200;
            toolTip.ReshowDelay = 100;
            toolTip.ShowAlways = true;

            this.Shown += (s, e) => {
                ApplyWindowStyles();
            };

            ThemeHelper.ThemeChanged += () => {
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke((Action)(() => {
                        ApplyTheme();
                        RefreshItems();
                    }));
                }
            };

            ClipboardHistoryManager.Instance.HistoryChanged += () => {
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke((Action)RefreshItems);
                }
            };

            BuildUI();
            ApplyTheme();

            this.Deactivate += (s, e) => {
                if (!isWindowPinned) CloseFlyout();
            };

            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape) CloseFlyout();
            };

            // Drag and drop для всего окна
            this.DragEnter += OnFormDragEnter;
            this.DragDrop += OnFormDragDrop;

            RefreshItems();
        }

        private void ApplyWindowStyles()
        {
            try
            {
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                int dark = ThemeHelper.IsDarkTheme() ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch {}
        }

        private void ApplyTheme()
        {
            ApplyWindowStyles();
            this.BackColor = ThemeHelper.Background;
            this.ForeColor = ThemeHelper.TextPrimary;

            if (pnlTopBar != null) pnlTopBar.BackColor = ThemeHelper.HeaderBackground;
            if (lblAppHeader != null) lblAppHeader.ForeColor = ThemeHelper.TextSecondary;
            if (btnClose != null) btnClose.ForeColor = ThemeHelper.TextSecondary;
            if (btnPinWindow != null && !isWindowPinned) btnPinWindow.ForeColor = ThemeHelper.TextSecondary;

            if (pnlMainTabs != null) pnlMainTabs.BackColor = ThemeHelper.HeaderBackground;
            if (pnlSubBar != null) pnlSubBar.BackColor = ThemeHelper.Background;

            if (btnClearAll != null)
            {
                btnClearAll.BackColor = ThemeHelper.CardBackground;
                btnClearAll.ForeColor = ThemeHelper.TextPrimary;
            }

            if (pnlSearch != null) pnlSearch.BackColor = ThemeHelper.Background;
            if (searchBoxBg != null) searchBoxBg.BackColor = ThemeHelper.CardBackground;
            if (txtSearch != null)
            {
                txtSearch.BackColor = ThemeHelper.CardBackground;
                txtSearch.ForeColor = ThemeHelper.TextPrimary;
            }

            if (pnlDropHint != null) pnlDropHint.BackColor = ThemeHelper.CardBackground;
            if (pnlViewport != null) pnlViewport.BackColor = ThemeHelper.Background;

            UpdateTabsVisual();
            this.Invalidate();
        }

        public void CloseFlyout()
        {
            try
            {
                if (openAnimTimer != null)
                {
                    openAnimTimer.Stop();
                    openAnimTimer.Dispose();
                    openAnimTimer = null;
                }
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

        public void AnimateIn(Point targetPoint, bool isFromBottom)
        {
            openTargetLocation = targetPoint;
            int offset = isFromBottom ? 12 : -12;
            openStartY = targetPoint.Y + offset;

            this.Opacity = 0.05;
            this.Location = new Point(targetPoint.X, openStartY);
            this.Show();
            this.BringToFront();
            SetForegroundWindow(this.Handle);
            this.Activate();

            if (openAnimTimer != null)
            {
                openAnimTimer.Stop();
                openAnimTimer.Dispose();
            }

            openAnimStep = 0;
            openAnimTimer = new System.Windows.Forms.Timer();
            openAnimTimer.Interval = 12;
            openAnimTimer.Tick += (s, e) => {
                if (this.IsDisposed || !this.IsHandleCreated)
                {
                    if (openAnimTimer != null)
                    {
                        openAnimTimer.Stop();
                        openAnimTimer.Dispose();
                        openAnimTimer = null;
                    }
                    return;
                }

                openAnimStep++;
                float t = (float)openAnimStep / OpenAnimTotalSteps;
                if (t > 1.0f) t = 1.0f;
                float ease = (float)(1.0 - Math.Pow(1.0 - t, 3));

                int curY = (int)(openStartY + (openTargetLocation.Y - openStartY) * ease);
                this.Location = new Point(openTargetLocation.X, curY);
                this.Opacity = Math.Min(1.0, 0.15 + 0.85 * ease);

                if (openAnimStep >= OpenAnimTotalSteps)
                {
                    openAnimTimer.Stop();
                    openAnimTimer.Dispose();
                    openAnimTimer = null;
                    this.Location = openTargetLocation;
                    this.Opacity = 1.0;
                }
            };
            openAnimTimer.Start();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                Point screenPt = new Point(m.LParam.ToInt32());
                Point clientPt = this.PointToClient(screenPt);
                int border = 8;

                bool onLeft = clientPt.X <= border;
                bool onRight = clientPt.X >= this.ClientSize.Width - border;
                bool onTop = clientPt.Y <= border;
                bool onBottom = clientPt.Y >= this.ClientSize.Height - border;

                if (onTop && onLeft) { m.Result = (IntPtr)HTTOPLEFT; return; }
                if (onTop && onRight) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                if (onBottom && onLeft) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                if (onBottom && onRight) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                if (onLeft) { m.Result = (IntPtr)HTLEFT; return; }
                if (onRight) { m.Result = (IntPtr)HTRIGHT; return; }
                if (onTop) { m.Result = (IntPtr)HTTOP; return; }
                if (onBottom) { m.Result = (IntPtr)HTBOTTOM; return; }
                return;
            }
            if (m.Msg == WM_GETMINMAXINFO)
            {
                MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                mmi.ptMinTrackSize = new Point(320, 380);
                mmi.ptMaxTrackSize = new Point(1200, 1200);
                Marshal.StructureToPtr(mmi, m.LParam, true);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutControls();
            if (this.WindowState == FormWindowState.Normal && this.Width >= 320 && this.Height >= 380)
            {
                Config.ClipboardFlyoutWidth = this.Width;
                Config.ClipboardFlyoutHeight = this.Height;
            }
        }

        private void LayoutControls()
        {
            if (pnlTopBar == null) return;
            this.SuspendLayout();

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            pnlTopBar.Width = w;
            int rightX = w - 10;
            if (btnClose != null)
            {
                btnClose.Location = new Point(rightX - 26, 3);
                rightX -= (26 + 6);
            }
            if (btnPinWindow != null)
            {
                btnPinWindow.Location = new Point(rightX - 26, 3);
                rightX -= (26 + 6);
            }
            if (btnOpenSettings != null)
            {
                btnOpenSettings.Location = new Point(rightX - 26, 3);
            }

            if (pnlMainTabs != null)
            {
                pnlMainTabs.Location = new Point(0, 30);
                pnlMainTabs.Width = w;
                int tabMargin = 12;
                int tabGap = 8;
                int tabW = Math.Max(100, (w - tabMargin * 2 - tabGap) / 2);
                if (btnMainClipboard != null)
                {
                    btnMainClipboard.Location = new Point(tabMargin, 2);
                    btnMainClipboard.Size = new Size(tabW, 28);
                }
                if (btnMainSuperHub != null)
                {
                    btnMainSuperHub.Location = new Point(tabMargin + tabW + tabGap, 2);
                    btnMainSuperHub.Size = new Size(tabW, 28);
                }
            }

            bool isClipboard = (currentMainTab == 0);

            if (pnlSubBar != null)
            {
                pnlSubBar.Location = new Point(0, 64);
                pnlSubBar.Width = w;

                if (isClipboard)
                {
                    // Подвкладки фильтрации
                    int subX = 12;
                    for (int i = 0; i < filterButtons.Count; i++)
                    {
                        filterButtons[i].Visible = true;
                        filterButtons[i].Location = new Point(subX, 3);
                        subX += filterButtons[i].Width + 6;
                    }

                    if (btnDragMode != null) btnDragMode.Visible = false;

                    // Кнопка очистить
                    if (btnClearAll != null)
                    {
                        btnClearAll.Width = 74;
                        btnClearAll.Text = "Очистить";
                        btnClearAll.Location = new Point(w - btnClearAll.Width - 12, 3);
                    }
                }
                else
                {
                    // SuperHub: подвкладок нет
                    for (int i = 0; i < filterButtons.Count; i++)
                    {
                        filterButtons[i].Visible = false;
                    }

                    if (btnDragMode != null)
                    {
                        bool isCopy = string.Equals(Config.SuperHubDragMode, "Copy", StringComparison.OrdinalIgnoreCase);
                        btnDragMode.Visible = true;
                        btnDragMode.Width = 72;
                        btnDragMode.Text = isCopy ? "Копия" : "Перенос";
                        btnDragMode.Location = new Point(12, 3);
                    }

                    if (btnClearAll != null)
                    {
                        btnClearAll.Width = (w < 380) ? 76 : 106;
                        btnClearAll.Text = (w < 380) ? "Очистить" : "Очистить полку";
                        btnClearAll.Location = new Point(w - btnClearAll.Width - 12, 3);
                    }
                }
            }

            int thirdY = 96;
            if (pnlSearch != null)
            {
                pnlSearch.Visible = isClipboard;
                if (pnlSearch.Visible)
                {
                    pnlSearch.Location = new Point(12, thirdY);
                    pnlSearch.Width = Math.Max(100, w - 24);
                    if (searchBoxBg != null)
                    {
                        searchBoxBg.Width = pnlSearch.Width;
                    }
                    if (txtSearch != null && searchBoxBg != null)
                    {
                        txtSearch.Width = Math.Max(100, searchBoxBg.ClientSize.Width - 36);
                    }
                }
            }

            if (pnlDropHint != null)
            {
                pnlDropHint.Visible = !isClipboard;
                if (pnlDropHint.Visible)
                {
                    pnlDropHint.Location = new Point(12, thirdY);
                    pnlDropHint.Size = new Size(Math.Max(100, w - 24), 30);
                }
            }

            int contentTop = 130;
            if (pnlViewport != null)
            {
                pnlViewport.Location = new Point(0, contentTop);
                pnlViewport.Size = new Size(w, Math.Max(50, h - contentTop - 4));
                if (cardsContainer != null)
                {
                    int cardW = Math.Max(100, pnlViewport.ClientSize.Width - 24);
                    cardsContainer.Location = new Point(12, cardsContainer.Top);
                    cardsContainer.Width = cardW;
                    foreach (Control c in cardsContainer.Controls)
                    {
                        if (!(c is Panel)) continue;
                        c.Width = cardW;

                        foreach (Control sub in c.Controls)
                        {
                            if (sub is Button)
                            {
                                string tag = sub.Tag != null ? sub.Tag.ToString() : "";
                                if (tag == "c1_r1") { sub.Left = cardW - 62; sub.Top = 6; }
                                else if (tag == "c2_r1") { sub.Left = cardW - 32; sub.Top = 6; }
                                else if (tag == "c1_r2") { sub.Left = cardW - 62; sub.Top = 36; }
                                else if (tag == "c2_r2") { sub.Left = cardW - 32; sub.Top = 36; }
                                else if (tag == "hub_view") { sub.Left = cardW - 62; sub.Top = (c.Height - sub.Height) / 2; }
                                else if (tag == "hub_remove") { sub.Left = cardW - 32; sub.Top = (c.Height - sub.Height) / 2; }
                            }
                            else if (sub is Label)
                            {
                                sub.Width = Math.Max(30, cardW - sub.Left - 70);
                            }
                        }
                    }
                }
            }

            this.ResumeLayout(true);
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
            pnlTopBar = new FlyoutTopBarPanel
            {
                Location = new Point(0, 0),
                Size = new Size(this.Width, 34),
                BackColor = Color.FromArgb(24, 24, 24),
                Cursor = Cursors.SizeAll
            };
            pnlTopBar.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };

            lblAppHeader = new Label
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
            btnClose = new Button
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
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
            btnClose.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 30, 20);
            btnClose.MouseEnter += (s, e) => { btnClose.ForeColor = Color.White; };
            btnClose.MouseLeave += (s, e) => { btnClose.ForeColor = ThemeHelper.TextSecondary; };
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
            btnPinWindow.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
            btnPinWindow.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
            btnPinWindow.Click += (s, e) => {
                isWindowPinned = !isWindowPinned;
                btnPinWindow.BackColor = isWindowPinned ? ThemeHelper.AccentBackground : Color.Transparent;
                btnPinWindow.ForeColor = isWindowPinned ? ThemeHelper.Accent : ThemeHelper.TextSecondary;
                toolTip.SetToolTip(btnPinWindow, isWindowPinned ? "Окно закреплено (кликните, чтобы открепить)" : "Закрепить окно на экране (перетаскивайте за заголовок)");
            };
            toolTip.SetToolTip(btnPinWindow, "Закрепить окно на экране (не закрывать при клике в другое окно)");
            pnlTopBar.Controls.Add(btnPinWindow);

            // Кнопка быстрого доступа к настройкам [⚙️]
            btnOpenSettings = new Button
            {
                Text = "⚙️",
                Location = new Point(this.Width - 94, 4),
                Size = new Size(26, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand
            };
            btnOpenSettings.FlatAppearance.BorderSize = 0;
            btnOpenSettings.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
            btnOpenSettings.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
            btnOpenSettings.Click += (s, e) => { Program.ShowMainForm(); };
            toolTip.SetToolTip(btnOpenSettings, "Открыть окно настроек");
            pnlTopBar.Controls.Add(btnOpenSettings);

            this.Controls.Add(pnlTopBar);

            // 2. Панель главных вкладок 1-го уровня (Main Tabs) - 34px
            pnlMainTabs = new Panel
            {
                Location = new Point(0, 30),
                Size = new Size(this.Width, 34),
                BackColor = Color.FromArgb(24, 24, 24),
                Cursor = Cursors.SizeAll
            };
            pnlMainTabs.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };

            // Кнопка [Буфер обмена]
            btnMainClipboard = new Button
            {
                Text = "Буфер обмена",
                Location = new Point(12, 2),
                Size = new Size(130, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ThemeHelper.TextPrimary,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnMainClipboard.FlatAppearance.BorderSize = 0;
            btnMainClipboard.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 255, 255, 255);
            btnMainClipboard.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 255, 255, 255);
            btnMainClipboard.Click += (s, e) => SwitchMainTab(0);
            pnlMainTabs.Controls.Add(btnMainClipboard);

            // Кнопка [SuperHub]
            btnMainSuperHub = new Button
            {
                Text = "SuperHub",
                Location = new Point(148, 2),
                Size = new Size(130, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ThemeHelper.TextSecondary,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnMainSuperHub.FlatAppearance.BorderSize = 0;
            btnMainSuperHub.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 255, 255, 255);
            btnMainSuperHub.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 255, 255, 255);
            btnMainSuperHub.Click += (s, e) => SwitchMainTab(1);
            pnlMainTabs.Controls.Add(btnMainSuperHub);

            pnlMainTabs.Paint += (s, e) => {
                Button actBtn = (currentMainTab == 0) ? btnMainClipboard : btnMainSuperHub;
                if (actBtn != null)
                {
                    int barW = Math.Min(48, actBtn.Width - 20);
                    int barX = actBtn.Left + (actBtn.Width - barW) / 2;
                    using (var brush = new SolidBrush(ThemeHelper.Accent))
                    using (var path = CreateRoundedPath(new Rectangle(barX, pnlMainTabs.Height - 3, barW, 3), 1))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.FillPath(brush, path);
                    }
                }
            };
            this.Controls.Add(pnlMainTabs);

            // 3. Панель подвкладок / панели действий 2-го уровня (SubBar) - 30px
            pnlSubBar = new Panel
            {
                Location = new Point(0, 64),
                Size = new Size(this.Width, 30),
                BackColor = Color.FromArgb(31, 31, 31)
            };

            // Подвкладки фильтрации: Все / Снимки / Текст / Файлы
            filterButtons.Clear();
            int subX = 12;
            for (int i = 0; i < filterNames.Length; i++)
            {
                int fIdx = i;
                Button fBtn = new Button
                {
                    Text = filterNames[i],
                    Location = new Point(subX, 3),
                    Size = new Size(i == 0 ? 38 : (i == 1 ? 56 : (i == 2 ? 46 : 50)), 24),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = (i == 0) ? ThemeHelper.CardBackground : Color.Transparent,
                    ForeColor = (i == 0) ? ThemeHelper.TextPrimary : ThemeHelper.TextSecondary,
                    Font = new Font("Segoe UI", 8.5f, (i == 0) ? FontStyle.Bold : FontStyle.Regular),
                    Cursor = Cursors.Hand,
                    TabStop = false
                };
                fBtn.FlatAppearance.BorderSize = 0;
                fBtn.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
                fBtn.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
                fBtn.Click += (s, e) => SwitchFilter(fIdx);
                filterButtons.Add(fBtn);
                pnlSubBar.Controls.Add(fBtn);
                subX += fBtn.Width + 6;
            }

            // Тумблер режима перетаскивания (Копия / Перенос)
            btnDragMode = new Button
            {
                Location = new Point(12, 3),
                Size = new Size(72, 24),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Visible = false,
                TabStop = false
            };
            btnDragMode.FlatAppearance.BorderSize = 0;
            btnDragMode.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
            btnDragMode.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
            btnDragMode.Click += (s, e) => {
                bool isCopy = string.Equals(Config.SuperHubDragMode, "Copy", StringComparison.OrdinalIgnoreCase);
                Config.SuperHubDragMode = isCopy ? "Move" : "Copy";
                UpdateDragModeButtonVisual();
                if (SuperHubDockForm.Instance != null && !SuperHubDockForm.Instance.IsDisposed)
                {
                    SuperHubDockForm.Instance.RefreshItems();
                }
            };
            pnlSubBar.Controls.Add(btnDragMode);

            // Кнопка очистки
            btnClearAll = new Button
            {
                Text = "Очистить",
                Location = new Point(this.Width - 86, 3),
                Size = new Size(74, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(44, 44, 44),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnClearAll.FlatAppearance.BorderSize = 0;
            btnClearAll.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
            btnClearAll.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
            btnClearAll.MouseEnter += (s, e) => btnClearAll.BackColor = ThemeHelper.ButtonHover;
            btnClearAll.MouseLeave += (s, e) => btnClearAll.BackColor = ThemeHelper.CardBackground;
            btnClearAll.Click += (s, e) => {
                bool isHub = (currentMainTab == 1);
                string msg = isHub
                    ? "Очистить все файлы на полке SuperHub?"
                    : "Очистить все незакрепленные элементы буфера?";
                if (MessageBox.Show(msg, isHub ? "SuperHub" : "Буфер обмена", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    ClipboardItemType? filter = null;
                    if (currentFilterIndex == 1) filter = ClipboardItemType.Image;
                    else if (currentFilterIndex == 2) filter = ClipboardItemType.Text;
                    else if (currentFilterIndex == 3) filter = ClipboardItemType.Files;

                    ClipboardHistoryManager.Instance.ClearAll(filter, isHub);
                    RefreshItems();
                }
            };
            toolTip.SetToolTip(btnClearAll, "Очистить все незакрепленные элементы в текущем списке");
            pnlSubBar.Controls.Add(btnClearAll);

            this.Controls.Add(pnlSubBar);

            // Зона сброса файлов для SuperHub (Drop Hint)
            pnlDropHint = new Panel
            {
                Location = new Point(12, 96),
                Size = new Size(this.Width - 24, 30),
                BackColor = Color.FromArgb(36, 36, 36),
                Visible = false,
                AllowDrop = true
            };
            pnlDropHint.Paint += (s, e) => {
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    pen.DashStyle = DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen, 0, 0, pnlDropHint.Width - 1, pnlDropHint.Height - 1);
                }
            };
            pnlDropHint.DragEnter += (s, e) => {
                if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText))
                {
                    e.Effect = DragDropEffects.Copy;
                    pnlDropHint.BackColor = ThemeHelper.AccentBackground;
                }
            };
            pnlDropHint.DragLeave += (s, e) => {
                pnlDropHint.BackColor = Color.FromArgb(36, 36, 36);
            };
            pnlDropHint.DragDrop += (s, e) => {
                pnlDropHint.BackColor = Color.FromArgb(36, 36, 36);
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    ClipboardHistoryManager.Instance.AddCustomSuperHubItem(files, null);
                    RefreshItems();
                }
                else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
                {
                    string txt = (string)e.Data.GetData(DataFormats.UnicodeText);
                    ClipboardHistoryManager.Instance.AddCustomSuperHubItem(null, txt);
                    RefreshItems();
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
            pnlDropHint.Controls.Add(lblDropHint);
            this.Controls.Add(pnlDropHint);

            // 4. Строка поиска (Search Bar) - 34px
            pnlSearch = new Panel
            {
                Location = new Point(0, 106),
                Size = new Size(this.Width, 34),
                BackColor = Color.FromArgb(31, 31, 31),
                Padding = new Padding(12, 2, 12, 4)
            };

            searchBoxBg = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(38, 38, 38),
                Padding = new Padding(8, 4, 8, 4)
            };
            searchBoxBg.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, searchBoxBg.Width - 1, searchBoxBg.Height - 1);
                }
                // Четкая векторная лупа в стиле Fluent
                using (var iconPen = new Pen(Color.FromArgb(160, 160, 160), 1.6f))
                {
                    e.Graphics.DrawEllipse(iconPen, 8f, 7f, 9.5f, 9.5f);
                    e.Graphics.DrawLine(iconPen, 15.5f, 14.5f, 19.5f, 18.5f);
                }
            };

            txtSearch = new TextBox
            {
                Location = new Point(28, 4),
                Width = Math.Max(100, searchBoxBg.ClientSize.Width - 36),
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(38, 38, 38),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f)
            };
            txtSearch.TextChanged += (s, e) => RefreshItems();
            toolTip.SetToolTip(txtSearch, "Поиск по тексту или имени файла в буфере");
            searchBoxBg.Controls.Add(txtSearch);

            pnlSearch.Controls.Add(searchBoxBg);
            this.Controls.Add(pnlSearch);

            // 5. Область просмотра карточек с кастомным Fluent-скроллбаром
            int contentTop = 142;
            pnlViewport = new FlyoutViewportPanel
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

            // Прокрутка колесиком мыши
            pnlViewport.MouseWheel += OnViewportMouseWheel;
            cardsContainer.MouseWheel += OnViewportMouseWheel;

            // Отрисовка темного Fluent Scrollbar и метки Resize Grip
            pnlViewport.Paint += RenderFluentScrollbar;
            pnlViewport.Paint += (s, e) => {
                using (var gripPen = new Pen(Color.FromArgb(90, 90, 90), 1.5f))
                {
                    int gx = pnlViewport.Width - 10;
                    int gy = pnlViewport.Height - 10;
                    e.Graphics.DrawLine(gripPen, gx + 6, gy + 2, gx + 2, gy + 6);
                    e.Graphics.DrawLine(gripPen, gx + 6, gy - 2, gx - 2, gy + 6);
                    e.Graphics.DrawLine(gripPen, gx + 6, gy - 6, gx - 6, gy + 6);
                }
            };
            pnlViewport.MouseMove += OnScrollbarMouseMove;
            pnlViewport.MouseDown += OnScrollbarMouseDown;
            pnlViewport.MouseUp += OnScrollbarMouseUp;
            pnlViewport.MouseLeave += (s, e) => {
                if (isHoveringScroll) { isHoveringScroll = false; pnlViewport.Invalidate(); }
            };

            this.Controls.Add(pnlViewport);

            // Внешняя тонкая рамка 1px вокруг формы
            this.Paint += (s, e) => {
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            this.ResumeLayout(false);
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
                toolTip.SetToolTip(btnDragMode, "Режим Drag-and-Drop: КОПИРОВАНИЕ файлов.\nПри перетаскивании наружу исходные файлы остаются на месте.\nКликните для переключения в режим Переноса.");
            }
            else
            {
                btnDragMode.Text = "Перенос";
                btnDragMode.BackColor = Color.FromArgb(80, 50, 20);
                btnDragMode.ForeColor = Color.FromArgb(255, 170, 60);
                toolTip.SetToolTip(btnDragMode, "Режим Drag-and-Drop: ПЕРЕМЕЩЕНИЕ файлов.\nПри перетаскивании наружу файлы переносятся (вырезаются).\nКликните для переключения в режим Копирования.");
            }
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
                ? ThemeHelper.ScrollBarThumbActive
                : (isHoveringScroll ? ThemeHelper.ScrollBarThumbHover : ThemeHelper.ScrollBarThumb);

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
                SwitchMainTab(1); // Переключаемся на SuperHub
            }
            else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                string t = (string)e.Data.GetData(DataFormats.UnicodeText);
                ClipboardHistoryManager.Instance.AddCustomSuperHubItem(null, t);
                SwitchMainTab(1);
            }
        }

        public void SwitchMainTab(int mainTab)
        {
            currentMainTab = mainTab;
            UpdateTabsVisual();
            LayoutControls();
            RefreshItems();
        }

        public void SwitchFilter(int filterIndex)
        {
            currentFilterIndex = filterIndex;
            UpdateTabsVisual();
            RefreshItems();
        }

        private void UpdateTabsVisual()
        {
            // 1. Главные вкладки (Буфер обмена / SuperHub)
            if (btnMainClipboard != null)
            {
                bool active = (currentMainTab == 0);
                btnMainClipboard.Font = new Font("Segoe UI", 9f, active ? FontStyle.Bold : FontStyle.Regular);
                btnMainClipboard.ForeColor = active ? ThemeHelper.TextPrimary : ThemeHelper.TextSecondary;
            }

            if (btnMainSuperHub != null)
            {
                bool active = (currentMainTab == 1);
                var superList = ClipboardHistoryManager.Instance.GetItems(null, true);
                string title = superList.Count > 0 ? ("SuperHub [" + superList.Count + "]") : "SuperHub";
                btnMainSuperHub.Text = title;
                btnMainSuperHub.Font = new Font("Segoe UI", 9f, active ? FontStyle.Bold : FontStyle.Regular);
                btnMainSuperHub.ForeColor = active ? ThemeHelper.TextPrimary : ThemeHelper.TextSecondary;
            }

            if (pnlMainTabs != null) pnlMainTabs.Invalidate();

            // 2. Подвкладки фильтров буфера
            for (int i = 0; i < filterButtons.Count; i++)
            {
                bool active = (i == currentFilterIndex);
                filterButtons[i].BackColor = active ? ThemeHelper.AccentBackground : Color.Transparent;
                filterButtons[i].ForeColor = active ? ThemeHelper.Accent : ThemeHelper.TextSecondary;
                filterButtons[i].Font = new Font("Segoe UI", 8f, active ? FontStyle.Bold : FontStyle.Regular);
            }

            UpdateDragModeButtonVisual();
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
            cardsContainer.SuspendLayout();
            SafeDisposeControls(cardsContainer);

            bool isHub = (currentMainTab == 1);
            ClipboardItemType? filter = null;
            if (!isHub)
            {
                if (currentFilterIndex == 1) filter = ClipboardItemType.Image;
                else if (currentFilterIndex == 2) filter = ClipboardItemType.Text;
                else if (currentFilterIndex == 3) filter = ClipboardItemType.Files;
            }

            string search = (!isHub && txtSearch != null) ? txtSearch.Text.Trim() : null;
            var list = ClipboardHistoryManager.Instance.GetItems(filter, isHub, search);

            var superList = ClipboardHistoryManager.Instance.GetItems(null, true);
            if (btnMainSuperHub != null)
            {
                btnMainSuperHub.Text = superList.Count > 0 ? ("SuperHub [" + superList.Count + "]") : "SuperHub";
            }

            int cardY = 4;
            int cardW = cardsContainer.Width;

            if (list.Count == 0)
            {
                Label empty = new Label
                {
                    Text = isHub
                        ? "Полка SuperHub пуста.\nПеретащите сюда файлы или добавьте через меню [···]."
                        : "История пуста.\nСкопируйте текст, снимок или файл,\nи они появятся здесь.",
                    ForeColor = ThemeHelper.TextMuted,
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
            int cardH = item.Type == ClipboardItemType.Image ? 78 : 70;
            bool isHub = (currentMainTab == 1);

            Panel card = new Panel
            {
                Size = new Size(width, cardH),
                BackColor = ThemeHelper.CardBackground,
                Cursor = Cursors.Hand
            };

            // Рамка карточки со скруглением 6px
            card.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color bColor = item.IsPinned ? ThemeHelper.Accent : ThemeHelper.CardBorder;
                using (var pen = new Pen(bColor, item.IsPinned ? 1.5f : 1f))
                using (var path = CreateRoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 6))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            };

            // Определение пути для открытия/запуска
            string targetPathForLaunch = null;
            if (item.Type == ClipboardItemType.Image && SafeFileExists(item.ImagePath))
            {
                targetPathForLaunch = item.ImagePath;
            }
            else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
            {
                targetPathForLaunch = item.FilePaths[0];
            }
            else if (item.Type == ClipboardItemType.Text && !string.IsNullOrEmpty(item.TextContent))
            {
                string trimmed = item.TextContent.Trim();
                if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    (trimmed.Length < 260 && (SafeFileExists(trimmed) || SafeDirectoryExists(trimmed))))
                {
                    targetPathForLaunch = trimmed;
                }
            }
            bool hasLaunch = !string.IsNullOrEmpty(targetPathForLaunch);

            // Строгая сетка кнопок 2x2 справа (ширина 56px, отступ 4px):
            // Колонка 1: X = card.Width - 58, Колонка 2: X = card.Width - 28
            // Ряд 1: Y = 6, Ряд 2: Y = 36
            if (!isHub)
            {
                // РЕЖИМ БУФЕРА ОБМЕНА:
                // 1. c1_r1: Вставка в активное окно [📥]
                Button btnPaste = new Button
                {
                    Text = "📥",
                    Location = new Point(card.Width - 62, 6),
                    Size = new Size(26, 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = ThemeHelper.TextSecondary,
                    Font = new Font("Segoe UI Emoji", 9.5f),
                    Cursor = Cursors.Hand,
                    TabStop = false,
                    Tag = "c1_r1"
                };
                btnPaste.FlatAppearance.BorderSize = 0;
                btnPaste.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
                btnPaste.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
                btnPaste.MouseEnter += (s, e) => { btnPaste.ForeColor = ThemeHelper.Accent; };
                btnPaste.MouseLeave += (s, e) => { btnPaste.ForeColor = ThemeHelper.TextSecondary; };
                btnPaste.Click += (s, e) => PasteItem(item);
                toolTip.SetToolTip(btnPaste, "Вставить в активное окно (Ctrl+V)");
                card.Controls.Add(btnPaste);

                // 2. c2_r1: Удалить из истории буфера обмена [✕]
                Button btnDelete = new Button
                {
                    Text = "✕",
                    Location = new Point(card.Width - 32, 6),
                    Size = new Size(26, 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = ThemeHelper.TextSecondary,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    TabStop = false,
                    Tag = "c2_r1"
                };
                btnDelete.FlatAppearance.BorderSize = 0;
                btnDelete.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
                btnDelete.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 30, 20);
                btnDelete.MouseEnter += (s, e) => { btnDelete.ForeColor = Color.White; };
                btnDelete.MouseLeave += (s, e) => { btnDelete.ForeColor = ThemeHelper.TextSecondary; };
                btnDelete.Click += (s, e) => {
                    ClipboardHistoryManager.Instance.DeleteItem(item.Id);
                    RefreshItems();
                };
                toolTip.SetToolTip(btnDelete, "Удалить элемент из истории буфера");
                card.Controls.Add(btnDelete);

                // 3. c1_r2: Быстрый просмотр [👁]
                Button btnView = new Button
                {
                    Text = "👁",
                    Location = new Point(card.Width - 62, 36),
                    Size = new Size(26, 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = ThemeHelper.TextSecondary,
                    Font = new Font("Segoe UI Emoji", 9.5f),
                    Cursor = Cursors.Hand,
                    TabStop = false,
                    Tag = "c1_r2"
                };
                btnView.FlatAppearance.BorderSize = 0;
                btnView.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
                btnView.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
                btnView.MouseEnter += (s, e) => { btnView.ForeColor = ThemeHelper.TextPrimary; };
                btnView.MouseLeave += (s, e) => { btnView.ForeColor = ThemeHelper.TextSecondary; };
                btnView.Click += (s, e) => ItemViewerForm.ShowViewer(item);
                toolTip.SetToolTip(btnView, "Просмотр содержимого");
                card.Controls.Add(btnView);

                // 4. c2_r2: Закрепление вверху [📌]
                Button btnPin = new Button
                {
                    Text = "📌",
                    Location = new Point(card.Width - 32, 36),
                    Size = new Size(26, 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = item.IsPinned ? ThemeHelper.Accent : ThemeHelper.TextMuted,
                    Font = new Font("Segoe UI Emoji", 8.5f),
                    Cursor = Cursors.Hand,
                    TabStop = false,
                    Tag = "c2_r2"
                };
                btnPin.FlatAppearance.BorderSize = 0;
                btnPin.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
                btnPin.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
                btnPin.MouseEnter += (s, e) => { btnPin.ForeColor = ThemeHelper.TextPrimary; };
                btnPin.MouseLeave += (s, e) => {
                    btnPin.ForeColor = item.IsPinned ? ThemeHelper.Accent : ThemeHelper.TextMuted;
                };
                btnPin.Click += (s, e) => {
                    ClipboardHistoryManager.Instance.TogglePin(item.Id);
                    RefreshItems();
                };
                toolTip.SetToolTip(btnPin, item.IsPinned ? "Открепить" : "Закрепить вверху");
                card.Controls.Add(btnPin);
            }
            else
            {
                // РЕЖИМ SUPERHUB:
                // 1. c1_r1: Быстрый просмотр [👁]
                Button btnView = new Button
                {
                    Text = "👁",
                    Location = new Point(card.Width - 62, 6),
                    Size = new Size(26, 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = ThemeHelper.TextSecondary,
                    Font = new Font("Segoe UI Emoji", 9.5f),
                    Cursor = Cursors.Hand,
                    TabStop = false,
                    Tag = "c1_r1"
                };
                btnView.FlatAppearance.BorderSize = 0;
                btnView.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
                btnView.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
                btnView.MouseEnter += (s, e) => { btnView.ForeColor = ThemeHelper.TextPrimary; };
                btnView.MouseLeave += (s, e) => { btnView.ForeColor = ThemeHelper.TextSecondary; };
                btnView.Click += (s, e) => ItemViewerForm.ShowViewer(item);
                toolTip.SetToolTip(btnView, "Просмотр содержимого");
                card.Controls.Add(btnView);

                // 2. c2_r1: Убрать с полки SuperHub [✕]
                Button btnRemove = new Button
                {
                    Text = "✕",
                    Location = new Point(card.Width - 32, 6),
                    Size = new Size(26, 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = ThemeHelper.TextSecondary,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    TabStop = false,
                    Tag = "c2_r1"
                };
                btnRemove.FlatAppearance.BorderSize = 0;
                btnRemove.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
                btnRemove.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 30, 20);
                btnRemove.MouseEnter += (s, e) => { btnRemove.ForeColor = Color.White; };
                btnRemove.MouseLeave += (s, e) => { btnRemove.ForeColor = ThemeHelper.TextSecondary; };
                btnRemove.Click += (s, e) => {
                    ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                    RefreshItems();
                };
                toolTip.SetToolTip(btnRemove, "Убрать файл с полки SuperHub");
                card.Controls.Add(btnRemove);

                // 3. c1_r2: Вставить в активное окно [📥]
                Button btnPaste = new Button
                {
                    Text = "📥",
                    Location = new Point(card.Width - 62, 36),
                    Size = new Size(26, 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = ThemeHelper.TextSecondary,
                    Font = new Font("Segoe UI Emoji", 9.5f),
                    Cursor = Cursors.Hand,
                    TabStop = false,
                    Tag = "c1_r2"
                };
                btnPaste.FlatAppearance.BorderSize = 0;
                btnPaste.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
                btnPaste.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
                btnPaste.MouseEnter += (s, e) => { btnPaste.ForeColor = ThemeHelper.Accent; };
                btnPaste.MouseLeave += (s, e) => { btnPaste.ForeColor = ThemeHelper.TextSecondary; };
                btnPaste.Click += (s, e) => PasteItem(item);
                toolTip.SetToolTip(btnPaste, "Вставить в активное окно");
                card.Controls.Add(btnPaste);

                // 4. c2_r2: Запуск в Windows [▷] ИЛИ Меню действий [···]
                Button btnAction = new Button
                {
                    Location = new Point(card.Width - 32, 36),
                    Size = new Size(26, 26),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = ThemeHelper.TextSecondary,
                    Cursor = Cursors.Hand,
                    TabStop = false,
                    Tag = "c2_r2"
                };
                btnAction.FlatAppearance.BorderSize = 0;
                btnAction.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
                btnAction.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
                if (hasLaunch)
                {
                    btnAction.Text = "▷";
                    btnAction.Font = new Font("Segoe UI", 9f);
                    string launchTarget = targetPathForLaunch;
                    btnAction.Click += (s, e) => OpenItemInAssociatedApp(launchTarget);
                    toolTip.SetToolTip(btnAction, "Открыть / Запустить файл");
                }
                else
                {
                    btnAction.Text = "···";
                    btnAction.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                    btnAction.Click += (s, e) => {
                        ContextMenu cm = CreateCardContextMenu(item);
                        cm.Show(btnAction, new Point(0, btnAction.Height));
                    };
                    toolTip.SetToolTip(btnAction, "Меню действий");
                }
                btnAction.MouseEnter += (s, e) => { btnAction.ForeColor = ThemeHelper.TextPrimary; };
                btnAction.MouseLeave += (s, e) => { btnAction.ForeColor = ThemeHelper.TextSecondary; };
                card.Controls.Add(btnAction);
            }

            // Контентная часть карточки:
            int textLeft = 14;
            int textWidth = Math.Max(40, card.Width - 70);
            List<Control> infoControls = new List<Control>();

            if (item.Type == ClipboardItemType.Image && SafeFileExists(item.ImagePath))
            {
                targetPathForLaunch = item.ImagePath;
                PictureBox pic = new PictureBox
                {
                    Location = new Point(8, (cardH - 58) / 2),
                    Size = new Size(58, 58),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = ThemeHelper.IsDarkTheme() ? Color.FromArgb(24, 24, 24) : Color.FromArgb(235, 235, 235),
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
                try { card.Controls.Add(pic); infoControls.Add(pic); } catch {}

                textLeft = 74;
                textWidth = Math.Max(40, card.Width - textLeft - 70);

                Label lblImgTitle = new Label
                {
                    Text = string.IsNullOrEmpty(item.SourceApp) ? SafeGetFileName(item.ImagePath) : item.SourceApp,
                    Font = new Font("Segoe UI Semibold", 9.5f),
                    ForeColor = ThemeHelper.TextPrimary,
                    Location = new Point(textLeft, 14),
                    Size = new Size(textWidth, 20),
                    AutoEllipsis = true,
                    Cursor = Cursors.Hand
                };

                string dim = (item.ImageWidth > 0 && item.ImageHeight > 0) ? (item.ImageWidth + "×" + item.ImageHeight + " • ") : "";
                string sz = item.FileSizeBytes > 0 ? ((item.FileSizeBytes / 1024) + " КБ • ") : "";
                Label lblImgSub = new Label
                {
                    Text = dim + sz + FormatDate(item.Timestamp),
                    Font = new Font("Segoe UI", 8f),
                    ForeColor = ThemeHelper.TextSecondary,
                    Location = new Point(textLeft, 38),
                    Size = new Size(textWidth, 18),
                    AutoEllipsis = true,
                    Cursor = Cursors.Hand
                };

                card.Controls.Add(lblImgTitle);
                card.Controls.Add(lblImgSub);
                infoControls.Add(lblImgTitle);
                infoControls.Add(lblImgSub);
            }
            else if (item.Type == ClipboardItemType.Files)
            {
                string firstFile = (item.FilePaths != null && item.FilePaths.Count > 0) ? item.FilePaths[0] : "";
                targetPathForLaunch = firstFile;
                Image ico = FileIconHelper.GetIconForPath(firstFile);

                PictureBox pic = new PictureBox
                {
                    Location = new Point(10, (cardH - 36) / 2),
                    Size = new Size(36, 36),
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                try { pic.Image = ico; } catch {}
                try { card.Controls.Add(pic); infoControls.Add(pic); } catch {}

                textLeft = 54;
                textWidth = Math.Max(40, card.Width - textLeft - 70);

                Label lblFileTitle = new Label
                {
                    Text = SafeGetFileName(firstFile),
                    Font = new Font("Segoe UI Semibold", 9.5f),
                    ForeColor = ThemeHelper.TextPrimary,
                    Location = new Point(textLeft, 12),
                    Size = new Size(textWidth, 20),
                    AutoEllipsis = true,
                    Cursor = Cursors.Hand
                };

                string sub = (item.FilePaths != null && item.FilePaths.Count > 1)
                    ? ("и еще " + (item.FilePaths.Count - 1) + " файлов • " + item.SourceApp)
                    : ((SafeDirectoryExists(firstFile) ? "Папка" : "Файл") + " • " + item.SourceApp);

                Label lblFileSub = new Label
                {
                    Text = sub + " • " + FormatDate(item.Timestamp),
                    Font = new Font("Segoe UI", 8f),
                    ForeColor = ThemeHelper.TextSecondary,
                    Location = new Point(textLeft, 36),
                    Size = new Size(textWidth, 18),
                    AutoEllipsis = true,
                    Cursor = Cursors.Hand
                };

                card.Controls.Add(lblFileTitle);
                card.Controls.Add(lblFileSub);
                infoControls.Add(lblFileTitle);
                infoControls.Add(lblFileSub);
            }
            else
            {
                string raw = item.TextContent ?? "";
                string line1 = raw.Replace("\r", " ").Replace("\n", " ").Trim();
                if (line1.Length > 90) line1 = line1.Substring(0, 90) + "...";

                textLeft = 14;
                textWidth = Math.Max(40, card.Width - textLeft - 70);

                Label lblTxtMain = new Label
                {
                    Text = line1,
                    Font = new Font("Segoe UI", 9.5f),
                    ForeColor = ThemeHelper.TextPrimary,
                    Location = new Point(textLeft, 12),
                    Size = new Size(textWidth, 22),
                    AutoEllipsis = true,
                    Cursor = Cursors.Hand
                };

                string src = string.IsNullOrEmpty(item.SourceApp) ? "Текст" : item.SourceApp;
                Label lblTxtSub = new Label
                {
                    Text = src + " • " + raw.Length + " симв. • " + FormatDate(item.Timestamp),
                    Font = new Font("Segoe UI", 8f),
                    ForeColor = ThemeHelper.TextSecondary,
                    Location = new Point(textLeft, 38),
                    Size = new Size(textWidth, 18),
                    AutoEllipsis = true,
                    Cursor = Cursors.Hand
                };

                card.Controls.Add(lblTxtMain);
                card.Controls.Add(lblTxtSub);
                infoControls.Add(lblTxtMain);
                infoControls.Add(lblTxtSub);
            }

            // Наведение и события клика/двойного клика
            Point dragStartPt = Point.Empty;
            bool isPotentialDrag = false;

            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                if (c is Button) return; // Изолируем кнопки карточки: hover и toolTip работают нативно

                c.MouseEnter += (s, e) => { card.BackColor = ThemeHelper.CardHover; };
                c.MouseLeave += (s, e) => { card.BackColor = ThemeHelper.CardBackground; };

                // Одиночный клик: строгое копирование в буфер
                c.Click += (s, e) => {
                    if (c is Button) return;
                    CopyItem(item);
                    ShowCopiedNotification();
                    if (!isWindowPinned)
                    {
                        CloseFlyout();
                    }
                };

                // Двойной клик открывает файл
                c.DoubleClick += (s, e) => {
                    if (!(c is Button) && !string.IsNullOrEmpty(targetPathForLaunch))
                    {
                        OpenItemInAssociatedApp(targetPathForLaunch);
                    }
                };

                // Запоминаем точку нажатия для детекции Drag vs Click
                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left && e.Clicks == 1 && !(c is Button))
                    {
                        dragStartPt = e.Location;
                        isPotentialDrag = true;
                    }
                };

                c.MouseMove += (s, e) => {
                    if (isPotentialDrag && e.Button == MouseButtons.Left && !(c is Button))
                    {
                        int dx = Math.Abs(e.X - dragStartPt.X);
                        int dy = Math.Abs(e.Y - dragStartPt.Y);
                        if (dx >= SystemInformation.DragSize.Width || dy >= SystemInformation.DragSize.Height)
                        {
                            isPotentialDrag = false;
                            StartDragItem(item, card);
                        }
                    }
                };

                c.MouseUp += (s, e) => {
                    isPotentialDrag = false;
                    // Правый клик мыши открывает контекстное меню на любом месте карточки
                    if (e.Button == MouseButtons.Right)
                    {
                        ContextMenu cm = CreateCardContextMenu(item);
                        cm.Show(c, e.Location);
                    }
                };

                foreach (Control sub in c.Controls)
                {
                    if (!(sub is Button))
                    {
                        attachEvents(sub);
                    }
                }
            };
            attachEvents(card);

            string tip = isHub
                ? "Клик: скопировать в буфер\nЗажать и потянуть: перетащить наружу\nПравый клик: меню действий"
                : "Клик: скопировать в буфер\nДвойной клик: открыть файл\nПравый клик: меню действий";
            foreach (Control ic in infoControls)
            {
                toolTip.SetToolTip(ic, tip);
            }

            return card;
        }

        private void HandleCardClick(ClipboardItem item)
        {
            CopyItem(item);
            ShowCopiedNotification();
            if (!isWindowPinned)
            {
                CloseFlyout();
            }
        }

        private void ShowCopiedNotification()
        {
            try
            {
                toolTip.Show("✓ Скопировано в буфер обмена!", this, this.Width / 2 - 80, this.Height - 60, 1500);
            }
            catch {}
        }

        private void OpenItemInAssociatedApp(string path)
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
                MessageBox.Show("Не удалось открыть файл:\n" + ex.Message, "Буфер обмена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private ContextMenu CreateCardContextMenu(ClipboardItem item)
        {
            var menu = new ContextMenu();

            menu.MenuItems.Add(new MenuItem("👁 Просмотр (зум/текст)", (s, e) => ItemViewerForm.ShowViewer(item)));

            string targetPath = null;
            if (item.Type == ClipboardItemType.Image) targetPath = item.ImagePath;
            else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0) targetPath = item.FilePaths[0];

            if (!string.IsNullOrEmpty(targetPath) && (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)))
            {
                menu.MenuItems.Add(new MenuItem("▷ Открыть в программе", (s, e) => OpenItemInAssociatedApp(targetPath)));
            }

            menu.MenuItems.Add(new MenuItem("-"));
            menu.MenuItems.Add(new MenuItem("Вставить в активное окно", (s, e) => PasteItem(item)));
            menu.MenuItems.Add(new MenuItem("Скопировать в буфер", (s, e) => {
                CopyItem(item);
                ShowCopiedNotification();
            }));
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
                menu.MenuItems.Add(new MenuItem("📁 Показать в Проводнике", (s, e) => ShowItemInExplorer(item)));
            }

            menu.MenuItems.Add(new MenuItem("-"));
            menu.MenuItems.Add(new MenuItem("🗑️ Удалить", (s, e) => {
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

            IntPtr targetWnd = previousForegroundWindow;
            if (targetWnd == IntPtr.Zero || IsOurAppWindow(targetWnd) || Program.IsTaskbarOrTrayWindow(targetWnd))
            {
                Screen scr = Screen.FromControl(this);
                targetWnd = Program.FindLastActiveUserWindow(scr);
            }

            if (targetWnd != IntPtr.Zero && !IsOurAppWindow(targetWnd))
            {
                this.previousForegroundWindow = targetWnd;
            }

            bool wasPinned = isWindowPinned;
            if (!wasPinned)
            {
                CloseFlyout();
            }

            ThreadPool.QueueUserWorkItem(_ => {
                try
                {
                    // Пауза для надежного закрытия окна и переключения фокуса Windows
                    Thread.Sleep(wasPinned ? 60 : 120);
                    if (targetWnd != IntPtr.Zero)
                    {
                        Program.ForceForegroundWindow(targetWnd);
                        Thread.Sleep(80);
                    }

                    // Сброс модификаторов (Alt, Shift, Win), если они остались зажатыми
                    keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Alt
                    keybd_event(0x10, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Shift
                    keybd_event(0x5B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Win

                    // Отправка комбинации Ctrl+V
                    keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(15);
                    keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(30);
                    keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Thread.Sleep(15);
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                    Logger.Log(string.Format("Pasted item {0} into targetWnd={1}", item.Id, targetWnd));
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
                // Учет выбранного режима: Копия или Перенос
                bool isMove = string.Equals(Config.SuperHubDragMode, "Move", StringComparison.OrdinalIgnoreCase);
                var dropEffectStream = new MemoryStream(new byte[] { (byte)(isMove ? 2 : 1), 0, 0, 0 });
                data.SetData("Preferred DropEffect", dropEffectStream);

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

                sourceControl.DoDragDrop(data, isMove ? DragDropEffects.Move : DragDropEffects.Copy);
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
            try { return Path.GetFileName(path); }
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
                    bool wasVisible = currentInstance.Visible;
                    currentInstance.CloseFlyout();
                    if (wasVisible)
                    {
                        return;
                    }
                }

                Screen scr = Screen.FromPoint(cursor);

                // Если предыдущее окно - это панель задач (клик по трею), находим настоящее пользовательское окно
                if (prevFg == IntPtr.Zero || Program.IsTaskbarOrTrayWindow(prevFg))
                {
                    IntPtr realUserWindow = Program.FindLastActiveUserWindow(scr);
                    if (realUserWindow != IntPtr.Zero)
                    {
                        prevFg = realUserWindow;
                    }
                }

                currentInstance = new ClipboardFlyoutForm(prevFg);
                currentInstance.RefreshItems();

                int x = cursor.X - currentInstance.Width / 2;

                // Учет вертикального положения панели задач
                int y;
                if (scr.WorkingArea.Top > scr.Bounds.Top + 10)
                {
                    y = scr.WorkingArea.Top + 8;
                }
                else if (scr.WorkingArea.Bottom < scr.Bounds.Bottom - 10)
                {
                    y = scr.WorkingArea.Bottom - currentInstance.Height - 8;
                }
                else
                {
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

                bool isFromBottom = (y > scr.Bounds.Top + scr.Bounds.Height / 2);
                currentInstance.AnimateIn(new Point(x, y), isFromBottom);

                Logger.Log(string.Format("Flyout shown at ({0},{1}) on {2} with prevFg={3}", x, y, scr.DeviceName, prevFg));
            }
        }
    }
}
