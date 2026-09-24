using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;

namespace PasteImageAsFile
{
    public class MainForm : Form
    {
        private Panel pnlHeader;
        private Label lblAppTitle;
        private Label lblVersion;
        private Panel pnlStatusDot;
        private Label lblStatus;
        private Label lblStatusSub;
        private Button btnToggleDaemon;

        private Panel pnlCardInstall;
        private Button btnInstall;
        private Button btnUninstall;

        // Вкладки настроек
        private TabControl tabSettings;
        private TabPage tabGeneral;
        private TabPage tabClipboard;
        private TabPage tabSuperHub;

        // General
        private ComboBox cmbTheme;
        private CheckBox chkAutoRun;
        private CheckBox chkContextMenu;
        private CheckBox chkClassicContextMenuWin11;
        private CheckBox chkPlaceUnderCursor;
        private CheckBox chkExtractOriginalName;
        private Label lblPrefix;
        private TextBox txtPrefix;

        // Clipboard
        private ComboBox cmbClipboardClickAction;
        private CheckBox chkTrayClickOpensClipboard;
        private CheckBox chkShowHistoryMenu;
        private CheckBox chkHistoryRememberText;
        private CheckBox chkHistoryRememberFiles;

        // SuperHub
        private CheckBox chkSuperHubEnabled;
        private ComboBox cmbSuperHubPosition;
        private ComboBox cmbSuperHubDragMode;
        private NumericUpDown numSuperHubSens;
        private Button btnTestSuperHub;

        private Panel pnlFooter;
        private Button btnOpenCache;
        private Button btnHideToTray;

        private System.Windows.Forms.Timer statusTimer;
        private ToolTip toolTip;

        public MainForm()
        {
            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 5000;
            toolTip.InitialDelay = 400;

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
            this.Size = new Size(520, 770);
            this.MinimumSize = new Size(520, 770);
            this.MaximumSize = new Size(520, 770);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Font = new Font("Segoe UI", 9f);

            // Иконка
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

            // 1. Header (70px)
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Color.FromArgb(32, 32, 32)
            };

            lblAppTitle = new Label
            {
                Text = "PasteImageAsFile",
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(20, 14)
            };

            lblVersion = new Label
            {
                Text = "v" + ShellIntegration.AppVersion,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(150, 165, 185),
                AutoSize = true,
                Location = new Point(20, 42)
            };

            pnlHeader.Controls.Add(lblAppTitle);
            pnlHeader.Controls.Add(lblVersion);

            // 2. Status Card (78px)
            Panel pnlStatusCard = CreateCard(16, 78, 472, 74);

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
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(120, 120, 120),
                AutoSize = false,
                Size = new Size(300, 32),
                Location = new Point(36, 38)
            };

            btnToggleDaemon = CreateButton("Остановить", 354, 20, 102, 32);
            btnToggleDaemon.Click += BtnToggleDaemon_Click;

            pnlStatusCard.Controls.Add(pnlStatusDot);
            pnlStatusCard.Controls.Add(lblStatus);
            pnlStatusCard.Controls.Add(lblStatusSub);
            pnlStatusCard.Controls.Add(btnToggleDaemon);

            // 3. Install Card (74px)
            pnlCardInstall = CreateCard(16, 158, 472, 70);

