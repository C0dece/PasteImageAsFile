using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace PasteImageAsFile
{
    public static class ImageDropHelper
    {
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".jfif", ".ico", ".tiff", ".tif", ".svg", ".avif"
        };

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int RegisterClipboardFormat(string lpszFormat);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern UIntPtr GlobalSize(IntPtr hMem);

        [DllImport("ole32.dll")]
        private static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM pmedium);

        public static void EnsureSecurityProtocols()
        {
            try
            {
                // Enable TLS 1.2 (3072), TLS 1.3 (12288), TLS 1.1 (768), and TLS 1.0 (192)
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)12288 |
                                                       (SecurityProtocolType)3072 |
                                                       (SecurityProtocolType)768 |
                                                       SecurityProtocolType.Tls;
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                ServicePointManager.DefaultConnectionLimit = 32;
                ServicePointManager.Expect100Continue = false;
            }
            catch (Exception ex)
            {
                Logger.Log("EnsureSecurityProtocols error: " + ex.Message);
            }
        }

        public static bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    string clean;
                    return IsImageUrl(path, out clean);
                }

                string ext = Path.GetExtension(path);
                return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsImageUrl(string url, out string cleanUrl)
        {
            cleanUrl = null;
            if (string.IsNullOrEmpty(url)) return false;
            try
            {
                url = url.Trim(new char[] { ' ', '"', '\'', '<', '>', '\r', '\n', '\t' });
                if (url.StartsWith("//")) url = "https:" + url;

                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

                cleanUrl = uri.AbsoluteUri;
                string path = uri.AbsolutePath;
                string ext = Path.GetExtension(path);
                if (!string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext))
                {
                    return true;
                }

                // Ключевые слова картинок в пути URL
                if (path.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("photo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("pic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("media", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("multimedia", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("avatar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("upload", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                // Проверяем Query string (часто там &format=jpg или &ext=png)
                string query = uri.Query;
                if (!string.IsNullOrEmpty(query))
                {
                    foreach (var imgExt in ImageExtensions)
                    {
                        if (query.IndexOf(imgExt.TrimStart('.'), StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch {}
            return false;
        }

        public static bool HasDropData(IDataObject data)
        {
            if (data == null) return false;
            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop) ||
                    data.GetDataPresent("FileContents") ||
                    data.GetDataPresent("FileGroupDescriptor") ||
                    data.GetDataPresent("FileGroupDescriptorW") ||
                    data.GetDataPresent(DataFormats.Bitmap) ||
                    data.GetDataPresent("PNG") ||
                    data.GetDataPresent("image/png") ||
                    data.GetDataPresent("DeviceIndependentBitmap") ||
                    data.GetDataPresent(DataFormats.Dib) ||
                    data.GetDataPresent(DataFormats.UnicodeText) ||
                    data.GetDataPresent(DataFormats.Text) ||
                    data.GetDataPresent(DataFormats.Html) ||
                    data.GetDataPresent("text/html") ||
                    data.GetDataPresent("UniformResourceLocator") ||
                    data.GetDataPresent("UniformResourceLocatorW"))
                {
                    return true;
                }
            }
            catch {}
            return false;
        }

        public static Image ExtractImage(IDataObject data)
        {
            if (data == null) return null;
            EnsureSecurityProtocols();

            // 1. OLE FileContents (виртуальный файл прямо из кэша браузера Chrome/Edge/Firefox/Yandex)
            try
            {
                byte[] fileBytes = ExtractFileContentsBytes(data);
                if (fileBytes != null && fileBytes.Length > 0)
                {
                    Image img = TryCreateImage(fileBytes);
                    if (img != null)
                    {
                        Logger.Log("ExtractImage: Found and decoded image in FileContents (" + img.Width + "x" + img.Height + ")");
                        return img;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ExtractImage FileContents error: " + ex.Message);
            }

            // 2. Стандартные форматы PNG, image/png
            try
            {
                string[] pngFormats = new string[] { "PNG", "image/png" };
                foreach (string fmt in pngFormats)
                {
                    if (data.GetDataPresent(fmt))
                    {
                        object obj = data.GetData(fmt);
                        byte[] bytes = ExtractBytesFromObject(obj);
                        if (bytes != null && bytes.Length > 0)
                        {
                            Image img = TryCreateImage(bytes);
                            if (img != null) return img;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ExtractImage PNG format error: " + ex.Message);
            }

            // 3. Стандартный DataFormats.Bitmap
            try
            {
                if (data.GetDataPresent(DataFormats.Bitmap))
                {
                    object obj = data.GetData(DataFormats.Bitmap);
                    if (obj is Image)
                    {
                        return (Image)obj;
                    }
                    byte[] bytes = ExtractBytesFromObject(obj);
                    if (bytes != null && bytes.Length > 0)
                    {
                        Image img = TryCreateImage(bytes);
                        if (img != null) return img;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ExtractImage Bitmap error: " + ex.Message);
            }

            // 4. Другие потоковые форматы картинок (JFIF, JPEG, GIF, BMP)
            try
            {
                string[] otherFormats = new string[] { "JFIF", "JPEG", "image/jpeg", "GIF", "image/gif", "image/bmp" };
                foreach (string fmt in otherFormats)
                {
                    if (data.GetDataPresent(fmt))
                    {
                        object obj = data.GetData(fmt);
                        byte[] bytes = ExtractBytesFromObject(obj);
                        if (bytes != null && bytes.Length > 0)
                        {
                            Image img = TryCreateImage(bytes);
                            if (img != null) return img;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ExtractImage other stream formats error: " + ex.Message);
            }

            // 5. DeviceIndependentBitmap (DIB)
            try
            {
                string[] dibFormats = new string[] { "DeviceIndependentBitmap", DataFormats.Dib };
                foreach (string fmt in dibFormats)
                {
                    if (data.GetDataPresent(fmt))
                    {
                        object obj = data.GetData(fmt);
                        byte[] dibBytes = ExtractBytesFromObject(obj);
                        if (dibBytes != null && dibBytes.Length > 40)
                        {
                            Image img = ImageFromDib(dibBytes);
                            if (img != null) return img;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ExtractImage DIB error: " + ex.Message);
            }

            // 6. HTML форматы (base64 data-URL или <img> ссылки)
            try
            {
                string html = GetHtmlString(data);
                if (!string.IsNullOrEmpty(html))
                {
                    // 6.1. base64
                    var matchBase64 = Regex.Match(html, @"src=[""']data:image/[^;]+;base64,([a-zA-Z0-9+/=]+)[""']", RegexOptions.IgnoreCase);
                    if (matchBase64.Success)
                    {
                        byte[] bytes = Convert.FromBase64String(matchBase64.Groups[1].Value);
                        Image img = TryCreateImage(bytes);
                        if (img != null) return img;
                    }

                    // 6.2. Веб-ссылка на картинку в HTML
                    string htmlImgUrl = ExtractImageUrlFromHtml(html);
                    if (!string.IsNullOrEmpty(htmlImgUrl))
                    {
                        Logger.Log("ExtractImage: Found image URL in HTML: " + htmlImgUrl);
                        Image img = DownloadWebImage(htmlImgUrl);
                        if (img != null) return img;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ExtractImage HTML error: " + ex.Message);
            }

            // 7. UniformResourceLocator / UniformResourceLocatorW
            try
            {
                string[] urlFormats = new string[] { "UniformResourceLocatorW", "UniformResourceLocator" };
                foreach (string uf in urlFormats)
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
                        string cleanUrl;
                        if (IsImageUrl(url, out cleanUrl))
                        {
                            Logger.Log("ExtractImage: Found image URL in URL format: " + cleanUrl);
                            Image img = DownloadWebImage(cleanUrl);
                            if (img != null) return img;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ExtractImage URL format error: " + ex.Message);
            }

            // 8. UnicodeText / Text
            try
            {
                if (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text))
                {
                    string txt = (data.GetData(DataFormats.UnicodeText) ?? data.GetData(DataFormats.Text)) as string;
                    string cleanUrl;
                    if (IsImageUrl(txt, out cleanUrl))
                    {
                        Logger.Log("ExtractImage: Found image URL in Text: " + cleanUrl);
                        Image img = DownloadWebImage(cleanUrl);
                        if (img != null) return img;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ExtractImage Text error: " + ex.Message);
            }

            return null;
        }

        public static byte[] ExtractFileContentsBytes(IDataObject data)
        {
            if (data == null) return null;
            try
            {
                if (data.GetDataPresent("FileContents"))
                {
                    object obj = data.GetData("FileContents");
                    if (obj is MemoryStream)
                    {
                        return ((MemoryStream)obj).ToArray();
                    }
                    if (obj is byte[])
                    {
                        return (byte[])obj;
                    }

                    // Win32 COM interop для OLE drag-drop с lindex = 0
                    var comData = data as System.Runtime.InteropServices.ComTypes.IDataObject;
                    if (comData != null)
                    {
                        int cf = RegisterClipboardFormat("FileContents");
                        var format = new System.Runtime.InteropServices.ComTypes.FORMATETC
                        {
                            cfFormat = (short)cf,
                            ptd = IntPtr.Zero,
                            dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                            lindex = 0,
                            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL | System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM
                        };

                        System.Runtime.InteropServices.ComTypes.STGMEDIUM medium;
                        comData.GetData(ref format, out medium);
                        try
                        {
                            if (medium.tymed == System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL && medium.unionmember != IntPtr.Zero)
                            {
                                IntPtr ptr = GlobalLock(medium.unionmember);
                                if (ptr != IntPtr.Zero)
                                {
                                    try
                                    {
                                        int size = (int)GlobalSize(medium.unionmember);
                                        if (size > 0)
                                        {
                                            byte[] buffer = new byte[size];
                                            Marshal.Copy(ptr, buffer, 0, size);
                                            return buffer;
                                        }
                                    }
                                    finally
                                    {
                                        GlobalUnlock(medium.unionmember);
                                    }
                                }
                            }
                            else if (medium.tymed == System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM && medium.unionmember != IntPtr.Zero)
                            {
                                var stream = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
                                if (stream != null)
                                {
                                    using (var ms = new MemoryStream())
                                    {
                                        byte[] buffer = new byte[16384];
                                        IntPtr read = Marshal.AllocHGlobal(sizeof(int));
                                        try
                                        {
                                            while (true)
                                            {
                                                stream.Read(buffer, buffer.Length, read);
                                                int bytesRead = Marshal.ReadInt32(read);
                                                if (bytesRead <= 0) break;
                                                ms.Write(buffer, 0, bytesRead);
                                            }
                                        }
                                        finally
                                        {
                                            Marshal.FreeHGlobal(read);
                                        }
                                        return ms.ToArray();
                                    }
                                }
                            }
                        }
                        finally
                        {
                            ReleaseStgMedium(ref medium);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ExtractFileContentsBytes error: " + ex.Message);
            }
            return null;
        }

        private static byte[] ExtractBytesFromObject(object obj)
        {
            if (obj == null) return null;
            if (obj is byte[]) return (byte[])obj;
            if (obj is MemoryStream) return ((MemoryStream)obj).ToArray();
            return null;
        }

        public static string ExtractImageUrlFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            // 1. Поиск в тегах img / source / a
            string[] patterns = new string[]
            {
                @"<img\b[^>]*?\bdata-original\s*=\s*[""']?([^""'\s>]+)",
                @"<img\b[^>]*?\bdata-src\s*=\s*[""']?([^""'\s>]+)",
                @"<img\b[^>]*?\bdata-url\s*=\s*[""']?([^""'\s>]+)",
                @"<img\b[^>]*?\bsrc\s*=\s*[""']?([^""'\s>]+)",
                @"<source\b[^>]*?\bsrcset\s*=\s*[""']?([^""'\s,>]+)",
                @"<img\b[^>]*?\bsrcset\s*=\s*[""']?([^""'\s,>]+)",
                @"background(?:-image)?\s*:\s*url\((?:['""]?)([^'""\)]+)(?:['""]?)\)",
                @"<a\b[^>]*?\bhref\s*=\s*[""']?([^""'\s>]+\.(?:png|jpg|jpeg|webp|gif|bmp))"
            };

            foreach (var pat in patterns)
            {
                var match = Regex.Match(html, pat, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string rawUrl = match.Groups[1].Value.Trim();
                    if (rawUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

                    string decodedUrl = WebUtility.HtmlDecode(rawUrl);

                    // Если протокол-относительный
                    if (decodedUrl.StartsWith("//"))
                    {
                        decodedUrl = "https:" + decodedUrl;
                    }
                    else if (decodedUrl.StartsWith("/"))
                    {
                        // Относительный URL: пробуем найти SourceURL в заголовках Clipboard HTML
                        var srcMatch = Regex.Match(html, @"SourceURL:(https?://[^\r\n]+)", RegexOptions.IgnoreCase);
                        if (srcMatch.Success)
                        {
                            try
                            {
                                Uri baseUri = new Uri(srcMatch.Groups[1].Value.Trim());
                                decodedUrl = new Uri(baseUri, decodedUrl).AbsoluteUri;
                            }
                            catch {}
                        }
                    }

                    if (decodedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        decodedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        return decodedUrl;
                    }
                }
            }

            return null;
        }

        public static Image DownloadWebImage(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                EnsureSecurityProtocols();

                url = url.Trim(new char[] { ' ', '"', '\'', '<', '>', '\r', '\n', '\t' });
                if (url.StartsWith("//")) url = "https:" + url;

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
                req.Accept = "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8";
                req.Headers.Set(HttpRequestHeader.AcceptLanguage, "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
                req.AllowAutoRedirect = true;
                req.MaximumAutomaticRedirections = 5;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                try
                {
                    Uri uri = new Uri(url);
                    req.Referer = uri.GetLeftPart(UriPartial.Authority) + "/";
                }
                catch {}

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    using (Stream stream = resp.GetResponseStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] buffer = new byte[16384];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ms.Write(buffer, 0, read);
                        }
                        byte[] bytes = ms.ToArray();
                        if (bytes.Length == 0) return null;

                        Image img = TryCreateImage(bytes);
                        if (img != null)
                        {
                            Logger.Log("DownloadWebImage: Successfully downloaded and decoded image from " + url + " (" + img.Width + "x" + img.Height + ")");
                            return img;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("DownloadWebImage error for " + url + ": " + ex.Message);
            }
            return null;
        }

        public static Image TryCreateImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;

            // 1. Попытка стандартного GDI+
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    return Image.FromStream(ms);
                }
            }
            catch
            {
                // 2. Если GDI+ не смог декодировать (например WebP), используем WIC
                return DecodeViaWic(bytes);
            }
        }

        public static Image DecodeViaWic(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    if (decoder.Frames != null && decoder.Frames.Count > 0)
                    {
                        var frame = decoder.Frames[0];
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(frame));
                        using (var outMs = new MemoryStream())
                        {
                            encoder.Save(outMs);
                            outMs.Position = 0;
                            return new Bitmap(outMs);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("DecodeViaWic error: " + ex.Message);
            }
            return null;
        }

        public static Image ImageFromDib(byte[] dibBytes)
        {
            if (dibBytes == null || dibBytes.Length < 40) return null;
            try
            {
                int headerSize = BitConverter.ToInt32(dibBytes, 0);
                if (headerSize < 40) return null;

                short bpp = BitConverter.ToInt16(dibBytes, 14);
                int compression = BitConverter.ToInt32(dibBytes, 16);
                int clrUsed = BitConverter.ToInt32(dibBytes, 32);

                int paletteSize = 0;
                if (bpp <= 8)
                {
                    int numColors = (clrUsed != 0) ? clrUsed : (1 << bpp);
                    paletteSize = numColors * 4;
                }
                else if (compression == 3) // BI_BITFIELDS
                {
                    paletteSize = 12;
                }

                int offBits = 14 + headerSize + paletteSize;
                int fileSize = 14 + dibBytes.Length;

                byte[] bmpBytes = new byte[fileSize];
                // BITMAPFILEHEADER
                bmpBytes[0] = (byte)'B';
                bmpBytes[1] = (byte)'M';
                BitConverter.GetBytes(fileSize).CopyTo(bmpBytes, 2);
                BitConverter.GetBytes(offBits).CopyTo(bmpBytes, 10);

                // DIB тело
                Array.Copy(dibBytes, 0, bmpBytes, 14, dibBytes.Length);

                using (var ms = new MemoryStream(bmpBytes))
                {
                    return Image.FromStream(ms);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ImageFromDib error: " + ex.Message);
                return null;
            }
        }

        public static string SaveImageToCache(Image img, IDataObject data, string customName = null)
        {
            if (img == null) return null;
            try
            {
                string bestName = customName;
                if (string.IsNullOrEmpty(bestName))
                {
                    bestName = NameHelper.GetBestImageName(data);
                }
                if (string.IsNullOrEmpty(bestName))
                {
                    bestName = "SuperHub_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                }

                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string cacheDir = Path.Combine(localApp, ShellIntegration.AppName, "Cache");
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

                string ext = ".png";
                string targetPath = Path.Combine(cacheDir, bestName + ext);
                int count = 1;
                while (File.Exists(targetPath))
                {
                    targetPath = Path.Combine(cacheDir, bestName + " (" + count + ")" + ext);
                    count++;
                }

                img.Save(targetPath, ImageFormat.Png);
                Logger.Log("ImageDropHelper: Saved image to " + targetPath);
                return targetPath;
            }
            catch (Exception ex)
            {
                Logger.Log("SaveImageToCache error: " + ex.Message);
                return null;
            }
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
                            return Encoding.UTF8.GetString(ms.ToArray());
                        }
                    }
                }
                if (data.GetDataPresent("text/html"))
                {
                    object raw = data.GetData("text/html");
                    if (raw is string) return (string)raw;
                    if (raw is MemoryStream)
                    {
                        using (var ms = (MemoryStream)raw)
                        {
                            return Encoding.UTF8.GetString(ms.ToArray());
                        }
                    }
                }
            }
            catch {}
            return null;
        }
    }
}
