using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WebSocketServerNetFramework
{
    /// <summary>
    /// Reports which parts of the game's client area other programs' windows cover, so the overlay
    /// can cut those parts out and look like it sits behind e.g. a browser in front of the game.
    /// Only read-only Win32 queries; never sends input to any window.
    /// </summary>
    public class GameWindowCover
    {
        // Overlaps thinner than this in either direction are shadows or stray edges.
        private const int MinRectSize = 2;
        // More rectangles than this is unusual; the smallest ones are merged into one bounding box
        // (which can cut a little too much, but never too little).
        private const int MaxRects = 32;

        /// <summary>The game client size in physical pixels and the rectangles covering it, relative to its top-left.</summary>
        public class State
        {
            public int Width;
            public int Height;
            public bool Minimized;
            public List<int[]> Rects = new List<int[]>();

            public bool SameAs(State other)
            {
                if (other == null || Width != other.Width || Height != other.Height || Minimized != other.Minimized || Rects.Count != other.Rects.Count)
                {
                    return false;
                }
                for (var i = 0; i < Rects.Count; i++)
                {
                    for (var j = 0; j < 4; j++)
                    {
                        if (Rects[i][j] != other.Rects[i][j])
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

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

        /// <summary>The current cover state; an empty one (nothing covering) when it can't be determined.</summary>
        public State Query(uint processId)
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
                return new State();
            }
        }

        private State Check(uint processId)
        {
            var state = new State();
            if (processId == 0)
            {
                return state;
            }
            if (processId != gamePid || gameWindow == IntPtr.Zero || !NativeMethods.IsWindow(gameWindow))
            {
                gamePid = processId;
                gameWindow = FindGameWindow(processId);
            }
            if (gameWindow == IntPtr.Zero)
            {
                return state;
            }
            // Nothing visible to draw over.
            if (NativeMethods.IsIconic(gameWindow))
            {
                state.Minimized = true;
                return state;
            }

            NativeMethods.RECT client;
            var origin = new NativeMethods.POINT();
            if (!NativeMethods.GetClientRect(gameWindow, out client) || !NativeMethods.ClientToScreen(gameWindow, ref origin))
            {
                return state;
            }
            var game = new NativeMethods.RECT
            {
                Left = origin.X,
                Top = origin.Y,
                Right = origin.X + client.Right,
                Bottom = origin.Y + client.Bottom,
            };
            state.Width = game.Right - game.Left;
            state.Height = game.Bottom - game.Top;
            if (state.Width <= 0 || state.Height <= 0)
            {
                return state;
            }

            var hwnd = NativeMethods.GetWindow(gameWindow, NativeMethods.GW_HWNDPREV);
            // Bounded in case the z-order changes under us.
            for (var i = 0; hwnd != IntPtr.Zero && i < 2000; i++, hwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDPREV))
            {
                int[] rect;
                if (Overlap(hwnd, game, out rect))
                {
                    state.Rects.Add(rect);
                }
            }
            if (state.Rects.Count > MaxRects)
            {
                // Keep the largest, merge the rest into their bounding box.
                state.Rects.Sort((a, b) => ((long)b[2] * b[3]).CompareTo((long)a[2] * a[3]));
                var rest = state.Rects.GetRange(MaxRects - 1, state.Rects.Count - (MaxRects - 1));
                state.Rects.RemoveRange(MaxRects - 1, rest.Count);
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
                foreach (var r in rest)
                {
                    x0 = Math.Min(x0, r[0]);
                    y0 = Math.Min(y0, r[1]);
                    x1 = Math.Max(x1, r[0] + r[2]);
                    y1 = Math.Max(y1, r[1] + r[3]);
                }
                state.Rects.Add(new[] { x0, y0, x1 - x0, y1 - y0 });
            }
            return state;
        }

        // The part of a window above the game that lies over the game's client area, relative to its top-left.
        private bool Overlap(IntPtr hwnd, NativeMethods.RECT game, out int[] rect)
        {
            rect = null;
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                return false;
            }
            // Cloaked: invisible UWP shells and windows on other virtual desktops.
            int cloaked;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return false;
            }

            var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            // Click-through overlays of other tools (Blish HUD, Discord, ...) don't hide anything.
            if ((ex & NativeMethods.WS_EX_TRANSPARENT) != 0 && (ex & NativeMethods.WS_EX_LAYERED) != 0)
            {
                return false;
            }
            // Non-activating tool windows (overlay widgets, tooltips).
            if ((ex & NativeMethods.WS_EX_NOACTIVATE) != 0 && (ex & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            {
                return false;
            }

            classBuffer.Clear();
            NativeMethods.GetClassName(hwnd, classBuffer, classBuffer.Capacity);
            var cls = classBuffer.ToString();
            foreach (var shell in ShellClasses)
            {
                if (cls == shell)
                {
                    return false;
                }
            }

            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            if (IsOwnProcess(pid))
            {
                return false;
            }

            NativeMethods.RECT r;
            // The extended frame excludes the invisible resize borders and shadow of Win10+ windows.
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(NativeMethods.RECT))) != 0
                && !NativeMethods.GetWindowRect(hwnd, out r))
            {
                return false;
            }

            var left = Math.Max(r.Left, game.Left);
            var top = Math.Max(r.Top, game.Top);
            var w = Math.Min(r.Right, game.Right) - left;
            var h = Math.Min(r.Bottom, game.Bottom) - top;
            if (w < MinRectSize || h < MinRectSize)
            {
                return false;
            }
            rect = new[] { left - game.Left, top - game.Top, w, h };
            return true;
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
