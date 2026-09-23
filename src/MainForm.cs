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
        // Цветовая схема
        static readonly Color AccentBlue = Color.FromArgb(86, 125, 210);
        static readonly Color AccentGreen = Color.FromArgb(46, 160, 67);
        static readonly Color AccentRed = Color.FromArgb(210, 65, 65);
        static readonly Color BgLight = Color.FromArgb(245, 247, 250);
        static readonly Color CardBg = Color.White;
        static readonly Color BorderColor = Color.FromArgb(218, 224, 232);
        static readonly Color TextPrimary = Color.FromArgb(36, 41, 47);
        static readonly Color TextSecondary = Color.FromArgb(100, 112, 130);
        static readonly Color HeaderBg = Color.FromArgb(36, 41, 51);
        static readonly Color HeaderText = Color.White;

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

        private Panel pnlCardSettings;
        private CheckBox chkAutoRun;
        private CheckBox chkContextMenu;
        private CheckBox chkShowHistoryMenu;
        private CheckBox chkPlaceUnderCursor;
        private CheckBox chkExtractOriginalName;
        private Label lblPrefix;
        private TextBox txtPrefix;

        private Panel pnlFooter;
        private Button btnOpenCache;
        private Button btnHideToTray;

        private System.Windows.Forms.Timer statusTimer;

        public MainForm()
        {
            InitializeComponent();
            LoadConfigToUi();
            UpdateStatus();

            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = 2000;
            statusTimer.Tick += (s, e) => UpdateStatus();
            statusTimer.Start();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.Text = "PasteImageAsFile";
            this.Size = new Size(500, 605);
            this.MinimumSize = new Size(500, 605);
            this.MaximumSize = new Size(500, 605);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = BgLight;
            this.Font = new Font("Segoe UI", 9.5f);

            // Загрузка иконки
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

            // --- Header ---
            pnlHeader = new Panel();
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 70;
            pnlHeader.BackColor = HeaderBg;

            lblAppTitle = new Label();
            lblAppTitle.Text = "PasteImageAsFile";
            lblAppTitle.Font = new Font("Segoe UI Semibold", 13f);
            lblAppTitle.ForeColor = HeaderText;
            lblAppTitle.AutoSize = true;
            lblAppTitle.Location = new Point(20, 14);

            lblVersion = new Label();
            lblVersion.Text = "v" + ShellIntegration.AppVersion;
            lblVersion.Font = new Font("Segoe UI", 8.5f);
            lblVersion.ForeColor = Color.FromArgb(140, 155, 175);
            lblVersion.AutoSize = true;
            lblVersion.Location = new Point(20, 43);

            pnlHeader.Controls.Add(lblAppTitle);
            pnlHeader.Controls.Add(lblVersion);

            // --- Status Card ---
            Panel pnlStatusCard = CreateCard(20, 82, 444, 80);

            pnlStatusDot = new Panel();
            pnlStatusDot.Size = new Size(12, 12);
            pnlStatusDot.Location = new Point(18, 20);
            pnlStatusDot.BackColor = AccentGreen;
            MakeRound(pnlStatusDot);

            lblStatus = new Label();
            lblStatus.Font = new Font("Segoe UI Semibold", 11f);
            lblStatus.ForeColor = TextPrimary;
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(38, 16);

            lblStatusSub = new Label();
            lblStatusSub.Font = new Font("Segoe UI", 8.5f);
            lblStatusSub.ForeColor = TextSecondary;
            lblStatusSub.AutoSize = false;
            lblStatusSub.Size = new Size(270, 32);
            lblStatusSub.Location = new Point(40, 42);

            btnToggleDaemon = CreateButton("Остановить", 326, 24, 100, 30);
            btnToggleDaemon.Click += BtnToggleDaemon_Click;

            pnlStatusCard.Controls.Add(pnlStatusDot);
            pnlStatusCard.Controls.Add(lblStatus);
            pnlStatusCard.Controls.Add(lblStatusSub);
            pnlStatusCard.Controls.Add(btnToggleDaemon);

            // --- Install Card ---
            pnlCardInstall = CreateCard(20, 172, 444, 80);

            Label lblInstallTitle = new Label();
            lblInstallTitle.Text = "Системная интеграция";
            lblInstallTitle.Font = new Font("Segoe UI Semibold", 9.5f);
            lblInstallTitle.ForeColor = TextPrimary;
            lblInstallTitle.AutoSize = true;
            lblInstallTitle.Location = new Point(18, 10);

            btnInstall = CreateButton("Установить", 18, 38, 200, 32);
            btnInstall.BackColor = AccentBlue;
            btnInstall.ForeColor = Color.White;
            btnInstall.FlatAppearance.BorderColor = AccentBlue;
            btnInstall.Click += BtnInstall_Click;

            btnUninstall = CreateButton("Удалить из системы", 226, 38, 200, 32);
            btnUninstall.Click += BtnUninstall_Click;

            pnlCardInstall.Controls.Add(lblInstallTitle);
            pnlCardInstall.Controls.Add(btnInstall);
            pnlCardInstall.Controls.Add(btnUninstall);

            // --- Settings Card ---
            pnlCardSettings = CreateCard(20, 262, 444, 232);

            Label lblSettingsTitle = new Label();
            lblSettingsTitle.Text = "Настройки";
            lblSettingsTitle.Font = new Font("Segoe UI Semibold", 9.5f);
            lblSettingsTitle.ForeColor = TextPrimary;
            lblSettingsTitle.AutoSize = true;
            lblSettingsTitle.Location = new Point(18, 10);

            int chkTop = 34;
            int chkStep = 27;

            chkAutoRun = CreateCheckBox("Автозапуск вместе со стартом Windows", 18, chkTop);
            chkAutoRun.CheckedChanged += (s, e) => { Config.AutoRun = chkAutoRun.Checked; ShellIntegration.SetAutoRun(chkAutoRun.Checked); };

            chkContextMenu = CreateCheckBox("Контекстное меню в Проводнике", 18, chkTop + chkStep);
            chkContextMenu.CheckedChanged += (s, e) => {
                Config.ContextMenu = chkContextMenu.Checked;
                chkShowHistoryMenu.Enabled = chkContextMenu.Checked;
                ShellIntegration.SetContextMenu(chkContextMenu.Checked);
            };

            chkShowHistoryMenu = CreateCheckBox("Пункт \"Буфер обмена\" (Win+V) в контекстном меню", 38, chkTop + chkStep * 2);
            chkShowHistoryMenu.CheckedChanged += (s, e) => {
                Config.ShowHistoryMenu = chkShowHistoryMenu.Checked;
                ShellIntegration.SetHistoryMenuItem(chkShowHistoryMenu.Checked);
            };

            chkPlaceUnderCursor = CreateCheckBox("Размещать файл под курсором на Рабочем столе", 18, chkTop + chkStep * 3);
            chkPlaceUnderCursor.CheckedChanged += (s, e) => { Config.PlaceUnderCursor = chkPlaceUnderCursor.Checked; };

            chkExtractOriginalName = CreateCheckBox("Извлекать имя картинки везде где возможно", 18, chkTop + chkStep * 4);
            chkExtractOriginalName.CheckedChanged += (s, e) => { Config.ExtractOriginalName = chkExtractOriginalName.Checked; };

            lblPrefix = new Label();
            lblPrefix.Text = "Префикс по умолчанию:";
            lblPrefix.ForeColor = TextSecondary;
            lblPrefix.AutoSize = true;
            lblPrefix.Location = new Point(18, chkTop + chkStep * 5 + 6);

            txtPrefix = new TextBox();
            txtPrefix.Location = new Point(195, chkTop + chkStep * 5 + 3);
            txtPrefix.Size = new Size(160, 26);
            txtPrefix.BorderStyle = BorderStyle.FixedSingle;
            txtPrefix.Font = new Font("Segoe UI", 9.5f);
            txtPrefix.TextChanged += (s, e) => { Config.DefaultPrefix = txtPrefix.Text.Trim(); };

            pnlCardSettings.Controls.Add(lblSettingsTitle);
            pnlCardSettings.Controls.Add(chkAutoRun);
            pnlCardSettings.Controls.Add(chkContextMenu);
            pnlCardSettings.Controls.Add(chkShowHistoryMenu);
            pnlCardSettings.Controls.Add(chkPlaceUnderCursor);
            pnlCardSettings.Controls.Add(chkExtractOriginalName);
            pnlCardSettings.Controls.Add(lblPrefix);
            pnlCardSettings.Controls.Add(txtPrefix);

            // --- Footer ---
            pnlFooter = new Panel();
            pnlFooter.Location = new Point(20, 508);
            pnlFooter.Size = new Size(444, 40);
            pnlFooter.BackColor = Color.Transparent;

            btnOpenCache = CreateButton("Открыть кэш", 0, 2, 130, 34);
            btnOpenCache.Click += (s, e) => {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShellIntegration.AppName, "Cache");
                Directory.CreateDirectory(cache);
                Process.Start("explorer.exe", cache);
            };

            btnHideToTray = CreateButton("Свернуть в трей", 314, 2, 130, 34);
            btnHideToTray.Click += (s, e) => { this.Hide(); };

            pnlFooter.Controls.Add(btnOpenCache);
            pnlFooter.Controls.Add(btnHideToTray);

            // --- Add all ---
            this.Controls.Add(pnlHeader);
            this.Controls.Add(pnlStatusCard);
            this.Controls.Add(pnlCardInstall);
            this.Controls.Add(pnlCardSettings);
            this.Controls.Add(pnlFooter);

            this.ResumeLayout(false);
        }

        private Panel CreateCard(int x, int y, int w, int h)
        {
            Panel card = new Panel();
            card.Location = new Point(x, y);
            card.Size = new Size(w, h);
            card.BackColor = CardBg;
            card.Paint += (s, e) => {
                using (Pen pen = new Pen(BorderColor, 1))
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
            Button btn = new Button();
            btn.Text = text;
            btn.Location = new Point(x, y);
            btn.Size = new Size(w, h);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = BorderColor;
            btn.FlatAppearance.BorderSize = 1;
            btn.BackColor = CardBg;
            btn.ForeColor = TextPrimary;
            btn.Font = new Font("Segoe UI", 9f);
            btn.Cursor = Cursors.Hand;
            return btn;
        }

        private CheckBox CreateCheckBox(string text, int x, int y)
        {
            CheckBox chk = new CheckBox();
            chk.Text = text;
            chk.Location = new Point(x, y);
            chk.AutoSize = true;
            chk.ForeColor = TextPrimary;
            chk.Font = new Font("Segoe UI", 9f);
            return chk;
        }

        private void MakeRound(Panel panel)
        {
            panel.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush brush = new SolidBrush(panel.BackColor))
                {
                    e.Graphics.Clear(panel.Parent != null ? panel.Parent.BackColor : BgLight);
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

                using (SolidBrush fill = new SolidBrush(CardBg))
                {
                    g.FillPath(fill, path);
                }
                g.DrawPath(pen, path);
            }
        }

        private void LoadConfigToUi()
        {
            chkAutoRun.Checked = Config.AutoRun;
            chkContextMenu.Checked = Config.ContextMenu;
            chkShowHistoryMenu.Checked = Config.ShowHistoryMenu;
            chkShowHistoryMenu.Enabled = Config.ContextMenu;
            chkPlaceUnderCursor.Checked = Config.PlaceUnderCursor;
            chkExtractOriginalName.Checked = Config.ExtractOriginalName;
            txtPrefix.Text = Config.DefaultPrefix;
        }

        public void UpdateStatus()
        {
            bool running = ShellIntegration.IsDaemonRunning() || Program.IsWatcherRunningInCurrentProcess;

            if (running)
            {
                pnlStatusDot.BackColor = AccentGreen;
                lblStatus.Text = "Служба активна";
                lblStatus.ForeColor = AccentGreen;
                lblStatusSub.Text = "Вставка Ctrl+V в Проводнике и на Рабочем столе работает";
                btnToggleDaemon.Text = "Остановить";
            }
            else
            {
                pnlStatusDot.BackColor = AccentRed;
                lblStatus.Text = "Служба остановлена";
                lblStatus.ForeColor = AccentRed;
                lblStatusSub.Text = "Фоновое дополнение буфера обмена выключено";
                btnToggleDaemon.Text = "Запустить";
            }
            pnlStatusDot.Invalidate();

            bool installed = ShellIntegration.IsInstalled;
            btnInstall.Enabled = !installed;
            btnUninstall.Enabled = installed;

            if (installed)
            {
                btnInstall.BackColor = Color.FromArgb(200, 210, 220);
                btnInstall.ForeColor = TextSecondary;
                btnInstall.FlatAppearance.BorderColor = BorderColor;
            }
            else
            {
                btnInstall.BackColor = AccentBlue;
                btnInstall.ForeColor = Color.White;
                btnInstall.FlatAppearance.BorderColor = AccentBlue;
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
                "Утилита установлена в систему!\n\n" +
                "1. Если в буфере изображение: нажимай Ctrl+V в Проводнике или на Рабочем столе.\n" +
                "2. Если последнее скопированное - это не картинка: в контекстном меню появится пункт 'Вставить изображение из буфера' для вставки последней картинки.\n" +
                "3. Пункт 'Буфер обмена' открывает системный журнал Win+V прямо возле курсора мыши.",
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
