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

        public static string DefaultPrefix
        {
            get { return GetString("DefaultPrefix", "Снимок"); }
            set { SetString("DefaultPrefix", string.IsNullOrEmpty(value) ? "Снимок" : value); }
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
