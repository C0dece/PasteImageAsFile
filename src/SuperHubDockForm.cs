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

        private const int ExpandedWidth = 270;
        private const int CollapsedWidth = 6;
        private const int ShelfHeight = 460;

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
        private int scrollY = 0;
        private int maxScrollY = 0;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

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
            this.Size = new Size(CollapsedWidth, ShelfHeight);
            this.TopMost = true;
            this.AllowDrop = true;
            this.DoubleBuffered = true;
            this.MinimumSize = new Size(1, 1);

            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 5000;
            toolTip.InitialDelay = 400;

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

        private bool IsPositionOnLeft()
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
                return scr.WorkingArea.Bottom - ShelfHeight - 24;
            }
            // Center
            return scr.WorkingArea.Top + (scr.WorkingArea.Height - ShelfHeight) / 2;
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            // Шапка полки SuperHub - 38px
            pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ExpandedWidth, 38),
                BackColor = Color.FromArgb(24, 24, 24),
                Visible = false
            };

            lblTitle = new Label
            {
                Text = "📦 SuperHub",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(8, 9),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);

            // Кнопка сворачивания
            btnCollapse = CreateToolButton("›", ExpandedWidth - 26, 7, 20, 24, "Свернуть полку SuperHub");
            btnCollapse.Click += (s, e) => { isPinned = false; CollapseShelf(); };
            pnlHeader.Controls.Add(btnCollapse);

            // Кнопка закрепить [📌]
            btnPin = CreateToolButton("📌", ExpandedWidth - 50, 7, 22, 24, "Закрепить полку на экране (не скрывать при уходе мыши)");
            btnPin.Click += (s, e) => {
                isPinned = !isPinned;
                btnPin.ForeColor = isPinned ? ThemeHelper.Accent : ThemeHelper.TextSecondary;
                toolTip.SetToolTip(btnPin, isPinned ? "Полка закреплена (кликните, чтобы открепить)" : "Закрепить полку на экране");
            };
            pnlHeader.Controls.Add(btnPin);

            // Кнопка шестеренки [⚙️] (Настройки)
            btnSettings = CreateToolButton("⚙️", ExpandedWidth - 74, 7, 22, 24, "Открыть настройки PasteImageAsFile и SuperHub");
            btnSettings.Click += (s, e) => { Program.ShowMainForm(); };
            pnlHeader.Controls.Add(btnSettings);

            // Кнопка "Перетащить все" [📦]
            btnDragAll = CreateToolButton("📦", ExpandedWidth - 98, 7, 22, 24, "Зажать левой кнопкой мыши и перетащить все файлы полки наружу");
            btnDragAll.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) DragAllFilesOut();
            };
            pnlHeader.Controls.Add(btnDragAll);

            // Кнопка режима Drag-and-Drop: [Копия / Перенос]
            btnDragMode = new Button
            {
                Location = new Point(ExpandedWidth - 164, 7),
                Size = new Size(62, 24),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDragMode.FlatAppearance.BorderSize = 0;
            btnDragMode.Click += (s, e) => {
                bool isCopy = string.Equals(Config.SuperHubDragMode, "Copy", StringComparison.OrdinalIgnoreCase);
                Config.SuperHubDragMode = isCopy ? "Move" : "Copy";
                UpdateDragModeButtonVisual();
            };
            UpdateDragModeButtonVisual();
            pnlHeader.Controls.Add(btnDragMode);

            // Кнопка очистки [🗑️]
            btnClear = CreateToolButton("🗑️", ExpandedWidth - 190, 7, 22, 24, "Очистить полку SuperHub (удалить все элементы)");
            btnClear.Click += (s, e) => {
                if (MessageBox.Show("Очистить все файлы на полке SuperHub?", "SuperHub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    ClipboardHistoryManager.Instance.ClearAll(null, true);
                    RefreshItems();
                }
            };
            pnlHeader.Controls.Add(btnClear);

            this.Controls.Add(pnlHeader);

            // Зона подсказки сброса файлов (Drop Hint) - 40px
            dropHint = new Panel
            {
                Location = new Point(8, 44),
                Size = new Size(ExpandedWidth - 16, 40),
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
                Location = new Point(0, 90),
                Size = new Size(ExpandedWidth, ShelfHeight - 94),
                BackColor = Color.FromArgb(28, 28, 28),
                Visible = false,
                AllowDrop = true
            };

            cardsList = new Panel
            {
                Location = new Point(8, 0),
                Width = ExpandedWidth - 18,
                BackColor = Color.Transparent,
                AllowDrop = true
            };
            pnlContent.Controls.Add(cardsList);

            pnlContent.MouseWheel += (s, e) => {
                if (maxScrollY <= 0) return;
                int step = 45;
                int newY = scrollY + (e.Delta > 0 ? -step : step);
                if (newY < 0) newY = 0;
                if (newY > maxScrollY) newY = maxScrollY;
                if (newY != scrollY)
                {
                    scrollY = newY;
                    cardsList.Top = -scrollY;
                    pnlContent.Invalidate();
                }
            };

            pnlContent.Paint += RenderScrollbar;

            this.Controls.Add(pnlContent);

            this.Paint += (s, e) => {
                if (!isExpanded)
                {
                    // В свернутом состоянии рисуем акцентную полоску индикатора
                    using (var b = new SolidBrush(ThemeHelper.Accent))
                    {
                        int indX = IsPositionOnLeft() ? (CollapsedWidth - 4) : 0;
                        e.Graphics.FillRectangle(b, indX, (this.Height - 64) / 2, 4, 64);
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
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = ThemeHelper.ButtonHover;
            btn.MouseLeave += (s, e) => btn.BackColor = Color.Transparent;
            toolTip.SetToolTip(btn, tip);
            return btn;
        }

        private void RenderScrollbar(object sender, PaintEventArgs e)
        {
            if (maxScrollY <= 0) return;
            int trackH = pnlContent.Height - 16;
            int thumbH = Math.Max(24, (int)((float)pnlContent.Height / cardsList.Height * trackH));
            int thumbY = 6 + (int)((float)scrollY / maxScrollY * (trackH - thumbH));

            using (var b = new SolidBrush(ThemeHelper.ScrollBarThumb))
            {
                e.Graphics.FillRectangle(b, pnlContent.Width - 5, thumbY, 3, thumbH);
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
            int y = CalculateShelfY(scr);
            int x = IsPositionOnLeft() ? scr.WorkingArea.Left : (scr.WorkingArea.Right - CollapsedWidth);

            this.Size = new Size(CollapsedWidth, ShelfHeight);
            this.Location = new Point(x, y);
            this.isExpanded = false;
            pnlHeader.Visible = false;
            dropHint.Visible = false;
            pnlContent.Visible = false;
            btnCollapse.Text = IsPositionOnLeft() ? "‹" : "›";
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

            int y = CalculateShelfY(scr);
            int x = IsPositionOnLeft() ? scr.WorkingArea.Left : (scr.WorkingArea.Right - ExpandedWidth);

            this.Location = new Point(x, y);
            this.Size = new Size(ExpandedWidth, ShelfHeight);
            this.isExpanded = true;
            pnlHeader.Visible = true;
            dropHint.Visible = true;
            pnlContent.Visible = true;
            btnCollapse.Text = IsPositionOnLeft() ? "‹" : "›";

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
            bool onLeft = IsPositionOnLeft();

            bool nearEdge = onLeft
                ? (cur.X <= scr.WorkingArea.Left + sens && cur.Y >= scr.WorkingArea.Top + 40 && cur.Y <= scr.WorkingArea.Bottom - 40)
                : (cur.X >= scr.WorkingArea.Right - sens && cur.Y >= scr.WorkingArea.Top + 40 && cur.Y <= scr.WorkingArea.Bottom - 40);

            bool isOverUs = this.Bounds.Contains(cur);

            if (isOverUs)
            {
                lastPointerInside = DateTime.UtcNow;
            }

            if (!isExpanded)
            {
                // Если идет операция перетаскивания (зажата мышь) у края экрана, или курсор над ярлычком
                if ((isLeftDown && nearEdge) || isOverUs)
                {
                    ExpandShelf();
                }
            }
            else
            {
                // Если развернуто, но курсор ушел и кнопка не зажата
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
                    ClipboardHistoryManager.Instance.AddCustomSuperHubItem(files, null);
                    RefreshItems();
                }
                else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
                {
                    string t = (string)e.Data.GetData(DataFormats.UnicodeText);
                    ClipboardHistoryManager.Instance.AddCustomSuperHubItem(null, t);
                    RefreshItems();
                }
            }
            catch (Exception ex)
            {
                Logger.Log("OnShelfDragDrop error: " + ex.Message);
            }
        }

        public void RefreshItems()
        {
            cardsList.SuspendLayout();
            cardsList.Controls.Clear();

            var list = ClipboardHistoryManager.Instance.GetItems(null, true);
            lblTitle.Text = "📦 SuperHub [" + list.Count + "]";

            int itemY = 4;
            int itemW = cardsList.Width;

            if (list.Count == 0)
            {
                Label empty = new Label
                {
                    Text = "Полка пуста.\nПеретащите файлы сюда.",
                    ForeColor = ThemeHelper.TextSecondary,
                    Font = new Font("Segoe UI", 8.5f),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(itemW, 100),
                    Location = new Point(0, 20)
                };
                cardsList.Controls.Add(empty);
                cardsList.Height = 150;
            }
            else
            {
                foreach (var item in list)
                {
                    var card = CreateShelfCard(item, itemW);
                    card.Location = new Point(0, itemY);
                    cardsList.Controls.Add(card);
                    itemY += card.Height + 6;
                }
                cardsList.Height = itemY + 8;
            }

            maxScrollY = Math.Max(0, cardsList.Height - pnlContent.Height);
            if (scrollY > maxScrollY) scrollY = maxScrollY;
            cardsList.Top = -scrollY;

            cardsList.ResumeLayout(true);
            pnlContent.Invalidate();

            // Повторно регистрируем Drop на новые карточки
            RegisterDropTarget(cardsList);
        }

        private Control CreateShelfCard(ClipboardItem item, int width)
        {
            Panel card = new Panel
            {
                Size = new Size(width, 48),
                BackColor = ThemeHelper.CardBackground,
                Cursor = Cursors.Hand
            };

            card.Paint += (s, e) => {
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            // Иконка слева
            PictureBox pic = new PictureBox
            {
                Location = new Point(6, 8),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            string title = "";
            string sub = "";
            string targetPath = null;

            if (item.Type == ClipboardItemType.Image && SafeFileExists(item.ImagePath))
            {
                targetPath = item.ImagePath;
                title = SafeGetFileName(item.ImagePath);
                sub = (item.FileSizeBytes / 1024) + " КБ";
                try
                {
                    using (var fs = new FileStream(item.ImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var img = Image.FromStream(fs))
                    {
                        pic.Image = new Bitmap(img);
                    }
                }
                catch {}
            }
            else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
            {
                targetPath = item.FilePaths[0];
                title = SafeGetFileName(targetPath);
                sub = item.FilePaths.Count > 1 ? ("и еще " + (item.FilePaths.Count - 1) + " файлов") : (SafeDirectoryExists(targetPath) ? "Папка" : "Файл");
                pic.Image = FileIconHelper.GetIconForPath(targetPath);
            }
            else
            {
                title = item.TextContent ?? "";
                if (title.Length > 25) title = title.Substring(0, 25) + "...";
                sub = "Текст • " + (item.TextContent != null ? item.TextContent.Length : 0) + " симв.";
                pic.Image = SystemIcons.Information.ToBitmap();
            }

            card.Controls.Add(pic);

            Label lblName = new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                ForeColor = ThemeHelper.TextPrimary,
                Location = new Point(44, 6),
                Size = new Size(card.Width - 72, 18),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblName);

            Label lblSub = new Label
            {
                Text = sub,
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = ThemeHelper.TextSecondary,
                Location = new Point(44, 26),
                Size = new Size(card.Width - 72, 16),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            card.Controls.Add(lblSub);

            // Кнопка удаления [✕]
            Button btnRemove = new Button
            {
                Text = "✕",
                Location = new Point(card.Width - 24, 12),
                Size = new Size(20, 22),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ThemeHelper.TextSecondary,
                Font = new Font("Segoe UI", 7.5f),
                Cursor = Cursors.Hand
            };
            btnRemove.FlatAppearance.BorderSize = 0;
            btnRemove.MouseEnter += (s, e) => { btnRemove.BackColor = Color.FromArgb(196, 43, 28); btnRemove.ForeColor = Color.White; };
            btnRemove.MouseLeave += (s, e) => { btnRemove.BackColor = Color.Transparent; btnRemove.ForeColor = ThemeHelper.TextSecondary; };
            btnRemove.Click += (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            };
            toolTip.SetToolTip(btnRemove, "Убрать файл с полки SuperHub");
            card.Controls.Add(btnRemove);

            // Контекстное меню по правому клику
            ContextMenu cardMenu = new ContextMenu();
            if (!string.IsNullOrEmpty(targetPath) && (SafeFileExists(targetPath) || SafeDirectoryExists(targetPath)))
            {
                cardMenu.MenuItems.Add(new MenuItem("▷ Открыть", (s, e) => LaunchFile(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("📁 Показать в Проводнике", (s, e) => ShowInExplorer(targetPath)));
                cardMenu.MenuItems.Add(new MenuItem("-"));
            }
            cardMenu.MenuItems.Add(new MenuItem("📋 Скопировать", (s, e) => CopyItem(item)));
            cardMenu.MenuItems.Add(new MenuItem("🗑️ Удалить с полки", (s, e) => {
                ClipboardHistoryManager.Instance.ToggleSuperHub(item.Id);
                RefreshItems();
            }));
            card.ContextMenu = cardMenu;

            // Нажатие и запуск по двойному клику или drag-out
            Action<Control> attachEvents = null;
            attachEvents = (c) => {
                c.MouseEnter += (s, e) => card.BackColor = ThemeHelper.CardHover;
                c.MouseLeave += (s, e) => card.BackColor = ThemeHelper.CardBackground;

                // Двойной клик открывает файл
                c.DoubleClick += (s, e) => {
                    if (c != btnRemove && !string.IsNullOrEmpty(targetPath))
                    {
                        LaunchFile(targetPath);
                    }
                };

                // Запуск Drag out наружу
                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left && c != btnRemove)
                    {
                        StartDragOut(item, card);
                    }
                };

                foreach (Control subCtrl in c.Controls) attachEvents(subCtrl);
            };
            attachEvents(card);

            toolTip.SetToolTip(card, "Двойной клик: открыть файл\nЗажать и потянуть: перетащить наружу (" + (Config.SuperHubDragMode == "Move" ? "Перемещение" : "Копирование") + ")");

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
                    data.SetFileDropList(sc);

                    bool isMove = string.Equals(Config.SuperHubDragMode, "Move", StringComparison.OrdinalIgnoreCase);
                    byte dropEffectVal = (byte)(isMove ? 2 : 1);
                    var dropEffectStream = new MemoryStream(new byte[] { dropEffectVal, 0, 0, 0 });
                    data.SetData("Preferred DropEffect", dropEffectStream);

                    DragDropEffects allowedEffects = isMove ? (DragDropEffects.Move | DragDropEffects.Copy) : DragDropEffects.Copy;
                    this.DoDragDrop(data, allowedEffects);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("DragAllFilesOut error: " + ex.Message);
            }
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
    }
}
