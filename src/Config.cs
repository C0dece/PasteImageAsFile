using System;
using Microsoft.Win32;

namespace PasteImageAsFile
{
    public static class Config
    {
        const string RegKeyPath = @"Software\PasteImageAsFile";

        public static bool AutoRun
        {
            get { return GetDword("AutoRun", 1) == 1; }
            set { SetDword("AutoRun", value ? 1 : 0); }
        }

        public static bool ContextMenu
        {
            get { return GetDword("ContextMenu", 1) == 1; }
            set { SetDword("ContextMenu", value ? 1 : 0); }
        }

        public static bool PlaceUnderCursor
        {
            get { return GetDword("PlaceUnderCursor", 1) == 1; }
            set { SetDword("PlaceUnderCursor", value ? 1 : 0); }
        }

        public static bool ExtractOriginalName
        {
            get { return GetDword("ExtractOriginalName", 1) == 1; }
            set { SetDword("ExtractOriginalName", value ? 1 : 0); }
        }

        public static bool ShowHistoryMenu
        {
            get { return GetDword("ShowHistoryMenu", 1) == 1; }
            set { SetDword("ShowHistoryMenu", value ? 1 : 0); }
        }

        public static bool TrayClickOpensClipboard
        {
            get { return GetDword("TrayClickOpensClipboard", 1) == 1; }
            set { SetDword("TrayClickOpensClipboard", value ? 1 : 0); }
        }

        public static bool ClassicContextMenuWin11
        {
            get { return GetDword("ClassicContextMenuWin11", 0) == 1; }
            set { SetDword("ClassicContextMenuWin11", value ? 1 : 0); }
        }

        public static string DefaultPrefix
        {
            get { return GetString("DefaultPrefix", "Снимок"); }
            set { SetString("DefaultPrefix", string.IsNullOrEmpty(value) ? "Снимок" : value); }
        }

        public static bool HistoryRememberText
        {
            get { return GetDword("HistoryRememberText", 1) == 1; }
            set { SetDword("HistoryRememberText", value ? 1 : 0); }
        }

        public static bool HistoryRememberFiles
        {
            get { return GetDword("HistoryRememberFiles", 1) == 1; }
            set { SetDword("HistoryRememberFiles", value ? 1 : 0); }
        }

        public static int MaxHistoryItems
        {
            get { return GetDword("MaxHistoryItems", 50); }
            set { SetDword("MaxHistoryItems", value); }
        }

        public static bool SuperHubFloatMode
        {
            get { return GetDword("SuperHubFloatMode", 0) == 1; }
            set { SetDword("SuperHubFloatMode", value ? 1 : 0); }
        }

        public static string ThemeMode
        {
            get { return GetString("ThemeMode", "System"); }
            set 
            { 
                SetString("ThemeMode", value); 
                ThemeHelper.NotifyThemeChanged();
            }
        }

        public static bool SuperHubEnabled
        {
            get { return GetDword("SuperHubEnabled", 1) == 1; }
            set { SetDword("SuperHubEnabled", value ? 1 : 0); }
        }

        public static string SuperHubPosition
        {
            get { return GetString("SuperHubPosition", "RightCenter"); }
            set { SetString("SuperHubPosition", value); }
        }

        public static string SuperHubDragMode
        {
            get { return GetString("SuperHubDragMode", "Copy"); }
            set { SetString("SuperHubDragMode", value); }
        }

        public static string ClipboardClickAction
        {
            get { return GetString("ClipboardClickAction", "Paste"); }
            set { SetString("ClipboardClickAction", value); }
        }

        public static int SuperHubSensitivity
        {
            get { return GetDword("SuperHubSensitivity", 60); }
            set { SetDword("SuperHubSensitivity", value); }
        }

        private static int GetDword(string name, int defaultValue)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegKeyPath, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(name);
                        if (val is int) return (int)val;
                    }
                }
            }
            catch {}
            return defaultValue;
        }

        private static void SetDword(string name, int value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegKeyPath))
                {
                    if (key != null) key.SetValue(name, value, RegistryValueKind.DWord);
                }
            }
            catch {}
        }

        private static string GetString(string name, string defaultValue)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegKeyPath, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(name);
                        if (val != null) return val.ToString();
                    }
                }
            }
            catch {}
            return defaultValue;
        }

        private static void SetString(string name, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegKeyPath))
                {
                    if (key != null) key.SetValue(name, value, RegistryValueKind.String);
                }
            }
            catch {}
        }
    }
}
