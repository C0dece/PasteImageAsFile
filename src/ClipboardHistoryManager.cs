using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace PasteImageAsFile
{
    public enum ClipboardItemType
    {
        Image = 0,
        Text = 1,
        Files = 2
    }

    public class ClipboardItem
    {
        public string Id { get; set; }
        public ClipboardItemType Type { get; set; }
        public DateTime Timestamp { get; set; }
        public string SourceApp { get; set; }
        public bool IsPinned { get; set; }
        public bool IsInSuperHub { get; set; }

        // Картинки
        public string ImagePath { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public long FileSizeBytes { get; set; }

        // Текст
        public string TextContent { get; set; }

        // Файлы
        public List<string> FilePaths { get; set; }

        public ClipboardItem()
        {
            Id = Guid.NewGuid().ToString("N");
            Timestamp = DateTime.Now;
            FilePaths = new List<string>();
            SourceApp = "";
            ImagePath = "";
            TextContent = "";
        }
    }

    public class ClipboardHistoryManager
    {
        private static ClipboardHistoryManager instance;
        private static readonly object instanceLock = new object();
        private readonly object fileLock = new object();

        private List<ClipboardItem> items = new List<ClipboardItem>();
        private string historyFilePath;

        public event Action HistoryChanged;

        private void NotifyChanged()
        {
            try
            {
                var handler = HistoryChanged;
                if (handler != null) handler();
            }
            catch {}
        }

        public static ClipboardHistoryManager Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (instanceLock)
                    {
                        if (instance == null)
                        {
                            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                            string path = Path.Combine(localApp, ShellIntegration.AppName, "history.json");
                            instance = new ClipboardHistoryManager(path);
                        }
                    }
                }
                return instance;
            }
        }

        public ClipboardHistoryManager(string filePath)
        {
            this.historyFilePath = filePath;
            Load();
            ImportExistingCache();
        }

        private void ImportExistingCache()
        {
            try
            {
                string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShellIntegration.AppName, "Cache");
                if (!Directory.Exists(cacheDir)) return;

                var existingImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                lock (fileLock)
                {
                    foreach (var it in items)
                    {
                        if (it.Type == ClipboardItemType.Image && !string.IsNullOrEmpty(it.ImagePath))
                        {
                            existingImages.Add(it.ImagePath);
                        }
                    }
                }

                var files = new DirectoryInfo(cacheDir).GetFiles("*.png");
                Array.Sort(files, (a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));

                bool addedAny = false;
                foreach (var fi in files)
                {
                    if (!existingImages.Contains(fi.FullName))
                    {
                        string title = Path.GetFileNameWithoutExtension(fi.Name);
                        int dIdx = title.LastIndexOf("_202");
                        if (dIdx > 0) title = title.Substring(0, dIdx);

                        var it = new ClipboardItem
                        {
                            Type = ClipboardItemType.Image,
                            ImagePath = fi.FullName,
                            SourceApp = title,
                            FileSizeBytes = fi.Length,
                            Timestamp = fi.LastWriteTime
                        };
                        lock (fileLock)
                        {
                            items.Insert(0, it);
                        }
                        addedAny = true;
                    }
                }

                if (addedAny)
                {
                    TrimHistory();
                    Save();
                }
            }
            catch {}
        }

        public List<ClipboardItem> GetItems(ClipboardItemType? filter = null, bool superHubOnly = false, string search = null)
        {
            lock (fileLock)
            {
                var res = new List<ClipboardItem>();
                foreach (var it in items)
                {
                    if (superHubOnly)
                    {
                        if (!it.IsInSuperHub) continue;
                    }
                    else
                    {
                        if (filter.HasValue && it.Type != filter.Value) continue;
                    }

                    if (!string.IsNullOrEmpty(search))
                    {
                        string s = search.ToLowerInvariant();
                        bool match = false;
                        if (!string.IsNullOrEmpty(it.SourceApp) && it.SourceApp.ToLowerInvariant().Contains(s)) match = true;
                        if (!match && !string.IsNullOrEmpty(it.TextContent) && it.TextContent.ToLowerInvariant().Contains(s)) match = true;
                        if (!match && it.FilePaths != null)
                        {
                            foreach (var f in it.FilePaths)
                            {
                                if (f.ToLowerInvariant().Contains(s)) { match = true; break; }
                            }
                        }
                        if (!match && !string.IsNullOrEmpty(it.ImagePath) && it.ImagePath.ToLowerInvariant().Contains(s)) match = true;
                        if (!match) continue;
                    }

                    res.Add(it);
                }

                // Сортировка: сначала закрепленные (IsPinned), затем по времени от новых к старым
                res.Sort((a, b) => {
                    if (a.IsPinned != b.IsPinned) return b.IsPinned.CompareTo(a.IsPinned);
                    return b.Timestamp.CompareTo(a.Timestamp);
                });
                return res;
            }
        }

        public ClipboardItem AddText(string text, string sourceApp)
        {
            if (string.IsNullOrEmpty(text)) return null;
            text = text.Trim();
            if (text.Length == 0) return null;

            lock (fileLock)
            {
                // Проверка дубликатов
                foreach (var it in items)
                {
                    if (it.Type == ClipboardItemType.Text && it.TextContent == text && !it.IsInSuperHub)
                    {
                        it.Timestamp = DateTime.Now;
                        if (!string.IsNullOrEmpty(sourceApp)) it.SourceApp = sourceApp;
                        Save();
                        return it;
                    }
                }

                var item = new ClipboardItem
                {
                    Type = ClipboardItemType.Text,
                    TextContent = text,
                    SourceApp = sourceApp ?? "",
                    Timestamp = DateTime.Now
                };

                items.Insert(0, item);
                TrimHistory();
                Save();
                return item;
            }
        }

        public ClipboardItem AddImage(string imagePath, string sourceApp, int width, int height, long size)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return null;

            lock (fileLock)
            {
                foreach (var it in items)
                {
                    if (it.Type == ClipboardItemType.Image && string.Equals(it.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                    {
                        it.Timestamp = DateTime.Now;
                        Save();
                        return it;
                    }
                }

                var item = new ClipboardItem
                {
                    Type = ClipboardItemType.Image,
                    ImagePath = imagePath,
                    ImageWidth = width,
                    ImageHeight = height,
                    FileSizeBytes = size,
                    SourceApp = sourceApp ?? "",
                    Timestamp = DateTime.Now
                };

                items.Insert(0, item);
                TrimHistory();
                Save();
                return item;
            }
        }

        public ClipboardItem AddFiles(IList<string> fileList, string sourceApp, bool asSuperHub = false)
        {
            if (fileList == null || fileList.Count == 0) return null;

            lock (fileLock)
            {
                var item = new ClipboardItem
                {
                    Type = ClipboardItemType.Files,
                    FilePaths = new List<string>(fileList),
                    SourceApp = sourceApp ?? "",
                    IsInSuperHub = asSuperHub,
                    Timestamp = DateTime.Now
                };

                items.Insert(0, item);
                TrimHistory();
                Save();
                return item;
            }
        }

        public void AddCustomSuperHubItem(string[] filePaths, string text)
        {
            lock (fileLock)
            {
                if (filePaths != null && filePaths.Length > 0)
                {
                    // Проверяем, если среди файлов есть картинки
                    foreach (var p in filePaths)
                    {
                        if (File.Exists(p))
                        {
                            string ext = Path.GetExtension(p).ToLowerInvariant();
                            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
                            {
                                var imgItem = new ClipboardItem
                                {
                                    Type = ClipboardItemType.Image,
                                    ImagePath = p,
                                    SourceApp = Path.GetFileName(p),
                                    IsInSuperHub = true,
                                    Timestamp = DateTime.Now
                                };
                                items.Insert(0, imgItem);
                                continue;
                            }
                        }

                        var fileItem = new ClipboardItem
                        {
                            Type = ClipboardItemType.Files,
                            FilePaths = new List<string> { p },
                            SourceApp = Path.GetFileName(p),
                            IsInSuperHub = true,
                            Timestamp = DateTime.Now
                        };
                        items.Insert(0, fileItem);
                    }
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    var textItem = new ClipboardItem
                    {
                        Type = ClipboardItemType.Text,
                        TextContent = text,
                        SourceApp = "SuperHub",
                        IsInSuperHub = true,
                        Timestamp = DateTime.Now
                    };
                    items.Insert(0, textItem);
                }

                Save();
            }
        }

        public void TogglePin(string id)
        {
            lock (fileLock)
            {
                foreach (var it in items)
                {
                    if (it.Id == id)
                    {
                        it.IsPinned = !it.IsPinned;
                        Save();
                        break;
                    }
                }
            }
        }

        public void ToggleSuperHub(string id)
        {
            lock (fileLock)
            {
                foreach (var it in items)
                {
                    if (it.Id == id)
                    {
                        it.IsInSuperHub = !it.IsInSuperHub;
                        Save();
                        break;
                    }
                }
            }
        }

        public void DeleteItem(string id)
        {
            lock (fileLock)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Id == id)
                    {
                        var it = items[i];
                        items.RemoveAt(i);
                        Save();
                        break;
                    }
                }
            }
        }

        public void ClearAll(ClipboardItemType? filter, bool superHubOnly)
        {
            lock (fileLock)
            {
                items.RemoveAll(it => {
                    if (it.IsPinned) return false; // Закрепленные не удаляем
                    if (superHubOnly) return it.IsInSuperHub;
                    if (filter.HasValue && it.Type != filter.Value) return false;
                    return !it.IsInSuperHub;
                });
                Save();
            }
        }

        private void TrimHistory()
        {
            int max = Config.MaxHistoryItems;
            if (max < 10) max = 50;

            int normalCount = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].IsPinned && !items[i].IsInSuperHub)
                {
                    normalCount++;
                    if (normalCount > max)
                    {
                        items.RemoveAt(i);
                        i--;
                    }
                }
            }
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    sb.AppendLine("  {");
                    sb.AppendLine("    \"id\": \"" + EscapeJson(it.Id) + "\",");
                    sb.AppendLine("    \"type\": " + (int)it.Type + ",");
                    sb.AppendLine("    \"timestamp\": \"" + it.Timestamp.ToString("o") + "\",");
                    sb.AppendLine("    \"sourceApp\": \"" + EscapeJson(it.SourceApp) + "\",");
                    sb.AppendLine("    \"isPinned\": " + (it.IsPinned ? "true" : "false") + ",");
                    sb.AppendLine("    \"isSuperHub\": " + (it.IsInSuperHub ? "true" : "false") + ",");
                    sb.AppendLine("    \"imagePath\": \"" + EscapeJson(it.ImagePath) + "\",");
                    sb.AppendLine("    \"imageWidth\": " + it.ImageWidth + ",");
                    sb.AppendLine("    \"imageHeight\": " + it.ImageHeight + ",");
                    sb.AppendLine("    \"fileSizeBytes\": " + it.FileSizeBytes + ",");
                    sb.AppendLine("    \"textContent\": \"" + EscapeJson(it.TextContent) + "\",");

                    sb.Append("    \"filePaths\": [");
                    if (it.FilePaths != null && it.FilePaths.Count > 0)
                    {
                        for (int f = 0; f < it.FilePaths.Count; f++)
                        {
                            sb.Append("\"" + EscapeJson(it.FilePaths[f]) + "\"");
                            if (f < it.FilePaths.Count - 1) sb.Append(", ");
                        }
                    }
                    sb.AppendLine("]");

                    sb.Append("  }");
                    if (i < items.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("]");

                string dir = Path.GetDirectoryName(historyFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string tmp = historyFilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(historyFilePath)) File.Delete(historyFilePath);
                File.Move(tmp, historyFilePath);
                NotifyChanged();
            }
            catch {}
        }

        public void Load()
        {
            try
            {
                items.Clear();
                if (!File.Exists(historyFilePath)) return;

                string json = File.ReadAllText(historyFilePath, Encoding.UTF8);
                int idx = 0;
                while (true)
                {
                    int startObj = json.IndexOf('{', idx);
                    if (startObj < 0) break;
                    int endObj = json.IndexOf('}', startObj);
                    if (endObj < 0) break;

                    string objChunk = json.Substring(startObj, endObj - startObj + 1);
                    var it = ParseItem(objChunk);
                    if (it != null) items.Add(it);

                    idx = endObj + 1;
                }
            }
            catch {}
        }

        private ClipboardItem ParseItem(string chunk)
        {
            try
            {
                var it = new ClipboardItem();
                it.Id = ExtractJsonString(chunk, "id") ?? Guid.NewGuid().ToString("N");
                it.Type = (ClipboardItemType)ExtractJsonInt(chunk, "type", 0);
                string ts = ExtractJsonString(chunk, "timestamp");
                DateTime dt;
                if (DateTime.TryParse(ts, out dt)) it.Timestamp = dt;
                it.SourceApp = ExtractJsonString(chunk, "sourceApp") ?? "";
                it.IsPinned = ExtractJsonBool(chunk, "isPinned", false);
                it.IsInSuperHub = ExtractJsonBool(chunk, "isSuperHub", false);
                it.ImagePath = ExtractJsonString(chunk, "imagePath") ?? "";
                it.ImageWidth = ExtractJsonInt(chunk, "imageWidth", 0);
                it.ImageHeight = ExtractJsonInt(chunk, "imageHeight", 0);
                it.FileSizeBytes = ExtractJsonLong(chunk, "fileSizeBytes", 0);
                it.TextContent = ExtractJsonString(chunk, "textContent") ?? "";

                int fpIdx = chunk.IndexOf("\"filePaths\":");
                if (fpIdx >= 0)
                {
                    int bOpen = chunk.IndexOf('[', fpIdx);
                    int bClose = chunk.IndexOf(']', fpIdx);
                    if (bOpen >= 0 && bClose > bOpen)
                    {
                        string arrStr = chunk.Substring(bOpen + 1, bClose - bOpen - 1);
                        string[] parts = arrStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in parts)
                        {
                            string s = p.Trim().Trim('\"');
                            if (!string.IsNullOrEmpty(s)) it.FilePaths.Add(UnescapeJson(s));
                        }
                    }
                }

                return it;
            }
            catch { return null; }
        }

        private static string ExtractJsonString(string json, string key)
        {
            string pattern = "\"" + key + "\": \"";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return null;
            idx += pattern.Length;
            StringBuilder sb = new StringBuilder();
            bool esc = false;
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (esc)
                {
                    if (c == 'n') sb.Append('\n');
                    else if (c == 'r') sb.Append('\r');
                    else if (c == 't') sb.Append('\t');
                    else sb.Append(c);
                    esc = false;
                }
                else if (c == '\\')
                {
                    esc = true;
                }
                else if (c == '\"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static int ExtractJsonInt(string json, string key, int defVal)
        {
            string pattern = "\"" + key + "\": ";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return defVal;
            idx += pattern.Length;
            int end = json.IndexOfAny(new char[] { ',', '\r', '\n', '}' }, idx);
            if (end < 0) end = json.Length;
            string num = json.Substring(idx, end - idx).Trim();
            int res;
            return int.TryParse(num, out res) ? res : defVal;
        }

        private static long ExtractJsonLong(string json, string key, long defVal)
        {
            string pattern = "\"" + key + "\": ";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return defVal;
            idx += pattern.Length;
            int end = json.IndexOfAny(new char[] { ',', '\r', '\n', '}' }, idx);
            if (end < 0) end = json.Length;
            string num = json.Substring(idx, end - idx).Trim();
            long res;
            return long.TryParse(num, out res) ? res : defVal;
        }

        private static bool ExtractJsonBool(string json, string key, bool defVal)
        {
            string pattern = "\"" + key + "\": ";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return defVal;
            idx += pattern.Length;
            int end = json.IndexOfAny(new char[] { ',', '\r', '\n', '}' }, idx);
            if (end < 0) end = json.Length;
            string val = json.Substring(idx, end - idx).Trim().ToLowerInvariant();
            return val == "true";
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
        }

        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\\\", "\\")
                    .Replace("\\\"", "\"")
                    .Replace("\\r", "\r")
                    .Replace("\\n", "\n")
                    .Replace("\\t", "\t");
        }
    }
}
