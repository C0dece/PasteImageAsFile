using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

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
        private const int WS_MAXIMIZE = 0x01000000;

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

                Screen[] screens = Screen.AllScreens;
                foreach (var s in screens)
                {
                    if (Config.IsScreenSelected(s.DeviceName))
                    {
                        var d = new SuperHubDockForm(s);
                        d.Show();
                        activeDocks.Add(d);
                    }
                }

                // Fallback: если ни один экран не был выбран или экраны изменились
                if (activeDocks.Count == 0 && screens.Length > 0)
                {
                    var d = new SuperHubDockForm(Screen.PrimaryScreen);
                    d.Show();
                    activeDocks.Add(d);
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
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST - поверх окон
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
            this.DragOver += (s, e) => {
                if (e.Data != null && e.Data.GetDataPresent("PasteImageAsFile_InternalDrag"))
                {
                    e.Effect = DragDropEffects.None;
                    return;
                }
                e.Effect = DragDropEffects.Copy;
            };
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
            pollTimer.Interval = 75;
            pollTimer.Tick += OnPollTick;
            pollTimer.Start();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent("PasteImageAsFile_InternalDrag"))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            if (ImageDropHelper.HasDropData(e.Data))
            {
                e.Effect = DragDropEffects.Copy;
                ExpandShelf();
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent("PasteImageAsFile_InternalDrag"))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            try
            {
                ClipboardHistoryManager.Instance.AddFromDataObject(e.Data, true);
                ExpandShelf();
            }
            catch (Exception ex)
            {
                Logger.Log("SuperHubDockForm OnDragDrop error: " + ex.Message);
            }
        }

        private static readonly uint CurrentPid = (uint)Process.GetCurrentProcess().Id;
        private static readonly Dictionary<string, FullscreenCacheEntry> fullScreenCache = new Dictionary<string, FullscreenCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object fsLock = new object();

        private struct FullscreenCacheEntry
        {
            public bool IsFullscreen;
            public long ExpiryTicks;
        }

        public static bool IsFullScreenGameOrAppActive(Screen screen)
        {
            string scrKey = (screen != null) ? screen.DeviceName : "primary";
            long now = DateTime.UtcNow.Ticks;
            lock (fsLock)
            {
                FullscreenCacheEntry entry;
                if (fullScreenCache.TryGetValue(scrKey, out entry))
                {
                    if (now < entry.ExpiryTicks) return entry.IsFullscreen;
                }
            }

            bool result = CheckIsFullScreen(screen);
            lock (fsLock)
            {
                FullscreenCacheEntry newEntry;
                newEntry.IsFullscreen = result;
                newEntry.ExpiryTicks = now + TimeSpan.FromMilliseconds(500).Ticks;
                fullScreenCache[scrKey] = newEntry;
            }
            return result;
        }

        private static bool CheckIsFullScreen(Screen screen)
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
                    // Исключаем окна собственного процесса
                    uint fgPid;
                    GetWindowThreadProcessId(fg, out fgPid);
                    if (fgPid == CurrentPid) return false;

                    // Если окно свернуто (минимизировано), оно не является активной игрой
                    if (IsIconic(fg)) return false;

                    var sb = new System.Text.StringBuilder(256);
                    GetClassName(fg, sb, sb.Capacity);
                    string cls = sb.ToString();
                    if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" ||
                        cls == "Windows.UI.Core.CoreWindow" || cls == "DV2ControlHost" ||
                        cls.StartsWith("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    int style = GetWindowLong(fg, GWL_STYLE);

                    // Обычные развернутые окна приложений имеют стиль WS_MAXIMIZE (0x01000000) — они НЕ являются играми!
                    if ((style & WS_MAXIMIZE) == WS_MAXIMIZE)
                    {
                        return false;
                    }

                    RECT rc;
                    if (GetWindowRect(fg, out rc))
                    {
                        Screen scr = screen ?? Screen.PrimaryScreen;
                        // Окно покрывает весь физический экран монитора
                        if (rc.Left <= scr.Bounds.Left && rc.Top <= scr.Bounds.Top &&
                            rc.Right >= scr.Bounds.Right && rc.Bottom >= scr.Bounds.Bottom)
                        {
                            // Если у окна отсутствует заголовок WS_CAPTION и оно не WS_MAXIMIZE, это настоящая полноэкранная игра
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

            // Зона срабатывания строго у маркера: границы маркера + минимальный буфер 2px
            Rectangle hitArea = this.Bounds;
            hitArea.Inflate(2, 2);

            bool isOverUs = hitArea.Contains(cur);

            if (isOverUs)
            {
                hoverExpandCounter++;
                if (hoverExpandCounter >= 1) // Мгновенный отклик при наведении прямо на маркер
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
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
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
