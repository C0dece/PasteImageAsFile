using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PasteImageAsFile
{
    public class FluentComboBox : ComboBox
    {
        private bool isHovered = false;
        private System.Windows.Forms.Timer hoverCheckTimer;

        public FluentComboBox()
        {
            SetStyle(ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer | 
                     ControlStyles.ResizeRedraw, true);

            this.DrawMode = DrawMode.OwnerDrawFixed;
            this.DropDownStyle = ComboBoxStyle.DropDownList;
            this.FlatStyle = FlatStyle.Flat;
            this.ItemHeight = 22;
            this.Font = new Font("Segoe UI", 9f);
            this.Cursor = Cursors.Hand;

            hoverCheckTimer = new System.Windows.Forms.Timer();
            hoverCheckTimer.Interval = 50;
            hoverCheckTimer.Tick += (s, e) => CheckHoverState();
        }

        private void CheckHoverState()
        {
            if (this.IsDisposed)
            {
                if (hoverCheckTimer != null) hoverCheckTimer.Stop();
                return;
            }

            bool shouldHover = false;
            if (this.Visible && this.Enabled)
            {
                Point cursorScreen = Cursor.Position;
                Point clientPt = this.PointToClient(cursorScreen);
                shouldHover = this.ClientRectangle.Contains(clientPt);
            }

            if (shouldHover != isHovered)
            {
                isHovered = shouldHover;
                this.Invalidate();
            }

            if (!isHovered && hoverCheckTimer != null && hoverCheckTimer.Enabled)
            {
                hoverCheckTimer.Stop();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!isHovered)
            {
                isHovered = true;
                this.Invalidate();
            }
            if (hoverCheckTimer != null && !hoverCheckTimer.Enabled)
            {
                hoverCheckTimer.Start();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!isHovered)
            {
                isHovered = true;
                this.Invalidate();
            }
            if (hoverCheckTimer != null && !hoverCheckTimer.Enabled)
            {
                hoverCheckTimer.Start();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            CheckHoverState();
        }

        protected override void OnDropDown(EventArgs e)
        {
            base.OnDropDown(e);
            if (hoverCheckTimer != null) hoverCheckTimer.Stop();
            isHovered = true;
            this.Invalidate();
        }

        protected override void OnDropDownClosed(EventArgs e)
        {
            base.OnDropDownClosed(e);
            CheckHoverState();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            isHovered = false;
            if (hoverCheckTimer != null) hoverCheckTimer.Stop();
            this.Invalidate();
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            isHovered = false;
            if (hoverCheckTimer != null) hoverCheckTimer.Stop();
            this.Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!this.Enabled && isHovered)
            {
                isHovered = false;
                if (hoverCheckTimer != null) hoverCheckTimer.Stop();
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color parentBg = MainForm.GetRealParentBackColor(this, ThemeHelper.CardBackground);
            g.Clear(parentBg);

            bool isDark = ThemeHelper.IsDarkTheme();
            Color bg = isDark
                ? (isHovered ? Color.FromArgb(24, 38, 56) : Color.FromArgb(17, 28, 42))
                : (isHovered ? Color.FromArgb(245, 248, 252) : Color.FromArgb(255, 255, 255));

            Color borderColor = isHovered
                ? ThemeHelper.Accent
                : (isDark ? ThemeHelper.CardBorder : Color.FromArgb(215, 220, 228));

            Color textCol = isDark ? ThemeHelper.TextPrimary : Color.FromArgb(25, 25, 25);
            Color arrowColor = isHovered ? ThemeHelper.Accent : (isDark ? Color.FromArgb(160, 175, 195) : Color.FromArgb(120, 120, 120));

            // 1. Скругленный фон и рамка Fluent (радиус 6px)
            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (var path = MainForm.CreateRoundedPath(rect, 6))
            {
                using (var b = new SolidBrush(bg))
                {
                    g.FillPath(b, path);
                }
                using (var p = new Pen(borderColor, isHovered ? 1.5f : 1f))
                {
                    g.DrawPath(p, path);
                }
            }

            // 2. Отображаемый текст с отступом слева
            string itemText = this.SelectedItem != null ? this.SelectedItem.ToString() : this.Text;
            if (!string.IsNullOrEmpty(itemText))
            {
                Rectangle textRect = new Rectangle(10, 0, Math.Max(10, this.Width - 36), this.Height);
                using (var textBrush = new SolidBrush(textCol))
                using (var sf = new StringFormat
                {
                    LineAlignment = StringAlignment.Center,
                    Alignment = StringAlignment.Near,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                {
                    g.DrawString(itemText, this.Font, textBrush, textRect, sf);
                }
            }

            // 3. Аккуратный скругленный шеврон Windows 11
            int cx = this.Width - 16;
            int cy = this.Height / 2;
            using (var p = new Pen(arrowColor, 1.8f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                Point[] chevron = new Point[] {
                    new Point(cx - 4, cy - 2),
                    new Point(cx, cy + 2),
                    new Point(cx + 4, cy - 2)
                };
                g.DrawLines(p, chevron);
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= this.Items.Count) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            bool isDark = ThemeHelper.IsDarkTheme();
            bool isSelected = (e.State & DrawItemState.Selected) != 0;

            Color bg = isDark ? Color.FromArgb(17, 28, 42) : Color.FromArgb(255, 255, 255);
            using (var brush = new SolidBrush(bg))
            {
                g.FillRectangle(brush, e.Bounds);
            }

            if (isSelected)
            {
                Rectangle highlightRect = new Rectangle(e.Bounds.Left + 3, e.Bounds.Top + 1, e.Bounds.Width - 6, e.Bounds.Height - 2);
                Color highlightBg = isDark ? Color.FromArgb(30, 48, 72) : Color.FromArgb(232, 240, 250);
                using (var hBrush = new SolidBrush(highlightBg))
                using (var path = MainForm.CreateRoundedPath(highlightRect, 4))
                {
                    g.FillPath(hBrush, path);
                }

                // Акцентная вертикальная полоска слева
                using (var p = new Pen(ThemeHelper.Accent, 2.5f))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawLine(p, e.Bounds.Left + 6, e.Bounds.Top + 4, e.Bounds.Left + 6, e.Bounds.Bottom - 4);
                }
            }

            Color textCol = isDark
                ? (isSelected ? Color.White : ThemeHelper.TextPrimary)
                : (isSelected ? Color.Black : Color.FromArgb(25, 25, 25));

            string itemText = this.Items[e.Index].ToString();
            Rectangle textRect = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top, e.Bounds.Width - 18, e.Bounds.Height);
            using (var textBrush = new SolidBrush(textCol))
            using (var sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Alignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                g.DrawString(itemText, this.Font, textBrush, textRect, sf);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (hoverCheckTimer != null)
                {
                    hoverCheckTimer.Stop();
                    hoverCheckTimer.Dispose();
                    hoverCheckTimer = null;
                }
            }
            base.Dispose(disposing);
        }
    }

    public class FluentTextBox : Panel
    {
        private TextBox innerBox;
        private bool isHovered = false;
        private bool isFocused = false;

        public TextBox InnerTextBox
        {
            get { return innerBox; }
        }

        public override string Text
        {
            get { return innerBox != null ? innerBox.Text : base.Text; }
            set { if (innerBox != null) innerBox.Text = value; else base.Text = value; }
        }

        public new event EventHandler TextChanged
        {
            add { innerBox.TextChanged += value; }
            remove { innerBox.TextChanged -= value; }
        }

        public FluentTextBox()
        {
            SetStyle(ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer | 
                     ControlStyles.ResizeRedraw, true);

            this.Cursor = Cursors.IBeam;
            this.Font = new Font("Segoe UI", 9.5f);
            this.Size = new Size(230, 28);

            innerBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(10, 5),
                Width = Math.Max(20, this.Width - 20)
            };

            innerBox.MouseEnter += (s, e) => { isHovered = true; this.Invalidate(); };
            innerBox.MouseLeave += (s, e) => { isHovered = false; this.Invalidate(); };
            innerBox.GotFocus += (s, e) => { isFocused = true; this.Invalidate(); };
            innerBox.LostFocus += (s, e) => { isFocused = false; this.Invalidate(); };

            this.MouseEnter += (s, e) => { isHovered = true; this.Invalidate(); };
            this.MouseLeave += (s, e) => { isHovered = false; this.Invalidate(); };
            this.Click += (s, e) => { innerBox.Focus(); };
            this.Resize += (s, e) => {
                if (innerBox != null)
                {
                    innerBox.Location = new Point(10, Math.Max(2, (this.Height - innerBox.Height) / 2));
                    innerBox.Width = Math.Max(20, this.Width - 20);
                }
            };

            this.Controls.Add(innerBox);
        }

        public void ApplyThemeColors()
        {
            bool isDark = ThemeHelper.IsDarkTheme();
            Color bg = isDark
                ? (isHovered || isFocused ? Color.FromArgb(24, 38, 56) : Color.FromArgb(17, 28, 42))
                : (isHovered || isFocused ? Color.FromArgb(245, 248, 252) : Color.FromArgb(255, 255, 255));
            Color textCol = isDark ? ThemeHelper.TextPrimary : Color.FromArgb(25, 25, 25);

            innerBox.BackColor = bg;
            innerBox.ForeColor = textCol;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentBg = MainForm.GetRealParentBackColor(this, ThemeHelper.CardBackground);
            g.Clear(parentBg);

            bool isDark = ThemeHelper.IsDarkTheme();
            Color bg = isDark
                ? (isHovered || isFocused ? Color.FromArgb(24, 38, 56) : Color.FromArgb(17, 28, 42))
                : (isHovered || isFocused ? Color.FromArgb(245, 248, 252) : Color.FromArgb(255, 255, 255));

            Color borderColor = (isFocused || isHovered)
                ? ThemeHelper.Accent
                : (isDark ? ThemeHelper.CardBorder : Color.FromArgb(215, 220, 228));

            innerBox.BackColor = bg;
            innerBox.ForeColor = isDark ? ThemeHelper.TextPrimary : Color.FromArgb(25, 25, 25);

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (var path = MainForm.CreateRoundedPath(rect, 6))
            {
                using (var b = new SolidBrush(bg))
                {
                    g.FillPath(b, path);
                }
                using (var pen = new Pen(borderColor, (isFocused || isHovered) ? 1.5f : 1f))
                {
                    g.DrawPath(pen, path);
                }
            }
        }
    }

    public class FluentButton : Button
    {
        private bool isHovered = false;
        private bool isPressed = false;
        public bool IsAccent { get; set; }
        public bool IsSuccess { get; set; }

        public FluentButton()
        {
            SetStyle(ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer | 
                     ControlStyles.ResizeRedraw, true);
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.Cursor = Cursors.Hand;
            this.Font = new Font("Segoe UI", 9f);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                isPressed = true;
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            isPressed = false;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color parentBg = MainForm.GetRealParentBackColor(this, ThemeHelper.Background);
            g.Clear(parentBg);

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color bg;
            Color border;
            Color fg;

            bool isDark = ThemeHelper.IsDarkTheme();

            if (IsSuccess)
            {
                bg = isDark ? Color.FromArgb(20, 48, 30) : Color.FromArgb(235, 248, 238);
                border = isDark ? Color.FromArgb(38, 96, 52) : Color.FromArgb(140, 205, 160);
                fg = isDark ? Color.FromArgb(92, 225, 135) : Color.FromArgb(24, 120, 52);
            }
            else if (IsAccent)
            {
                bg = isPressed ? ThemeHelper.Accent : (isHovered ? ThemeHelper.AccentHover : ThemeHelper.Accent);
                border = bg;
                fg = Color.White;
            }
            else
            {
                bg = isPressed ? ThemeHelper.ButtonActive : (isHovered ? ThemeHelper.ButtonHover : ThemeHelper.CardBackground);
                border = isHovered ? ThemeHelper.CardBorderHover : ThemeHelper.CardBorder;
                fg = ThemeHelper.TextPrimary;
            }

            using (var path = MainForm.CreateRoundedPath(rect, 6))
            {
                using (var b = new SolidBrush(bg))
                {
                    g.FillPath(b, path);
                }
                using (var p = new Pen(border, 1))
                {
                    g.DrawPath(p, path);
                }
            }

            TextRenderer.DrawText(g, this.Text, this.Font, rect, fg, 
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    public class MainForm : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                cp.ExStyle |= 0x02000000;    // WS_EX_COMPOSITED
                return cp;
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

        private static void DrawMinimizeIcon(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cy = r.Y + r.Height / 2;
            using (var pen = new Pen(color, 1.3f))
            {
                g.DrawLine(pen, r.X + 8, cy, r.Right - 8, cy);
            }
        }

        private static void DrawCloseIcon(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.4f))
            {
                g.DrawLine(pen, r.X + 8, r.Y + 8, r.Right - 8, r.Bottom - 8);
                g.DrawLine(pen, r.Right - 8, r.Y + 8, r.X + 8, r.Bottom - 8);
            }
        }

        public static Color GetRealParentBackColor(Control c, Color defaultColor)
        {
            if (c == null) return defaultColor;
            Control p = c.Parent;
            while (p != null)
            {
                if (p.BackColor != Color.Transparent && p.BackColor.A == 255)
                {
                    return p.BackColor;
                }
                p = p.Parent;
            }
            return defaultColor;
        }

        private bool isInitializingConfig = false;

        private Panel pnlHeader;
        private Label lblAppTitle;
        private Label lblVersion;
        private Button btnHeaderMinimize;
        private Button btnHeaderClose;

        // Карточка статуса
        private Panel pnlStatusCard;
        private Panel pnlStatusDot;
        private Label lblStatus;
        private Label lblStatusSub;
        private FluentButton btnToggleDaemon;

        // Карточка интеграции
        private Panel pnlCardInstall;
        private Label lblInstallTitle;
        private Label lblInstallSub;
        private FluentButton btnInstall;
        private FluentButton btnUninstall;

        // Fluent навигация по вкладкам
        private Panel pnlTabNav;
        private readonly List<Button> navTabButtons = new List<Button>();
        private int currentTabIndex = 0;
        private readonly string[] tabTitles = new string[] { "Основные", "Буфер обмена", "SuperHub" };

        // Карточка настроек (контейнер страниц)
        private Panel pnlSettingsCard;
        private Panel pnlPageGeneral;
        private Panel pnlPageClipboard;
        private Panel pnlPageSuperHub;

        // Элементы страницы "Основные"
        private FluentComboBox cmbTheme;
        private FluentComboBox cmbAccentColor;
        private Panel pnlAccentPreview;
        private CheckBox chkAutoRun;
        private CheckBox chkContextMenu;
        private CheckBox chkClassicContextMenuWin11;
        private CheckBox chkPlaceUnderCursor;
        private CheckBox chkExtractOriginalName;
        private Label lblPrefix;
        private FluentTextBox txtPrefix;

        // Элементы страницы "Буфер обмена"
        private FluentComboBox cmbClipboardClickAction;
        private CheckBox chkTrayClickOpensClipboard;
        private CheckBox chkShowHistoryMenu;
        private CheckBox chkHistoryRememberText;
        private CheckBox chkHistoryRememberFiles;

        // Элементы страницы "SuperHub"
        private CheckBox chkSuperHubEnabled;
        private CheckBox chkSuperHubFoldersEnabled;
        private Panel pnlScreensBox;
        private readonly List<CheckBox> chkScreenList = new List<CheckBox>();
        private FluentComboBox cmbSuperHubViewMode;
        private FluentComboBox cmbSuperHubSize;
        private FluentComboBox cmbSuperHubPosition;
        private FluentComboBox cmbSuperHubDragMode;
        private FluentComboBox cmbSuperHubSens;
        private FluentButton btnTestSuperHub;

        // Подвал формы
        private Panel pnlFooter;
        private FluentButton btnOpenCache;
        private FluentButton btnHideToTray;

        private System.Windows.Forms.Timer statusTimer;
        private ToolTip toolTip;

        public MainForm()
        {
            toolTip = ThemeHelper.CreateFluentToolTip();

            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape)
                {
                    this.Hide();
                }
            };

            InitializeComponent();
            LoadConfigToUi();
            UpdateStatus();

            ThemeHelper.ThemeChanged += () => {
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke((Action)(() => ApplyTheme()));
                }
            };
            ApplyTheme();

            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = 2000;
            statusTimer.Tick += (s, e) => UpdateStatus();
            statusTimer.Start();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.Text = "PasteImageAsFile - Настройки";
            this.Size = new Size(514, 694);
            this.MinimumSize = new Size(514, 694);
            this.MaximumSize = new Size(514, 694);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.MaximizeBox = false;
            this.Font = new Font("Segoe UI", 9f);
            this.DoubleBuffered = true;

            // Иконка приложения
            try
            {
                string icoPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "app.ico");
                if (File.Exists(icoPath))
                {
                    this.Icon = new Icon(icoPath);
                }
                else
                {
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch {}

            // 1. Верхний заголовок (Header) - выполнен как UI рамка с перетаскиванием окна
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = ThemeHelper.HeaderBackground
            };

            pnlHeader.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };

            pnlHeader.Paint += (s, e) => {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Векторная плашка логотипа 36x36 слева
                Rectangle iconRect = new Rectangle(18, 14, 36, 36);
                using (var iconBgBrush = new SolidBrush(ThemeHelper.IsDarkTheme() ? Color.FromArgb(18, 38, 58) : Color.FromArgb(220, 235, 252)))
                using (var iconPath = CreateRoundedPath(iconRect, 8))
                {
                    g.FillPath(iconBgBrush, iconPath);
                    using (var p = new Pen(ThemeHelper.Accent, 1.2f))
                    {
                        g.DrawPath(p, iconPath);
                    }
                }

                // Иконка картинки внутри плашки
                int fx = iconRect.X + 8;
                int fy = iconRect.Y + 8;
                using (var p = new Pen(ThemeHelper.Accent, 1.6f))
                {
                    g.DrawRectangle(p, fx, fy, 19, 19);
                    using (var dotBrush = new SolidBrush(ThemeHelper.Accent))
                    {
                        g.FillEllipse(dotBrush, fx + 4, fy + 4, 3, 3);
                    }
                    Point[] pts = new Point[] {
                        new Point(fx + 2, fy + 15),
                        new Point(fx + 7, fy + 10),
                        new Point(fx + 11, fy + 13),
                        new Point(fx + 15, fy + 7),
                        new Point(fx + 17, fy + 15)
                    };
                    g.DrawLines(p, pts);
                }

                // Тонкая разделительная черта снизу шапки
                using (var sepPen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    g.DrawLine(sepPen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
                }
            };

            lblAppTitle = new Label
            {
                Text = "PasteImageAsFile",
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                ForeColor = ThemeHelper.TextPrimary,
                AutoSize = true,
                Location = new Point(66, 12),
                Cursor = Cursors.Default
            };
            lblAppTitle.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };

            lblVersion = new Label
            {
                Text = "v" + ShellIntegration.AppVersion + " • Настройки утилиты",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = ThemeHelper.TextSecondary,
                AutoSize = true,
                Location = new Point(66, 37),
                Cursor = Cursors.Default
            };
            lblVersion.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };

            pnlHeader.Controls.Add(lblAppTitle);
            pnlHeader.Controls.Add(lblVersion);

            // Кнопка свернуть ─ в стиле Fluent UI
            btnHeaderMinimize = new Button
            {
                Location = new Point(pnlHeader.Width - 72, 12),
                Size = new Size(30, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnHeaderMinimize.FlatAppearance.BorderSize = 0;
            btnHeaderMinimize.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btnHeaderMinimize.FlatAppearance.MouseDownBackColor = Color.Transparent;
            bool isMinHover = false;
            btnHeaderMinimize.MouseEnter += (s, e) => { isMinHover = true; btnHeaderMinimize.Invalidate(); };
            btnHeaderMinimize.MouseLeave += (s, e) => { isMinHover = false; btnHeaderMinimize.Invalidate(); };
            btnHeaderMinimize.Paint += (s, e) => {
                Color parentBg = GetRealParentBackColor(btnHeaderMinimize, ThemeHelper.HeaderBackground);
                e.Graphics.Clear(parentBg);
                if (isMinHover)
                {
                    using (var b = new SolidBrush(ThemeHelper.ButtonHover))
                    using (var p = CreateRoundedPath(new Rectangle(0, 0, btnHeaderMinimize.Width - 1, btnHeaderMinimize.Height - 1), 4))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.FillPath(b, p);
                    }
                }
                DrawMinimizeIcon(e.Graphics, new Rectangle(0, 0, btnHeaderMinimize.Width, btnHeaderMinimize.Height), isMinHover ? ThemeHelper.TextPrimary : ThemeHelper.TextSecondary);
            };
            btnHeaderMinimize.Click += (s, e) => { this.WindowState = FormWindowState.Minimized; };
            toolTip.SetToolTip(btnHeaderMinimize, "Свернуть");
            pnlHeader.Controls.Add(btnHeaderMinimize);

            // Кнопка закрытия ✕ в стиле Fluent UI (скрывает окно в трей)
            btnHeaderClose = new Button
            {
                Location = new Point(pnlHeader.Width - 38, 12),
                Size = new Size(30, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnHeaderClose.FlatAppearance.BorderSize = 0;
            btnHeaderClose.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btnHeaderClose.FlatAppearance.MouseDownBackColor = Color.Transparent;
            bool isCloseHover = false;
            btnHeaderClose.MouseEnter += (s, e) => { isCloseHover = true; btnHeaderClose.Invalidate(); };
            btnHeaderClose.MouseLeave += (s, e) => { isCloseHover = false; btnHeaderClose.Invalidate(); };
            btnHeaderClose.Paint += (s, e) => {
                Color parentBg = GetRealParentBackColor(btnHeaderClose, ThemeHelper.HeaderBackground);
                e.Graphics.Clear(parentBg);
                if (isCloseHover)
                {
                    using (var b = new SolidBrush(Color.FromArgb(196, 43, 28)))
                    using (var p = CreateRoundedPath(new Rectangle(0, 0, btnHeaderClose.Width - 1, btnHeaderClose.Height - 1), 4))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.FillPath(b, p);
                    }
                }
                DrawCloseIcon(e.Graphics, new Rectangle(0, 0, btnHeaderClose.Width, btnHeaderClose.Height), isCloseHover ? Color.White : ThemeHelper.TextSecondary);
            };
            btnHeaderClose.Click += (s, e) => { this.Hide(); };
            toolTip.SetToolTip(btnHeaderClose, "Закрыть в трей (Esc)");
            pnlHeader.Controls.Add(btnHeaderClose);

            // 2. Карточка статуса службы (70px)
            pnlStatusCard = CreateCard(16, 72, 482, 70);

            pnlStatusDot = new Panel
            {
                Size = new Size(12, 12),
                Location = new Point(16, 17),
                BackColor = Color.FromArgb(46, 160, 67)
            };
            MakeRound(pnlStatusDot);

            lblStatus = new Label
            {
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(36, 13)
            };

            lblStatusSub = new Label
            {
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(150, 150, 150),
                AutoSize = false,
                Size = new Size(310, 32),
                Location = new Point(36, 36)
            };

            btnToggleDaemon = CreateButton("Остановить", 364, 17, 102, 34);
            btnToggleDaemon.Click += BtnToggleDaemon_Click;

            pnlStatusCard.Controls.Add(pnlStatusDot);
            pnlStatusCard.Controls.Add(lblStatus);
            pnlStatusCard.Controls.Add(lblStatusSub);
            pnlStatusCard.Controls.Add(btnToggleDaemon);

            // 3. Карточка системной интеграции (70px)
            pnlCardInstall = CreateCard(16, 150, 482, 70);

            lblInstallTitle = new Label
            {
                Text = "Системная интеграция",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 12)
            };

            lblInstallSub = new Label
            {
                Text = "Автозапуск и меню Проводника",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(140, 140, 140),
                AutoSize = true,
                Location = new Point(16, 34)
            };

            btnInstall = CreateButton("Установить", 246, 17, 112, 34, true);
            btnInstall.Click += BtnInstall_Click;

            btnUninstall = CreateButton("Удалить", 366, 17, 100, 34);
            btnUninstall.Click += BtnUninstall_Click;

            pnlCardInstall.Controls.Add(lblInstallTitle);
            pnlCardInstall.Controls.Add(lblInstallSub);
            pnlCardInstall.Controls.Add(btnInstall);
            pnlCardInstall.Controls.Add(btnUninstall);

            // 4. Панель вкладок Fluent (без белых рамок WinForms TabControl)
            pnlTabNav = new Panel
            {
                Location = new Point(16, 228),
                Size = new Size(482, 32),
                BackColor = ThemeHelper.Background
            };

            int tabX = 0;
            for (int i = 0; i < tabTitles.Length; i++)
            {
                int idx = i;
                Button btnTab = new Button
                {
                    Text = tabTitles[i],
                    Location = new Point(tabX, 0),
                    Size = new Size(idx == 1 ? 120 : (idx == 2 ? 100 : 96), 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = (i == currentTabIndex) ? Color.White : Color.FromArgb(160, 160, 160),
                    Font = new Font("Segoe UI", 9f, (i == currentTabIndex) ? FontStyle.Bold : FontStyle.Regular),
                    Cursor = Cursors.Hand,
                    TabStop = false
                };
                btnTab.FlatAppearance.BorderSize = 0;
                btnTab.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
                btnTab.FlatAppearance.MouseDownBackColor = ThemeHelper.CardBackground;
                btnTab.MouseEnter += (s, e) => {
                    btnTab.ForeColor = ThemeHelper.TextPrimary;
                };
                btnTab.MouseLeave += (s, e) => {
                    btnTab.ForeColor = (idx == currentTabIndex) ? ThemeHelper.TextPrimary : ThemeHelper.TextSecondary;
                };
                btnTab.Click += (s, e) => SwitchTab(idx);

                navTabButtons.Add(btnTab);
                pnlTabNav.Controls.Add(btnTab);
                tabX += btnTab.Width + 6;
            }

            pnlTabNav.Paint += (s, e) => {
                if (currentTabIndex >= 0 && currentTabIndex < navTabButtons.Count)
                {
                    Button act = navTabButtons[currentTabIndex];
                    int barW = act.Width - 16;
                    int barX = act.Left + 8;
                    using (var b = new SolidBrush(ThemeHelper.Accent))
                    using (var path = CreateRoundedPath(new Rectangle(barX, pnlTabNav.Height - 3, barW, 3), 1))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.FillPath(b, path);
                    }
                }
            };

            // 5. Единая карточка настроек (Settings Card Container) - 356px, просторная, с красивыми отступами
            pnlSettingsCard = CreateCard(16, 266, 482, 356);

            // Страница 1: Основные
            pnlPageGeneral = new Panel
            {
                Location = new Point(4, 4),
                Size = new Size(pnlSettingsCard.Width - 8, pnlSettingsCard.Height - 8),
                BackColor = ThemeHelper.CardBackground
            };
            BuildGeneralPage();
            pnlSettingsCard.Controls.Add(pnlPageGeneral);

            // Страница 2: Буфер обмена
            pnlPageClipboard = new Panel
            {
                Location = new Point(4, 4),
                Size = new Size(pnlSettingsCard.Width - 8, pnlSettingsCard.Height - 8),
                BackColor = ThemeHelper.CardBackground,
                Visible = false
            };
            BuildClipboardPage();
            pnlSettingsCard.Controls.Add(pnlPageClipboard);

            // Страница 3: SuperHub
            pnlPageSuperHub = new Panel
            {
                Location = new Point(4, 4),
                Size = new Size(pnlSettingsCard.Width - 8, pnlSettingsCard.Height - 8),
                BackColor = ThemeHelper.CardBackground,
                Visible = false
            };
            BuildSuperHubPage();
            pnlSettingsCard.Controls.Add(pnlPageSuperHub);

            // 6. Подвал (Footer)
            pnlFooter = new Panel
            {
                Location = new Point(16, 634),
                Size = new Size(482, 42),
                BackColor = ThemeHelper.Background
            };

            btnOpenCache = CreateButton("Папка кэша", 0, 4, 130, 34);
            btnOpenCache.Click += (s, e) => {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShellIntegration.AppName, "Cache");
                Directory.CreateDirectory(cache);
                Process.Start("explorer.exe", cache);
            };
            toolTip.SetToolTip(btnOpenCache, "Открыть папку с сохраненными снимками");

            btnHideToTray = CreateButton("Свернуть в трей", 352, 4, 130, 34);
            btnHideToTray.Click += (s, e) => { this.Hide(); };
            toolTip.SetToolTip(btnHideToTray, "Спрятать окно настроек в системный трей");

            pnlFooter.Controls.Add(btnOpenCache);
            pnlFooter.Controls.Add(btnHideToTray);

            // Добавляем все основные компоненты
            this.Controls.Add(pnlHeader);
            this.Controls.Add(pnlStatusCard);
            this.Controls.Add(pnlCardInstall);
            this.Controls.Add(pnlTabNav);
            this.Controls.Add(pnlSettingsCard);
            this.Controls.Add(pnlFooter);

            this.ResumeLayout(false);
        }

        public void SwitchTab(int index)
        {
            currentTabIndex = index;
            for (int i = 0; i < navTabButtons.Count; i++)
            {
                bool active = (i == currentTabIndex);
                navTabButtons[i].Font = new Font("Segoe UI", 9f, active ? FontStyle.Bold : FontStyle.Regular);
                navTabButtons[i].ForeColor = active ? ThemeHelper.TextPrimary : ThemeHelper.TextSecondary;
            }
            pnlTabNav.Invalidate();

            pnlPageGeneral.Visible = (index == 0);
            pnlPageClipboard.Visible = (index == 1);
            pnlPageSuperHub.Visible = (index == 2);
        }

        private void BuildGeneralPage()
        {
            int top = 10;
            int step = 26;

            // Тема оформления
            Label lblTheme = new Label
            {
                Text = "Тема интерфейса:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 4),
                AutoSize = true
            };
            cmbTheme = CreateStyledComboBox(220, top, 230);
            cmbTheme.Items.AddRange(new object[] { "Системная (Windows)", "Темная тема", "Светлая тема" });
            cmbTheme.SelectedIndexChanged += (s, e) => {
                if (isInitializingConfig) return;
                if (cmbTheme.SelectedIndex == 1) Config.ThemeMode = "Dark";
                else if (cmbTheme.SelectedIndex == 2) Config.ThemeMode = "Light";
                else Config.ThemeMode = "System";
                ApplyTheme();
            };
            toolTip.SetToolTip(cmbTheme, "Выбор темы оформления (адаптируется к Windows 11)");
            pnlPageGeneral.Controls.Add(lblTheme);
            pnlPageGeneral.Controls.Add(cmbTheme);

            top += 34;

            // Акцентный цвет
            Label lblAccent = new Label
            {
                Text = "Акцентный цвет:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 4),
                AutoSize = true
            };
            cmbAccentColor = CreateStyledComboBox(220, top, 192);
            cmbAccentColor.Items.AddRange(new object[] {
                "Системный (Windows)",
                "Синий (Windows Blue)",
                "Бирюзовый (Teal)",
                "Фиолетовый (Purple)",
                "Изумрудный (Green)",
                "Коралловый (Coral)",
                "Оранжевый (Orange)",
                "Свой цвет..."
            });

            pnlAccentPreview = new Panel
            {
                Location = new Point(418, top),
                Size = new Size(32, 28),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            pnlAccentPreview.Paint += (s, e) => {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color parentBg = GetRealParentBackColor(pnlAccentPreview, ThemeHelper.CardBackground);
                g.Clear(parentBg);

                Rectangle rect = new Rectangle(0, 0, pnlAccentPreview.Width - 1, pnlAccentPreview.Height - 1);
                using (var path = CreateRoundedPath(rect, 6))
                {
                    using (var b = new SolidBrush(ThemeHelper.Accent))
                    {
                        g.FillPath(b, path);
                    }
                    using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            };
            toolTip.SetToolTip(pnlAccentPreview, "Кликните, чтобы выбрать произвольный акцентный цвет");

            Action pickCustomColor = () => {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.Color = ThemeHelper.Accent;
                    cd.FullOpen = true;
                    if (cd.ShowDialog() == DialogResult.OK)
                    {
                        string hex = string.Format("#{0:X2}{1:X2}{2:X2}", cd.Color.R, cd.Color.G, cd.Color.B);
                        Config.AccentColor = hex;
                        pnlAccentPreview.BackColor = cd.Color;
                        pnlAccentPreview.Invalidate();
                        ThemeHelper.NotifyThemeChanged();
                        if (cmbAccentColor != null) cmbAccentColor.SelectedIndex = 7;
                    }
                }
            };
            pnlAccentPreview.Click += (s, e) => pickCustomColor();

            cmbAccentColor.SelectedIndexChanged += (s, e) => {
                if (isInitializingConfig) return;
                string chosenHex = "";
                switch (cmbAccentColor.SelectedIndex)
                {
                    case 0: chosenHex = ""; break;
                    case 1: chosenHex = "#0078D4"; break;
                    case 2: chosenHex = "#008272"; break;
                    case 3: chosenHex = "#8764B8"; break;
                    case 4: chosenHex = "#107C41"; break;
                    case 5: chosenHex = "#D13438"; break;
                    case 6: chosenHex = "#CA5010"; break;
                    case 7: pickCustomColor(); return;
                }
                Config.AccentColor = chosenHex;
                pnlAccentPreview.BackColor = ThemeHelper.Accent;
                pnlAccentPreview.Invalidate();
                ThemeHelper.NotifyThemeChanged();
            };
            toolTip.SetToolTip(cmbAccentColor, "Акцентный цвет маркеров SuperHub, кнопок и активных элементов");
            pnlPageGeneral.Controls.Add(lblAccent);
            pnlPageGeneral.Controls.Add(cmbAccentColor);
            pnlPageGeneral.Controls.Add(pnlAccentPreview);

            top += 34;

            chkAutoRun = CreateCheckBox("Автозапуск вместе со стартом Windows", 14, top);
            chkAutoRun.CheckedChanged += (s, e) => {
                if (isInitializingConfig) return;
                Config.AutoRun = chkAutoRun.Checked;
                ShellIntegration.SetAutoRun(chkAutoRun.Checked);
            };
            toolTip.SetToolTip(chkAutoRun, "Автоматически запускать службу при входе в систему");
            pnlPageGeneral.Controls.Add(chkAutoRun);
            top += step;

            chkContextMenu = CreateCheckBox("Контекстное меню в Проводнике", 14, top);
            chkContextMenu.CheckedChanged += (s, e) => {
                Config.ContextMenu = chkContextMenu.Checked;
                chkShowHistoryMenu.Enabled = chkContextMenu.Checked;
                ShellIntegration.SetContextMenu(chkContextMenu.Checked);
            };
            toolTip.SetToolTip(chkContextMenu, "Добавить пункт 'Вставить изображение из буфера' в Проводник");
            pnlPageGeneral.Controls.Add(chkContextMenu);
            top += step;

            chkClassicContextMenuWin11 = CreateCheckBox("Классическое контекстное меню Windows 11", 14, top);
            chkClassicContextMenuWin11.CheckedChanged += (s, e) => {
                Config.ClassicContextMenuWin11 = chkClassicContextMenuWin11.Checked;
                ShellIntegration.SetClassicContextMenuWin11(chkClassicContextMenuWin11.Checked);
            };
            toolTip.SetToolTip(chkClassicContextMenuWin11, "Открывать сразу полное классическое меню без пункта 'Показать дополнительные параметры'");
            pnlPageGeneral.Controls.Add(chkClassicContextMenuWin11);
            top += step;

            chkPlaceUnderCursor = CreateCheckBox("Размещать файл под курсором на Рабочем столе", 14, top);
            chkPlaceUnderCursor.CheckedChanged += (s, e) => { Config.PlaceUnderCursor = chkPlaceUnderCursor.Checked; };
            toolTip.SetToolTip(chkPlaceUnderCursor, "При нажатии Ctrl+V на Рабочем столе значок созданного файла помещается точно под стрелку мыши");
            pnlPageGeneral.Controls.Add(chkPlaceUnderCursor);
            top += step;

            chkExtractOriginalName = CreateCheckBox("Извлекать имя картинки везде где возможно", 14, top);
            chkExtractOriginalName.CheckedChanged += (s, e) => { Config.ExtractOriginalName = chkExtractOriginalName.Checked; };
            toolTip.SetToolTip(chkExtractOriginalName, "Автоматически находить понятное имя файла из HTML-разметки или окна источника");
            pnlPageGeneral.Controls.Add(chkExtractOriginalName);
            top += step + 4;

            lblPrefix = new Label
            {
                Text = "Префикс файлов по умолчанию:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 4),
                AutoSize = true
            };
            txtPrefix = CreateStyledTextBox(220, top, 230);
            txtPrefix.TextChanged += (s, e) => { Config.DefaultPrefix = txtPrefix.Text.Trim(); };
            toolTip.SetToolTip(txtPrefix, "Базовое имя файла, если оригинальное имя не удалось распознать (например, Снимок)");
            toolTip.SetToolTip(txtPrefix.InnerTextBox, "Базовое имя файла, если оригинальное имя не удалось распознать (например, Снимок)");
            pnlPageGeneral.Controls.Add(lblPrefix);
            pnlPageGeneral.Controls.Add(txtPrefix);
        }

        private void BuildClipboardPage()
        {
            int top = 10;
            int step = 28;

            Label lblAction = new Label
            {
                Text = "Действие по клику на карточку:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 4),
                AutoSize = true
            };
            cmbClipboardClickAction = CreateStyledComboBox(220, top, 230);
            cmbClipboardClickAction.Items.AddRange(new object[] { "Вставить сразу [Ctrl+V]", "Только скопировать в буфер" });
            cmbClipboardClickAction.SelectedIndexChanged += (s, e) => {
                if (isInitializingConfig) return;
                Config.ClipboardClickAction = (cmbClipboardClickAction.SelectedIndex == 1) ? "Copy" : "Paste";
            };
            toolTip.SetToolTip(cmbClipboardClickAction, "Что делать при одиночном клике по карточке в журнале буфера обмена");
            pnlPageClipboard.Controls.Add(lblAction);
            pnlPageClipboard.Controls.Add(cmbClipboardClickAction);

            top += 36;

            chkTrayClickOpensClipboard = CreateCheckBox("Клик по значку в трее открывает буфер обмена", 14, top);
            chkTrayClickOpensClipboard.CheckedChanged += (s, e) => {
                if (isInitializingConfig) return;
                Config.TrayClickOpensClipboard = chkTrayClickOpensClipboard.Checked;
            };
            toolTip.SetToolTip(chkTrayClickOpensClipboard, "При нажатии левой кнопкой на иконку у часов открывать всплывающий буфер вместо окна настроек");
            pnlPageClipboard.Controls.Add(chkTrayClickOpensClipboard);
            top += step;

            chkShowHistoryMenu = CreateCheckBox("Пункт \"Буфер обмена\" в контекстном меню", 14, top);
            chkShowHistoryMenu.CheckedChanged += (s, e) => {
                if (isInitializingConfig) return;
                Config.ShowHistoryMenu = chkShowHistoryMenu.Checked;
                ShellIntegration.SetHistoryMenuItem(chkShowHistoryMenu.Checked);
            };
            toolTip.SetToolTip(chkShowHistoryMenu, "Показывать вызов журнала буфера в контекстном меню Проводника");
            pnlPageClipboard.Controls.Add(chkShowHistoryMenu);
            top += step;

            chkHistoryRememberText = CreateCheckBox("Запоминать скопированный текст", 14, top);
            chkHistoryRememberText.CheckedChanged += (s, e) => {
                if (isInitializingConfig) return;
                Config.HistoryRememberText = chkHistoryRememberText.Checked;
            };
            toolTip.SetToolTip(chkHistoryRememberText, "Вести историю скопированных фрагментов текста с возможностью быстрого поиска");
            pnlPageClipboard.Controls.Add(chkHistoryRememberText);
            top += step;

            chkHistoryRememberFiles = CreateCheckBox("Запоминать скопированные файлы и папки", 14, top);
            chkHistoryRememberFiles.CheckedChanged += (s, e) => {
                if (isInitializingConfig) return;
                Config.HistoryRememberFiles = chkHistoryRememberFiles.Checked;
            };
            toolTip.SetToolTip(chkHistoryRememberFiles, "Вести историю скопированных путей к файлам с отображением системных иконок");
            pnlPageClipboard.Controls.Add(chkHistoryRememberFiles);
            top += step + 6;

            Panel pnlClipboardHelp = new Panel
            {
                Location = new Point(14, top),
                Size = new Size(446, 88),
                BackColor = Color.Transparent
            };
            pnlClipboardHelp.Paint += (s, e) => {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, pnlClipboardHelp.Width - 1, pnlClipboardHelp.Height - 1);
                using (var path = CreateRoundedPath(rect, 6))
                {
                    using (var b = new SolidBrush(ThemeHelper.IsDarkTheme() ? Color.FromArgb(16, 26, 38) : Color.FromArgb(242, 245, 249)))
                    {
                        g.FillPath(b, path);
                    }
                    using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            };

            Label lblHint = new Label
            {
                Text = "Советы по использованию:\n" +
                       "• Двойной клик по карточке запускает файл или ссылку.\n" +
                       "• Перетаскивание карточки наружу копирует файл в нужную папку.\n" +
                       "• Кнопка в шапке меняет режим: [Вставлять] или [Копировать].",
                Location = new Point(10, 8),
                Size = new Size(426, 72),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = ThemeHelper.TextSecondary
            };
            pnlClipboardHelp.Controls.Add(lblHint);
            pnlPageClipboard.Controls.Add(pnlClipboardHelp);
        }

        private void BuildSuperHubPage()
        {
            int top = 6;
            int step = 31;

            chkSuperHubEnabled = CreateCheckBox("Включить плавающую полку SuperHub у края экрана", 14, top);
            chkSuperHubEnabled.CheckedChanged += (s, e) => {
                if (isInitializingConfig) return;
                Config.SuperHubEnabled = chkSuperHubEnabled.Checked;
                SuperHubDockForm.SyncAllDocks();
            };
            toolTip.SetToolTip(chkSuperHubEnabled, "Выдвижная полка для временного накопления и переноса файлов между программами");
            pnlPageSuperHub.Controls.Add(chkSuperHubEnabled);
            top += 24;

            // 0. Экраны (Мульти-выбор мониторов)
            Label lblScreens = new Label
            {
                Text = "Отображать полку на экранах (отметьте нужные мониторы):",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 3),
                AutoSize = true
            };
            pnlPageSuperHub.Controls.Add(lblScreens);
            top += 21;

            pnlScreensBox = new Panel
            {
                Location = new Point(14, top),
                Size = new Size(446, 52),
                BackColor = ThemeHelper.IsDarkTheme() ? Color.FromArgb(18, 28, 40) : Color.FromArgb(242, 245, 250),
                AutoScroll = true
            };
            pnlScreensBox.Paint += (s, e) => {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, pnlScreensBox.Width - 1, pnlScreensBox.Height - 1);
                using (var path = CreateRoundedPath(rect, 6))
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    g.DrawPath(pen, path);
                }
            };
            pnlPageSuperHub.Controls.Add(pnlScreensBox);

            Screen[] screens = Screen.AllScreens;
            int scrY = 4;
            chkScreenList.Clear();
            for (int i = 0; i < screens.Length; i++)
            {
                Screen scr = screens[i];
                string dev = scr.DeviceName;
                string title = string.Format("Экран {0}: {1}x{2}{3}", i + 1, scr.Bounds.Width, scr.Bounds.Height, scr.Primary ? " (Основной)" : "");
                CheckBox chkScr = CreateCheckBox(title, 10, scrY);
                chkScr.Tag = dev;
                chkScr.Checked = Config.IsScreenSelected(dev);
                chkScr.CheckedChanged += (s, e) => {
                    if (isInitializingConfig) return;
                    SaveSelectedScreensFromUi();
                    SuperHubDockForm.SyncAllDocks();
                };
                pnlScreensBox.Controls.Add(chkScr);
                chkScreenList.Add(chkScr);
                scrY += 22;
            }
            if (scrY > 52) pnlScreensBox.Height = Math.Min(74, scrY + 4);
            top += pnlScreensBox.Height + 7;

            // Чекбокс включения папок (вкладок) в SuperHub
            chkSuperHubFoldersEnabled = CreateCheckBox("Включить папки (вкладки браузера) в SuperHub", 14, top);
            chkSuperHubFoldersEnabled.CheckedChanged += (s, e) => {
                if (isInitializingConfig) return;
                Config.SuperHubFoldersEnabled = chkSuperHubFoldersEnabled.Checked;
                if (ClipboardFlyoutForm.CurrentInstance != null && !ClipboardFlyoutForm.CurrentInstance.IsDisposed)
                {
                    ClipboardFlyoutForm.CurrentInstance.SwitchMainTab(1);
                }
            };
            toolTip.SetToolTip(chkSuperHubFoldersEnabled, "Отображает подтабы папок (как вкладки в браузере), в которые можно группировать файлы и заметки");
            pnlPageSuperHub.Controls.Add(chkSuperHubFoldersEnabled);
            top += 26;

            // 1. Режим полки (Все табы vs Только SuperHub)
            Label lblMode = new Label
            {
                Text = "Режим выдвижной полки:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 4),
                AutoSize = true
            };
            cmbSuperHubViewMode = CreateStyledComboBox(220, top, 230);
            cmbSuperHubViewMode.Items.AddRange(new object[] {
                "Буфер обмена и SuperHub (с вкладками)",
                "Только полка SuperHub (минималистичный)"
            });
            cmbSuperHubViewMode.SelectedIndexChanged += (s, e) => {
                if (isInitializingConfig) return;
                Config.SuperHubViewMode = (cmbSuperHubViewMode.SelectedIndex == 1) ? "OnlyHub" : "Tabs";
                SuperHubDockForm.RefreshAllDocks();
            };
            toolTip.SetToolTip(cmbSuperHubViewMode, "Выбор режима: отображать вкладки всех категорий буфера обмена или только файлы полки SuperHub");
            pnlPageSuperHub.Controls.Add(lblMode);
            pnlPageSuperHub.Controls.Add(cmbSuperHubViewMode);
            top += step;

            // 2. Размер полки
            Label lblSize = new Label
            {
                Text = "Размер выдвижной полки:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 4),
                AutoSize = true
            };
            cmbSuperHubSize = CreateStyledComboBox(220, top, 230);
            cmbSuperHubSize.Items.AddRange(new object[] {
                "Компактный (280 × 420)",
                "Стандартный (340 × 500)",
                "Большой (420 × 620)",
                "Широкий (500 × 700)"
            });
            cmbSuperHubSize.SelectedIndexChanged += (s, e) => {
                if (isInitializingConfig) return;
                switch (cmbSuperHubSize.SelectedIndex)
                {
                    case 0:
                        Config.SuperHubWidthVertical = 280; Config.SuperHubHeightVertical = 420;
                        Config.SuperHubWidthHorizontal = 520; Config.SuperHubHeightHorizontal = 190;
                        Config.ClipboardFlyoutWidth = 280; Config.ClipboardFlyoutHeight = 420;
                        break;
                    case 1:
                        Config.SuperHubWidthVertical = 340; Config.SuperHubHeightVertical = 500;
                        Config.SuperHubWidthHorizontal = 620; Config.SuperHubHeightHorizontal = 220;
                        Config.ClipboardFlyoutWidth = 340; Config.ClipboardFlyoutHeight = 500;
                        break;
                    case 2:
                        Config.SuperHubWidthVertical = 420; Config.SuperHubHeightVertical = 620;
                        Config.SuperHubWidthHorizontal = 750; Config.SuperHubHeightHorizontal = 260;
                        Config.ClipboardFlyoutWidth = 420; Config.ClipboardFlyoutHeight = 620;
                        break;
                    case 3:
                        Config.SuperHubWidthVertical = 500; Config.SuperHubHeightVertical = 700;
                        Config.SuperHubWidthHorizontal = 900; Config.SuperHubHeightHorizontal = 300;
                        Config.ClipboardFlyoutWidth = 500; Config.ClipboardFlyoutHeight = 700;
                        break;
                }
                SuperHubDockForm.RepositionAllDocks();
            };
            toolTip.SetToolTip(cmbSuperHubSize, "Базовый размер полки. Вы также можете свободно изменять размер перетаскиванием краев окна мышью.");
            pnlPageSuperHub.Controls.Add(lblSize);
            pnlPageSuperHub.Controls.Add(cmbSuperHubSize);
            top += step;

            // 3. Расположение на экране
            Label lblPos = new Label
            {
                Text = "Расположение на экране:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 4),
                AutoSize = true
            };
            cmbSuperHubPosition = CreateStyledComboBox(220, top, 230);
            cmbSuperHubPosition.Items.AddRange(new object[] {
                "Справа по центру",
                "Справа вверху",
                "Справа внизу",
                "Слева по центру",
                "Слева вверху",
                "Слева внизу",
                "Сверху по центру",
                "Снизу по центру",
                "В правом нижнем углу (полукруг)",
                "В левом нижнем углу (полукруг)",
                "В правом верхнем углу (полукруг)",
                "В левом верхнем углу (полукруг)"
            });
            cmbSuperHubPosition.SelectedIndexChanged += (s, e) => {
                if (isInitializingConfig) return;
                string newPos = "RightCenter";
                switch (cmbSuperHubPosition.SelectedIndex)
                {
                    case 0: newPos = "RightCenter"; break;
                    case 1: newPos = "RightTop"; break;
                    case 2: newPos = "RightBottom"; break;
                    case 3: newPos = "LeftCenter"; break;
                    case 4: newPos = "LeftTop"; break;
                    case 5: newPos = "LeftBottom"; break;
                    case 6: newPos = "TopCenter"; break;
                    case 7: newPos = "BottomCenter"; break;
                    case 8: newPos = "CornerBottomRight"; break;
                    case 9: newPos = "CornerBottomLeft"; break;
                    case 10: newPos = "CornerTopRight"; break;
                    case 11: newPos = "CornerTopLeft"; break;
                }
                foreach (var scr in Screen.AllScreens)
                {
                    Config.SetScreenPosition(scr.DeviceName, newPos);
                }
                Config.SuperHubPosition = newPos;
                SuperHubDockForm.RepositionAllDocks();
            };
            toolTip.SetToolTip(cmbSuperHubPosition, "К какому краю или углу монитора прикреплять выдвижную полку SuperHub");
            pnlPageSuperHub.Controls.Add(lblPos);
            pnlPageSuperHub.Controls.Add(cmbSuperHubPosition);
            top += step;

            // 4. Режим переноса файлов
            Label lblDrag = new Label
            {
                Text = "Перетаскивание файлов наружу:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 4),
                AutoSize = true
            };
            cmbSuperHubDragMode = CreateStyledComboBox(220, top, 230);
            cmbSuperHubDragMode.Items.AddRange(new object[] {
                "Копировать файлы (безопасно)",
                "Перемещать файлы (вырезать)",
                "Создавать ярлыки (.lnk)"
            });
            cmbSuperHubDragMode.SelectedIndexChanged += (s, e) => {
                if (cmbSuperHubDragMode.SelectedIndex == 1) Config.SuperHubDragMode = "Move";
                else if (cmbSuperHubDragMode.SelectedIndex == 2) Config.SuperHubDragMode = "Link";
                else Config.SuperHubDragMode = "Copy";
                SuperHubDockForm.RefreshAllDocks();
            };
            toolTip.SetToolTip(cmbSuperHubDragMode, "В режиме Копирования файлы всегда остаются на месте. Перемещение вырезает файлы. Режим Ярлыка создаёт ярлык .lnk.");
            pnlPageSuperHub.Controls.Add(lblDrag);
            pnlPageSuperHub.Controls.Add(cmbSuperHubDragMode);
            top += step;

            // 5. Чувствительность у края
            Label lblSens = new Label
            {
                Text = "Чувствительность у края:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 4),
                AutoSize = true
            };
            cmbSuperHubSens = CreateStyledComboBox(220, top, 230);
            cmbSuperHubSens.Items.AddRange(new object[] {
                "30 пикс (Узкая зона)",
                "60 пикс (Стандартная зона)",
                "90 пикс (Широкая зона)",
                "120 пикс (Очень широкая зона)"
            });
            cmbSuperHubSens.SelectedIndexChanged += (s, e) => {
                switch (cmbSuperHubSens.SelectedIndex)
                {
                    case 0: Config.SuperHubSensitivity = 30; break;
                    case 1: Config.SuperHubSensitivity = 60; break;
                    case 2: Config.SuperHubSensitivity = 90; break;
                    case 3: Config.SuperHubSensitivity = 120; break;
                    default: Config.SuperHubSensitivity = 60; break;
                }
            };
            toolTip.SetToolTip(cmbSuperHubSens, "Ширина зоны приближения курсора к краю экрана для автоматического выдвижения полки");
            pnlPageSuperHub.Controls.Add(lblSens);
            pnlPageSuperHub.Controls.Add(cmbSuperHubSens);
            top += step + 4;

            btnTestSuperHub = CreateButton("Раскрыть полку SuperHub сейчас", 14, top, 240, 28);
            btnTestSuperHub.Click += (s, e) => {
                SuperHubDockForm dock = SuperHubDockForm.Instance;
                if (dock != null) dock.ExpandShelf();
            };
            toolTip.SetToolTip(btnTestSuperHub, "Полка выдвигается у края экрана при наведении курсора или перетаскивании файлов");
            pnlPageSuperHub.Controls.Add(btnTestSuperHub);
        }

        private void SaveSelectedScreensFromUi()
        {
            var selected = new List<string>();
            foreach (var chk in chkScreenList)
            {
                if (chk.Checked && chk.Tag != null)
                {
                    selected.Add(chk.Tag.ToString());
                }
            }
            if (selected.Count == 0 && Screen.AllScreens.Length > 0)
            {
                selected.Add(Screen.PrimaryScreen.DeviceName);
            }
            Config.SuperHubSelectedScreens = string.Join("|", selected.ToArray());
            Config.SuperHubScreenMode = (selected.Count == Screen.AllScreens.Length) ? "All" : "Specific";
        }

        private void UpdatePositionComboForCurrentScreen()
        {
            SelectPositionInCombo(Config.SuperHubPosition);
        }

        private void SelectPositionInCombo(string pos)
        {
            if (cmbSuperHubPosition == null) return;
            if (string.Equals(pos, "RightTop", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 1;
            else if (string.Equals(pos, "RightBottom", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 2;
            else if (string.Equals(pos, "LeftCenter", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 3;
            else if (string.Equals(pos, "LeftTop", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 4;
            else if (string.Equals(pos, "LeftBottom", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 5;
            else if (string.Equals(pos, "TopCenter", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 6;
            else if (string.Equals(pos, "BottomCenter", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 7;
            else if (string.Equals(pos, "CornerBottomRight", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 8;
            else if (string.Equals(pos, "CornerBottomLeft", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 9;
            else if (string.Equals(pos, "CornerTopRight", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 10;
            else if (string.Equals(pos, "CornerTopLeft", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 11;
            else cmbSuperHubPosition.SelectedIndex = 0;
        }

        private FluentComboBox CreateStyledComboBox(int x, int y, int w)
        {
            return new FluentComboBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 28)
            };
        }

        private FluentTextBox CreateStyledTextBox(int x, int y, int w)
        {
            return new FluentTextBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 28)
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowStyles();
        }

        private void ApplyWindowStyles()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10 && this.IsHandleCreated)
                {
                    int corner = DWMWCP_ROUND;
                    DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

                    int dark = ThemeHelper.IsDarkTheme() ? 1 : 0;
                    int hr = DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                    if (hr != 0)
                    {
                        DwmSetWindowAttribute(this.Handle, 19, ref dark, sizeof(int));
                    }
                }
            }
            catch {}
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(ThemeHelper.CardBorder, 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }

        private void ApplyTheme()
        {
            ApplyWindowStyles();

            bool isDark = ThemeHelper.IsDarkTheme();
            Color bg = ThemeHelper.Background;
            Color text = ThemeHelper.TextPrimary;
            Color secText = ThemeHelper.TextSecondary;
            Color headerBg = ThemeHelper.HeaderBackground;
            Color cardBg = ThemeHelper.CardBackground;
            Color inputBg = isDark ? Color.FromArgb(19, 32, 48) : Color.FromArgb(255, 255, 255);

            this.BackColor = bg;
            this.ForeColor = text;

            pnlHeader.BackColor = headerBg;
            lblAppTitle.ForeColor = text;
            lblVersion.ForeColor = secText;
            pnlHeader.Invalidate();

            pnlTabNav.BackColor = bg;
            pnlPageGeneral.BackColor = cardBg;
            pnlPageClipboard.BackColor = cardBg;
            pnlPageSuperHub.BackColor = cardBg;
            pnlFooter.BackColor = bg;

            // Стилизация карточек
            pnlStatusCard.BackColor = cardBg;
            lblStatus.ForeColor = text;
            lblStatusSub.ForeColor = secText;

            pnlCardInstall.BackColor = cardBg;
            lblInstallTitle.ForeColor = text;
            lblInstallSub.ForeColor = secText;

            pnlSettingsCard.BackColor = cardBg;

            // Навигационные кнопки
            for (int i = 0; i < navTabButtons.Count; i++)
            {
                bool active = (i == currentTabIndex);
                navTabButtons[i].ForeColor = active ? text : secText;
            }
            pnlTabNav.Invalidate();

            // Поля ввода
            if (cmbTheme != null)
            {
                cmbTheme.BackColor = inputBg;
                cmbTheme.ForeColor = text;
            }
            if (cmbAccentColor != null)
            {
                cmbAccentColor.BackColor = inputBg;
                cmbAccentColor.ForeColor = text;
            }
            if (pnlAccentPreview != null)
            {
                pnlAccentPreview.BackColor = ThemeHelper.Accent;
                pnlAccentPreview.Invalidate();
            }
            if (cmbClipboardClickAction != null)
            {
                cmbClipboardClickAction.BackColor = inputBg;
                cmbClipboardClickAction.ForeColor = text;
            }
            if (cmbSuperHubPosition != null)
            {
                cmbSuperHubPosition.BackColor = inputBg;
                cmbSuperHubPosition.ForeColor = text;
            }
            if (cmbSuperHubDragMode != null)
            {
                cmbSuperHubDragMode.BackColor = inputBg;
                cmbSuperHubDragMode.ForeColor = text;
            }
            if (txtPrefix != null)
            {
                txtPrefix.ApplyThemeColors();
            }
            if (cmbSuperHubSens != null)
            {
                cmbSuperHubSens.BackColor = inputBg;
                cmbSuperHubSens.ForeColor = text;
            }
            if (cmbSuperHubViewMode != null)
            {
                cmbSuperHubViewMode.BackColor = inputBg;
                cmbSuperHubViewMode.ForeColor = text;
            }
            if (cmbSuperHubSize != null)
            {
                cmbSuperHubSize.BackColor = inputBg;
                cmbSuperHubSize.ForeColor = text;
            }

            // Чекбоксы
            ApplyCheckTheme(chkAutoRun);
            ApplyCheckTheme(chkContextMenu);
            ApplyCheckTheme(chkClassicContextMenuWin11);
            ApplyCheckTheme(chkPlaceUnderCursor);
            ApplyCheckTheme(chkExtractOriginalName);
            ApplyCheckTheme(chkTrayClickOpensClipboard);
            ApplyCheckTheme(chkShowHistoryMenu);
            ApplyCheckTheme(chkHistoryRememberText);
            ApplyCheckTheme(chkHistoryRememberFiles);
            ApplyCheckTheme(chkSuperHubEnabled);
            ApplyCheckTheme(chkSuperHubFoldersEnabled);
            foreach (var chk in chkScreenList)
            {
                ApplyCheckTheme(chk);
            }
            if (pnlScreensBox != null)
            {
                pnlScreensBox.BackColor = isDark ? Color.FromArgb(18, 28, 40) : Color.FromArgb(242, 245, 250);
                pnlScreensBox.Invalidate();
            }

            // Кнопки
            if (btnHeaderMinimize != null) btnHeaderMinimize.Invalidate();
            if (btnHeaderClose != null) btnHeaderClose.Invalidate();
            if (btnToggleDaemon != null) btnToggleDaemon.Invalidate();
            if (btnInstall != null) btnInstall.Invalidate();
            if (btnUninstall != null) btnUninstall.Invalidate();
            if (btnOpenCache != null) btnOpenCache.Invalidate();
            if (btnHideToTray != null) btnHideToTray.Invalidate();
            if (btnTestSuperHub != null) btnTestSuperHub.Invalidate();

            this.Invalidate(true);
        }

        private void ApplyCheckTheme(CheckBox chk)
        {
            if (chk != null)
            {
                chk.ForeColor = ThemeHelper.TextPrimary;
            }
        }

        private Panel CreateCard(int x, int y, int w, int h)
        {
            Panel card = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = ThemeHelper.CardBackground
            };
            card.Paint += (s, e) => {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color parentBg = GetRealParentBackColor(card, ThemeHelper.Background);

                Rectangle rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using (var path = CreateRoundedPath(rect, 8))
                {
                    using (var region = new Region(new Rectangle(0, 0, card.Width, card.Height)))
                    {
                        region.Exclude(path);
                        using (var parentBrush = new SolidBrush(parentBg))
                        {
                            g.FillRegion(parentBrush, region);
                        }
                    }
                    using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            };
            return card;
        }

        private FluentButton CreateButton(string text, int x, int y, int w, int h, bool isAccent = false)
        {
            FluentButton btn = new FluentButton
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                IsAccent = isAccent
            };
            return btn;
        }

        private CheckBox CreateCheckBox(string text, int x, int y)
        {
            CheckBox chk = new CheckBox
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = ThemeHelper.TextPrimary,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            return chk;
        }

        private void MakeRound(Panel panel)
        {
            panel.Paint += (s, e) => {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color parentBg = GetRealParentBackColor(panel, ThemeHelper.CardBackground);
                g.Clear(parentBg);
                using (SolidBrush brush = new SolidBrush(panel.BackColor))
                {
                    g.FillEllipse(brush, 0, 0, panel.Width - 1, panel.Height - 1);
                }
            };
        }

        public static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
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

        private void LoadConfigToUi()
        {
            isInitializingConfig = true;
            try
            {
                // Theme
                string th = Config.ThemeMode;
                if (string.Equals(th, "Dark", StringComparison.OrdinalIgnoreCase)) cmbTheme.SelectedIndex = 1;
                else if (string.Equals(th, "Light", StringComparison.OrdinalIgnoreCase)) cmbTheme.SelectedIndex = 2;
                else cmbTheme.SelectedIndex = 0;

                // Accent Color
                if (cmbAccentColor != null)
                {
                    string acc = Config.AccentColor;
                    if (string.IsNullOrEmpty(acc)) cmbAccentColor.SelectedIndex = 0;
                    else if (string.Equals(acc, "#0078D4", StringComparison.OrdinalIgnoreCase)) cmbAccentColor.SelectedIndex = 1;
                    else if (string.Equals(acc, "#008272", StringComparison.OrdinalIgnoreCase)) cmbAccentColor.SelectedIndex = 2;
                    else if (string.Equals(acc, "#8764B8", StringComparison.OrdinalIgnoreCase)) cmbAccentColor.SelectedIndex = 3;
                    else if (string.Equals(acc, "#107C41", StringComparison.OrdinalIgnoreCase)) cmbAccentColor.SelectedIndex = 4;
                    else if (string.Equals(acc, "#D13438", StringComparison.OrdinalIgnoreCase)) cmbAccentColor.SelectedIndex = 5;
                    else if (string.Equals(acc, "#CA5010", StringComparison.OrdinalIgnoreCase)) cmbAccentColor.SelectedIndex = 6;
                    else cmbAccentColor.SelectedIndex = 7;
                }
                if (pnlAccentPreview != null)
                {
                    pnlAccentPreview.BackColor = ThemeHelper.Accent;
                    pnlAccentPreview.Invalidate();
                }

                chkAutoRun.Checked = Config.AutoRun;
                chkContextMenu.Checked = Config.ContextMenu;
                chkShowHistoryMenu.Checked = Config.ShowHistoryMenu;
                chkShowHistoryMenu.Enabled = Config.ContextMenu;
                chkTrayClickOpensClipboard.Checked = Config.TrayClickOpensClipboard;
                chkClassicContextMenuWin11.Checked = Config.ClassicContextMenuWin11 || ShellIntegration.IsClassicContextMenuWin11Enabled();
                chkPlaceUnderCursor.Checked = Config.PlaceUnderCursor;
                chkExtractOriginalName.Checked = Config.ExtractOriginalName;
                chkHistoryRememberText.Checked = Config.HistoryRememberText;
                chkHistoryRememberFiles.Checked = Config.HistoryRememberFiles;
                txtPrefix.Text = Config.DefaultPrefix;

                // Clipboard Action
                bool isPaste = string.Equals(Config.ClipboardClickAction, "Paste", StringComparison.OrdinalIgnoreCase);
                cmbClipboardClickAction.SelectedIndex = isPaste ? 0 : 1;

                // SuperHub
                chkSuperHubEnabled.Checked = Config.SuperHubEnabled;
                if (chkSuperHubFoldersEnabled != null)
                {
                    chkSuperHubFoldersEnabled.Checked = Config.SuperHubFoldersEnabled;
                }

                foreach (var chk in chkScreenList)
                {
                    if (chk.Tag != null)
                    {
                        chk.Checked = Config.IsScreenSelected(chk.Tag.ToString());
                    }
                }

                UpdatePositionComboForCurrentScreen();

                bool isMove = string.Equals(Config.SuperHubDragMode, "Move", StringComparison.OrdinalIgnoreCase);
                bool isLink = string.Equals(Config.SuperHubDragMode, "Link", StringComparison.OrdinalIgnoreCase);
                if (isMove) cmbSuperHubDragMode.SelectedIndex = 1;
                else if (isLink) cmbSuperHubDragMode.SelectedIndex = 2;
                else cmbSuperHubDragMode.SelectedIndex = 0;

                // SuperHub ViewMode
                bool isOnlyHub = string.Equals(Config.SuperHubViewMode, "OnlyHub", StringComparison.OrdinalIgnoreCase);
                if (cmbSuperHubViewMode != null) cmbSuperHubViewMode.SelectedIndex = isOnlyHub ? 1 : 0;

                // SuperHub Size Preset (синхронизируем с текущей фактической шириной flyout)
                int flyoutW = Config.ClipboardFlyoutWidth;
                if (cmbSuperHubSize != null)
                {
                    if (flyoutW <= 300) cmbSuperHubSize.SelectedIndex = 0;
                    else if (flyoutW <= 380) cmbSuperHubSize.SelectedIndex = 1;
                    else if (flyoutW <= 460) cmbSuperHubSize.SelectedIndex = 2;
                    else cmbSuperHubSize.SelectedIndex = 3;
                }

                int sens = Config.SuperHubSensitivity;
                if (sens <= 40) cmbSuperHubSens.SelectedIndex = 0;
                else if (sens <= 75) cmbSuperHubSens.SelectedIndex = 1;
                else if (sens <= 105) cmbSuperHubSens.SelectedIndex = 2;
                else cmbSuperHubSens.SelectedIndex = 3;
            }
            finally
            {
                isInitializingConfig = false;
            }
        }

        public void UpdateStatus()
        {
            bool running = ShellIntegration.IsDaemonRunning() || Program.IsWatcherRunningInCurrentProcess;

            if (running)
            {
                pnlStatusDot.BackColor = Color.FromArgb(46, 160, 67);
                lblStatus.Text = "Служба активна";
                lblStatus.ForeColor = Color.FromArgb(46, 160, 67);
                lblStatusSub.Text = "Вставка Ctrl+V в Проводнике и полка SuperHub работают";
                btnToggleDaemon.Text = "Остановить";
                btnToggleDaemon.IsAccent = false;
            }
            else
            {
                pnlStatusDot.BackColor = Color.FromArgb(210, 65, 65);
                lblStatus.Text = "Служба остановлена";
                lblStatus.ForeColor = Color.FromArgb(210, 65, 65);
                lblStatusSub.Text = "Фоновое дополнение буфера обмена выключено";
                btnToggleDaemon.Text = "Запустить";
                btnToggleDaemon.IsAccent = true;
            }
            pnlStatusDot.Invalidate();
            btnToggleDaemon.Invalidate();

            bool installed = ShellIntegration.IsInstalled;
            btnUninstall.Enabled = installed;

            if (installed)
            {
                btnInstall.Text = "✓ Установлено";
                btnInstall.IsSuccess = true;
                btnInstall.IsAccent = false;
                btnInstall.Cursor = Cursors.Default;
            }
            else
            {
                btnInstall.Text = "Установить";
                btnInstall.IsSuccess = false;
                btnInstall.IsAccent = true;
                btnInstall.Cursor = Cursors.Hand;
            }
            btnInstall.Invalidate();
            btnUninstall.Invalidate();
        }

        private void BtnToggleDaemon_Click(object sender, EventArgs e)
        {
            if (ShellIntegration.IsDaemonRunning() || Program.IsWatcherRunningInCurrentProcess)
            {
                Program.StopWatcher();
                ShellIntegration.StopDaemon();
            }
            else
            {
                Program.StartWatcher();
            }
            UpdateStatus();
        }

        private void BtnInstall_Click(object sender, EventArgs e)
        {
            if (ShellIntegration.IsInstalled)
            {
                MessageBox.Show(
                    "Утилита уже интегрирована в систему.\n\nДля переустановки или сброса сначала нажмите 'Удалить'.",
                    "PasteImageAsFile",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            ShellIntegration.Install(chkAutoRun.Checked, chkContextMenu.Checked);
            Program.StartWatcher();
            MessageBox.Show(
                "Утилита успешно установлена в систему!\n\n" +
                "1. Если в буфере изображение: нажимайте Ctrl+V в Проводнике или на Рабочем столе.\n" +
                "2. Буфер обмена (Win+V) и полка SuperHub доступны из трея и по жестам мыши.",
                "PasteImageAsFile",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            UpdateStatus();
        }

        private void BtnUninstall_Click(object sender, EventArgs e)
        {
            var res = MessageBox.Show(
                "Удалить интеграцию PasteImageAsFile из Проводника и автозапуска?",
                "Удаление",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (res == DialogResult.Yes)
            {
                Program.StopWatcher();
                ShellIntegration.Uninstall();
                UpdateStatus();
                MessageBox.Show(
                    "Интеграция удалена из системы.",
                    "PasteImageAsFile",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            base.OnFormClosing(e);
        }
    }
}
