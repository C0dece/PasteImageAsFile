using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PasteImageAsFile
{
    public class UpdateReleaseInfo
    {
        public bool HasUpdate;
        public string LatestTag;
        public string LatestVersion;
        public string CurrentVersion;
        public string ReleaseTitle;
        public string ReleaseNotes;
        public string DownloadUrl;
        public string HtmlUrl;
        public string ErrorMessage;
    }

    /// <summary>
    /// Помощник для проверки и применения обновлений с GitHub.
    /// </summary>
    public static class UpdateHelper
    {
        public const string RepoOwner = "C0dece";
        public const string RepoName = "PasteImageAsFile";
        public const string GitHubApiLatestReleaseUrl = "https://api.github.com/repos/C0dece/PasteImageAsFile/releases/latest";
        public const string GitHubLatestReleasePageUrl = "https://github.com/C0dece/PasteImageAsFile/releases/latest";

        static UpdateHelper()
        {
            try
            {
                // Включаем поддержку TLS 1.2 (0xC00 = 3072) для безопасного подключения к GitHub
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;
            }
            catch {}
        }

        /// <summary>
        /// Проверяет наличие обновлений на GitHub в фоновом потоке.
        /// </summary>
        public static void CheckForUpdatesAsync(Action<UpdateReleaseInfo> callback)
        {
            ThreadPool.QueueUserWorkItem(delegate {
                UpdateReleaseInfo info = CheckForUpdates();
                if (callback != null)
                {
                    Program.PostToUi(delegate {
                        try { callback(info); } catch (Exception ex) { Logger.Log("Update callback error: " + ex.Message); }
                    });
                }
            });
        }

        public static UpdateReleaseInfo CheckForUpdates()
        {
            UpdateReleaseInfo result = new UpdateReleaseInfo();
            result.CurrentVersion = ShellIntegration.AppVersion;
            result.HtmlUrl = "https://github.com/" + RepoOwner + "/" + RepoName + "/releases";

            try
            {
                // Шаг 1: Запрос к официальному GitHub Releases API
                string json = null;
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(GitHubApiLatestReleaseUrl);
                    request.UserAgent = "PasteImageAsFile/" + ShellIntegration.AppVersion;
                    request.Accept = "application/vnd.github.v3+json";
                    request.Timeout = 7000;
                    request.ReadWriteTimeout = 7000;

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }
                }
                catch (WebException webEx)
                {
                    Logger.Log("GitHub API request failed, trying redirect fallback: " + webEx.Message);
                }

                if (!string.IsNullOrEmpty(json))
                {
                    ParseGitHubReleaseJson(json, result);
                }
                else
                {
                    // Шаг 2: Fallback через HTTP 302 редирект страницы releases/latest (не зависит от лимитов GitHub API)
                    CheckViaRedirectFallback(result);
                }

                if (string.IsNullOrEmpty(result.LatestVersion))
                {
                    if (string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        result.ErrorMessage = "Не удалось определить последнюю версию на GitHub.";
                    }
                    return result;
                }

                // Сравнение версий
                result.HasUpdate = IsNewerVersion(result.LatestVersion, result.CurrentVersion);
                return result;
            }
            catch (Exception ex)
            {
                Logger.Log("CheckForUpdates error: " + ex.ToString());
                result.ErrorMessage = "Ошибка при проверке обновлений: " + ex.Message;
                return result;
            }
        }

        private static void ParseGitHubReleaseJson(string json, UpdateReleaseInfo result)
        {
            result.LatestTag = ExtractJsonString(json, "tag_name");
            result.LatestVersion = CleanVersionString(result.LatestTag);
            result.ReleaseTitle = ExtractJsonString(json, "name");
            result.ReleaseNotes = ExtractJsonString(json, "body");
            string html = ExtractJsonString(json, "html_url");
            if (!string.IsNullOrEmpty(html)) result.HtmlUrl = html;

            // Ищем asset PasteImageAsFile.exe
            int exeIdx = json.IndexOf("PasteImageAsFile.exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx >= 0)
            {
                string searchPart = json.Substring(exeIdx);
                string dlUrl = ExtractJsonString(searchPart, "browser_download_url");
                if (string.IsNullOrEmpty(dlUrl))
                {
                    // Возможно browser_download_url был до имени
                    int startBlock = Math.Max(0, exeIdx - 400);
                    string block = json.Substring(startBlock, exeIdx - startBlock + 200);
                    dlUrl = ExtractJsonString(block, "browser_download_url");
                }
                result.DownloadUrl = dlUrl;
            }

            if (string.IsNullOrEmpty(result.DownloadUrl) && !string.IsNullOrEmpty(result.LatestTag))
            {
                result.DownloadUrl = "https://github.com/" + RepoOwner + "/" + RepoName + "/releases/download/" + result.LatestTag + "/PasteImageAsFile.exe";
            }
        }

        private static void CheckViaRedirectFallback(UpdateReleaseInfo result)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(GitHubLatestReleasePageUrl);
                req.UserAgent = "PasteImageAsFile/" + ShellIntegration.AppVersion;
                req.AllowAutoRedirect = false;
                req.Timeout = 7000;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    string loc = resp.Headers["Location"];
                    if (!string.IsNullOrEmpty(loc))
                    {
                        result.HtmlUrl = loc;
                        int tagIdx = loc.LastIndexOf('/');
                        if (tagIdx >= 0 && tagIdx < loc.Length - 1)
                        {
                            result.LatestTag = loc.Substring(tagIdx + 1);
                            result.LatestVersion = CleanVersionString(result.LatestTag);
                            result.ReleaseTitle = "Версия " + result.LatestTag;
                            result.DownloadUrl = "https://github.com/" + RepoOwner + "/" + RepoName + "/releases/download/" + result.LatestTag + "/PasteImageAsFile.exe";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Redirect fallback error: " + ex.Message);
                result.ErrorMessage = "Не удалось подключиться к GitHub (" + ex.Message + ")";
            }
        }

        public static string CleanVersionString(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            tag = tag.Trim();
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                tag = tag.Substring(1).Trim();
            }
            return tag;
        }

        public static bool IsNewerVersion(string latestVerStr, string currentVerStr)
        {
            try
            {
                Version latest = ParseVersion(latestVerStr);
                Version current = ParseVersion(currentVerStr);
                return latest > current;
            }
            catch
            {
                return !string.Equals(latestVerStr, currentVerStr, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static Version ParseVersion(string verStr)
        {
            if (string.IsNullOrEmpty(verStr)) return new Version(0, 0, 0);
            verStr = verStr.Trim();
            // Оставляем только цифры и точки
            var sb = new StringBuilder();
            foreach (char c in verStr)
            {
                if (char.IsDigit(c) || c == '.') sb.Append(c);
                else break;
            }
            string cleaned = sb.ToString();
            string[] parts = cleaned.Split('.');
            if (parts.Length == 1) return new Version(int.Parse(parts[0]), 0, 0);
            if (parts.Length == 2) return new Version(int.Parse(parts[0]), int.Parse(parts[1]), 0);
            if (parts.Length >= 3) return new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
            return new Version(0, 0, 0);
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            int valStart = idx + pattern.Length;
            while (valStart < json.Length && (json[valStart] == ' ' || json[valStart] == '\t' || json[valStart] == '\r' || json[valStart] == '\n'))
            {
                valStart++;
            }

            if (valStart >= json.Length || json[valStart] != '"') return null;
            valStart++; // Пропускаем открывающую кавычку

            var sb = new StringBuilder();
            bool escape = false;
            for (int i = valStart; i < json.Length; i++)
            {
                char c = json[i];
                if (escape)
                {
                    if (c == 'r') sb.Append('\r');
                    else if (c == 'n') sb.Append('\n');
                    else if (c == 't') sb.Append('\t');
                    else sb.Append(c);
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
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

        /// <summary>
        /// Скачивает новый исполняемый файл и применяет обновление.
        /// </summary>
        public static void ApplyUpdate(string downloadUrl, Action<int> onProgress, Action<string> onError)
        {
            ThreadPool.QueueUserWorkItem(delegate {
                string tempExe = null;
                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "PasteImageAsFile_Update");
                    Directory.CreateDirectory(tempDir);
                    tempExe = Path.Combine(tempDir, "PasteImageAsFile_new.exe");

                    if (File.Exists(tempExe))
                    {
                        try { File.Delete(tempExe); } catch {}
                    }

                    using (WebClient client = new WebClient())
                    {
                        client.Headers[HttpRequestHeader.UserAgent] = "PasteImageAsFile-Updater";
                        client.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e) {
                            if (onProgress != null)
                            {
                                Program.PostToUi(delegate { onProgress(e.ProgressPercentage); });
                            }
                        };

                        var waitHandle = new ManualResetEvent(false);
                        Exception downloadEx = null;

                        client.DownloadFileCompleted += delegate(object s, System.ComponentModel.AsyncCompletedEventArgs e) {
                            if (e.Error != null) downloadEx = e.Error;
                            waitHandle.Set();
                        };

                        client.DownloadFileAsync(new Uri(downloadUrl), tempExe);
                        waitHandle.WaitOne();

                        if (downloadEx != null) throw downloadEx;
                    }

                    FileInfo fi = new FileInfo(tempExe);
                    if (!fi.Exists || fi.Length < 100000)
                    {
                        throw new InvalidOperationException("Загруженный файл поврежден или имеет недопустимый размер (" + (fi.Exists ? fi.Length : 0) + " байт).");
                    }

                    // Формируем bat-скрипт для подмены exe
                    string currentExe = Application.ExecutablePath;
                    int pid = Process.GetCurrentProcess().Id;
                    string updateBat = Path.Combine(tempDir, "apply_update.bat");

                    StringBuilder bat = new StringBuilder();
                    bat.AppendLine("@echo off");
                    bat.AppendLine("chcp 65001 >nul");
                    bat.AppendLine("timeout /t 1 /nobreak >nul");
                    bat.AppendLine(string.Format("taskkill /f /pid {0} >nul 2>&1", pid));
                    bat.AppendLine("timeout /t 1 /nobreak >nul");
                    bat.AppendLine(string.Format("copy /Y \"{0}\" \"{1}\" >nul", tempExe, currentExe));
                    bat.AppendLine(string.Format("start \"\" \"{0}\" --daemon", currentExe));
                    bat.AppendLine("(goto) 2>nul & del \"%~f0\"");

                    File.WriteAllText(updateBat, bat.ToString(), Encoding.GetEncoding(1251));

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c \"" + updateBat + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);

                    // Закрываем текущее приложение
                    Program.PostToUi(delegate {
                        Application.Exit();
                    });
                }
                catch (Exception ex)
                {
                    Logger.Log("ApplyUpdate failed: " + ex.ToString());
                    if (onError != null)
                    {
                        Program.PostToUi(delegate { onError(ex.Message); });
                    }
                }
            });
        }
    }
}
