using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace PasteImageAsFile
{
    public static class NameHelper
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(DesktopHelper.POINT Point);

        [DllImport("user32.dll", ExactSpelling = true)]
        static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        const uint GA_ROOT = 2;

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                name = Uri.UnescapeDataString(name);
            }
            catch {}

            // Удаляем HTML теги и спецсимволы
            name = Regex.Replace(name, @"<[^>]+>", " ");
            name = Regex.Replace(name, @"&[a-zA-Z0-9#]+;", " ");

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            name = name.Replace('\\', '_').Replace('/', '_').Replace(':', '_').Replace('*', '_');
            name = name.Replace('?', '_').Replace('"', '_').Replace('<', '_').Replace('>', '_').Replace('|', '_');
            name = Regex.Replace(name, @"\s+", " ");
            name = name.Trim(new char[] { ' ', '.', '_', '-', '\'', '\"', '\t', '\r', '\n' });

            if (name.Length > 80) name = name.Substring(0, 80).Trim();

            if (string.IsNullOrEmpty(name)) return null;

            string lower = name.ToLowerInvariant();
            if (IsGenericName(lower)) return null;

            return name;
        }

        private static bool IsGenericName(string lower)
        {
            return lower == "image" || lower == "photo" || lower == "img" || lower == "download" ||
                   lower == "screenshot" || lower == "unnamed" || lower == "unknown" || lower == "file" ||
                   lower == "picture" || lower == "preview" || lower == "thumb" || lower == "thumbnail" ||
                   lower == "snimok" || lower == "снимок" || lower == "безымянный" || lower == "untitled" ||
                   lower == "icon" || lower == "avatar" || lower == "logo" || lower == "banner";
        }

        public static string ExtractFileNameFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                url = url.Trim(new char[] { ' ', '"', '\'', '\\' });
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;

                int qIdx = url.IndexOfAny(new char[] { '?', '#' });
                string clean = qIdx >= 0 ? url.Substring(0, qIdx) : url;

                string leaf = Path.GetFileName(Uri.UnescapeDataString(clean));
                if (string.IsNullOrEmpty(leaf)) return null;

                string withoutExt = Path.GetFileNameWithoutExtension(leaf);
                return SanitizeFileName(withoutExt);
            }
            catch { return null; }
        }

        public static string ExtractImageNameFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            // 1. Атрибуты изображения: data-src, data-original, src, download
            string[] patterns = new string[]
            {
                @"<img\b[^>]*?\bdata-original\s*=\s*[""'\\]*([^""'\\>\s]+)",
                @"<img\b[^>]*?\bdata-src\s*=\s*[""'\\]*([^""'\\>\s]+)",
                @"<img\b[^>]*?\bdata-url\s*=\s*[""'\\]*([^""'\\>\s]+)",
                @"<img\b[^>]*?\bsrc\s*=\s*[""'\\]*([^""'\\>\s]+)",
                @"<a\b[^>]*?\bdownload\s*=\s*[""'\\]*([^""'\\>]+)",
                @"<img\b[^>]*?\balt\s*=\s*[""'\\]*([^""'\\>]{2,80})",
                @"<img\b[^>]*?\btitle\s*=\s*[""'\\]*([^""'\\>]{2,80})",
                @"<img\b[^>]*?\baria-label\s*=\s*[""'\\]*([^""'\\>]{2,80})",
                @"<figcaption[^>]*>([^<]{2,80})</figcaption>",
                @"<meta\b[^>]*?property\s*=\s*[""']og:title[""'][^>]*?content\s*=\s*[""']([^""']{2,80})",
                @"<title[^>]*>([^<]{2,80})</title>"
            };

            foreach (var pat in patterns)
            {
                var match = Regex.Match(html, pat, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string rawVal = match.Groups[1].Value;
                    string res = pat.Contains("http") || pat.Contains("src") || pat.Contains("download")
                        ? ExtractFileNameFromUrl(rawVal)
                        : SanitizeFileName(rawVal);

                    if (!string.IsNullOrEmpty(res)) return res;
                }
            }

            // 2. Поиск в srcset (берем последний URL как максимальное разрешение)
            var mSrcset = Regex.Match(html, @"<img\b[^>]*?\bsrcset\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (mSrcset.Success)
            {
                string[] parts = mSrcset.Groups[1].Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    string p = parts[i].Trim();
                    int spaceIdx = p.IndexOf(' ');
                    string u = spaceIdx > 0 ? p.Substring(0, spaceIdx) : p;
                    string name = ExtractFileNameFromUrl(u);
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }

            return null;
        }

        public static string ExtractNameFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            text = text.Trim();
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractFileNameFromUrl(text);
            }

            if (text.Length >= 2 && text.Length <= 60 && !text.Contains("\n") && !text.Contains("\r"))
            {
                return SanitizeFileName(text);
            }

            return null;
        }

        private static bool IsValidTargetWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                StringBuilder sb = new StringBuilder(64);
                GetClassName(hwnd, sb, 64);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                {
                    return false;
                }
                return true;
            }
            catch { return false; }
        }

        public static string GetActiveWindowTitle()
        {
            try
            {
                DesktopHelper.POINT pt;
                if (!DesktopHelper.TryGetCursorPosition(out pt)) return null;

                Screen currentScreen = Screen.FromPoint(new Point(pt.x, pt.y));
                IntPtr hwnd = IntPtr.Zero;

                // 1. Приоритет: окно непосредственно под курсором мыши (на активном мониторе)
                IntPtr underCursor = WindowFromPoint(pt);
                if (underCursor != IntPtr.Zero)
                {
                    IntPtr root = GetAncestor(underCursor, GA_ROOT);
                    IntPtr candidate = (root != IntPtr.Zero) ? root : underCursor;

                    StringBuilder sbCls = new StringBuilder(64);
                    GetClassName(candidate, sbCls, 64);
                    string cls = sbCls.ToString();

                    // Если под курсором рабочий стол или панель задач на текущем мониторе:
                    // Значит на этом мониторе нет открытых сторонних окон - НЕ берем окна со второго экрана!
                    if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                    {
                        Logger.Log("GetActiveWindowTitle: cursor is over desktop/taskbar on screen " + currentScreen.DeviceName + ", ignoring secondary screen windows");
                        return null;
                    }

                    if (IsValidTargetWindow(candidate))
                    {
                        hwnd = candidate;
                    }
                }

                // 2. Если окно под курсором не определилось, проверяем GetForegroundWindow()
                // ТОЛЬКО ЕСЛИ оно физически расположено на том же мониторе, где находится курсор!
                if (hwnd == IntPtr.Zero)
                {
                    IntPtr fg = GetForegroundWindow();
                    if (fg != IntPtr.Zero && IsValidTargetWindow(fg))
                    {
                        RECT rc;
                        GetWindowRect(fg, out rc);
                        Rectangle winBounds = new Rectangle(rc.Left, rc.Top, Math.Max(1, rc.Right - rc.Left), Math.Max(1, rc.Bottom - rc.Top));
                        if (currentScreen.Bounds.IntersectsWith(winBounds))
                        {
                            hwnd = fg;
                        }
                        else
                        {
                            Logger.Log("GetActiveWindowTitle: foreground window is on another screen, ignoring");
                        }
                    }
                }

                if (hwnd == IntPtr.Zero || !IsValidTargetWindow(hwnd)) return null;

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                string procName = "";
                try { procName = Process.GetProcessById((int)pid).ProcessName; } catch {}

                StringBuilder sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, 256);
                string title = sb.ToString().Trim();

                if (!string.IsNullOrEmpty(title))
                {
                    // Убираем суффиксы браузеров и популярных приложений
                    string[] suffixes = new string[] {
                        " - Google Chrome", " - Microsoft Edge", " - Mozilla Firefox",
                        " - Opera", " - Yandex", " - Brave", " - Visual Studio Code",
                        " - Visual Studio", " - Telegram", " - Discord", " - Figma", " - Photoshop"
                    };

                    foreach (var s in suffixes)
                    {
                        if (title.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                        {
                            title = title.Substring(0, title.Length - s.Length).Trim();
                            break;
                        }
                    }

                    string clean = SanitizeFileName(title);
                    if (!string.IsNullOrEmpty(clean)) return clean;
                }

                if (!string.IsNullOrEmpty(procName) && !IsGenericName(procName.ToLowerInvariant()))
                {
                    return SanitizeFileName(procName);
                }
            }
            catch {}
            return null;
        }

        public static string GetBestImageName(IDataObject data)
        {
            if (Config.ExtractOriginalName && data != null)
            {
                try
                {
                    // 1. Проверяем HTML
                    string html = GetHtmlString(data);
                    if (!string.IsNullOrEmpty(html))
                    {
                        string extracted = ExtractImageNameFromHtml(html);
                        if (!string.IsNullOrEmpty(extracted))
                        {
                            Logger.Log("NameHelper: extracted from HTML: " + extracted);
                            return extracted;
                        }
                    }

                    // 2. Проверяем URL форматы
                    string[] urlFormats = new string[] { "UniformResourceLocatorW", "UniformResourceLocator", "URL" };
                    foreach (var uf in urlFormats)
                    {
                        if (data.GetDataPresent(uf))
                        {
                            object val = data.GetData(uf);
                            string url = val as string;
                            if (string.IsNullOrEmpty(url) && val is MemoryStream)
                            {
                                using (var ms = (MemoryStream)val)
                                {
                                    url = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\0');
                                }
                            }
                            string fromUrl = ExtractFileNameFromUrl(url);
                            if (!string.IsNullOrEmpty(fromUrl))
                            {
                                Logger.Log("NameHelper: extracted from URL format: " + fromUrl);
                                return fromUrl;
                            }
                        }
                    }

                    // 3. Проверяем текстовый буфер
                    if (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text))
                    {
                        string txt = (data.GetData(DataFormats.UnicodeText) ?? data.GetData(DataFormats.Text)) as string;
                        string fromTxt = ExtractNameFromText(txt);
                        if (!string.IsNullOrEmpty(fromTxt))
                        {
                            Logger.Log("NameHelper: extracted from clipboard Text: " + fromTxt);
                            return fromTxt;
                        }
                    }

                    // 4. Проверяем активное окно источника
                    string winTitle = GetActiveWindowTitle();
                    if (!string.IsNullOrEmpty(winTitle))
                    {
                        Logger.Log("NameHelper: extracted from active window: " + winTitle);
                        return winTitle + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("NameHelper error: " + ex.Message);
                }
            }

            string prefix = Config.DefaultPrefix;
            if (string.IsNullOrEmpty(prefix)) prefix = "Снимок";
            return prefix + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        }

        private static string GetHtmlString(IDataObject data)
        {
            try
            {
                if (data.GetDataPresent(DataFormats.Html))
                {
                    object raw = data.GetData(DataFormats.Html);
                    if (raw is string) return (string)raw;
                    if (raw is MemoryStream)
                    {
                        using (var ms = (MemoryStream)raw)
                        {
                            byte[] bytes = ms.ToArray();
                            return Encoding.UTF8.GetString(bytes);
                        }
                    }
                }
            }
            catch {}
            return null;
        }
    }
}