            Label lblInstallTitle = new Label
            {
                Text = "Системная интеграция",
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 8)
            };

            btnInstall = CreateButton("Установить", 16, 30, 214, 30);
            btnInstall.Click += BtnInstall_Click;

            btnUninstall = CreateButton("Удалить из системы", 242, 30, 214, 30);
            btnUninstall.Click += BtnUninstall_Click;

            pnlCardInstall.Controls.Add(lblInstallTitle);
            pnlCardInstall.Controls.Add(btnInstall);
            pnlCardInstall.Controls.Add(btnUninstall);

            // 4. TabControl с настройками (390px)
            tabSettings = new TabControl
            {
                Location = new Point(16, 234),
                Size = new Size(472, 420),
                Font = new Font("Segoe UI", 9f)
            };

            // --- Tab 1: Основные ---
            tabGeneral = new TabPage("Основные") { Padding = new Padding(12) };
            BuildGeneralTab();
            tabSettings.TabPages.Add(tabGeneral);

            // --- Tab 2: Буфер обмена ---
            tabClipboard = new TabPage("Буфер обмена") { Padding = new Padding(12) };
            BuildClipboardTab();
            tabSettings.TabPages.Add(tabClipboard);

            // --- Tab 3: SuperHub ---
            tabSuperHub = new TabPage("SuperHub") { Padding = new Padding(12) };
            BuildSuperHubTab();
            tabSettings.TabPages.Add(tabSuperHub);

            // 5. Footer (42px)
            pnlFooter = new Panel
            {
                Location = new Point(16, 664),
                Size = new Size(472, 42),
                BackColor = Color.Transparent
            };

            btnOpenCache = CreateButton("Папка кэша", 0, 4, 130, 32);
            btnOpenCache.Click += (s, e) => {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShellIntegration.AppName, "Cache");
                Directory.CreateDirectory(cache);
                Process.Start("explorer.exe", cache);
            };

            btnHideToTray = CreateButton("Свернуть в трей", 342, 4, 130, 32);
            btnHideToTray.Click += (s, e) => { this.Hide(); };

            pnlFooter.Controls.Add(btnOpenCache);
            pnlFooter.Controls.Add(btnHideToTray);

            // Добавляем все элементы на форму
            this.Controls.Add(pnlHeader);
            this.Controls.Add(pnlStatusCard);
            this.Controls.Add(pnlCardInstall);
            this.Controls.Add(tabSettings);
            this.Controls.Add(pnlFooter);

            this.ResumeLayout(false);
        }

        private void BuildGeneralTab()
        {
            int top = 14;
            int step = 28;

            Label lblTheme = new Label
            {
                Text = "Тема интерфейса:",
                Location = new Point(12, top + 4),
                AutoSize = true
            };
            cmbTheme = new ComboBox
            {
                Location = new Point(220, top),
                Size = new Size(210, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbTheme.Items.AddRange(new object[] { "Системная (Windows)", "Темная тема", "Светлая тема" });
            cmbTheme.SelectedIndexChanged += (s, e) => {
                if (cmbTheme.SelectedIndex == 1) Config.ThemeMode = "Dark";
                else if (cmbTheme.SelectedIndex == 2) Config.ThemeMode = "Light";
                else Config.ThemeMode = "System";
                ApplyTheme();
            };
            tabGeneral.Controls.Add(lblTheme);
            tabGeneral.Controls.Add(cmbTheme);

            top += step + 8;

            chkAutoRun = CreateCheckBox("Автозапуск вместе со стартом Windows", 12, top);
            chkAutoRun.CheckedChanged += (s, e) => { Config.AutoRun = chkAutoRun.Checked; ShellIntegration.SetAutoRun(chkAutoRun.Checked); };
            tabGeneral.Controls.Add(chkAutoRun);
            top += step;

            chkContextMenu = CreateCheckBox("Контекстное меню в Проводнике", 12, top);
            chkContextMenu.CheckedChanged += (s, e) => {
                Config.ContextMenu = chkContextMenu.Checked;
                chkShowHistoryMenu.Enabled = chkContextMenu.Checked;
                ShellIntegration.SetContextMenu(chkContextMenu.Checked);
            };
            tabGeneral.Controls.Add(chkContextMenu);
            top += step;

            chkClassicContextMenuWin11 = CreateCheckBox("Классическое контекстное меню Windows 11", 12, top);
            chkClassicContextMenuWin11.CheckedChanged += (s, e) => {
                Config.ClassicContextMenuWin11 = chkClassicContextMenuWin11.Checked;
                ShellIntegration.SetClassicContextMenuWin11(chkClassicContextMenuWin11.Checked);
            };
            tabGeneral.Controls.Add(chkClassicContextMenuWin11);
            top += step;

            chkPlaceUnderCursor = CreateCheckBox("Размещать файл под курсором на Рабочем столе", 12, top);
            chkPlaceUnderCursor.CheckedChanged += (s, e) => { Config.PlaceUnderCursor = chkPlaceUnderCursor.Checked; };
            tabGeneral.Controls.Add(chkPlaceUnderCursor);
            top += step;

            chkExtractOriginalName = CreateCheckBox("Извлекать имя картинки везде где возможно", 12, top);
            chkExtractOriginalName.CheckedChanged += (s, e) => { Config.ExtractOriginalName = chkExtractOriginalName.Checked; };
            tabGeneral.Controls.Add(chkExtractOriginalName);
            top += step + 4;

            lblPrefix = new Label
            {
                Text = "Префикс файлов по умолчанию:",
                Location = new Point(12, top + 4),
                AutoSize = true
            };
            txtPrefix = new TextBox
            {
                Location = new Point(220, top),
                Size = new Size(210, 24),
                BorderStyle = BorderStyle.FixedSingle
            };
            txtPrefix.TextChanged += (s, e) => { Config.DefaultPrefix = txtPrefix.Text.Trim(); };
            tabGeneral.Controls.Add(lblPrefix);
            tabGeneral.Controls.Add(txtPrefix);
        }

        private void BuildClipboardTab()
        {
            int top = 14;
            int step = 28;

            Label lblAction = new Label
            {
                Text = "Действие по клику на карточку:",
                Location = new Point(12, top + 4),
                AutoSize = true
            };
            cmbClipboardClickAction = new ComboBox
            {
                Location = new Point(220, top),
                Size = new Size(210, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbClipboardClickAction.Items.AddRange(new object[] { "Вставить в активное окно [Ctrl+V]", "Только скопировать в буфер" });
            cmbClipboardClickAction.SelectedIndexChanged += (s, e) => {
                Config.ClipboardClickAction = (cmbClipboardClickAction.SelectedIndex == 1) ? "Copy" : "Paste";
            };
            tabClipboard.Controls.Add(lblAction);
            tabClipboard.Controls.Add(cmbClipboardClickAction);

            top += step + 8;

            chkTrayClickOpensClipboard = CreateCheckBox("Клик по иконке в трее открывает буфер обмена", 12, top);
            chkTrayClickOpensClipboard.CheckedChanged += (s, e) => {
                Config.TrayClickOpensClipboard = chkTrayClickOpensClipboard.Checked;
            };
            tabClipboard.Controls.Add(chkTrayClickOpensClipboard);
            top += step;

            chkShowHistoryMenu = CreateCheckBox("Пункт \"Буфер обмена\" в контекстном меню", 12, top);
            chkShowHistoryMenu.CheckedChanged += (s, e) => {
                Config.ShowHistoryMenu = chkShowHistoryMenu.Checked;
                ShellIntegration.SetHistoryMenuItem(chkShowHistoryMenu.Checked);
            };
            tabClipboard.Controls.Add(chkShowHistoryMenu);
            top += step;

            chkHistoryRememberText = CreateCheckBox("Запоминать скопированный текст", 12, top);
            chkHistoryRememberText.CheckedChanged += (s, e) => { Config.HistoryRememberText = chkHistoryRememberText.Checked; };
            tabClipboard.Controls.Add(chkHistoryRememberText);
            top += step;

            chkHistoryRememberFiles = CreateCheckBox("Запоминать скопированные файлы и папки", 12, top);
            chkHistoryRememberFiles.CheckedChanged += (s, e) => { Config.HistoryRememberFiles = chkHistoryRememberFiles.Checked; };
            tabClipboard.Controls.Add(chkHistoryRememberFiles);
            top += step + 8;

            Label lblHint = new Label
            {
                Text = "💡 Подсказка:\n• Двойной клик по картинке или файлу в буфере открывает его.\n• При перетаскивании карточки на Рабочий стол создается копия файла.",
                Location = new Point(12, top),
                Size = new Size(420, 50),
                ForeColor = Color.FromArgb(120, 120, 120)
            };
            tabClipboard.Controls.Add(lblHint);
        }

        private void BuildSuperHubTab()
        {
            int top = 14;
            int step = 32;

            chkSuperHubEnabled = CreateCheckBox("Включить плавающую полку SuperHub у края экрана", 12, top);
            chkSuperHubEnabled.CheckedChanged += (s, e) => {
                Config.SuperHubEnabled = chkSuperHubEnabled.Checked;
                SuperHubDockForm.Instance.PositionCollapsed();
            };
            tabSuperHub.Controls.Add(chkSuperHubEnabled);
            top += step;

            Label lblPos = new Label
            {
                Text = "Расположение полки на экране:",
                Location = new Point(12, top + 4),
                AutoSize = true
            };
            cmbSuperHubPosition = new ComboBox
            {
                Location = new Point(220, top),
                Size = new Size(210, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbSuperHubPosition.Items.AddRange(new object[] {
                "Справа по центру",
                "Справа вверху",
                "Справа внизу",
                "Слева по центру",
                "Слева вверху",
                "Слева внизу"
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
                }
                SuperHubDockForm.Instance.PositionCollapsed();
            };
            tabSuperHub.Controls.Add(lblPos);
            tabSuperHub.Controls.Add(cmbSuperHubPosition);
            top += step;

            Label lblDrag = new Label
            {
                Text = "Перетаскивание файлов наружу:",
                Location = new Point(12, top + 4),
                AutoSize = true
            };
            cmbSuperHubDragMode = new ComboBox
            {
                Location = new Point(220, top),
                Size = new Size(210, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbSuperHubDragMode.Items.AddRange(new object[] {
                "Копировать файлы (безопасно)",
                "Перемещать файлы (вырезать)"
            });
            cmbSuperHubDragMode.SelectedIndexChanged += (s, e) => {
                Config.SuperHubDragMode = (cmbSuperHubDragMode.SelectedIndex == 1) ? "Move" : "Copy";
                SuperHubDockForm.Instance.RefreshItems();
            };
            tabSuperHub.Controls.Add(lblDrag);
            tabSuperHub.Controls.Add(cmbSuperHubDragMode);
            top += step;

            Label lblSens = new Label
            {
                Text = "Чувствительность у края (пикс):",
                Location = new Point(12, top + 4),
                AutoSize = true
            };
            numSuperHubSens = new NumericUpDown
            {
                Location = new Point(220, top),
                Size = new Size(80, 24),
                Minimum = 20,
                Maximum = 150,
                Value = 60
            };
            numSuperHubSens.ValueChanged += (s, e) => {
                Config.SuperHubSensitivity = (int)numSuperHubSens.Value;
            };
            tabSuperHub.Controls.Add(lblSens);
            tabSuperHub.Controls.Add(numSuperHubSens);
            top += step + 8;

            btnTestSuperHub = CreateButton("Раскрыть полку SuperHub сейчас", 12, top, 240, 32);
            btnTestSuperHub.Click += (s, e) => {
                SuperHubDockForm.Instance.ExpandShelf();
            };
            tabSuperHub.Controls.Add(btnTestSuperHub);
            top += step + 8;

            Label lblSuperHint = new Label
            {
                Text = "💡 Возможности SuperHub:\n" +
                       "• Автоматически выдвигается при зажатой левой кнопке мыши у края экрана.\n" +
                       "• Перетащите любые файлы, картинки или текст прямо на полку.\n" +
                       "• Кнопка [📦] в шапке позволяет за раз вытащить все накопленные файлы.\n" +
                       "• Переключатель [📋 Копия]/[✂️ Перенос] доступен прямо в шапке полки.",
                Location = new Point(12, top),
                Size = new Size(440, 85),
                ForeColor = Color.FromArgb(120, 120, 120)
            };
            tabSuperHub.Controls.Add(lblSuperHint);
        }

        private void ApplyTheme()
        {
            bool dark = ThemeHelper.IsDarkTheme();
            Color bg = ThemeHelper.Background;
            Color text = ThemeHelper.TextPrimary;
            Color headerBg = ThemeHelper.HeaderBackground;
            Color cardBg = ThemeHelper.CardBackground;

            this.BackColor = bg;
            this.ForeColor = text;

            pnlHeader.BackColor = headerBg;
            lblAppTitle.ForeColor = text;

            tabSettings.BackColor = bg;
            tabGeneral.BackColor = cardBg;
            tabGeneral.ForeColor = text;
            tabClipboard.BackColor = cardBg;
            tabClipboard.ForeColor = text;
            tabSuperHub.BackColor = cardBg;
            tabSuperHub.ForeColor = text;

            this.Invalidate(true);
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
                    DrawRoundedRect(e.Graphics, pen, rect, 6);
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

        private void DrawRoundedRect(Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();

                using (SolidBrush fill = new SolidBrush(ThemeHelper.CardBackground))
                {
                    g.FillPath(fill, path);
                }
                g.DrawPath(pen, path);
            }
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
            else cmbSuperHubPosition.SelectedIndex = 0;

            bool isMove = string.Equals(Config.SuperHubDragMode, "Move", StringComparison.OrdinalIgnoreCase);
            cmbSuperHubDragMode.SelectedIndex = isMove ? 1 : 0;

            numSuperHubSens.Value = Math.Max(20, Math.Min(150, Config.SuperHubSensitivity));
        }

        public void UpdateStatus()
        {
            bool running = ShellIntegration.IsDaemonRunning() || Program.IsWatcherRunningInCurrentProcess;

            if (running)
            {
                pnlStatusDot.BackColor = Color.FromArgb(46, 160, 67);
                lblStatus.Text = "Служба активна";
                lblStatus.ForeColor = Color.FromArgb(46, 160, 67);
                lblStatusSub.Text = "Вставка Ctrl+V в Проводнике и SuperHub работают";
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
            btnInstall.Enabled = !installed;
            btnUninstall.Enabled = installed;

            if (installed)
            {
                btnInstall.BackColor = ThemeHelper.ButtonHover;
                btnInstall.ForeColor = ThemeHelper.TextSecondary;
            }
            else
            {
                btnInstall.BackColor = ThemeHelper.Accent;
                btnInstall.ForeColor = Color.White;
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
