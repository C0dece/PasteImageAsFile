using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PasteImageAsFile
{
    internal class AnchorForm : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CreateCaret(IntPtr hWnd, IntPtr hBitmap, int nWidth, int nHeight);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowCaret(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCaretPos(int X, int Y);

        private TextBox txtBox;
        private System.Windows.Forms.Timer autoHideTimer;

        public AnchorForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(16, 16);
            this.TopMost = true;
            this.BackColor = Color.FromArgb(24, 24, 24);

            txtBox = new TextBox
            {
                Location = new Point(0, 0),
                Size = new Size(16, 16),
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.White
            };
            this.Controls.Add(txtBox);

            autoHideTimer = new System.Windows.Forms.Timer();
            autoHideTimer.Interval = 3500;
            autoHideTimer.Tick += (s, e) => {
                autoHideTimer.Stop();
                SafeHide();
            };

            this.Deactivate += (s, e) => {
                SafeHide();
            };
        }

        private void SafeHide()
        {
            try
            {
                autoHideTimer.Stop();
                this.Hide();
            }
            catch {}
        }

        public void PositionAndFocus(int x, int y)
        {
            try
            {
                this.Location = new Point(x, y);
                if (!this.Visible)
                {
                    this.Show();
                }
                this.BringToFront();
                this.Activate();
                txtBox.Focus();
                CreateCaret(txtBox.Handle, IntPtr.Zero, 2, 16);
                SetCaretPos(2, 2);
                ShowCaret(txtBox.Handle);

                autoHideTimer.Stop();
                autoHideTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Log("AnchorForm PositionAndFocus error: " + ex.Message);
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (autoHideTimer != null)
                {
                    autoHideTimer.Dispose();
                    autoHideTimer = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
