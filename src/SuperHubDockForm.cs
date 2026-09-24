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

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        const int VK_LBUTTON = 0x01;

        // Размеры вертикального режима
        private const int ExpandedWidthVertical = 320;
        private const int ShelfHeightVertical = 480;

        // Размеры горизонтального режима (сверху / снизу)
        private const int ExpandedWidthHorizontal = 580;
        private const int ShelfHeightHorizontal = 210;

        // Размеры видимого ярлычка в свернутом виде
        private const int CollapsedBarThickness = 6;
        private const int CollapsedBarLength = 80;

        private static SuperHubDockForm instance;
        private static readonly object instanceLock = new object();

        private bool isExpanded = false;
        private bool isPinned = false;
        private System.Windows.Forms.Timer pollTimer;
        private DateTime lastPointerInside = DateTime.MinValue;

        private Panel pnlHeader;
        private Label lblTitle;
        private Button btnDragMode;
        private Button btnSettings;
        private Button btnPin;
        private Button btnDragAll;
        private Button btnClear;
        private Button btnCollapse;

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

            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 5000;
            toolTip.InitialDelay = 400;

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

            BuildUI();
            ApplyTheme();
            PositionCollapsed();

            // Таймер отслеживания приближения курсора и перетаскивания файлов
            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Interval = 50;
            pollTimer.Tick += OnPollTick;
            pollTimer.Start();

            // Настройка сплошного приема Drop на всё окно и дочерние контролы
            RegisterDropTarget(this);
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

            if (pnlHeader != null)
            {
                pnlHeader.BackColor = ThemeHelper.HeaderBackground;
                lblTitle.ForeColor = ThemeHelper.TextPrimary;
            }

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

            // Шапка полки SuperHub - 38px
            pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ExpandedWidthVertical, 38),
                BackColor = Color.FromArgb(24, 24, 24),
                Visible = false
            };

            lblTitle = new Label
            {
                Text = "📦 SuperHub",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(8, 9),
                Size = new Size(110, 20),
                AutoEllipsis = true
            };
            pnlHeader.Controls.Add(lblTitle);

            // Кнопка сворачивания
            btnCollapse = CreateToolButton("›", 260, 7, 22, 24, "Свернуть полку SuperHub");
            btnCollapse.Click += (s, e) => { isPinned = false; CollapseShelf(); };
            pnlHeader.Controls.Add(btnCollapse);

            // Кнопка закрепить [📌]
            btnPin = CreateToolButton("📌", 234, 7, 22, 24, "Закрепить полку на экране (не скрывать при уходе мыши)");
            btnPin.Click += (s, e) => {
                isPinned = !isPinned;
                btnPin.ForeColor = isPinned ? ThemeHelper.Accent : ThemeHelper.TextSecondary;
                toolTip.SetToolTip(btnPin, isPinned ? "Полка закреплена (кликните, чтобы открепить)" : "Закрепить полку на экране");
            };
            pnlHeader.Controls.Add(btnPin);

            // Кнопка шестеренки [⚙] (Настройки)
            btnSettings = CreateToolButton("⚙", 208, 7, 22, 24, "Открыть настройки PasteImageAsFile и SuperHub");
            btnSettings.Click += (s, e) => { Program.ShowMainForm(); };
            pnlHeader.Controls.Add(btnSettings);

            // Кнопка "Перетащить все" [📦]
            btnDragAll = CreateToolButton("📦", 182, 7, 22, 24, "Зажать левой кнопкой мыши и перетащить все файлы полки наружу");
            btnDragAll.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) DragAllFilesOut();
            };
            pnlHeader.Controls.Add(btnDragAll);

            // Кнопка очистки [🗑]
            btnClear = CreateToolButton("🗑", 156, 7, 22, 24, "Очистить полку SuperHub (удалить все элементы)");
            btnClear.Click += (s, e) => {
                if (MessageBox.Show("Очистить все файлы на полке SuperHub?", "SuperHub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    ClipboardHistoryManager.Instance.ClearAll(null, true);
                    RefreshItems();
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

            // Зона подсказки сброса файлов (Drop Hint) - 34px
            dropHint = new Panel
            {
                Location = new Point(8, 42),
                Size = new Size(ExpandedWidthVertical - 16, 34),
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

            // Область контента карточек
            pnlContent = new Panel
            {
                Location = new Point(0, 80),
                Size = new Size(ExpandedWidthVertical, ShelfHeightVertical - 84),
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
                    // В свернутом состоянии рисуем сплошной темный фон и акцентную линию цвета темы
                    using (var bgBrush = new SolidBrush(ThemeHelper.HeaderBackground))
                    {
                        e.Graphics.FillRectangle(bgBrush, 0, 0, this.Width, this.Height);
                    }

                    using (var b = new SolidBrush(ThemeHelper.Accent))
                    {
                        if (IsHorizontalPosition())
                        {
                            int barY = IsPositionTop() ? 0 : (this.Height - 3);
                            e.Graphics.FillRectangle(b, 4, barY, this.Width - 8, 3);
                        }
                        else
                        {
                            int indX = IsPositionOnLeft() ? 0 : (this.Width - 3);
                            e.Graphics.FillRectangle(b, indX, 4, 3, this.Height - 8);
                        }
                    }

                    using (var borderPen = new Pen(ThemeHelper.CardBorder, 1))
                    {
                        e.Graphics.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
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

        private void LayoutHeaderControls()
        {
            if (pnlHeader == null) return;
            int r = pnlHeader.Width - 4;

            // Кнопки справа налево:
            btnCollapse.Location = new Point(r - 22, 7);
            btnCollapse.Size = new Size(22, 24);
            r -= 24;

            btnPin.Location = new Point(r - 22, 7);
            btnPin.Size = new Size(22, 24);
            r -= 24;

            btnSettings.Location = new Point(r - 22, 7);
            btnSettings.Size = new Size(22, 24);
            r -= 24;

            btnDragAll.Location = new Point(r - 22, 7);
            btnDragAll.Size = new Size(22, 24);
            r -= 24;

            btnClear.Location = new Point(r - 22, 7);
            btnClear.Size = new Size(22, 24);
            r -= 26;

            btnDragMode.Location = new Point(r - 64, 7);
            btnDragMode.Size = new Size(64, 24);
            r -= 68;

            // Заголовок занимает пространство слева
            lblTitle.Location = new Point(8, 9);
            lblTitle.Size = new Size(Math.Max(50, r - 12), 20);
        }

        private void UpdateDragModeButtonVisual()
        {
            if (btnDragMode == null) return;
            bool isCopy = string.Equals(Config.SuperHubDragMode, "Copy", StringComparison.OrdinalIgnoreCase);
            if (isCopy)
            {
                btnDragMode.Text = "📋 Копия";
                btnDragMode.BackColor = ThemeHelper.AccentBackground;
                btnDragMode.ForeColor = ThemeHelper.Accent;
                toolTip.SetToolTip(btnDragMode, "Режим: КОПИРОВАНИЕ файлов.\nПри перетаскивании наружу исходные файлы остаются на месте.\nКликните для переключения в режим Переноса.");
            }
            else
            {
                btnDragMode.Text = "✂️ Перенос";
                btnDragMode.BackColor = Color.FromArgb(80, 50, 20);
                btnDragMode.ForeColor = Color.FromArgb(255, 170, 60);
                toolTip.SetToolTip(btnDragMode, "Режим: ПЕРЕМЕЩЕНИЕ файлов.\nПри перетаскивании наружу файлы вырезаются и переносятся.\nКликните для переключения в режим Копирования.");
            }
        }

        private Button CreateToolButton(string text, int x, int y, int w, int h, string tip)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ThemeHelper.TextSecondary,
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 45, 45);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 60, 60);
            btn.MouseEnter += (s, e) => btn.BackColor = ThemeHelper.ButtonHover;
            btn.MouseLeave += (s, e) => btn.BackColor = Color.Transparent;
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

        public void PositionCollapsed()
        {
            if (!Config.SuperHubEnabled)
            {
                this.Visible = false;
                return;
            }

            Screen scr = Screen.PrimaryScreen;
            this.isExpanded = false;
            pnlHeader.Visible = false;
            dropHint.Visible = false;
            pnlContent.Visible = false;

            if (IsHorizontalPosition())
            {
                int x = scr.WorkingArea.Left + (scr.WorkingArea.Width - CollapsedBarLength) / 2;
                int y = IsPositionTop() ? scr.WorkingArea.Top : (scr.WorkingArea.Bottom - CollapsedBarThickness);
                this.Size = new Size(CollapsedBarLength, CollapsedBarThickness);
                this.Location = new Point(x, y);
                btnCollapse.Text = IsPositionTop() ? "▲" : "▼";
            }
            else
            {
                int y = CalculateShelfY(scr) + (ShelfHeightVertical - CollapsedBarLength) / 2;
                int x = IsPositionOnLeft() ? scr.WorkingArea.Left : (scr.WorkingArea.Right - CollapsedBarThickness);
                this.Size = new Size(CollapsedBarThickness, CollapsedBarLength);
                this.Location = new Point(x, y);
                btnCollapse.Text = IsPositionOnLeft() ? "‹" : "›";
            }

            this.Visible = true;
            this.Invalidate();
        }

        public void ExpandShelf()
        {
            if (!Config.SuperHubEnabled) return;
            if (isExpanded) return;

            DesktopHelper.POINT pt;
            GetCursorPos(out pt);
            Screen scr = Screen.FromPoint(new Point(pt.x, pt.y));

            this.isExpanded = true;

            if (IsHorizontalPosition())
            {
                int w = ExpandedWidthHorizontal;
                int h = ShelfHeightHorizontal;
                int x = scr.WorkingArea.Left + (scr.WorkingArea.Width - w) / 2;
                int y = IsPositionTop() ? scr.WorkingArea.Top : (scr.WorkingArea.Bottom - h);

                this.Location = new Point(x, y);
                this.Size = new Size(w, h);
                btnCollapse.Text = IsPositionTop() ? "▲" : "▼";

                pnlHeader.Size = new Size(w, 38);
                dropHint.Location = new Point(8, 42);
                dropHint.Size = new Size(w - 16, 32);
                pnlContent.Location = new Point(0, 78);
                pnlContent.Size = new Size(w, h - 80);
            }
            else
            {
                int w = ExpandedWidthVertical;
                int h = ShelfHeightVertical;
                int y = CalculateShelfY(scr);
                int x = IsPositionOnLeft() ? scr.WorkingArea.Left : (scr.WorkingArea.Right - w);

                this.Location = new Point(x, y);
                this.Size = new Size(w, h);
                btnCollapse.Text = IsPositionOnLeft() ? "‹" : "›";

                pnlHeader.Size = new Size(w, 38);
                dropHint.Location = new Point(8, 42);
                dropHint.Size = new Size(w - 16, 34);
                pnlContent.Location = new Point(0, 80);
                pnlContent.Size = new Size(w, h - 84);
            }

            LayoutHeaderControls();
            pnlHeader.Visible = true;
            dropHint.Visible = true;
            pnlContent.Visible = true;

            RefreshItems();
            this.BringToFront();
            this.Invalidate();
        }

        public void CollapseShelf()
        {
            if (!isExpanded || isPinned) return;
            PositionCollapsed();
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
            Screen scr = Screen.FromPoint(cur);

            int sens = Math.Max(20, Config.SuperHubSensitivity);
            bool isLeftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

            // Логика приближения: только непосредственно в районе ярлычка полки
            bool nearEdge = false;
            if (IsHorizontalPosition())
            {
                int midX = this.Left + this.Width / 2;
                if (IsPositionTop())
                {
                    nearEdge = (cur.Y <= scr.WorkingArea.Top + sens && Math.Abs(cur.X - midX) <= 120);
                }
                else
                {
                    nearEdge = (cur.Y >= scr.WorkingArea.Bottom - sens && Math.Abs(cur.X - midX) <= 120);
                }
            }
            else
            {
                int midY = this.Top + this.Height / 2;
                if (IsPositionOnLeft())
                {
                    nearEdge = (cur.X <= scr.WorkingArea.Left + sens && Math.Abs(cur.Y - midY) <= 120);
                }
                else
                {
                    nearEdge = (cur.X >= scr.WorkingArea.Right - sens && Math.Abs(cur.Y - midY) <= 120);
                }
            }

            bool isOverUs = this.Bounds.Contains(cur);

            if (isOverUs)
            {
                lastPointerInside = DateTime.UtcNow;
            }

            if (!isExpanded)
            {
                // Раскрытие: только если курсор над самим видимым ярлычком или зажата мышь вблизи ярлычка
                if ((isLeftDown && nearEdge) || isOverUs)
                {
                    ExpandShelf();
                }
            }
            else
            {
                // Сворачивание при уходе мыши
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
            }
        }

        private void OnShelfDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                e.Effect = DragDropEffects.Copy;
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

                RefreshItems();
            }
            catch (Exception ex)
            {
                Logger.Log("OnShelfDragDrop error: " + ex.Message);
            }
        }

        public void RefreshItems()
        {
            if (!isExpanded) return;

            var items = ClipboardHistoryManager.Instance.GetItems(null, true);
            lblTitle.Text = string.Format("📦 SuperHub [{0}]", items.Count);
            LayoutHeaderControls();

            cardsList.Controls.Clear();
            scrollOffset = 0;

            bool isHorizontal = IsHorizontalPosition();
            int curPos = 0;

            if (isHorizontal)
            {
                // Горизонтальный ряд карточек
                cardsList.Top = 4;
                cardsList.Left = 8;
                cardsList.Height = pnlContent.Height - 12;

                foreach (var item in items)
                {
                    Panel card = CreateCardControlHorizontal(item, curPos, 0, 190, cardsList.Height - 8);
                    cardsList.Controls.Add(card);
                    curPos += card.Width + 8;
                }

                cardsList.Width = Math.Max(pnlContent.Width - 16, curPos);
                maxScrollOffset = Math.Max(0, cardsList.Width - pnlContent.Width + 16);
            }
            else
            {
                // Вертикальный список карточек
                cardsList.Left = 8;
                cardsList.Top = 0;
                cardsList.Width = pnlContent.Width - 18;

                foreach (var item in items)
                {
                    Panel card = CreateCardControlVertical(item, 0, curPos, cardsList.Width, 54);
                    cardsList.Controls.Add(card);
                    curPos += card.Height + 6;
                }

                cardsList.Height = Math.Max(pnlContent.Height, curPos);
                maxScrollOffset = Math.Max(0, cardsList.Height - pnlContent.Height);
            }

            pnlContent.Invalidate();
        }

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
                title = Path.GetFileName(item.ImagePath ?? "Снимок");
                sub = "Изображение";
                targetPath = item.ImagePath;
                if (SafeFileExists(item.ImagePath)) iconImg = FileIconHelper.GetIconForPath(item.ImagePath);
            }
            else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
            {
                targetPath = item.FilePaths[0];
                title = Path.GetFileName(targetPath);
                sub = (item.FilePaths.Count > 1) ? ("Файлов: " + item.FilePaths.Count) : "Файл";
                if (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)) iconImg = FileIconHelper.GetIconForPath(targetPath);
            }
            else
            {
                title = (item.TextContent ?? "").Replace("\r", "").Replace("\n", " ");
                sub = "Текст";
            }

            // Иконка
            PictureBox pic = new PictureBox
            {
                Location = new Point(8, 10),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            if (iconImg != null) pic.Image = iconImg;
            else pic.Image = SystemIcons.Application.ToBitmap();
            card.Controls.Add(pic);

            // Заголовок
            Label lblName = new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                ForeColor = ThemeHelper.TextPrimary,
                Location = new Point(44, 8),
                Size = new Size(card.Width - 94, 18),
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
                Location = new Point(44, 28),
                Size = new Size(card.Width - 94, 16),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblSub);

            // Кнопка предпросмотра [👁]
            Button btnView = CreateToolButton("👁", card.Width - 48, 14, 22, 24, "Просмотр содержимого файла или текста");
            btnView.Click += (s, e) => ItemViewerForm.ShowViewer(item, targetPath);
            card.Controls.Add(btnView);

            // Кнопка удаления [✕]
            Button btnRemove = CreateToolButton("✕", card.Width - 26, 14, 22, 24, "Убрать файл с полки SuperHub");
            btnRemove.MouseEnter += (s, e) => { btnRemove.BackColor = Color.FromArgb(196, 43, 28); btnRemove.ForeColor = Color.White; };
            btnRemove.MouseLeave += (s, e) => { btnRemove.BackColor = Color.Transparent; btnRemove.ForeColor = ThemeHelper.TextSecondary; };
            btnRemove.Click += (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnRemove);

            // Контекстное меню
            ContextMenu cardMenu = new ContextMenu();
            cardMenu.MenuItems.Add(new MenuItem("👁 Просмотр / Детали", (s, e) => ItemViewerForm.ShowViewer(item, targetPath)));
            if (!string.IsNullOrEmpty(targetPath) && (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)))
            {
                cardMenu.MenuItems.Add(new MenuItem("▷ Открыть в системе", (s, e) => LaunchFile(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("📁 Показать в Проводнике", (s, e) => ShowInExplorer(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("-"));
            }
            cardMenu.MenuItems.Add(new MenuItem("📋 Скопировать в буфер", (s, e) => CopyItem(item)));
            cardMenu.MenuItems.Add(new MenuItem("🗑 Удалить с полки", (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            }));
            card.ContextMenu = cardMenu;

            // Нажатие и запуск по двойному клику или drag-out
            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                c.MouseEnter += (s, e) => card.BackColor = ThemeHelper.CardHover;
                c.MouseLeave += (s, e) => card.BackColor = ThemeHelper.CardBackground;

                c.DoubleClick += (s, e) => {
                    if (c != btnRemove && c != btnView)
                    {
                        ItemViewerForm.ShowViewer(item, targetPath);
                    }
                };

                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left && c != btnRemove && c != btnView)
                    {
                        StartDragOut(item, card);
                    }
                };

                foreach (Control subCtrl in c.Controls) attachEvents(subCtrl);
            };
            attachEvents(card);

            toolTip.SetToolTip(card, "Двойной клик: предпросмотр\nЗажать и потянуть: перетащить наружу (" + (Config.SuperHubDragMode == "Move" ? "Перемещение" : "Копирование") + ")");
            return card;
        }

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
                title = Path.GetFileName(item.ImagePath ?? "Снимок");
                sub = "Изображение";
                targetPath = item.ImagePath;
                if (SafeFileExists(item.ImagePath)) iconImg = FileIconHelper.GetIconForPath(item.ImagePath);
            }
            else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
            {
                targetPath = item.FilePaths[0];
                title = Path.GetFileName(targetPath);
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
                Location = new Point((card.Width - 36) / 2, 10),
                Size = new Size(36, 36),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            if (iconImg != null) pic.Image = iconImg;
            else pic.Image = SystemIcons.Application.ToBitmap();
            card.Controls.Add(pic);

            // Кнопка удаления [✕] в правом верхнем углу
            Button btnRemove = CreateToolButton("✕", card.Width - 24, 4, 20, 20, "Убрать файл с полки");
            btnRemove.MouseEnter += (s, e) => { btnRemove.BackColor = Color.FromArgb(196, 43, 28); btnRemove.ForeColor = Color.White; };
            btnRemove.MouseLeave += (s, e) => { btnRemove.BackColor = Color.Transparent; btnRemove.ForeColor = ThemeHelper.TextSecondary; };
            btnRemove.Click += (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            };
            card.Controls.Add(btnRemove);

            // Кнопка предпросмотра [👁] в левом верхнем углу
            Button btnView = CreateToolButton("👁", 4, 4, 20, 20, "Просмотр содержимого");
            btnView.Click += (s, e) => ItemViewerForm.ShowViewer(item, targetPath);
            card.Controls.Add(btnView);

            // Заголовок
            Label lblName = new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold),
                ForeColor = ThemeHelper.TextPrimary,
                Location = new Point(6, 52),
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
                Location = new Point(6, 72),
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
                cardMenu.MenuItems.Add(new MenuItem("▷ Открыть в системе", (s, e) => LaunchFile(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("📁 Показать в Проводнике", (s, e) => ShowInExplorer(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("-"));
            }
            cardMenu.MenuItems.Add(new MenuItem("📋 Скопировать в буфер", (s, e) => CopyItem(item)));
            cardMenu.MenuItems.Add(new MenuItem("🗑 Удалить с полки", (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            }));
            card.ContextMenu = cardMenu;

            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                c.MouseEnter += (s, e) => card.BackColor = ThemeHelper.CardHover;
                c.MouseLeave += (s, e) => card.BackColor = ThemeHelper.CardBackground;

                c.DoubleClick += (s, e) => {
                    if (c != btnRemove && c != btnView)
                    {
                        ItemViewerForm.ShowViewer(item, targetPath);
                    }
                };

                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left && c != btnRemove && c != btnView)
                    {
                        StartDragOut(item, card);
                    }
                };

                foreach (Control subCtrl in c.Controls) attachEvents(subCtrl);
            };
            attachEvents(card);

            toolTip.SetToolTip(card, "Двойной клик: предпросмотр\nЗажать и потянуть: перетащить наружу (" + (Config.SuperHubDragMode == "Move" ? "Перемещение" : "Копирование") + ")");
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

        private void StartDragOut(ClipboardItem item, Control src)
        {
            try
            {
                DataObject data = new DataObject();
                bool isMove = string.Equals(Config.SuperHubDragMode, "Move", StringComparison.OrdinalIgnoreCase);

                // Задаем Preferred DropEffect: 1 = DROPEFFECT_COPY, 2 = DROPEFFECT_MOVE
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
    }
}
