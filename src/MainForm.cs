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
        private const int WM_PAINT = 0x000F;
        private bool isHovered = false;

        public FluentComboBox()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.DrawMode = DrawMode.OwnerDrawFixed;
            this.DropDownStyle = ComboBoxStyle.DropDownList;
            this.FlatStyle = FlatStyle.Flat;
            this.ItemHeight = 22;
            this.Font = new Font("Segoe UI", 9f);
            this.Cursor = Cursors.Hand;
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

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= this.Items.Count) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            bool isDark = ThemeHelper.IsDarkTheme();
            bool isSelected = (e.State & DrawItemState.Selected) != 0;

            Color bg = isDark
                ? (isSelected ? ThemeHelper.AccentBackground : Color.FromArgb(38, 38, 38))
                : (isSelected ? ThemeHelper.AccentBackground : Color.FromArgb(255, 255, 255));

            Color textCol = isDark
                ? (isSelected ? ThemeHelper.Accent : Color.FromArgb(240, 240, 240))
                : (isSelected ? ThemeHelper.Accent : Color.FromArgb(25, 25, 25));

            using (var brush = new SolidBrush(bg))
            {
                g.FillRectangle(brush, e.Bounds);
            }

            string itemText = this.Items[e.Index].ToString();
            Rectangle textRect = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 12, e.Bounds.Height);
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

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_PAINT)
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(this.Handle))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        bool isDark = ThemeHelper.IsDarkTheme();

                        Color borderColor = isDark
                            ? (isHovered ? Color.FromArgb(90, 90, 90) : Color.FromArgb(60, 60, 60))
                            : (isHovered ? Color.FromArgb(160, 160, 160) : Color.FromArgb(210, 210, 210));

                        Color arrowColor = isDark ? Color.FromArgb(210, 210, 210) : Color.FromArgb(90, 90, 90);

                        // 1. Аккуратный шеврон ∨ справа
                        int cx = this.Width - 15;
                        int cy = this.Height / 2;
                        using (var p = new Pen(arrowColor, 1.5f))
                        {
                            g.DrawLine(p, cx - 4, cy - 2, cx, cy + 2);
                            g.DrawLine(p, cx, cy + 2, cx + 4, cy - 2);
                        }

                        // 2. Тонкая аккуратная рамка 1px
                        using (var p = new Pen(borderColor, 1))
                        {
                            g.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
                        }
                    }
                }
                catch
                {
                }
            }
        }
    }

    public class MainForm : Form
    {
        private Panel pnlHeader;
        private Label lblAppTitle;
        private Label lblVersion;

        // Карточка статуса
        private Panel pnlStatusCard;
        private Panel pnlStatusDot;
        private Label lblStatus;
        private Label lblStatusSub;
        private Button btnToggleDaemon;

        // Карточка интеграции
        private Panel pnlCardInstall;
        private Label lblInstallTitle;
        private Label lblInstallSub;
        private Button btnInstall;
        private Button btnUninstall;

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
        private CheckBox chkAutoRun;
        private CheckBox chkContextMenu;
        private CheckBox chkClassicContextMenuWin11;
        private CheckBox chkPlaceUnderCursor;
        private CheckBox chkExtractOriginalName;
        private Label lblPrefix;
        private TextBox txtPrefix;

        // Элементы страницы "Буфер обмена"
        private FluentComboBox cmbClipboardClickAction;
        private CheckBox chkTrayClickOpensClipboard;
        private CheckBox chkShowHistoryMenu;
        private CheckBox chkHistoryRememberText;
        private CheckBox chkHistoryRememberFiles;

        // Элементы страницы "SuperHub"
        private CheckBox chkSuperHubEnabled;
        private FluentComboBox cmbSuperHubViewMode;
        private FluentComboBox cmbSuperHubSize;
        private FluentComboBox cmbSuperHubPosition;
        private FluentComboBox cmbSuperHubDragMode;
        private FluentComboBox cmbSuperHubSens;
        private Button btnTestSuperHub;

        // Подвал формы
        private Panel pnlFooter;
        private Button btnOpenCache;
        private Button btnHideToTray;

        private System.Windows.Forms.Timer statusTimer;
        private ToolTip toolTip;

        public MainForm()
        {
            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 6000;
            toolTip.InitialDelay = 300;

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
            this.Size = new Size(530, 770);
            this.MinimumSize = new Size(530, 770);
            this.MaximumSize = new Size(530, 770);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Font = new Font("Segoe UI", 9f);

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

            // 1. Верхний заголовок (Header)
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Color.FromArgb(32, 32, 32)
            };

            lblAppTitle = new Label
            {
                Text = "PasteImageAsFile",
                Font = new Font("Segoe UI Semibold", 13.5f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(20, 12)
            };

            lblVersion = new Label
            {
                Text = "v" + ShellIntegration.AppVersion,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(145, 160, 180),
                AutoSize = true,
                Location = new Point(20, 42)
            };

            pnlHeader.Controls.Add(lblAppTitle);
            pnlHeader.Controls.Add(lblVersion);

            // 2. Карточка статуса службы (72px)
            pnlStatusCard = CreateCard(16, 78, 482, 72);

            pnlStatusDot = new Panel
            {
                Size = new Size(12, 12),
                Location = new Point(16, 18),
                BackColor = Color.FromArgb(46, 160, 67)
            };
            MakeRound(pnlStatusDot);

            lblStatus = new Label
            {
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(36, 14)
            };

            lblStatusSub = new Label
            {
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(150, 150, 150),
                AutoSize = false,
                Size = new Size(310, 32),
                Location = new Point(36, 38)
            };

            btnToggleDaemon = CreateButton("Остановить", 364, 18, 102, 34);
            btnToggleDaemon.Click += BtnToggleDaemon_Click;

            pnlStatusCard.Controls.Add(pnlStatusDot);
            pnlStatusCard.Controls.Add(lblStatus);
            pnlStatusCard.Controls.Add(lblStatusSub);
            pnlStatusCard.Controls.Add(btnToggleDaemon);

            // 3. Карточка системной интеграции (72px)
            pnlCardInstall = CreateCard(16, 158, 482, 72);

            lblInstallTitle = new Label
            {
                Text = "Системная интеграция",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 8)
            };

            lblInstallSub = new Label
            {
                Text = "Автозапуск и меню Проводника",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(140, 140, 140),
                AutoSize = true,
                Location = new Point(16, 28)
            };

            btnInstall = CreateButton("Установить", 246, 18, 112, 34);
            btnInstall.Click += BtnInstall_Click;

            btnUninstall = CreateButton("Удалить", 366, 18, 100, 34);
            btnUninstall.Click += BtnUninstall_Click;

            pnlCardInstall.Controls.Add(lblInstallTitle);
            pnlCardInstall.Controls.Add(lblInstallSub);
            pnlCardInstall.Controls.Add(btnInstall);
            pnlCardInstall.Controls.Add(btnUninstall);

            // 4. Панель вкладок Fluent (без белых рамок WinForms TabControl)
            pnlTabNav = new Panel
            {
                Location = new Point(16, 238),
                Size = new Size(482, 34),
                BackColor = Color.Transparent
            };

            int tabX = 0;
            for (int i = 0; i < tabTitles.Length; i++)
            {
                int idx = i;
                Button btnTab = new Button
                {
                    Text = tabTitles[i],
                    Location = new Point(tabX, 0),
                    Size = new Size(idx == 1 ? 120 : (idx == 2 ? 100 : 96), 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = (i == currentTabIndex) ? Color.White : Color.FromArgb(160, 160, 160),
                    Font = new Font("Segoe UI", 9f, (i == currentTabIndex) ? FontStyle.Bold : FontStyle.Regular),
                    Cursor = Cursors.Hand,
                    TabStop = false
                };
                btnTab.FlatAppearance.BorderSize = 0;
                btnTab.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 45, 45);
                btnTab.FlatAppearance.MouseDownBackColor = Color.FromArgb(55, 55, 55);
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

            // 5. Единая карточка настроек (Settings Card Container) - 380px
            pnlSettingsCard = CreateCard(16, 274, 482, 380);

            // Страница 1: Основные
            pnlPageGeneral = new Panel
            {
                Location = new Point(4, 4),
                Size = new Size(pnlSettingsCard.Width - 8, pnlSettingsCard.Height - 8),
                BackColor = Color.Transparent
            };
            BuildGeneralPage();
            pnlSettingsCard.Controls.Add(pnlPageGeneral);

            // Страница 2: Буфер обмена
            pnlPageClipboard = new Panel
            {
                Location = new Point(4, 4),
                Size = new Size(pnlSettingsCard.Width - 8, pnlSettingsCard.Height - 8),
                BackColor = Color.Transparent,
                Visible = false
            };
            BuildClipboardPage();
            pnlSettingsCard.Controls.Add(pnlPageClipboard);

            // Страница 3: SuperHub
            pnlPageSuperHub = new Panel
            {
                Location = new Point(4, 4),
                Size = new Size(pnlSettingsCard.Width - 8, pnlSettingsCard.Height - 8),
                BackColor = Color.Transparent,
                Visible = false
            };
            BuildSuperHubPage();
            pnlSettingsCard.Controls.Add(pnlPageSuperHub);

            // 6. Подвал (Footer)
            pnlFooter = new Panel
            {
                Location = new Point(16, 666),
                Size = new Size(482, 42),
                BackColor = Color.Transparent
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
            int top = 14;
            int step = 30;

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
                if (cmbTheme.SelectedIndex == 1) Config.ThemeMode = "Dark";
                else if (cmbTheme.SelectedIndex == 2) Config.ThemeMode = "Light";
                else Config.ThemeMode = "System";
                ApplyTheme();
            };
            toolTip.SetToolTip(cmbTheme, "Выбор темы оформления (адаптируется к Windows 11)");
            pnlPageGeneral.Controls.Add(lblTheme);
            pnlPageGeneral.Controls.Add(cmbTheme);

            top += step + 8;

            chkAutoRun = CreateCheckBox("Автозапуск вместе со стартом Windows", 14, top);
            chkAutoRun.CheckedChanged += (s, e) => { Config.AutoRun = chkAutoRun.Checked; ShellIntegration.SetAutoRun(chkAutoRun.Checked); };
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
            top += step + 8;

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
            pnlPageGeneral.Controls.Add(lblPrefix);
            pnlPageGeneral.Controls.Add(txtPrefix);
        }

        private void BuildClipboardPage()
        {
            int top = 14;
            int step = 32;

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
                Config.ClipboardClickAction = (cmbClipboardClickAction.SelectedIndex == 1) ? "Copy" : "Paste";
            };
            toolTip.SetToolTip(cmbClipboardClickAction, "Что делать при одиночном клике по карточке в журнале буфера обмена");
            pnlPageClipboard.Controls.Add(lblAction);
            pnlPageClipboard.Controls.Add(cmbClipboardClickAction);

            top += step + 8;

            chkTrayClickOpensClipboard = CreateCheckBox("Клик по значку в трее открывает буфер обмена", 14, top);
            chkTrayClickOpensClipboard.CheckedChanged += (s, e) => {
                Config.TrayClickOpensClipboard = chkTrayClickOpensClipboard.Checked;
            };
            toolTip.SetToolTip(chkTrayClickOpensClipboard, "При нажатии левой кнопкой на иконку у часов открывать всплывающий буфер вместо окна настроек");
            pnlPageClipboard.Controls.Add(chkTrayClickOpensClipboard);
            top += step;

            chkShowHistoryMenu = CreateCheckBox("Пункт \"Буфер обмена\" в контекстном меню", 14, top);
            chkShowHistoryMenu.CheckedChanged += (s, e) => {
                Config.ShowHistoryMenu = chkShowHistoryMenu.Checked;
                ShellIntegration.SetHistoryMenuItem(chkShowHistoryMenu.Checked);
            };
            toolTip.SetToolTip(chkShowHistoryMenu, "Показывать вызов журнала буфера в контекстном меню Проводника");
            pnlPageClipboard.Controls.Add(chkShowHistoryMenu);
            top += step;

            chkHistoryRememberText = CreateCheckBox("Запоминать скопированный текст", 14, top);
            chkHistoryRememberText.CheckedChanged += (s, e) => { Config.HistoryRememberText = chkHistoryRememberText.Checked; };
            toolTip.SetToolTip(chkHistoryRememberText, "Вести историю скопированных фрагментов текста с возможностью быстрого поиска");
            pnlPageClipboard.Controls.Add(chkHistoryRememberText);
            top += step;

            chkHistoryRememberFiles = CreateCheckBox("Запоминать скопированные файлы и папки", 14, top);
            chkHistoryRememberFiles.CheckedChanged += (s, e) => { Config.HistoryRememberFiles = chkHistoryRememberFiles.Checked; };
            toolTip.SetToolTip(chkHistoryRememberFiles, "Вести историю скопированных путей к файлам с отображением системных иконок");
            pnlPageClipboard.Controls.Add(chkHistoryRememberFiles);
            top += step + 12;

            Panel pnlClipboardHelp = new Panel
            {
                Location = new Point(14, top),
                Size = new Size(440, 76),
                BackColor = Color.Transparent
            };
            pnlClipboardHelp.Paint += (s, e) => {
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rect = new Rectangle(0, 0, pnlClipboardHelp.Width - 1, pnlClipboardHelp.Height - 1);
                    using (var path = CreateRoundedPath(rect, 4))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };

            Label lblHint = new Label
            {
                Text = "Советы по использованию:\n" +
                       "- Двойной клик по карточке запускает файл или ссылку.\n" +
                       "- Перетаскивание карточки наружу копирует файл в папку.\n" +
                       "- Кнопка в шапке меняет режим: [Вставлять] или [Копировать].",
                Location = new Point(10, 8),
                Size = new Size(430, 65),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = ThemeHelper.TextSecondary
            };
            pnlClipboardHelp.Controls.Add(lblHint);
            pnlPageClipboard.Controls.Add(pnlClipboardHelp);
        }

        private void BuildSuperHubPage()
        {
            int top = 10;
            int step = 28;

            chkSuperHubEnabled = CreateCheckBox("Включить плавающую полку SuperHub у края экрана", 14, top);
            chkSuperHubEnabled.CheckedChanged += (s, e) => {
                Config.SuperHubEnabled = chkSuperHubEnabled.Checked;
                SuperHubDockForm.Instance.PositionCollapsed();
            };
            toolTip.SetToolTip(chkSuperHubEnabled, "Выдвижная полка для временного накопления и переноса файлов между программами");
            pnlPageSuperHub.Controls.Add(chkSuperHubEnabled);
            top += step;

            // 1. Режим полки (Все табы vs Только SuperHub)
            Label lblMode = new Label
            {
                Text = "Режим выдвижной полки:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 3),
                AutoSize = true
            };
            cmbSuperHubViewMode = CreateStyledComboBox(220, top, 230);
            cmbSuperHubViewMode.Items.AddRange(new object[] {
                "Буфер обмена и SuperHub (с вкладками)",
                "Только полка SuperHub (минималистичный)"
            });
            cmbSuperHubViewMode.SelectedIndexChanged += (s, e) => {
                Config.SuperHubViewMode = (cmbSuperHubViewMode.SelectedIndex == 1) ? "OnlyHub" : "Tabs";
                SuperHubDockForm.Instance.RefreshItems();
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
                Location = new Point(14, top + 3),
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
                switch (cmbSuperHubSize.SelectedIndex)
                {
                    case 0:
                        Config.SuperHubWidthVertical = 280; Config.SuperHubHeightVertical = 420;
                        Config.SuperHubWidthHorizontal = 520; Config.SuperHubHeightHorizontal = 190;
                        break;
                    case 1:
                        Config.SuperHubWidthVertical = 340; Config.SuperHubHeightVertical = 500;
                        Config.SuperHubWidthHorizontal = 620; Config.SuperHubHeightHorizontal = 220;
                        break;
                    case 2:
                        Config.SuperHubWidthVertical = 420; Config.SuperHubHeightVertical = 620;
                        Config.SuperHubWidthHorizontal = 750; Config.SuperHubHeightHorizontal = 260;
                        break;
                    case 3:
                        Config.SuperHubWidthVertical = 500; Config.SuperHubHeightVertical = 700;
                        Config.SuperHubWidthHorizontal = 900; Config.SuperHubHeightHorizontal = 300;
                        break;
                }
                SuperHubDockForm.Instance.PositionCollapsed();
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
                Location = new Point(14, top + 3),
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
                "Снизу по центру"
            });
            cmbSuperHubPosition.SelectedIndexChanged += (s, e) => {
                switch (cmbSuperHubPosition.SelectedIndex)
                {
                    case 0: Config.SuperHubPosition = "RightCenter"; break;
                    case 1: Config.SuperHubPosition = "RightTop"; break;
                    case 2: Config.SuperHubPosition = "RightBottom"; break;
                    case 3: Config.SuperHubPosition = "LeftCenter"; break;
                    case 4: Config.SuperHubPosition = "LeftTop"; break;
                    case 5: Config.SuperHubPosition = "LeftBottom"; break;
                    case 6: Config.SuperHubPosition = "TopCenter"; break;
                    case 7: Config.SuperHubPosition = "BottomCenter"; break;
                }
                SuperHubDockForm.Instance.PositionCollapsed();
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
                Location = new Point(14, top + 3),
                AutoSize = true
            };
            cmbSuperHubDragMode = CreateStyledComboBox(220, top, 230);
            cmbSuperHubDragMode.Items.AddRange(new object[] {
                "Копировать файлы (безопасно)",
                "Перемещать файлы (вырезать)"
            });
            cmbSuperHubDragMode.SelectedIndexChanged += (s, e) => {
                Config.SuperHubDragMode = (cmbSuperHubDragMode.SelectedIndex == 1) ? "Move" : "Copy";
                SuperHubDockForm.Instance.RefreshItems();
            };
            toolTip.SetToolTip(cmbSuperHubDragMode, "В режиме Копирования файлы всегда остаются на своих местах. В режиме Перемещения они вырезаются.");
            pnlPageSuperHub.Controls.Add(lblDrag);
            pnlPageSuperHub.Controls.Add(cmbSuperHubDragMode);
            top += step;

            // 5. Чувствительность у края
            Label lblSens = new Label
            {
                Text = "Чувствительность у края:",
                Font = new Font("Segoe UI Semibold", 9f),
                Location = new Point(14, top + 3),
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
                SuperHubDockForm.Instance.ExpandShelf();
            };
            toolTip.SetToolTip(btnTestSuperHub, "Немедленно показать полку SuperHub для проверки");
            pnlPageSuperHub.Controls.Add(btnTestSuperHub);
            top += 34;

            Panel pnlSuperHelp = new Panel
            {
                Location = new Point(14, top),
                Size = new Size(440, 78),
                BackColor = Color.Transparent
            };
            pnlSuperHelp.Paint += (s, e) => {
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rect = new Rectangle(0, 0, pnlSuperHelp.Width - 1, pnlSuperHelp.Height - 1);
                    using (var path = CreateRoundedPath(rect, 4))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };

            Label lblSuperHint = new Label
            {
                Text = "Возможности полки SuperHub:\n" +
                       "- Выдвигается при наведении курсора на ярлычок у края экрана.\n" +
                       "- Принимает файлы, папки и текст; поддерживает ресайз за края окна.\n" +
                       "- Режим с вкладками объединяет журнал буфера обмена и полку SuperHub.",
                Location = new Point(10, 8),
                Size = new Size(420, 64),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = ThemeHelper.TextSecondary
            };
            pnlSuperHelp.Controls.Add(lblSuperHint);
            pnlPageSuperHub.Controls.Add(pnlSuperHelp);
        }

        private FluentComboBox CreateStyledComboBox(int x, int y, int w)
        {
            return new FluentComboBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 26)
            };
        }

        private TextBox CreateStyledTextBox(int x, int y, int w)
        {
            TextBox txt = new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 24),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5f)
            };
            return txt;
        }

        private void ApplyTheme()
        {
            bool isDark = ThemeHelper.IsDarkTheme();
            Color bg = ThemeHelper.Background;
            Color text = ThemeHelper.TextPrimary;
            Color secText = ThemeHelper.TextSecondary;
            Color headerBg = ThemeHelper.HeaderBackground;
            Color cardBg = ThemeHelper.CardBackground;
            Color inputBg = isDark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(255, 255, 255);

            this.BackColor = bg;
            this.ForeColor = text;

            pnlHeader.BackColor = headerBg;
            lblAppTitle.ForeColor = text;
            lblVersion.ForeColor = secText;

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
                txtPrefix.BackColor = inputBg;
                txtPrefix.ForeColor = text;
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
                using (Pen pen = new Pen(ThemeHelper.CardBorder, 1))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                    using (var path = CreateRoundedPath(rect, 6))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };
            return card;
        }

        private Button CreateButton(string text, int x, int y, int w, int h)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeHelper.CardBackground,
                ForeColor = ThemeHelper.TextPrimary,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = ThemeHelper.CardBorder;
            btn.FlatAppearance.BorderSize = 1;
            btn.MouseEnter += (s, e) => btn.BackColor = ThemeHelper.ButtonHover;
            btn.MouseLeave += (s, e) => btn.BackColor = ThemeHelper.CardBackground;
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
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush brush = new SolidBrush(panel.BackColor))
                {
                    e.Graphics.Clear(panel.Parent != null ? panel.Parent.BackColor : this.BackColor);
                    e.Graphics.FillEllipse(brush, 0, 0, panel.Width - 1, panel.Height - 1);
                }
            };
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

        private void LoadConfigToUi()
        {
            // Theme
            string th = Config.ThemeMode;
            if (string.Equals(th, "Dark", StringComparison.OrdinalIgnoreCase)) cmbTheme.SelectedIndex = 1;
            else if (string.Equals(th, "Light", StringComparison.OrdinalIgnoreCase)) cmbTheme.SelectedIndex = 2;
            else cmbTheme.SelectedIndex = 0;

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
            string pos = Config.SuperHubPosition;
            if (string.Equals(pos, "RightTop", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 1;
            else if (string.Equals(pos, "RightBottom", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 2;
            else if (string.Equals(pos, "LeftCenter", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 3;
            else if (string.Equals(pos, "LeftTop", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 4;
            else if (string.Equals(pos, "LeftBottom", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 5;
            else if (string.Equals(pos, "TopCenter", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 6;
            else if (string.Equals(pos, "BottomCenter", StringComparison.OrdinalIgnoreCase)) cmbSuperHubPosition.SelectedIndex = 7;
            else cmbSuperHubPosition.SelectedIndex = 0;

            bool isMove = string.Equals(Config.SuperHubDragMode, "Move", StringComparison.OrdinalIgnoreCase);
            cmbSuperHubDragMode.SelectedIndex = isMove ? 1 : 0;

            // SuperHub ViewMode
            bool isOnlyHub = string.Equals(Config.SuperHubViewMode, "OnlyHub", StringComparison.OrdinalIgnoreCase);
            if (cmbSuperHubViewMode != null) cmbSuperHubViewMode.SelectedIndex = isOnlyHub ? 1 : 0;

            // SuperHub Size Preset
            int wV = Config.SuperHubWidthVertical;
            if (cmbSuperHubSize != null)
            {
                if (wV <= 300) cmbSuperHubSize.SelectedIndex = 0;
                else if (wV <= 380) cmbSuperHubSize.SelectedIndex = 1;
                else if (wV <= 460) cmbSuperHubSize.SelectedIndex = 2;
                else cmbSuperHubSize.SelectedIndex = 3;
            }

            int sens = Config.SuperHubSensitivity;
            if (sens <= 40) cmbSuperHubSens.SelectedIndex = 0;
            else if (sens <= 75) cmbSuperHubSens.SelectedIndex = 1;
            else if (sens <= 105) cmbSuperHubSens.SelectedIndex = 2;
            else cmbSuperHubSens.SelectedIndex = 3;
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
            }
            else
            {
                pnlStatusDot.BackColor = Color.FromArgb(210, 65, 65);
                lblStatus.Text = "Служба остановлена";
                lblStatus.ForeColor = Color.FromArgb(210, 65, 65);
                lblStatusSub.Text = "Фоновое дополнение буфера обмена выключено";
                btnToggleDaemon.Text = "Запустить";
            }
            pnlStatusDot.Invalidate();

            bool installed = ShellIntegration.IsInstalled;
            btnUninstall.Enabled = installed;

            if (installed)
            {
                btnInstall.Text = "✓ Установлено";
                btnInstall.BackColor = Color.FromArgb(28, 52, 34);
                btnInstall.ForeColor = Color.FromArgb(100, 225, 135);
                btnInstall.FlatAppearance.BorderColor = Color.FromArgb(45, 110, 55);
                btnInstall.Cursor = Cursors.Default;
            }
            else
            {
                btnInstall.Text = "Установить";
                btnInstall.BackColor = ThemeHelper.Accent;
                btnInstall.ForeColor = Color.White;
                btnInstall.FlatAppearance.BorderColor = ThemeHelper.Accent;
                btnInstall.Cursor = Cursors.Hand;
            }
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
