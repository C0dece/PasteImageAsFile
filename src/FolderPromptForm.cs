using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PasteImageAsFile
{
    /// <summary>
    /// Компактный диалог ввода текста (создание и переименование папок SuperHub).
    /// Выполнен в Fluent стиле с поддержкой темной/светлой темы Windows.
    /// </summary>
    public class FolderPromptForm : Form
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
        private Label lblTitle;
        private Button btnClose;
        private TextBox txtInput;
        private Panel txtWrapper;
        private Button btnOk;
        private Button btnCancel;

        public string InputValue { get; private set; }

        public FolderPromptForm(string title, string prompt, string initialValue = "")
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new Size(380, 156);
            this.MinimumSize = new Size(320, 150);
            this.BackColor = ThemeHelper.Background;
            this.ShowInTaskbar = false;
            this.TopMost = true;

            ApplyWindowStyles();
            InitializeUI(title, prompt, initialValue);
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

        private void InitializeUI(string title, string prompt, string initialValue)
        {
            // Шапка формы
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = ThemeHelper.HeaderBackground
            };
            pnlHeader.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
                }
            };

            lblTitle = new Label
            {
                Text = title ?? "Папка",
                Font = new Font("Segoe UI Semibold", 9f),
                ForeColor = ThemeHelper.TextPrimary,
                AutoSize = true,
                Location = new Point(14, 9),
                Cursor = Cursors.SizeAll
            };
            lblTitle.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
                }
            };
            pnlHeader.Controls.Add(lblTitle);

            btnClose = new Button
            {
                Size = new Size(36, 36),
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                Text = "✕",
                Font = new Font("Segoe UI", 9f),
                ForeColor = ThemeHelper.TextSecondary,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
            btnClose.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 30, 20);
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = Color.White;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = ThemeHelper.TextSecondary;
            btnClose.Click += (s, e) => {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            pnlHeader.Controls.Add(btnClose);
            this.Controls.Add(pnlHeader);

            // Метка описания
            Label lblPrompt = new Label
            {
                Text = prompt ?? "Введите название:",
                Font = new Font("Segoe UI", 9f),
                ForeColor = ThemeHelper.TextSecondary,
                Location = new Point(14, 46),
                AutoSize = true
            };
            this.Controls.Add(lblPrompt);

            // Поле ввода
            txtWrapper = new Panel
            {
                Location = new Point(14, 68),
                Size = new Size(this.Width - 28, 32),
                BackColor = ThemeHelper.CardBackground
            };
            txtWrapper.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, txtWrapper.Width - 1, txtWrapper.Height - 1);
                using (var path = CreateRoundedPath(rect, 4))
                {
                    using (var pen = new Pen(ThemeHelper.CardBorder, 1))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };

            txtInput = new TextBox
            {
                Text = initialValue ?? "",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = ThemeHelper.TextPrimary,
                BackColor = ThemeHelper.CardBackground,
                BorderStyle = BorderStyle.None,
                Location = new Point(8, 6),
                Width = txtWrapper.Width - 16
            };
            txtInput.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Submit();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            };
            txtWrapper.Controls.Add(txtInput);
            this.Controls.Add(txtWrapper);

            // Кнопка [OK]
            btnOk = new Button
            {
                Text = "OK",
                Font = new Font("Segoe UI Semibold", 9f),
                Size = new Size(80, 30),
                Location = new Point(this.Width - 14 - 80 - 80 - 8, 112),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.BackColor = ThemeHelper.Accent;
            btnOk.ForeColor = Color.White;
            btnOk.Click += (s, e) => Submit();
            this.Controls.Add(btnOk);

            // Кнопка [Отмена]
            btnCancel = new Button
            {
                Text = "Отмена",
                Font = new Font("Segoe UI", 9f),
                Size = new Size(80, 30),
                Location = new Point(this.Width - 14 - 80, 112),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.BackColor = ThemeHelper.CardBackground;
            btnCancel.ForeColor = ThemeHelper.TextPrimary;
            btnCancel.Click += (s, e) => {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            this.Controls.Add(btnCancel);

            this.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
                using (var pen = new Pen(ThemeHelper.CardBorder, 1.2f))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
            };

            this.Shown += (s, e) => {
                txtInput.Focus();
                txtInput.SelectAll();
            };
        }

        private void Submit()
        {
            string val = txtInput.Text.Trim();
            if (string.IsNullOrEmpty(val))
            {
                txtInput.Focus();
                return;
            }
            this.InputValue = val;
            this.DialogResult = DialogResult.OK;
            this.Close();
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
    }
}
