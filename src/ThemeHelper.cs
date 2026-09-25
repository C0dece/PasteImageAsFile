using System;
using System.Drawing;
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
            get { return IsDarkTheme() ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); }
        }

        public static Color HeaderBackground
        {
            get { return IsDarkTheme() ? Color.FromArgb(38, 38, 38) : Color.FromArgb(236, 236, 236); }
        }

        public static Color CardBackground
        {
            get { return IsDarkTheme() ? Color.FromArgb(43, 43, 43) : Color.FromArgb(255, 255, 255); }
        }

        public static Color CardHover
        {
            get { return IsDarkTheme() ? Color.FromArgb(53, 53, 53) : Color.FromArgb(245, 245, 245); }
        }

        public static Color CardBorder
        {
            get { return IsDarkTheme() ? Color.FromArgb(56, 56, 56) : Color.FromArgb(226, 226, 226); }
        }

        public static Color CardBorderHover
        {
            get { return IsDarkTheme() ? Color.FromArgb(80, 80, 80) : Color.FromArgb(190, 190, 190); }
        }

        public static Color TextPrimary
        {
            get { return IsDarkTheme() ? Color.FromArgb(240, 240, 240) : Color.FromArgb(25, 25, 25); }
        }

        public static Color TextSecondary
        {
            get { return IsDarkTheme() ? Color.FromArgb(160, 160, 160) : Color.FromArgb(100, 100, 100); }
        }

        public static Color TextMuted
        {
            get { return IsDarkTheme() ? Color.FromArgb(120, 120, 120) : Color.FromArgb(140, 140, 140); }
        }

        public static Color Accent
        {
            get { return IsDarkTheme() ? Color.FromArgb(96, 205, 255) : Color.FromArgb(0, 103, 192); }
        }

        public static Color AccentHover
        {
            get { return IsDarkTheme() ? Color.FromArgb(120, 215, 255) : Color.FromArgb(0, 90, 170); }
        }

        public static Color AccentBackground
        {
            get { return IsDarkTheme() ? Color.FromArgb(30, 60, 90) : Color.FromArgb(220, 238, 255); }
        }

        public static Color ScrollBarTrack
        {
            get { return IsDarkTheme() ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); }
        }

        public static Color ScrollBarThumb
        {
            get { return IsDarkTheme() ? Color.FromArgb(65, 65, 65) : Color.FromArgb(190, 190, 190); }
        }

        public static Color ScrollBarThumbHover
        {
            get { return IsDarkTheme() ? Color.FromArgb(90, 90, 90) : Color.FromArgb(160, 160, 160); }
        }

        public static Color ScrollBarThumbActive
        {
            get { return IsDarkTheme() ? Color.FromArgb(120, 120, 120) : Color.FromArgb(130, 130, 130); }
        }

        public static Color BadgeBackground
        {
            get { return IsDarkTheme() ? Color.FromArgb(50, 50, 50) : Color.FromArgb(232, 232, 232); }
        }

        public static Color BadgeText
        {
            get { return IsDarkTheme() ? Color.FromArgb(200, 200, 200) : Color.FromArgb(70, 70, 70); }
        }

        public static Color Separator
        {
            get { return IsDarkTheme() ? Color.FromArgb(48, 48, 48) : Color.FromArgb(222, 222, 222); }
        }

        public static Color ButtonHover
        {
            get { return IsDarkTheme() ? Color.FromArgb(55, 55, 55) : Color.FromArgb(230, 230, 230); }
        }

        public static Color ButtonActive
        {
            get { return IsDarkTheme() ? Color.FromArgb(65, 65, 65) : Color.FromArgb(215, 215, 215); }
        }

        public static Color ButtonPressed
        {
            get { return ButtonActive; }
        }
    }
}
