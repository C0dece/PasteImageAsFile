using System;
using System.IO;
using System.Threading;

namespace PasteImageAsFile
{
    public static class Logger
    {
        public static void Log(string msg)
        {
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string path = Path.Combine(localApp, "PasteImageAsFile", "app.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + Thread.CurrentThread.ManagedThreadId + "] " + msg;
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(line + Environment.NewLine);
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    fs.Write(bytes, 0, bytes.Length);
                }
            }
            catch {}
        }
    }
}
