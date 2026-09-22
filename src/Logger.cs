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
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + Thread.CurrentThread.ManagedThreadId + "] " + msg + Environment.NewLine;
                File.AppendAllText(path, line);
            }
            catch {}
        }
    }
}
