using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace PasteImageAsFile
{
    public static class FileIconHelper
    {
        private static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly object cacheLock = new object();

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Image GetIconForPath(string path, bool isLarge = false)
        {
            if (string.IsNullOrEmpty(path)) return SystemIcons.Application.ToBitmap();

            string key = path;
            bool isDir = false;
            try
            {
                if (Directory.Exists(path))
                {
                    key = ":folder:";
                    isDir = true;
                }
                else
                {
                    key = Path.GetExtension(path).ToLowerInvariant();
                    if (string.IsNullOrEmpty(key)) key = ":file:";
                }
            }
            catch
            {
                key = ":file:";
            }

            lock (cacheLock)
            {
                if (cache.ContainsKey(key))
                {
                    try
                    {
                        var cached = cache[key];
                        if (cached != null) return new Bitmap(cached);
                    }
                    catch
                    {
                        cache.Remove(key);
                    }
                }
            }

            Image img = ExtractIcon(path, isDir);
            if (img == null) img = SystemIcons.Application.ToBitmap();

            lock (cacheLock)
            {
                if (!cache.ContainsKey(key)) cache[key] = img;
            }

            try
            {
                return new Bitmap(img);
            }
            catch
            {
                return SystemIcons.Application.ToBitmap();
            }
        }

        private static Image ExtractIcon(string path, bool isDir)
        {
            try
            {
                SHFILEINFO shinfo = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_SMALLICON;
                uint attrs = isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                }

                IntPtr res = SHGetFileInfo(path, attrs, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    using (Icon ico = Icon.FromHandle(shinfo.hIcon))
                    {
                        Bitmap bmp = new Bitmap(ico.ToBitmap());
                        DestroyIcon(shinfo.hIcon);
                        return bmp;
                    }
                }
            }
            catch {}

            try
            {
                if (File.Exists(path))
                {
                    using (Icon ico = Icon.ExtractAssociatedIcon(path))
                    {
                        if (ico != null) return new Bitmap(ico.ToBitmap());
                    }
                }
            }
            catch {}

            return SystemIcons.Application.ToBitmap();
        }
    }
}
