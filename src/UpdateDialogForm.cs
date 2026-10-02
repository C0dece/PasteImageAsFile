using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PasteImageAsFile
{
    /// <summary>
    /// Окно просмотра и применения обновлений в стиле Windows 11 Fluent Design.
    /// </summary>
    public class UpdateDialogForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private Panel pnlHeader;
        private Label lblHeaderTitle;
        private Button btnClose;

        private Label lblVersionInfo;
        private Label lblStatus;
        private TextBox txtNotes;
        private Panel txtWrapper;
        private ProgressBar progressBar;
        private FluentButton btnAction;
        private FluentButton btnOpenBrowser;
        private FluentButton btnCancel;

        private UpdateReleaseInfo releaseInfo;
        private bool isDownloading = false;

        public UpdateDialogForm(UpdateReleaseInfo info)
        {
            this.releaseInfo = info;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(470, 350);
            this.MinimumSize = new Size(430, 310);
            this.BackColor = ThemeHelper.Background;
            this.ShowInTaskbar = false;
            this.TopMost = true;

            ApplyWindowStyles();
            InitializeUI();
        }

        private void ApplyWindowStyles()
        {
            try
            {
                int darkMode = ThemeHelper.IsDarkTheme() ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                int roundPref = DWMWCP_ROUND;
                DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundPref, sizeof(int));
            }
            catch {}
        }

        private void InitializeUI()
        {
            Color textCol = ThemeHelper.TextPrimary;
            Color secTextCol = ThemeHelper.TextSecondary;
            Color cardBg = ThemeHelper.CardBackground;

            // 1. Шапка
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = ThemeHelper.HeaderBackground
            };
            pnlHeader.MouseDown += delegate(object s, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
                }
            };

            lblHeaderTitle = new Label
            {
                Text = "Обновление PasteImageAsFile",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = textCol,
                AutoSize = true,
                Location = new Point(14, 9)
            };
            lblHeaderTitle.MouseDown += delegate(object s, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
                }
            };
            pnlHeader.Controls.Add(lblHeaderTitle);

            btnClose = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 8.5f),
                Size = new Size(36, 30),
                Location = new Point(this.Width - 42, 4),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                ForeColor = secTextCol,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
            btnClose.MouseEnter += delegate { btnClose.ForeColor = Color.White; };
            btnClose.MouseLeave += delegate { btnClose.ForeColor = secTextCol; };
            btnClose.Click += delegate { this.Close(); };
            pnlHeader.Controls.Add(btnClose);
            this.Controls.Add(pnlHeader);

            // 2. Информация о версиях
            string titleText = releaseInfo.HasUpdate
                ? ("Доступна новая версия " + releaseInfo.LatestTag + "!")
                : ("У вас установлена последняя версия v" + releaseInfo.CurrentVersion);

            lblVersionInfo = new Label
            {
                Text = titleText,
                Font = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold),
                ForeColor = releaseInfo.HasUpdate ? ThemeHelper.Accent : textCol,
                Location = new Point(18, 50),
                Size = new Size(434, 26)
            };
            this.Controls.Add(lblVersionInfo);

            string subText;
            if (!string.IsNullOrEmpty(releaseInfo.ErrorMessage))
            {
                subText = releaseInfo.ErrorMessage;
            }
            else if (releaseInfo.HasUpdate)
            {
                subText = "Текущая: v" + releaseInfo.CurrentVersion + "  ➔  Новая: " + releaseInfo.LatestTag;
                if (!string.IsNullOrEmpty(releaseInfo.ReleaseTitle)) subText += " (" + releaseInfo.ReleaseTitle + ")";
            }
            else
            {
                subText = "Обновлений не требуется. Все модули и компоненты актуальны.";
            }

            lblStatus = new Label
            {
                Text = subText,
                Font = new Font("Segoe UI", 9f),
                ForeColor = secTextCol,
                Location = new Point(18, 78),
                Size = new Size(434, 24)
            };
            this.Controls.Add(lblStatus);

            // 3. Заметки о выпуске (Release Notes)
            string notesText = releaseInfo.ReleaseNotes;
            if (string.IsNullOrEmpty(notesText))
            {
                notesText = releaseInfo.HasUpdate 
                    ? "Улучшения стабильности, оптимизация переключения мониторов и обновленные компоненты интерфейса." 
                    : "Нет описания изменений.";
            }

            txtWrapper = new Panel
            {
                Location = new Point(18, 108),
                Size = new Size(434, 142),
                BackColor = cardBg,
                Padding = new Padding(10, 8, 10, 8)
            };
            txtWrapper.Paint += delegate(object s, PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                using (var path = MainForm.CreateRoundedPath(new Rectangle(0, 0, txtWrapper.Width - 1, txtWrapper.Height - 1), 6))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            };

            txtNotes = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = cardBg,
                ForeColor = textCol,
                Font = new Font("Segoe UI", 9f),
                TabStop = false,
                Text = notesText.Replace("\r\n", "\n").Replace("\n", "\r\n")
            };
            txtNotes.GotFocus += delegate {
                txtNotes.Select(0, 0);
                if (btnAction != null && btnAction.Visible && btnAction.Enabled) btnAction.Focus();
                else btnCancel.Focus();
            };
            txtWrapper.Controls.Add(txtNotes);
            this.Controls.Add(txtWrapper);

            // Прогресс бар для скачивания
            progressBar = new ProgressBar
            {
                Location = new Point(18, 258),
                Size = new Size(434, 8),
                Visible = false,
                Minimum = 0,
                Maximum = 100
            };
            this.Controls.Add(progressBar);

            // 4. Кнопки действий (Fluent)
            int btnY = 292;

            btnOpenBrowser = new FluentButton
            {
                Text = "GitHub",
                Font = new Font("Segoe UI", 9f),
                Size = new Size(90, 34),
                Location = new Point(18, btnY)
            };
            btnOpenBrowser.Click += delegate {
                try { Process.Start(releaseInfo.HtmlUrl ?? UpdateHelper.GitHubLatestReleasePageUrl); } catch {}
            };
            this.Controls.Add(btnOpenBrowser);

            btnCancel = new FluentButton
            {
                Text = releaseInfo.HasUpdate ? "Позже" : "Закрыть",
                Font = new Font("Segoe UI", 9f),
                Size = new Size(100, 34),
                Location = new Point(this.Width - 118, btnY),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnCancel.Click += delegate { this.Close(); };
            this.Controls.Add(btnCancel);

            if (releaseInfo.HasUpdate && !string.IsNullOrEmpty(releaseInfo.DownloadUrl))
            {
                btnAction = new FluentButton
                {
                    Text = "Обновить сейчас",
                    Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                    Size = new Size(150, 34),
                    Location = new Point(btnCancel.Left - 160, btnY),
                    Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                    IsAccent = true
                };
                btnAction.Click += delegate { StartDownloadAndApply(); };
                this.Controls.Add(btnAction);
            }

            this.Shown += delegate {
                txtNotes.Select(0, 0);
                if (btnAction != null) btnAction.Focus();
                else btnCancel.Focus();
            };
        }

        private void StartDownloadAndApply()
        {
            if (isDownloading) return;
            isDownloading = true;

            btnAction.Enabled = false;
            btnAction.Text = "Загрузка...";
            btnCancel.Enabled = false;
            progressBar.Visible = true;
            lblStatus.Text = "Скачивание обновления с GitHub...";
            lblStatus.ForeColor = ThemeHelper.Accent;

            UpdateHelper.ApplyUpdate(
                releaseInfo.DownloadUrl,
                delegate(int percent) {
                    if (this.IsDisposed || !this.IsHandleCreated) return;
                    progressBar.Value = Math.Max(0, Math.Min(100, percent));
                    lblStatus.Text = string.Format("Скачивание обновления: {0}%", percent);
                },
                delegate(string error) {
                    if (this.IsDisposed || !this.IsHandleCreated) return;
                    isDownloading = false;
                    btnAction.Enabled = true;
                    btnAction.Text = "Повторить";
                    btnCancel.Enabled = true;
                    progressBar.Visible = false;
                    lblStatus.Text = "Ошибка обновления: " + error;
                    lblStatus.ForeColor = Color.FromArgb(232, 17, 35);
                }
            );
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(ThemeHelper.CardBorder, 1))
            {
                e.Graphics.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
            }
        }
    }
}
