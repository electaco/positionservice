using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WebSocketServerNetFramework
{
    /// <summary>
    /// Tells whether other programs' windows cover part of the game window, so the overlay can hide
    /// instead of drawing over e.g. a browser that sits in front of the game.
    /// Only read-only Win32 queries; never sends input to any window.
    /// </summary>
    public class GameWindowCover
    {
        // Covered when other windows overlap at least this fraction of the game's client area.
        // Small enough to catch a narrow window, large enough to ignore a stray edge or shadow.
        private const double CoverFraction = 0.01;

        // Windows owned by these processes are our own overlay (Electron) and never count.
        private static readonly string[] OwnExecutables = { "electron.exe", "electaco.exe" };
        private static readonly string[] ShellClasses =
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "NotifyIconOverflowWindow",
        };

        private readonly uint parentPid = GetParentPid();
        private readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        private readonly Dictionary<uint, bool> ownProcessCache = new Dictionary<uint, bool>();
        private readonly StringBuilder classBuffer = new StringBuilder(256);
        private IntPtr gameWindow;
        private uint gamePid;
        private DateTime lastErrorLog = DateTime.MinValue;

        /// <summary>True when the game window is minimized or other windows overlap it.</summary>
        public bool IsCovered(uint processId)
        {
            try
            {
                return Check(processId);
            }
            catch (Exception e)
            {
                if (DateTime.UtcNow - lastErrorLog > TimeSpan.FromMinutes(1))
                {
                    lastErrorLog = DateTime.UtcNow;
                    Log.Error(e, "Could not determine whether the game window is covered");
                }
                return false;
            }
        }

        private bool Check(uint processId)
        {
            if (processId == 0)
            {
                return false;
            }
            if (processId != gamePid || gameWindow == IntPtr.Zero || !NativeMethods.IsWindow(gameWindow))
            {
                gamePid = processId;
                gameWindow = FindGameWindow(processId);
            }
            if (gameWindow == IntPtr.Zero)
            {
                return false;
            }
            // Nothing visible to draw over.
            if (NativeMethods.IsIconic(gameWindow))
            {
                return true;
            }

            NativeMethods.RECT client;
            var origin = new NativeMethods.POINT();
            if (!NativeMethods.GetClientRect(gameWindow, out client) || !NativeMethods.ClientToScreen(gameWindow, ref origin))
            {
                return false;
            }
            var game = new NativeMethods.RECT
            {
                Left = origin.X,
                Top = origin.Y,
                Right = origin.X + client.Right,
                Bottom = origin.Y + client.Bottom,
            };
            var gameArea = (long)(game.Right - game.Left) * (game.Bottom - game.Top);
            if (gameArea <= 0)
            {
                return false;
            }

            long covered = 0;
            var limit = (long)Math.Ceiling(gameArea * CoverFraction);
            var hwnd = NativeMethods.GetWindow(gameWindow, NativeMethods.GW_HWNDPREV);
            // Bounded in case the z-order changes under us.
            for (var i = 0; hwnd != IntPtr.Zero && i < 2000; i++, hwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDPREV))
            {
                covered += Overlap(hwnd, game);
                if (covered >= limit)
                {
                    return true;
                }
            }
            return false;
        }

        private long Overlap(IntPtr hwnd, NativeMethods.RECT game)
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                return 0;
            }
            // Cloaked: invisible UWP shells and windows on other virtual desktops.
            int cloaked;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return 0;
            }

            var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            // Click-through overlays of other tools (Blish HUD, Discord, ...) don't hide anything.
            if ((ex & NativeMethods.WS_EX_TRANSPARENT) != 0 && (ex & NativeMethods.WS_EX_LAYERED) != 0)
            {
                return 0;
            }
            // Non-activating tool windows (overlay widgets, tooltips).
            if ((ex & NativeMethods.WS_EX_NOACTIVATE) != 0 && (ex & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            {
                return 0;
            }

            classBuffer.Clear();
            NativeMethods.GetClassName(hwnd, classBuffer, classBuffer.Capacity);
            var cls = classBuffer.ToString();
            foreach (var shell in ShellClasses)
            {
                if (cls == shell)
                {
                    return 0;
                }
            }

            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            if (IsOwnProcess(pid))
            {
                return 0;
            }

            NativeMethods.RECT r;
            // The extended frame excludes the invisible resize borders and shadow of Win10+ windows.
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(NativeMethods.RECT))) != 0
                && !NativeMethods.GetWindowRect(hwnd, out r))
            {
                return 0;
            }

            var w = Math.Min(r.Right, game.Right) - Math.Max(r.Left, game.Left);
            var h = Math.Min(r.Bottom, game.Bottom) - Math.Max(r.Top, game.Top);
            return w > 0 && h > 0 ? (long)w * h : 0;
        }

        private bool IsOwnProcess(uint pid)
        {
            if (pid == parentPid || pid == ownPid)
            {
                return true;
            }
            bool own;
            if (!ownProcessCache.TryGetValue(pid, out own))
            {
                var name = GetExecutableName(pid);
                own = false;
                foreach (var exe in OwnExecutables)
                {
                    if (string.Equals(name, exe, StringComparison.OrdinalIgnoreCase))
                    {
                        own = true;
                    }
                }
                // Pids get reused, so don't let stale entries pile up.
                if (ownProcessCache.Count > 512)
                {
                    ownProcessCache.Clear();
                }
                ownProcessCache[pid] = own;
            }
            return own;
        }

        private static string GetExecutableName(uint pid)
        {
            var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                var sb = new StringBuilder(1024);
                var size = sb.Capacity;
                if (!NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref size))
                {
                    return null;
                }
                var path = sb.ToString();
                return path.Substring(path.LastIndexOf('\\') + 1);
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        private static uint GetParentPid()
        {
            try
            {
                var pbi = new NativeMethods.PROCESS_BASIC_INFORMATION();
                int len;
                if (NativeMethods.NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0, ref pbi, Marshal.SizeOf(pbi), out len) == 0)
                {
                    return (uint)pbi.InheritedFromUniqueProcessId.ToInt64();
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Could not determine the parent process");
            }
            return 0;
        }

        private static IntPtr FindGameWindow(uint pid)
        {
            var found = IntPtr.Zero;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                uint wpid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out wpid);
                if (wpid == pid && NativeMethods.IsWindowVisible(hwnd) && NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) == IntPtr.Zero)
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static class NativeMethods
        {
            public const uint GW_HWNDPREV = 3;
            public const uint GW_OWNER = 4;
            public const int GWL_EXSTYLE = -20;
            public const long WS_EX_TRANSPARENT = 0x20;
            public const long WS_EX_TOOLWINDOW = 0x80;
            public const long WS_EX_LAYERED = 0x80000;
            public const long WS_EX_NOACTIVATE = 0x08000000;
            public const int DWMWA_CLOAKED = 14;
            public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
            public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT { public int Left, Top, Right, Bottom; }

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT { public int X, Y; }

            [StructLayout(LayoutKind.Sequential)]
            public struct PROCESS_BASIC_INFORMATION
            {
                public IntPtr ExitStatus;
                public IntPtr PebBaseAddress;
                public IntPtr AffinityMask;
                public IntPtr BasePriority;
                public IntPtr UniqueProcessId;
                public IntPtr InheritedFromUniqueProcessId;
            }

            public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

            [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
            [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
            [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
            [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
            [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
            [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
            // GetWindowLongPtr only exists as an export on 64-bit; GetWindowLong is fine for the ex style on both.
            [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
            public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => new IntPtr(GetWindowLong(hwnd, index));

            [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
            [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

            [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
            [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
            [DllImport("ntdll.dll")] public static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref PROCESS_BASIC_INFORMATION info, int size, out int returned);
        }
    }
}
