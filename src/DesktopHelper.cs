using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PasteImageAsFile
{
    public static class DesktopHelper
    {
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr ILCreateFromPathW(string pszPath);

        [DllImport("shell32.dll")]
        public static extern IntPtr ILFindLastID(IntPtr pidl);

        [DllImport("shell32.dll")]
        public static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll", ExactSpelling = true)]
        public static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, uint dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, string dwItem1, string dwItem2);

        const uint SHCNE_CREATE = 0x00000002;
        const uint SHCNF_PATHW = 0x0005;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        const uint SVSI_SELECT = 0x00000001;
        const uint SVSI_DESELECTOTHERS = 0x00000004;
        const uint SVSI_ENSUREVISIBLE = 0x00000008;
        const uint SVSI_POSITIONITEM = 0x00000080;

        [ComImport]
        [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [PreserveSig]
            int QueryService([In] ref Guid guidService, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
        }

        [ComImport]
        [Guid("000214E2-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            void GetWindow(out IntPtr phwnd);
            void ContextSensitiveHelp(bool fEnterMode);
            void InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
            void SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
            void RemoveMenusSB(IntPtr hmenuShared);
            void SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string pszStatusText);
            void EnableModelessSB(bool fEnable);
            void TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
            void BrowseObject(IntPtr pidl, uint wFlags);
            void GetViewStateStream(uint grfMode, IntPtr ppStrm);
            void GetControlWindow(uint id, out IntPtr phwnd);
            void SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr pret);
            [PreserveSig]
            int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object ppshv);
        }

        [ComImport]
        [Guid("cde725b0-ccc9-4519-917e-325d72fab4ce")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFolderView
        {
            void GetCurrentViewMode(out uint pViewMode);
            void SetCurrentViewMode(uint ViewMode);
            void GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            void Item(int iItemIndex, out IntPtr ppidl);
            void ItemCount(uint uFlags, out int pcItems);
            void Items(uint uFlags, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            void GetSelectionMarkedItem(out int piItem);
            void GetFocusedItem(out int piItem);
            void GetItemPosition(IntPtr pidl, out POINT ppt);
            void GetSpacing(out POINT ppt);
            void GetDefaultSpacing(out POINT ppt);
            void GetAutoArrange();
            void SelectItem(int iItem, uint dwFlags);
            [PreserveSig]
            int SelectAndPositionItems(
                uint cidl,
                [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl,
                [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] POINT[] apt,
                uint dwFlags);
        }

        public static bool IsDesktopPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string norm = Path.GetFullPath(path).TrimEnd('\\');

                string userDesk = Environment.GetFolderPath(Environment.SpecialFolder.Desktop).TrimEnd('\\');
                if (string.Equals(norm, userDesk, StringComparison.OrdinalIgnoreCase)) return true;

                string commDesk = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory).TrimEnd('\\');
                if (string.Equals(norm, commDesk, StringComparison.OrdinalIgnoreCase)) return true;

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string defaultDesk = Path.Combine(userProfile, "Desktop").TrimEnd('\\');
                if (string.Equals(norm, defaultDesk, StringComparison.OrdinalIgnoreCase)) return true;

                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", false))
                    {
                        if (k != null)
                        {
                            object val = k.GetValue("Desktop");
                            if (val != null)
                            {
                                string exp = Environment.ExpandEnvironmentVariables(val.ToString()).TrimEnd('\\');
                                if (string.Equals(norm, exp, StringComparison.OrdinalIgnoreCase)) return true;
                            }
                        }
                    }
                }
                catch {}
            }
            catch { return false; }
            return false;
        }

        public static bool TryGetCursorPosition(out POINT pt)
        {
            pt = new POINT();
            try
            {
                if (GetCursorPos(out pt) && (pt.x != 0 || pt.y != 0))
                {
                    return true;
                }
            }
            catch {}

            try
            {
                var cp = Cursor.Position;
                pt.x = cp.X;
                pt.y = cp.Y;
                return true;
            }
            catch {}

            return false;
        }

        public static void PositionFile(string filePath, bool placeUnderCursor)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Logger.Log("PositionFile early exit: filePath invalid or not exists: " + filePath);
                return;
            }

            string folder = Path.GetDirectoryName(filePath);
            bool isDesk = IsDesktopPath(folder);
            Logger.Log("PositionFile: file=" + Path.GetFileName(filePath) + ", isDesk=" + isDesk + ", placeUnderCursor=" + placeUnderCursor);

            if (isDesk && placeUnderCursor)
            {
                POINT pt;
                if (TryGetCursorPosition(out pt))
                {
                    Logger.Log("Cursor position resolved: " + pt.x + "," + pt.y);
                    if (TryPositionOnDesktop(filePath, pt))
                    {
                        Logger.Log("TryPositionOnDesktop SUCCESS");
                        return;
                    }
                    else
                    {
                        Logger.Log("TryPositionOnDesktop FAILED");
                    }
                }
                else
                {
                    Logger.Log("TryGetCursorPosition failed");
                }
            }

            HighlightItem(filePath);
        }

        public static bool TryPositionOnDesktop(string filePath, POINT pt)
        {
            try
            {
                SHChangeNotify(SHCNE_CREATE, SHCNF_PATHW, filePath, null);
                Thread.Sleep(80);

                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return false;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic windows = shell.Windows();
                object dummy1 = 0;
                object dummy2 = 0;
                int hwndOut = 0;

                // SWC_DESKTOP = 8
                dynamic desktopWin = windows.FindWindowSW(ref dummy1, ref dummy2, 8, out hwndOut, 1);
                if (desktopWin == null)
                {
                    Logger.Log("FindWindowSW returned null for SWC_DESKTOP");
                    return false;
                }

                IServiceProvider sp = desktopWin as IServiceProvider;
                if (sp == null)
                {
                    Logger.Log("desktopWin does not implement IServiceProvider");
                    return false;
                }

                Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
                object sbObj;
                int hr = sp.QueryService(ref SID_STopLevelBrowser, ref IID_IShellBrowser, out sbObj);
                if (hr != 0 || sbObj == null)
                {
                    Logger.Log("QueryService SID_STopLevelBrowser failed: 0x" + hr.ToString("X"));
                    return false;
                }

                IShellBrowser sb = sbObj as IShellBrowser;
                if (sb == null) return false;

                object svObj;
                hr = sb.QueryActiveShellView(out svObj);
                if (hr != 0 || svObj == null)
                {
                    Logger.Log("QueryActiveShellView failed: 0x" + hr.ToString("X"));
                    return false;
                }

                IFolderView fv = svObj as IFolderView;
                if (fv == null)
                {
                    Logger.Log("svObj does not implement IFolderView");
                    return false;
                }

                IntPtr pidl = ILCreateFromPathW(filePath);
                if (pidl == IntPtr.Zero)
                {
                    Logger.Log("ILCreateFromPathW returned IntPtr.Zero for " + filePath);
                    return false;
                }

                try
                {
                    IntPtr childPidl = ILFindLastID(pidl);
                    uint flags = SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_ENSUREVISIBLE | SVSI_POSITIONITEM;

                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        hr = fv.SelectAndPositionItems(1, new IntPtr[] { childPidl }, new POINT[] { pt }, flags);
                        if (hr == 0)
                        {
                            Logger.Log("SelectAndPositionItems SUCCESS on attempt " + (attempt + 1) + " at " + pt.x + "," + pt.y);
                            return true;
                        }
                        Thread.Sleep(100);
                    }

                    Logger.Log("SelectAndPositionItems failed after 5 attempts: 0x" + hr.ToString("X"));
                    return false;
                }
                finally
                {
                    ILFree(pidl);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("TryPositionOnDesktop exception: " + ex.Message);
                return false;
            }
        }

        public static void HighlightItem(string filePath)
        {
            try
            {
                IntPtr pidl = ILCreateFromPathW(filePath);
                if (pidl != IntPtr.Zero)
                {
                    try
                    {
                        SHOpenFolderAndSelectItems(pidl, 0, null, 0);
                    }
                    finally
                    {
                        ILFree(pidl);
                    }
                }
            }
            catch {}
        }
    }
}
