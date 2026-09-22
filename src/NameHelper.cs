using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PasteImageAsFile
{
    public static class NameHelper
    {
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            name = name.Replace('\\', '_').Replace('/', '_');
            name = name.Trim(new char[] { ' ', '.', '_', '-', '"', '\'' });
            if (name.Length > 80) name = name.Substring(0, 80);
            return string.IsNullOrEmpty(name) ? null : name;
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
                string sanitized = SanitizeFileName(withoutExt);
                if (string.IsNullOrEmpty(sanitized)) return null;

                string lower = sanitized.ToLowerInvariant();
                if (lower == "image" || lower == "photo" || lower == "img" || lower == "download" ||
                    lower == "screenshot" || lower == "unnamed" || lower == "unknown" || lower == "file" ||
                    lower == "picture" || lower == "preview" || lower == "thumb" || lower == "thumbnail")
                {
                    return null;
                }
                return sanitized;
            }
            catch { return null; }
        }

        public static string ExtractImageNameFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            var mSrc = Regex.Match(html, @"<img\b[^>]*?\bsrc\s*=\s*[""'\\]*([""'\\>\s]+)", RegexOptions.IgnoreCase);
            if (mSrc.Success)
            {
                string fromUrl = ExtractFileNameFromUrl(mSrc.Groups[1].Value);
                if (!string.IsNullOrEmpty(fromUrl)) return fromUrl;
            }

            var mAlt = Regex.Match(html, @"<img\b[^>]*?\balt\s*=\s*[""'\\]*([^""'\\>]{2,80})", RegexOptions.IgnoreCase);
            if (mAlt.Success)
            {
                string fromAlt = SanitizeFileName(mAlt.Groups[1].Value);
                if (!string.IsNullOrEmpty(fromAlt)) return fromAlt;
            }

            var mTitle = Regex.Match(html, @"<img\b[^>]*?\btitle\s*=\s*[""'\\]*([^""'\\>]{2,80})", RegexOptions.IgnoreCase);
            if (mTitle.Success)
            {
                string fromTitle = SanitizeFileName(mTitle.Groups[1].Value);
                if (!string.IsNullOrEmpty(fromTitle)) return fromTitle;
            }

            return null;
        }

        public static string GetBestImageName(IDataObject data)
        {
            if (Config.ExtractOriginalName && data != null)
            {
                try
                {
                    string html = GetHtmlString(data);
                    if (!string.IsNullOrEmpty(html))
                    {
                        string extracted = ExtractImageNameFromHtml(html);
                        if (!string.IsNullOrEmpty(extracted))
                        {
                            Logger.Log("NameHelper extracted original name: " + extracted);
                            return extracted;
                        }
                    }

                    if (data.GetDataPresent("UniformResourceLocatorW"))
                    {
                        string url = data.GetData("UniformResourceLocatorW") as string;
                        string fromUrl = ExtractFileNameFromUrl(url);
                        if (!string.IsNullOrEmpty(fromUrl))
                        {
                            Logger.Log("NameHelper extracted URL name: " + fromUrl);
                            return fromUrl;
                        }
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
                            return System.Text.Encoding.UTF8.GetString(bytes);
                        }
                    }
                }
            }
            catch {}
            return null;
        }
    }
}
