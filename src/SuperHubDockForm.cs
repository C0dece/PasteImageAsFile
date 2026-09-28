using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PasteImageAsFile
{
    /// <summary>
    /// Автономный легковесный маркер (Dock Strip / Marker) у края или угла экрана.
    /// Поддерживает работу на всех экранах (AllScreens) или выбранном экране.
    /// При наведении, клике или Drop активирует единое всплывающее окно на соответствующем экране.
    /// </summary>
    public class SuperHubDockForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out DesktopHelper.POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

        private enum QUERY_USER_NOTIFICATION_STATE
        {
            QUNS_NOT_PRESENT = 1,
            QUNS_BUSY = 2,
            QUNS_RUNNING_D3D_FULL_SCREEN = 3,
            QUNS_PRESENTATION_MODE = 4,
            QUNS_ACCEPTS_NOTIFICATIONS = 5,
            QUNS_QUIET_TIME = 6,
            QUNS_APP = 7
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;

        private const int VK_LBUTTON = 0x01;

        // Размеры маркера у края экрана
        private const int BarThickness = 4;
        private const int BarLength = 84;
        private const int CornerRadius = 16;

        private static readonly List<SuperHubDockForm> activeDocks = new List<SuperHubDockForm>();
        private static readonly object instanceLock = new object();
        private static bool isDisplayListenerRegistered = false;

        private System.Windows.Forms.Timer pollTimer;
        private int hoverExpandCounter = 0;
        private bool isMouseHovering = false;

        public Screen AttachedScreen { get; private set; }

        public static SuperHubDockForm Instance
        {
            get
            {
                lock (instanceLock)
                {
                    if (activeDocks.Count == 0 || activeDocks[0].IsDisposed)
                    {
                        SyncAllDocks();
                    }
                    return activeDocks.Count > 0 ? activeDocks[0] : null;
                }
            }
        }

        public static void SyncAllDocks()
        {
            lock (instanceLock)
            {
                if (!isDisplayListenerRegistered)
                {
                    try
                    {
                        SystemEvents.DisplaySettingsChanged += (s, e) => {
                            try { SyncAllDocks(); } catch {}
                        };
                        isDisplayListenerRegistered = true;
                    }
                    catch {}
                }

                foreach (var dock in activeDocks)
                {
                    try
                    {
                        dock.Hide();
                        dock.Dispose();
                    }
                    catch {}
                }
                activeDocks.Clear();

                if (!Config.SuperHubEnabled) return;

                string mode = Config.SuperHubScreenMode;
                Screen[] screens = Screen.AllScreens;

                if (string.Equals(mode, "Primary", StringComparison.OrdinalIgnoreCase))
                {
                    var d = new SuperHubDockForm(Screen.PrimaryScreen);
                    d.Show();
                    activeDocks.Add(d);
                }
                else if (string.Equals(mode, "Specific", StringComparison.OrdinalIgnoreCase))
                {
                    string targetDev = Config.SuperHubTargetScreen;
                    Screen targetScreen = null;
                    foreach (var s in screens)
                    {
                        if (string.Equals(s.DeviceName, targetDev, StringComparison.OrdinalIgnoreCase))
                        {
                            targetScreen = s;
                            break;
                        }
                    }
                    if (targetScreen == null) targetScreen = Screen.PrimaryScreen;
                    var d = new SuperHubDockForm(targetScreen);
                    d.Show();
                    activeDocks.Add(d);
                }
                else // "All" - На всех подключенных мониторах
                {
                    foreach (var s in screens)
                    {
                        var d = new SuperHubDockForm(s);
                        d.Show();
                        activeDocks.Add(d);
                    }
                }
            }
        }

        public static void RepositionAllDocks()
        {
            lock (instanceLock)
            {
                if (activeDocks.Count == 0)
                {
                    SyncAllDocks();
                    return;
                }
                foreach (var d in activeDocks)
                {
                    if (!d.IsDisposed) d.PositionCollapsed();
                }
            }
        }

        public static void RefreshAllDocks()
        {
            lock (instanceLock)
            {
                foreach (var d in activeDocks)
                {
                    if (!d.IsDisposed) d.Invalidate();
                }
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE - не перехватывает фокус ввода
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW - не виден в Alt+Tab
                return cp;
            }
        }

        public SuperHubDockForm(Screen screen = null)
        {
            this.AttachedScreen = screen ?? Screen.PrimaryScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(BarThickness, BarLength);
            this.TopMost = true;
            this.AllowDrop = true;
            this.DoubleBuffered = true;
            this.MinimumSize = new Size(1, 1);
            this.BackColor = ThemeHelper.Accent;
            this.Cursor = Cursors.Hand;

            this.Click += (s, e) => ExpandShelf();

            this.MouseEnter += (s, e) => {
                isMouseHovering = true;
                this.Invalidate();
            };
            this.MouseLeave += (s, e) => {
                isMouseHovering = false;
                this.Invalidate();
            };

            this.DragEnter += OnDragEnter;
            this.DragOver += (s, e) => { e.Effect = DragDropEffects.Copy; };
            this.DragDrop += OnDragDrop;

            ThemeHelper.ThemeChanged += () => {
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke((Action)(() => {
                        this.BackColor = ThemeHelper.Accent;
                        this.Invalidate();
                    }));
                }
            };

            PositionCollapsed();

            // Таймер опроса курсора (детекция наведения и перетаскивания файлов к краю)
            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Interval = 50;
            pollTimer.Tick += OnPollTick;
            pollTimer.Start();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                e.Effect = DragDropEffects.Copy;
                ExpandShelf();
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    ClipboardHistoryManager.Instance.AddCustomSuperHubItem(files, null);
                }
                else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
                {
                    string text = (string)e.Data.GetData(DataFormats.UnicodeText);
                    ClipboardHistoryManager.Instance.AddCustomSuperHubItem(null, text);
                }
                ExpandShelf();
            }
            catch (Exception ex)
            {
                Logger.Log("SuperHubDockForm OnDragDrop error: " + ex.Message);
            }
        }

        public static bool IsFullScreenGameOrAppActive(Screen screen)
        {
            try
            {
                // 1. Проверка через Shell API (Direct3D/DirectX/Vulkan эксклюзивные полноэкранные игры)
                QUERY_USER_NOTIFICATION_STATE state;
                if (SHQueryUserNotificationState(out state) == 0)
                {
                    if (state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN)
                    {
                        return true;
                    }
                }

                // 2. Проверка активного переднего окна (на случай Borderless Fullscreen)
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    var sb = new System.Text.StringBuilder(256);
                    GetClassName(fg, sb, sb.Capacity);
                    string cls = sb.ToString();
                    if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                    {
                        return false;
                    }

                    // Проверяем, не является ли окно окном нашей утилиты
                    foreach (Form openForm in Application.OpenForms)
                    {
                        if (openForm.Handle == fg)
                        {
                            return false;
                        }
                    }

                    RECT rc;
                    if (GetWindowRect(fg, out rc))
                    {
                        Screen scr = screen ?? Screen.PrimaryScreen;
                        // Окно покрывает весь экран
                        if (rc.Left <= scr.Bounds.Left && rc.Top <= scr.Bounds.Top &&
                            rc.Right >= scr.Bounds.Right && rc.Bottom >= scr.Bounds.Bottom)
                        {
                            int style = GetWindowLong(fg, GWL_STYLE);
                            // Если у окна отсутствует заголовок WS_CAPTION, это полноэкранная игра или видеоплеер
                            if ((style & WS_CAPTION) != WS_CAPTION)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch {}
            return false;
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            if (!Config.SuperHubEnabled)
            {
                if (this.Visible) this.Visible = false;
                return;
            }

            // Автоматическое скрытие и блокировка маркера, если запущена полноэкранная игра
            if (IsFullScreenGameOrAppActive(this.AttachedScreen))
            {
                if (this.Visible)
                {
                    this.Visible = false;
                }
                hoverExpandCounter = 0;
                return;
            }
            else
            {
                if (!this.Visible)
                {
                    this.Visible = true;
                }
            }

            DesktopHelper.POINT pt;
            if (!GetCursorPos(out pt)) return;

            Point cur = new Point(pt.x, pt.y);

            // Зона срабатывания строго у маркера: границы маркера + небольшой буфер 6px
            Rectangle hitArea = this.Bounds;
            hitArea.Inflate(6, 6);

            bool isOverUs = hitArea.Contains(cur);

            if (isOverUs)
            {
                hoverExpandCounter++;
                if (hoverExpandCounter >= 2) // Дебаунс 100мс (2 тика по 50мс) для предотвращения случайного срабатывания
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

        public void ExpandShelf(bool animate = true)
        {
            if (!Config.SuperHubEnabled) return;
            if (IsFullScreenGameOrAppActive(this.AttachedScreen)) return;
            try
            {
                Screen scr = this.AttachedScreen ?? Screen.FromPoint(Cursor.Position);
                IntPtr prevFg = Program.FindLastActiveUserWindow(scr);
                string pos = Config.GetScreenPosition(scr.DeviceName, Config.SuperHubPosition);
                ClipboardFlyoutForm.ShowDockFlyout(scr, pos, prevFg, true, this.Bounds);
            }
            catch (Exception ex)
            {
                Logger.Log("SuperHubDockForm ExpandShelf error: " + ex.Message);
            }
        }

        public void CollapseShelf(bool animate = true)
        {
            // Маркер всегда в свернутом виде
        }

        public void RefreshItems()
        {
            this.Invalidate();
        }

        public bool IsPositionOnLeft()
        {
            string pos = Config.GetScreenPosition(this.AttachedScreen != null ? this.AttachedScreen.DeviceName : "", Config.SuperHubPosition);
            return pos.StartsWith("Left", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsHorizontalPosition()
        {
            string pos = Config.GetScreenPosition(this.AttachedScreen != null ? this.AttachedScreen.DeviceName : "", Config.SuperHubPosition);
            return string.Equals(pos, "TopCenter", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(pos, "BottomCenter", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsCornerPosition()
        {
            string pos = Config.GetScreenPosition(this.AttachedScreen != null ? this.AttachedScreen.DeviceName : "", Config.SuperHubPosition);
            return pos.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void PositionCollapsed()
        {
            if (!Config.SuperHubEnabled)
            {
                this.Visible = false;
                return;
            }

            Screen scr = this.AttachedScreen ?? Screen.PrimaryScreen;
            Rectangle work = scr.WorkingArea;
            string pos = Config.GetScreenPosition(scr.DeviceName, Config.SuperHubPosition);

            int x = 0, y = 0, w = BarThickness, h = BarLength;

            if (string.Equals(pos, "CornerBottomRight", StringComparison.OrdinalIgnoreCase))
            {
                w = CornerRadius;
                h = CornerRadius;
                x = work.Right - w;
                y = work.Bottom - h;
            }
            else if (string.Equals(pos, "CornerBottomLeft", StringComparison.OrdinalIgnoreCase))
            {
                w = CornerRadius;
                h = CornerRadius;
                x = work.Left;
                y = work.Bottom - h;
            }
            else if (string.Equals(pos, "CornerTopRight", StringComparison.OrdinalIgnoreCase))
            {
                w = CornerRadius;
                h = CornerRadius;
                x = work.Right - w;
                y = work.Top;
            }
            else if (string.Equals(pos, "CornerTopLeft", StringComparison.OrdinalIgnoreCase))
            {
                w = CornerRadius;
                h = CornerRadius;
                x = work.Left;
                y = work.Top;
            }
            else if (string.Equals(pos, "TopCenter", StringComparison.OrdinalIgnoreCase))
            {
                w = BarLength;
                h = BarThickness;
                x = work.Left + (work.Width - w) / 2;
                y = work.Top;
            }
            else if (string.Equals(pos, "BottomCenter", StringComparison.OrdinalIgnoreCase))
            {
                w = BarLength;
                h = BarThickness;
                x = work.Left + (work.Width - w) / 2;
                y = work.Bottom - h;
            }
            else if (pos.StartsWith("Left", StringComparison.OrdinalIgnoreCase))
            {
                w = BarThickness;
                h = BarLength;
                x = work.Left;
                if (pos.EndsWith("Top", StringComparison.OrdinalIgnoreCase)) y = work.Top + 24;
                else if (pos.EndsWith("Bottom", StringComparison.OrdinalIgnoreCase)) y = work.Bottom - h - 24;
                else y = work.Top + (work.Height - h) / 2;
            }
            else // Right (RightCenter, RightTop, RightBottom)
            {
                w = BarThickness;
                h = BarLength;
                x = work.Right - w;
                if (pos.EndsWith("Top", StringComparison.OrdinalIgnoreCase)) y = work.Top + 24;
                else if (pos.EndsWith("Bottom", StringComparison.OrdinalIgnoreCase)) y = work.Bottom - h - 24;
                else y = work.Top + (work.Height - h) / 2;
            }

            this.SetBounds(x, y, w, h);

            // Создание скругленного региона маркера:
            // Сторона, прилегающая к краю монитора — строго ПРЯМАЯ.
            // Сторона, смотрящая внутрь экрана — скруглена в углах.
            try
            {
                using (var path = new GraphicsPath())
                {
                    if (IsCornerPosition())
                    {
                        if (string.Equals(pos, "CornerBottomRight", StringComparison.OrdinalIgnoreCase))
                        {
                            path.AddPie(0, 0, w * 2, h * 2, 180, 90);
                        }
                        else if (string.Equals(pos, "CornerBottomLeft", StringComparison.OrdinalIgnoreCase))
                        {
                            path.AddPie(-w, 0, w * 2, h * 2, 270, 90);
                        }
                        else if (string.Equals(pos, "CornerTopRight", StringComparison.OrdinalIgnoreCase))
                        {
                            path.AddPie(0, -h, w * 2, h * 2, 90, 90);
                        }
                        else // CornerTopLeft
                        {
                            path.AddPie(-w, -h, w * 2, h * 2, 0, 90);
                        }
                    }
                    else if (pos.StartsWith("Left", StringComparison.OrdinalIgnoreCase))
                    {
                        // Прижат слева к краю монитора: скругление только справа внутрь экрана
                        int r = w;
                        path.AddLine(0, h, 0, 0);
                        path.AddLine(0, 0, Math.Max(0, w - r), 0);
                        path.AddArc(w - r * 2, 0, r * 2, r * 2, 270, 90);
                        path.AddLine(w, r, w, h - r);
                        path.AddArc(w - r * 2, h - r * 2, r * 2, r * 2, 0, 90);
                        path.AddLine(Math.Max(0, w - r), h, 0, h);
                        path.CloseFigure();
                    }
                    else if (string.Equals(pos, "TopCenter", StringComparison.OrdinalIgnoreCase))
                    {
                        // Прижат сверху к краю монитора: скругление только снизу
                        int r = h;
                        path.AddLine(0, 0, w, 0);
                        path.AddLine(w, 0, w, Math.Max(0, h - r));
                        path.AddArc(w - r * 2, h - r * 2, r * 2, r * 2, 0, 90);
                        path.AddLine(w - r, h, r, h);
                        path.AddArc(0, h - r * 2, r * 2, r * 2, 90, 90);
                        path.AddLine(0, Math.Max(0, h - r), 0, 0);
                        path.CloseFigure();
                    }
                    else if (string.Equals(pos, "BottomCenter", StringComparison.OrdinalIgnoreCase))
                    {
                        // Прижат снизу к краю монитора: скругление только сверху
                        int r = h;
                        path.AddLine(w, h, 0, h);
                        path.AddLine(0, h, 0, r);
                        path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                        path.AddLine(r, 0, w - r, 0);
                        path.AddArc(w - r * 2, 0, r * 2, r * 2, 270, 90);
                        path.AddLine(w, r, w, h);
                        path.CloseFigure();
                    }
                    else
                    {
                        // Прижат справа к краю монитора (Right*): скругление только слева внутрь экрана
                        int r = w;
                        path.AddLine(w, 0, w, h);
                        path.AddLine(w, h, r, h);
                        path.AddArc(0, h - r * 2, r * 2, r * 2, 90, 90);
                        path.AddLine(0, h - r, 0, r);
                        path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                        path.AddLine(r, 0, w, 0);
                        path.CloseFigure();
                    }
                    this.Region = new Region(path);
                }
            }
            catch {}

            this.Visible = true;
            this.BringToFront();
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color markerColor = isMouseHovering ? ThemeHelper.AccentHover : ThemeHelper.Accent;
            using (var b = new SolidBrush(markerColor))
            {
                e.Graphics.FillRectangle(b, 0, 0, this.Width, this.Height);
            }
        }
    }
}
