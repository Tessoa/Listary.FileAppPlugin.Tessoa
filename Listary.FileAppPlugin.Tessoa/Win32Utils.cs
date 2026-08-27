using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Listary.FileAppPlugin.Tessoa
{
    internal static class Win32Utils
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint GA_ROOT = 2;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags,
                                                              StringBuilder exeName, ref uint size);

        public static int GetProcessId(IntPtr hWnd)
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            return (int)pid;
        }

        /// <summary>Full path of the executable backing a process, or an empty string.</summary>
        public static string GetProcessPath(int processId)
        {
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
            if (process == IntPtr.Zero)
            {
                return string.Empty;
            }
            try
            {
                uint size = 1024;
                var buffer = new StringBuilder((int)size);
                if (QueryFullProcessImageNameW(process, 0, buffer, ref size))
                {
                    return buffer.ToString(0, (int)size);
                }
                return string.Empty;
            }
            finally
            {
                CloseHandle(process);
            }
        }

        /// <summary>A visible, titled, top-level window (as opposed to a popup or a child).</summary>
        public static bool IsTopLevelWindow(IntPtr hWnd)
        {
            return GetAncestor(hWnd, GA_ROOT) == hWnd
                && IsWindowVisible(hWnd)
                && GetWindowTextLength(hWnd) > 0;
        }
    }
}
