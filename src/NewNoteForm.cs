using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PasteImageAsFile
{
    /// <summary>
    /// Диалоговое окно для создания пользовательской текстовой заметки прямо в SuperHub.
    /// Поддерживает темную тему, скругленные углы, горячие клавиши Ctrl+Enter и Esc.
    /// </summary>
    public class NewNoteForm : Form
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
        private TextBox txtContent;
        private Panel txtWrapper;
        private Panel pnlBottom;
        private Label lblHint;
        private Button btnSave;
        private Button btnCancel;

        public string NoteText { get; private set; }

        public NewNoteForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new Size(420, 260);
            this.MinimumSize = new Size(320, 200);
            this.BackColor = ThemeHelper.Background;
            this.ShowInTaskbar = false;
            this.TopMost = true;

            ApplyWindowRoundingAndDarkTitle();
            InitializeComponents();
        }

        private void ApplyWindowRoundingAndDarkTitle()
        {
            try
            {
                int darkMode = 1;
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                int roundPref = DWMWCP_ROUND;
                DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundPref, sizeof(int));
            }
            catch {}
        }

        private void InitializeComponents()
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
                Text = "Новая заметка (SuperHub)",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = ThemeHelper.TextPrimary,
                AutoSize = true,
                Location = new Point(12, 9),
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
                Text = "✕",
                Size = new Size(26, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ThemeHelper.TextSecondary,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
            btnClose.FlatAppearance.MouseDownBackColor = Color.FromArgb(200, 15, 30);
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = Color.White;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = ThemeHelper.TextSecondary;
            btnClose.Click += (s, e) => {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            pnlHeader.Controls.Add(btnClose);
            pnlHeader.Resize += (s, e) => {
                btnClose.Location = new Point(pnlHeader.ClientSize.Width - btnClose.Width - 6, 5);
            };
            this.Controls.Add(pnlHeader);

            // Нижняя панель действий
            pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                BackColor = ThemeHelper.HeaderBackground
            };

            lblHint = new Label
            {
                Text = "Ctrl+Enter - сохранить, Esc - отмена",
                Font = new Font("Segoe UI", 8f),
                ForeColor = ThemeHelper.TextSecondary,
                AutoSize = true,
                Location = new Point(12, 14)
            };
            pnlBottom.Controls.Add(lblHint);

            btnCancel = new Button
            {
                Text = "Отмена",
                Size = new Size(74, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ThemeHelper.TextPrimary,
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.FlatAppearance.MouseOverBackColor = ThemeHelper.ButtonHover;
            btnCancel.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
            btnCancel.Click += (s, e) => {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            pnlBottom.Controls.Add(btnCancel);

            btnSave = new Button
            {
                Text = "Добавить",
                Size = new Size(80, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeHelper.Accent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.FlatAppearance.MouseOverBackColor = ThemeHelper.AccentHover;
            btnSave.FlatAppearance.MouseDownBackColor = ThemeHelper.ButtonPressed;
            btnSave.Click += (s, e) => SaveNote();
            pnlBottom.Controls.Add(btnSave);

            pnlBottom.Resize += (s, e) => {
                btnSave.Location = new Point(pnlBottom.ClientSize.Width - btnSave.Width - 12, 9);
                btnCancel.Location = new Point(btnSave.Left - btnCancel.Width - 8, 9);
            };

            this.Controls.Add(pnlBottom);

            // Контейнер и поле ввода текста
            txtWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 10, 12, 10),
                BackColor = ThemeHelper.Background
            };

            txtContent = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                BackColor = ThemeHelper.CardBackground,
                ForeColor = ThemeHelper.TextPrimary,
                Font = new Font("Segoe UI", 9.5f),
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                AcceptsReturn = true,
                AcceptsTab = true
            };
            txtContent.KeyDown += (s, e) => {
                if (e.Control && e.KeyCode == Keys.A)
                {
                    txtContent.SelectAll();
                    e.SuppressKeyPress = true;
                }
            };

            txtWrapper.Controls.Add(txtContent);
            this.Controls.Add(txtWrapper);
            txtWrapper.BringToFront();

            this.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(ThemeHelper.CardBorder, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter))
            {
                SaveNote();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SaveNote()
        {
            string t = txtContent.Text;
            if (string.IsNullOrWhiteSpace(t))
            {
                txtContent.Focus();
                return;
            }
            this.NoteText = t.Trim();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (pnlHeader != null && btnClose != null)
            {
                btnClose.Location = new Point(pnlHeader.ClientSize.Width - btnClose.Width - 6, 5);
            }
            if (pnlBottom != null && btnSave != null && btnCancel != null)
            {
                btnSave.Location = new Point(pnlBottom.ClientSize.Width - btnSave.Width - 12, 9);
                btnCancel.Location = new Point(btnSave.Left - btnCancel.Width - 8, 9);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            txtContent.Focus();
        }
    }
}
