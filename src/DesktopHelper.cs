using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PasteImageAsFile
{
    public static class DesktopHelper
    {
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr ILCreateFromPathW(string pszPath);

        [DllImport("shell32.dll")]
        public static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll", ExactSpelling = true)]
        public static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, uint dwFlags);

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
            int SelectAndPositionItems(uint cidl, [In] IntPtr[] apidl, [In] POINT[] apt, uint dwFlags);
        }

        public static bool IsDesktopPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string norm = Path.GetFullPath(path).TrimEnd('\\');
                string userDesk = Environment.GetFolderPath(Environment.SpecialFolder.Desktop).TrimEnd('\\');
                string commDesk = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory).TrimEnd('\\');

                return string.Equals(norm, userDesk, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(norm, commDesk, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static void PositionFile(string filePath, bool placeUnderCursor)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            string folder = Path.GetDirectoryName(filePath);
            bool isDesk = IsDesktopPath(folder);

            if (isDesk && placeUnderCursor)
            {
                POINT pt;
                if (GetCursorPos(out pt))
                {
                    if (TryPositionOnDesktop(filePath, pt))
                    {
                        return;
                    }
                }
            }

            HighlightItem(filePath);
        }

        private static bool TryPositionOnDesktop(string filePath, POINT pt)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return false;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic windows = shell.Windows();
                object dummy1 = 0;
                object dummy2 = 0;
                int hwndOut = 0;

                dynamic desktopWin = windows.FindWindowSW(ref dummy1, ref dummy2, 8, out hwndOut, 1);
                if (desktopWin == null) return false;

                IServiceProvider sp = desktopWin as IServiceProvider;
                if (sp == null) return false;

                Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
                object sbObj;
                int hr = sp.QueryService(ref SID_STopLevelBrowser, ref IID_IShellBrowser, out sbObj);
                if (hr != 0 || sbObj == null) return false;

                IShellBrowser sb = sbObj as IShellBrowser;
                if (sb == null) return false;

                object svObj;
                hr = sb.QueryActiveShellView(out svObj);
                if (hr != 0 || svObj == null) return false;

                IFolderView fv = svObj as IFolderView;
                if (fv == null) return false;

                IntPtr pidl = ILCreateFromPathW(filePath);
                if (pidl == IntPtr.Zero) return false;

                try
                {
                    uint flags = SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_ENSUREVISIBLE | SVSI_POSITIONITEM;
                    hr = fv.SelectAndPositionItems(1, new IntPtr[] { pidl }, new POINT[] { pt }, flags);
                    return hr == 0;
                }
                finally
                {
                    ILFree(pidl);
                }
            }
            catch
            {
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
