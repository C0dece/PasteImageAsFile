using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;

namespace PasteImageAsFile
{
    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }

    public class ItemViewerForm : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private readonly ClipboardItem item;
        private readonly string filePath;

        private Panel pnlHeader;
        private Label lblTitle;
        private Label lblSubtitle;
        private Button btnPin;
        private Button btnCopy;
        private Button btnOpenExternal;

        // Элементы для режима картинок
        private DoubleBufferedPanel pnlImageContainer;
        private Image loadedImage;
        private float zoomFactor = 1.0f;
        private Point panOffset = Point.Empty;
        private bool isPanning = false;
        private Point panStartMouse = Point.Empty;
        private Point panStartOffset = Point.Empty;
        private Label lblZoomInfo;
        private Panel pnlZoomBar;

        // Элементы для режима текста / документов
        private TextBox txtContent;
        private Panel pnlTextBottom;

        private bool isPinned = false;
        private ToolTip toolTip;

        public static void ShowViewer(ClipboardItem item, string specificPath = null)
        {
            if (item == null && string.IsNullOrEmpty(specificPath)) return;
            try
            {
                var viewer = new ItemViewerForm(item, specificPath);
                viewer.Show();
                viewer.BringToFront();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть просмотр:\n" + ex.Message, "Просмотр", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public ItemViewerForm(ClipboardItem item, string specificPath = null)
        {
            this.item = item;
            this.filePath = specificPath;
            if (string.IsNullOrEmpty(this.filePath) && item != null)
            {
                if (item.Type == ClipboardItemType.Image) this.filePath = item.ImagePath;
                else if (item.Type == ClipboardItemType.Files && item.FilePaths != null && item.FilePaths.Count > 0)
                {
                    this.filePath = item.FilePaths[0];
                }
            }

            this.Size = new Size(720, 560);
            this.MinimumSize = new Size(400, 300);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9f);
            this.ShowIcon = false;
            this.Text = "Просмотр - PasteImageAsFile";

            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 5000;
            toolTip.InitialDelay = 300;

            this.Shown += (s, e) => ApplyWindowStyles();
            ThemeHelper.ThemeChanged += () => {
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke((Action)(() => ApplyTheme()));
                }
            };

            BuildUI();
            ApplyTheme();
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
            bool isDark = ThemeHelper.IsDarkTheme();
            this.BackColor = ThemeHelper.Background;
            this.ForeColor = ThemeHelper.TextPrimary;

            if (pnlHeader != null)
            {
                pnlHeader.BackColor = ThemeHelper.HeaderBackground;
                lblTitle.ForeColor = ThemeHelper.TextPrimary;
                lblSubtitle.ForeColor = ThemeHelper.TextSecondary;
            }

            if (pnlZoomBar != null)
            {
                pnlZoomBar.BackColor = ThemeHelper.CardBackground;
                lblZoomInfo.ForeColor = ThemeHelper.TextPrimary;
            }

            if (txtContent != null)
            {
                txtContent.BackColor = isDark ? Color.FromArgb(24, 24, 24) : Color.FromArgb(255, 255, 255);
                txtContent.ForeColor = isDark ? Color.FromArgb(230, 230, 230) : Color.FromArgb(20, 20, 20);
            }

            if (pnlImageContainer != null)
            {
                pnlImageContainer.BackColor = isDark ? Color.FromArgb(18, 18, 18) : Color.FromArgb(240, 240, 240);
                pnlImageContainer.Invalidate();
            }

            UpdatePinVisual();
            this.Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutControls();
        }

        private void LayoutControls()
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            if (pnlHeader != null)
            {
                pnlHeader.Bounds = new Rectangle(0, 0, w, 46);
                RelayoutHeaderButtons();
            }

            int topY = 46;

            if (IsImageItem())
            {
                int bottomH = 36;
                if (pnlZoomBar != null)
                {
                    pnlZoomBar.Bounds = new Rectangle(0, h - bottomH, w, bottomH);
                }
                int contentH = Math.Max(20, h - topY - bottomH);
                if (pnlImageContainer != null)
                {
                    pnlImageContainer.Bounds = new Rectangle(0, topY, w, contentH);
                }
            }
            else
            {
                int bottomH = 34;
                if (pnlTextBottom != null)
                {
                    pnlTextBottom.Bounds = new Rectangle(0, h - bottomH, w, bottomH);
                }
                int contentH = Math.Max(20, h - topY - bottomH);
                if (txtContent != null)
                {
                    txtContent.Bounds = new Rectangle(0, topY, w, contentH);
                }
            }
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            // 1. Шапка (Header) - 46px
            pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(this.ClientSize.Width, 46),
                BackColor = Color.FromArgb(30, 30, 30)
            };

            lblTitle = new Label
            {
                Location = new Point(12, 6),
                Size = new Size(this.Width - 180, 18),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                AutoEllipsis = true,
                Text = GetDisplayTitle()
            };
            pnlHeader.Controls.Add(lblTitle);

            lblSubtitle = new Label
            {
                Location = new Point(12, 25),
                Size = new Size(this.Width - 180, 16),
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(160, 160, 160),
                AutoEllipsis = true,
                Text = GetDisplaySubtitle()
            };
            pnlHeader.Controls.Add(lblSubtitle);

            // Кнопка Закрепить
            btnPin = CreateHeaderButton("Закрепить", this.Width - 190, 8, 86, 28, "Закрепить окно поверх остальных (чтобы постоянно видеть)");
            btnPin.MouseEnter += (s, e) => {
                if (!isPinned) btnPin.BackColor = ThemeHelper.ButtonHover;
            };
            btnPin.MouseLeave += (s, e) => {
                if (!isPinned) { btnPin.BackColor = Color.Transparent; btnPin.ForeColor = ThemeHelper.TextSecondary; }
                else { btnPin.BackColor = ThemeHelper.AccentBackground; btnPin.ForeColor = ThemeHelper.Accent; }
            };
            btnPin.Click += (s, e) => {
                isPinned = !isPinned;
                UpdatePinVisual();
            };
            pnlHeader.Controls.Add(btnPin);

            // Кнопка Копия
            btnCopy = CreateHeaderButton("Копия", this.Width - 96, 8, 56, 28, "Скопировать содержимое в буфер обмена");
            btnCopy.Click += (s, e) => CopyContentToClipboard();
            pnlHeader.Controls.Add(btnCopy);

            // Кнопка [↗] Открыть в ассоциированной программе
            btnOpenExternal = CreateHeaderButton("↗", this.Width - 38, 8, 32, 28, "Открыть файл в стандартной программе Windows");
            btnOpenExternal.Click += (s, e) => OpenExternal();
            pnlHeader.Controls.Add(btnOpenExternal);

            pnlHeader.Resize += (s, e) => RelayoutHeaderButtons();
            pnlHeader.Paint += (s, e) => {
                if (isPinned)
                {
                    using (var brush = new SolidBrush(ThemeHelper.Accent))
                    {
                        e.Graphics.FillRectangle(brush, 0, pnlHeader.Height - 2, pnlHeader.Width, 2);
                    }
                }
            };
            this.Controls.Add(pnlHeader);

            UpdatePinVisual();

            // 2. Определение режима отображения: картинка или текст/документ
            if (IsImageItem())
            {
                BuildImageViewer();
            }
            else
            {
                BuildTextViewer();
            }

            LayoutControls();
            this.ResumeLayout(false);
        }

        private void UpdatePinVisual()
        {
            if (btnPin == null) return;
            this.TopMost = isPinned;
            if (isPinned)
            {
                btnPin.Text = "Закреплено";
                btnPin.Width = 96;
                btnPin.BackColor = ThemeHelper.AccentBackground;
                btnPin.ForeColor = ThemeHelper.Accent;
                btnPin.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
                toolTip.SetToolTip(btnPin, "Окно закреплено поверх всех (кликните для открепления)");
                this.Text = "[Закреплено] " + GetDisplayTitle() + " - PasteImageAsFile";
                if (lblTitle != null) lblTitle.Text = GetDisplayTitle();
            }
            else
            {
                btnPin.Text = "Закрепить";
                btnPin.Width = 86;
                btnPin.BackColor = Color.Transparent;
                btnPin.ForeColor = ThemeHelper.TextSecondary;
                btnPin.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
                toolTip.SetToolTip(btnPin, "Закрепить окно поверх остальных");
                this.Text = GetDisplayTitle() + " - PasteImageAsFile";
                if (lblTitle != null) lblTitle.Text = GetDisplayTitle();
            }
            RelayoutHeaderButtons();
            if (pnlHeader != null) pnlHeader.Invalidate();
        }

        private void RelayoutHeaderButtons()
        {
            if (pnlHeader == null || btnOpenExternal == null || btnCopy == null || btnPin == null) return;
            btnOpenExternal.Left = pnlHeader.Width - btnOpenExternal.Width - 8;
            btnCopy.Left = btnOpenExternal.Left - btnCopy.Width - 6;
            btnPin.Left = btnCopy.Left - btnPin.Width - 8;
            if (lblTitle != null) lblTitle.Width = Math.Max(100, btnPin.Left - 20);
            if (lblSubtitle != null) lblSubtitle.Width = Math.Max(100, btnPin.Left - 20);
        }

        private string GetDisplayTitle()
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                return Path.GetFileName(filePath);
            }
            if (item != null && item.Type == ClipboardItemType.Text)
            {
                return "Текстовая заметка";
            }
            return "Просмотр содержимого";
        }

        private string GetDisplaySubtitle()
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    var fi = new FileInfo(filePath);
                    return string.Format("{0} • {1}", FormatFileSize(fi.Length), filePath);
                }
                catch {}
            }
            if (item != null && item.Type == ClipboardItemType.Text)
            {
                int len = (item.TextContent != null) ? item.TextContent.Length : 0;
                return string.Format("Длина текста: {0} символов", len);
            }
            return "";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return bytes + " Б";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " КБ";
            return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " МБ";
        }

        private bool IsImageItem()
        {
            if (item != null && item.Type == ClipboardItemType.Image) return true;
            if (!string.IsNullOrEmpty(filePath))
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".webp" || ext == ".ico";
            }
            return false;
        }

        // ==================== РЕЖИМ ПРОСМОТРА КАРТИНОК ====================

        private void BuildImageViewer()
        {
            LoadImage();

            pnlImageContainer = new DoubleBufferedPanel
            {
                BackColor = Color.FromArgb(18, 18, 18)
            };

            pnlImageContainer.Paint += (s, e) => RenderImage(e.Graphics);

            // Зум колесиком мыши
            pnlImageContainer.MouseWheel += (s, e) => {
                if (loadedImage == null) return;
                Point mousePos = pnlImageContainer.PointToClient(Cursor.Position);

                float oldZoom = zoomFactor;
                if (e.Delta > 0) zoomFactor *= 1.2f;
                else zoomFactor /= 1.2f;

                zoomFactor = Math.Max(0.05f, Math.Min(20.0f, zoomFactor));

                // Зум с центровкой на курсор мыши
                panOffset.X = (int)(mousePos.X - (mousePos.X - panOffset.X) * (zoomFactor / oldZoom));
                panOffset.Y = (int)(mousePos.Y - (mousePos.Y - panOffset.Y) * (zoomFactor / oldZoom));

                UpdateZoomLabel();
                pnlImageContainer.Invalidate();
            };

            // Панорамирование зажатой левой кнопкой мыши
            pnlImageContainer.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    isPanning = true;
                    panStartMouse = e.Location;
                    panStartOffset = panOffset;
                    pnlImageContainer.Cursor = Cursors.SizeAll;
                }
            };

            pnlImageContainer.MouseMove += (s, e) => {
                if (isPanning)
                {
                    panOffset.X = panStartOffset.X + (e.X - panStartMouse.X);
                    panOffset.Y = panStartOffset.Y + (e.Y - panStartMouse.Y);
                    pnlImageContainer.Invalidate();
                }
            };

            pnlImageContainer.MouseUp += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    isPanning = false;
                    pnlImageContainer.Cursor = Cursors.Default;
                }
            };

            // Нижняя панель инструментов зума - 36px
            pnlZoomBar = new Panel
            {
                BackColor = Color.FromArgb(28, 28, 28)
            };

            Button btnZoomOut = CreateZoomButton("－", 12, 6, 28, 24, "Уменьшить масштаб");
            btnZoomOut.Click += (s, e) => ZoomRelative(1.0f / 1.25f);
            pnlZoomBar.Controls.Add(btnZoomOut);

            lblZoomInfo = new Label
            {
                Location = new Point(46, 9),
                Size = new Size(60, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 8.5f),
                Text = "100%"
            };
            pnlZoomBar.Controls.Add(lblZoomInfo);

            Button btnZoomIn = CreateZoomButton("＋", 112, 6, 28, 24, "Увеличить масштаб");
            btnZoomIn.Click += (s, e) => ZoomRelative(1.25f);
            pnlZoomBar.Controls.Add(btnZoomIn);

            Button btnFit = CreateZoomButton("По окну", 148, 6, 76, 24, "Вписать картинку по размеру окна");
            btnFit.Click += (s, e) => FitImageToWindow();
            pnlZoomBar.Controls.Add(btnFit);

            Button btnActual = CreateZoomButton("100%", 230, 6, 52, 24, "Оригинальный масштаб 1:1");
            btnActual.Click += (s, e) => ResetZoomToActual();
            pnlZoomBar.Controls.Add(btnActual);

            this.Controls.Add(pnlImageContainer);
            this.Controls.Add(pnlZoomBar);
            LayoutControls();

            // Начальное вписывание картинки
            this.Shown += (s, e) => FitImageToWindow();
        }

        private void LoadImage()
        {
            try
            {
                string path = filePath;
                if (string.IsNullOrEmpty(path) && item != null) path = item.ImagePath;

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var bmp = new Bitmap(fs);
                        loadedImage = new Bitmap(bmp);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("LoadImage error: " + ex.Message);
            }
        }

        private void RenderImage(Graphics g)
        {
            g.InterpolationMode = (zoomFactor >= 1.0f) ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            if (loadedImage != null)
            {
                int dw = (int)(loadedImage.Width * zoomFactor);
                int dh = (int)(loadedImage.Height * zoomFactor);
                g.DrawImage(loadedImage, panOffset.X, panOffset.Y, dw, dh);
            }
            else
            {
                using (var b = new SolidBrush(ThemeHelper.TextSecondary))
                {
                    g.DrawString("Не удалось загрузить изображение", this.Font, b, 20, 20);
                }
            }
        }

        private void ZoomRelative(float factor)
        {
            if (loadedImage == null || pnlImageContainer == null) return;
            Point center = new Point(pnlImageContainer.Width / 2, pnlImageContainer.Height / 2);
            float oldZoom = zoomFactor;
            zoomFactor = Math.Max(0.05f, Math.Min(20.0f, zoomFactor * factor));

            panOffset.X = (int)(center.X - (center.X - panOffset.X) * (zoomFactor / oldZoom));
            panOffset.Y = (int)(center.Y - (center.Y - panOffset.Y) * (zoomFactor / oldZoom));

            UpdateZoomLabel();
            pnlImageContainer.Invalidate();
        }

        private void FitImageToWindow()
        {
            if (loadedImage == null || pnlImageContainer == null) return;
            int cw = pnlImageContainer.Width;
            int ch = pnlImageContainer.Height;
            if (cw <= 0 || ch <= 0) return;

            float zx = (float)(cw - 32) / loadedImage.Width;
            float zy = (float)(ch - 32) / loadedImage.Height;
            zoomFactor = Math.Min(zx, zy);
            if (zoomFactor > 1.0f) zoomFactor = 1.0f;

            int dw = (int)(loadedImage.Width * zoomFactor);
            int dh = (int)(loadedImage.Height * zoomFactor);
            panOffset = new Point((cw - dw) / 2, (ch - dh) / 2);

            UpdateZoomLabel();
            pnlImageContainer.Invalidate();
        }

        private void ResetZoomToActual()
        {
            if (loadedImage == null || pnlImageContainer == null) return;
            zoomFactor = 1.0f;
            int cw = pnlImageContainer.Width;
            int ch = pnlImageContainer.Height;
            panOffset = new Point((cw - loadedImage.Width) / 2, (ch - loadedImage.Height) / 2);

            UpdateZoomLabel();
            pnlImageContainer.Invalidate();
        }

        private void UpdateZoomLabel()
        {
            if (lblZoomInfo != null)
            {
                lblZoomInfo.Text = (int)(zoomFactor * 100) + "%";
            }
        }

        // ==================== РЕЖИМ ПРОСМОТРА ТЕКСТА И ДОКУМЕНТОВ ====================

        private void BuildTextViewer()
        {
            txtContent = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                Font = new Font("Consolas", 10f),
                BorderStyle = BorderStyle.None,
                WordWrap = true
            };

            string text = ExtractTextContent();
            txtContent.Text = text;
            txtContent.Select(0, 0);

            // Нижняя информационная панель - 34px
            pnlTextBottom = new Panel
            {
                BackColor = Color.FromArgb(28, 28, 28)
            };

            Label lblStats = new Label
            {
                Location = new Point(12, 8),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = ThemeHelper.TextSecondary,
                Text = string.Format("Строк: {0} • Символов: {1}", txtContent.Lines.Length, text.Length)
            };
            pnlTextBottom.Controls.Add(lblStats);

            Button btnCopyAll = CreateZoomButton("Копировать весь текст", this.Width - 190, 4, 160, 26, "Скопировать полный текст документа в буфер");
            btnCopyAll.Click += (s, e) => {
                if (!string.IsNullOrEmpty(txtContent.Text))
                {
                    Clipboard.SetText(txtContent.Text);
                    MessageBox.Show("Весь текст скопирован в буфер обмена!", "Просмотр", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            pnlTextBottom.Controls.Add(btnCopyAll);

            Button btnOpenNotepad = CreateZoomButton("Блокнот", this.Width - 286, 4, 90, 26, "Открыть текст во внешнем Блокноте Windows");
            btnOpenNotepad.Click += (s, e) => OpenExternal();
            pnlTextBottom.Controls.Add(btnOpenNotepad);

            pnlTextBottom.Resize += (s, e) => {
                btnCopyAll.Left = pnlTextBottom.Width - 175;
                btnOpenNotepad.Left = btnCopyAll.Left - 96;
            };

            this.Controls.Add(txtContent);
            this.Controls.Add(pnlTextBottom);
            LayoutControls();
        }

        private string ExtractTextContent()
        {
            if (item != null && item.Type == ClipboardItemType.Text && !string.IsNullOrEmpty(item.TextContent))
            {
                return item.TextContent;
            }

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return "Файл не найден или недоступен.";
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            // 1. Формат .DOCX (ZIP контейнер с word/document.xml)
            if (ext == ".docx")
            {
                return ExtractTextFromDocx(filePath);
            }

            // 2. Формат .DOC (старый бинарный Word)
            if (ext == ".doc")
            {
                return ExtractTextFromDoc(filePath);
            }

            // 3. Чтение файла с совместным доступом FileShare.ReadWrite (не блокирует и не падает на занятых файлах)
            try
            {
                byte[] bytes = ReadAllBytesShared(filePath);
                if (bytes == null || bytes.Length == 0) return "[Файл пуст]";

                // 4. Формат .XML (форматирование с отступами)
                if (ext == ".xml")
                {
                    string rawXml = DecodeTextFromBytes(bytes);
                    return FormatXml(rawXml);
                }

                // 5. Формат .JSON (форматирование структуры)
                if (ext == ".json")
                {
                    string rawJson = DecodeTextFromBytes(bytes);
                    return FormatJson(rawJson);
                }

                // 6. Проверка: если файл двоичный (содержит 0x00 байты в начале)
                if (IsBinaryFile(bytes))
                {
                    return string.Format("[Двоичный файл: {0} ({1})]\r\n\r\nСодержимое этого файла имеет двоичный формат и не может быть отображено как текст.\r\nНажмите кнопку '↗' в правом верхнем углу, чтобы открыть файл в ассоциированной программе Windows.",
                        Path.GetFileName(filePath), FormatFileSize(bytes.Length));
                }

                // 7. Обычный текст / код (.txt, .log, .ini, .cfg, .html, .css, .js, .ts, .cs, .py, .cpp, .sql, .md, .yml, .bat, .ps1 и т.д.)
                return DecodeTextFromBytes(bytes);
            }
            catch (Exception ex)
            {
                return "Ошибка при чтении файла:\n" + ex.Message;
            }
        }

        private static byte[] ReadAllBytesShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] buf = new byte[fs.Length];
                int total = 0;
                while (total < buf.Length)
                {
                    int n = fs.Read(buf, total, buf.Length - total);
                    if (n <= 0) break;
                    total += n;
                }
                return buf;
            }
        }

        private static string DecodeTextFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";

            // Проверка BOM: UTF-8
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            // UTF-16 LE
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            // Попытка раскодировать как UTF-8
            string utf8Text = Encoding.UTF8.GetString(bytes);
            if (!utf8Text.Contains("\uFFFD"))
            {
                return utf8Text;
            }
            // Иначе Windows-1251
            try
            {
                return Encoding.GetEncoding(1251).GetString(bytes);
            }
            catch
            {
                return utf8Text;
            }
        }

        private static bool IsBinaryFile(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return false;
            int checkLen = Math.Min(bytes.Length, 4096);
            for (int i = 0; i < checkLen; i++)
            {
                if (bytes[i] == 0) return true;
            }
            return false;
        }

        private static string FormatXml(string rawXml)
        {
            if (string.IsNullOrEmpty(rawXml)) return "[XML файл пуст]";
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(rawXml);
                var sb = new StringBuilder();
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    NewLineChars = "\r\n",
                    OmitXmlDeclaration = false
                };
                using (var writer = XmlWriter.Create(sb, settings))
                {
                    doc.Save(writer);
                }
                return sb.ToString();
            }
            catch
            {
                // Если парсинг XML завершился с ошибкой (например фрагмент), возвращаем исходный текст
                return rawXml;
            }
        }

        private static string FormatJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return "[JSON пуст]";
            try
            {
                var sb = new StringBuilder();
                int indent = 0;
                bool inQuotes = false;
                for (int i = 0; i < json.Length; i++)
                {
                    char ch = json[i];
                    if (ch == '\\' && i + 1 < json.Length && inQuotes)
                    {
                        sb.Append(ch);
                        sb.Append(json[++i]);
                        continue;
                    }
                    if (ch == '"')
                    {
                        inQuotes = !inQuotes;
                        sb.Append(ch);
                        continue;
                    }
                    if (!inQuotes)
                    {
                        if (ch == '{' || ch == '[')
                        {
                            sb.Append(ch);
                            sb.AppendLine();
                            indent++;
                            sb.Append(new string(' ', indent * 2));
                            continue;
                        }
                        if (ch == '}' || ch == ']')
                        {
                            sb.AppendLine();
                            indent--;
                            if (indent < 0) indent = 0;
                            sb.Append(new string(' ', indent * 2));
                            sb.Append(ch);
                            continue;
                        }
                        if (ch == ',')
                        {
                            sb.Append(ch);
                            sb.AppendLine();
                            sb.Append(new string(' ', indent * 2));
                            continue;
                        }
                        if (ch == ':')
                        {
                            sb.Append(": ");
                            continue;
                        }
                        if (char.IsWhiteSpace(ch)) continue;
                    }
                    sb.Append(ch);
                }
                return sb.ToString().Trim();
            }
            catch
            {
                return json;
            }
        }

        private static string ExtractTextFromDocx(string docxPath)
        {
            try
            {
                using (var fs = new FileStream(docxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    // Ищем запись word/document.xml внутри zip
                    byte[] docBytes = ExtractZipEntry(fs, "word/document.xml");
                    if (docBytes != null && docBytes.Length > 0)
                    {
                        string xml = Encoding.UTF8.GetString(docBytes);
                        // Заменяем параграфы на переводы строк
                        xml = Regex.Replace(xml, @"</w:p>", "\r\n");
                        xml = Regex.Replace(xml, @"<w:tab/>", "\t");
                        xml = Regex.Replace(xml, @"<w:br/>", "\r\n");
                        // Извлекаем чистый текст тегов
                        string plainText = Regex.Replace(xml, @"<[^>]+>", "");
                        plainText = System.Net.WebUtility.HtmlDecode(plainText);
                        return plainText.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ExtractTextFromDocx error: " + ex.Message);
            }
            return "[Не удалось извлечь текст из docx]";
        }

        private static byte[] ExtractZipEntry(Stream stream, string entryName)
        {
            using (var br = new BinaryReader(stream))
            {
                while (stream.Position < stream.Length - 30)
                {
                    uint sig = br.ReadUInt32();
                    if (sig == 0x04034b50) // Local file header signature PK\x03\x04
                    {
                        stream.Seek(14, SeekOrigin.Current);
                        uint compSize = br.ReadUInt32();
                        uint uncompSize = br.ReadUInt32();
                        ushort nameLen = br.ReadUInt16();
                        ushort extraLen = br.ReadUInt16();
                        byte[] nameBytes = br.ReadBytes(nameLen);
                        string name = Encoding.UTF8.GetString(nameBytes);
                        stream.Seek(extraLen, SeekOrigin.Current);

                        if (string.Equals(name, entryName, StringComparison.OrdinalIgnoreCase))
                        {
                            byte[] compData = br.ReadBytes((int)compSize);
                            using (var msIn = new MemoryStream(compData))
                            using (var deflate = new DeflateStream(msIn, CompressionMode.Decompress))
                            using (var msOut = new MemoryStream())
                            {
                                byte[] buf = new byte[4096];
                                int read;
                                while ((read = deflate.Read(buf, 0, buf.Length)) > 0)
                                {
                                    msOut.Write(buf, 0, read);
                                }
                                return msOut.ToArray();
                            }
                        }
                        else
                        {
                            stream.Seek(compSize, SeekOrigin.Current);
                        }
                    }
                    else
                    {
                        stream.Seek(1, SeekOrigin.Current);
                    }
                }
            }
            return null;
        }

        private static string ExtractTextFromDoc(string docPath)
        {
            try
            {
                byte[] bytes = ReadAllBytesShared(docPath);
                var sb = new StringBuilder();
                int minLen = 4;
                var current = new StringBuilder();

                // Сканируем читаемый текст Unicode и ASCII
                for (int i = 0; i < bytes.Length; i++)
                {
                    byte b = bytes[i];
                    if (b >= 32 && b <= 126 || b >= 192 || b == 10 || b == 13 || b == 9)
                    {
                        current.Append((char)b);
                    }
                    else
                    {
                        if (current.Length >= minLen)
                        {
                            sb.AppendLine(current.ToString());
                        }
                        current.Length = 0;
                    }
                }
                if (current.Length >= minLen) sb.AppendLine(current.ToString());

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result)) return result;
            }
            catch {}
            return "[Бинарный документ Word 97-2003. Нажмите '↗' для открытия в Word]";
        }

        private void CopyContentToClipboard()
        {
            try
            {
                if (IsImageItem() && loadedImage != null)
                {
                    Clipboard.SetImage(loadedImage);
                    MessageBox.Show("Изображение скопировано в буфер обмена!", "Просмотр", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (txtContent != null && !string.IsNullOrEmpty(txtContent.Text))
                {
                    if (txtContent.SelectionLength > 0)
                    {
                        Clipboard.SetText(txtContent.SelectedText);
                        MessageBox.Show("Выделенный текст скопирован в буфер обмена!", "Просмотр", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        Clipboard.SetText(txtContent.Text);
                        MessageBox.Show("Текст скопирован в буфер обмена!", "Просмотр", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка копирования: " + ex.Message, "Просмотр", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenExternal()
        {
            try
            {
                if (item != null && item.Type == ClipboardItemType.Text)
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "PasteImageAsFile");
                    if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                    string tempFile = Path.Combine(tempDir, string.Format("Заметка_{0:yyyyMMdd_HHmmss}.txt", DateTime.Now));
                    File.WriteAllText(tempFile, item.TextContent ?? "", Encoding.UTF8);
                    Process.Start(new ProcessStartInfo("notepad.exe", "\"" + tempFile + "\"") { UseShellExecute = true });
                    return;
                }

                if (!string.IsNullOrEmpty(filePath) && (File.Exists(filePath) || Directory.Exists(filePath)))
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось запустить файл:\n" + ex.Message, "Просмотр", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private Button CreateHeaderButton(string text, int x, int y, int w, int h, string tip)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ThemeHelper.TextSecondary,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 45, 45);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 60, 60);
            btn.MouseEnter += (s, e) => btn.ForeColor = ThemeHelper.TextPrimary;
            btn.MouseLeave += (s, e) => btn.ForeColor = ThemeHelper.TextSecondary;
            toolTip.SetToolTip(btn, tip);
            return btn;
        }

        private Button CreateZoomButton(string text, int x, int y, int w, int h, string tip)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeHelper.CardBackground,
                ForeColor = ThemeHelper.TextPrimary,
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = ThemeHelper.CardBorder;
            btn.MouseEnter += (s, e) => btn.BackColor = ThemeHelper.ButtonHover;
            btn.MouseLeave += (s, e) => btn.BackColor = ThemeHelper.CardBackground;
            toolTip.SetToolTip(btn, tip);
            return btn;
        }
    }
}
