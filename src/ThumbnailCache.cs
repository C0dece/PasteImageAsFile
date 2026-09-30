using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PasteImageAsFile
{
    public static class ThumbnailCache
    {
        private static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> accessOrder = new List<string>();
        private static readonly object cacheLock = new object();
        private const int MaxCacheItems = 250;

        public static Image GetThumbnail(string path, int targetW, int targetH)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string key = path.ToLowerInvariant() + "_" + targetW + "x" + targetH;

            lock (cacheLock)
            {
                Image cached;
                if (cache.TryGetValue(key, out cached))
                {
                    accessOrder.Remove(key);
                    accessOrder.Add(key);
                    return cached;
                }
            }

            if (!File.Exists(path)) return null;

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var orig = Image.FromStream(stream, false, false))
                {
                    int ow = orig.Width;
                    int oh = orig.Height;
                    if (ow <= 0 || oh <= 0) return null;

                    Bitmap thumb = new Bitmap(targetW, targetH, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(thumb))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighSpeed;

                        float scale = Math.Max((float)targetW / ow, (float)targetH / oh);
                        float dw = ow * scale;
                        float dh = oh * scale;
                        float dx = (targetW - dw) / 2f;
                        float dy = (targetH - dh) / 2f;

                        g.DrawImage(orig, dx, dy, dw, dh);
                    }

                    lock (cacheLock)
                    {
                        if (cache.Count >= MaxCacheItems && accessOrder.Count > 0)
                        {
                            string oldest = accessOrder[0];
                            accessOrder.RemoveAt(0);
                            Image oldImg;
                            if (cache.TryGetValue(oldest, out oldImg))
                            {
                                cache.Remove(oldest);
                                if (oldImg != null)
                                {
                                    try { oldImg.Dispose(); } catch {}
                                }
                            }
                        }

                        cache[key] = thumb;
                        accessOrder.Add(key);
                    }

                    return thumb;
                }
            }
            catch
            {
                return null;
            }
        }

        public static void Clear()
        {
            lock (cacheLock)
            {
                foreach (var kvp in cache)
                {
                    if (kvp.Value != null)
                    {
                        try { kvp.Value.Dispose(); } catch {}
                    }
                }
                cache.Clear();
                accessOrder.Clear();
            }
        }
    }
}
