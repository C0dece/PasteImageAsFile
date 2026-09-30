using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PasteImageAsFile
{
    public static class ThemeHelper
    {
        public static event Action ThemeChanged;

        static ThemeHelper()
        {
            try
            {
                SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle)
                    {
                        var handler = ThemeChanged;
                        if (handler != null) handler();
                    }
                };
            }
            catch {}
        }

        public static void NotifyThemeChanged()
        {
            try
            {
                var handler = ThemeChanged;
                if (handler != null) handler();
            }
            catch {}
        }

        public static bool IsDarkTheme()
        {
            string mode = Config.ThemeMode;
            if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase)) return false;

            // System default
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AppsUseLightTheme");
                        if (val is int)
                        {
                            return ((int)val) == 0;
                        }
                    }
                }
            }
            catch {}

            return true; // Default dark
        }

        // Colors based on current theme
        public static Color Background
        {
            get { return IsDarkTheme() ? Color.FromArgb(14, 22, 34) : Color.FromArgb(243, 244, 246); }
        }

        public static Color HeaderBackground
        {
            get { return IsDarkTheme() ? Color.FromArgb(16, 25, 38) : Color.FromArgb(236, 238, 242); }
        }

        public static Color CardBackground
        {
            get { return IsDarkTheme() ? Color.FromArgb(19, 30, 43) : Color.FromArgb(255, 255, 255); }
        }

        public static Color CardHover
        {
            get { return IsDarkTheme() ? Color.FromArgb(24, 38, 55) : Color.FromArgb(245, 247, 250); }
        }

        public static Color CardBorder
        {
            get { return IsDarkTheme() ? Color.FromArgb(30, 45, 64) : Color.FromArgb(222, 226, 232); }
        }

        public static Color CardBorderHover
        {
            get { return IsDarkTheme() ? Color.FromArgb(46, 68, 96) : Color.FromArgb(185, 195, 208); }
        }

        public static Color TextPrimary
        {
            get { return IsDarkTheme() ? Color.FromArgb(242, 246, 250) : Color.FromArgb(25, 28, 32); }
        }

        public static Color TextSecondary
        {
            get { return IsDarkTheme() ? Color.FromArgb(122, 142, 166) : Color.FromArgb(98, 110, 124); }
        }

        public static Color TextMuted
        {
            get { return IsDarkTheme() ? Color.FromArgb(86, 106, 128) : Color.FromArgb(140, 148, 158); }
        }

        public static Color Accent
        {
            get
            {
                string custom = Config.AccentColor;
                if (!string.IsNullOrEmpty(custom))
                {
                    try
                    {
                        return ColorTranslator.FromHtml(custom);
                    }
                    catch {}
                }
                return IsDarkTheme() ? Color.FromArgb(0, 162, 237) : Color.FromArgb(0, 120, 215);
            }
        }

        public static Color AccentHover
        {
            get
            {
                Color baseCol = Accent;
                if (IsDarkTheme())
                {
                    int r = Math.Min(255, baseCol.R + 25);
                    int g = Math.Min(255, baseCol.G + 25);
                    int b = Math.Min(255, baseCol.B + 18);
                    return Color.FromArgb(r, g, b);
                }
                else
                {
                    int r = Math.Max(0, baseCol.R - 25);
                    int g = Math.Max(0, baseCol.G - 25);
                    int b = Math.Max(0, baseCol.B - 25);
                    return Color.FromArgb(r, g, b);
                }
            }
        }

        public static Color AccentBackground
        {
            get
            {
                Color baseCol = Accent;
                if (IsDarkTheme())
                {
                    return Color.FromArgb(12, 76, 119);
                }
                else
                {
                    int r = Math.Min(255, baseCol.R + (255 - baseCol.R) * 4 / 5);
                    int g = Math.Min(255, baseCol.G + (255 - baseCol.G) * 4 / 5);
                    int b = Math.Min(255, baseCol.B + (255 - baseCol.B) * 4 / 5);
                    return Color.FromArgb(r, g, b);
                }
            }
        }

        public static Color ScrollBarTrack
        {
            get { return IsDarkTheme() ? Color.FromArgb(14, 22, 34) : Color.FromArgb(243, 244, 246); }
        }

        public static Color ScrollBarThumb
        {
            get { return IsDarkTheme() ? Color.FromArgb(45, 62, 85) : Color.FromArgb(190, 195, 202); }
        }

        public static Color ScrollBarThumbHover
        {
            get { return IsDarkTheme() ? Color.FromArgb(65, 88, 118) : Color.FromArgb(160, 168, 178); }
        }

        public static Color ScrollBarThumbActive
        {
            get { return IsDarkTheme() ? Color.FromArgb(90, 120, 160) : Color.FromArgb(130, 140, 152); }
        }

        public static Color BadgeBackground
        {
            get { return IsDarkTheme() ? Color.FromArgb(24, 38, 55) : Color.FromArgb(232, 235, 240); }
        }

        public static Color BadgeText
        {
            get { return IsDarkTheme() ? Color.FromArgb(180, 200, 225) : Color.FromArgb(60, 75, 95); }
        }

        public static Color Separator
        {
            get { return IsDarkTheme() ? Color.FromArgb(30, 45, 64) : Color.FromArgb(222, 226, 232); }
        }

        public static Color ButtonHover
        {
            get { return IsDarkTheme() ? Color.FromArgb(26, 40, 58) : Color.FromArgb(230, 234, 240); }
        }

        public static Color ButtonActive
        {
            get { return IsDarkTheme() ? Color.FromArgb(34, 52, 75) : Color.FromArgb(215, 222, 230); }
        }

        public static Color ButtonPressed
        {
            get { return ButtonActive; }
        }

        public static void ApplyButtonStyle(Button btn, bool isActive = false)
        {
            if (btn == null) return;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ButtonHover;
            btn.FlatAppearance.MouseDownBackColor = ButtonPressed;
            if (isActive)
            {
                btn.BackColor = AccentBackground;
                btn.ForeColor = Accent;
            }
        }

        public static ToolTip CreateFluentToolTip()
        {
            var tt = new ToolTip();
            tt.AutoPopDelay = 6000;
            tt.InitialDelay = 300;
            tt.ReshowDelay = 150;
            tt.OwnerDraw = true;

            tt.Draw += (s, e) => {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                bool isDark = IsDarkTheme();
                Color bg = isDark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(250, 250, 250);
                Color border = isDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(205, 205, 205);
                Color textCol = isDark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(25, 25, 25);

                Rectangle rect = new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                using (var b = new SolidBrush(bg))
                {
                    g.FillRectangle(b, e.Bounds);
                }
                using (var p = new Pen(border, 1))
                {
                    g.DrawRectangle(p, rect);
                }

                TextRenderer.DrawText(g, e.ToolTipText, new Font("Segoe UI", 9f),
                    new Rectangle(8, 4, e.Bounds.Width - 16, e.Bounds.Height - 8),
                    textCol, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };

            tt.Popup += (s, e) => {
                string text = tt.GetToolTip(e.AssociatedControl);
                Size sz = TextRenderer.MeasureText(text, new Font("Segoe UI", 9f));
                e.ToolTipSize = new Size(sz.Width + 18, Math.Max(24, sz.Height + 10));
            };

            return tt;
        }
    }
}
