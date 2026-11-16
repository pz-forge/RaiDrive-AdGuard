// RaiDrive AdGuard - closes the RaiDrive Mount startup ad popup.
//
// The popup is a borderless WPF top-level window owned by RaiDrive.Mount.exe that
// hosts an embedded WebView2. It has no title, which is what separates it from the
// app's real windows ("RaiDrive Mount", "Hidden Window", notification helpers).
//
// Strategy: catch the window at CREATE/SHOW, hide it before it paints, then verify
// it really is the ad shell and close it. Anything else gets restored.
//
// Keep this file pure ASCII: the .NET Framework compilers parse sources as ANSI on
// Chinese-locale systems and choke on non-ASCII bytes with a misleading
// "unterminated string" error.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RaiDriveAdGuard
{
    internal static class Program
    {
        private static string _logPath;
        private static readonly object _logLock = new object();

        private const int EVENT_OBJECT_CREATE = 0x8000;
        private const int EVENT_OBJECT_SHOW = 0x8002;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const uint WM_CLOSE = 0x0010;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private const string TargetProcess = "RaiDrive.Mount";
        private const string TargetClassPrefix = "HwndWrapper[RaiDrive";

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private static void Log(string msg)
        {
            lock (_logLock)
            {
                try
                {
                    File.AppendAllText(_logPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + msg + Environment.NewLine,
                        Encoding.UTF8);
                }
                catch { }
            }
        }

        private static int Main(string[] args)
        {
            string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _logPath = Path.Combine(baseDir, "guard.log");

            if (args.Length > 0 && args[0] == "--scan")
            {
                Console.WriteLine("closed=" + Sweep());
                return 0;
            }

            Log("=== start pid=" + Process.GetCurrentProcess().Id + " ===");

            WinEventDelegate cb = OnWinEvent;
            IntPtr h1 = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE, IntPtr.Zero, cb, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            IntPtr h2 = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, cb, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            Log("hooks create=" + h1 + " show=" + h2);

            var t = new Thread(PollLoop);
            t.IsBackground = true;
            t.Priority = ThreadPriority.Highest;
            t.Start();

            Sweep();

            while (true) Thread.Sleep(1000);
        }

        private static void PollLoop()
        {
            while (true)
            {
                try { Sweep(); }
                catch (Exception ex) { Log("poll " + ex.Message); }
                Thread.Sleep(20);
            }
        }

        // A RaiDrive top-level shell about to show. The ad popup paints a beat after
        // it becomes visible, so hide it now and decide what it was off-thread.
        private static void OnWinEvent(IntPtr hHook, uint ev, IntPtr hwnd,
            int idObject, int idChild, uint tid, uint time)
        {
            try
            {
                if (hwnd == IntPtr.Zero || idObject != 0) return;
                if (!IsShellCandidate(hwnd)) return;
                if (!IsWindowVisible(hwnd)) return;

                ShowWindow(hwnd, SW_HIDE);

                IntPtr target = hwnd;
                ThreadPool.QueueUserWorkItem(delegate { Resolve(target); });
            }
            catch { }
        }

        // Top-level, right process, right WPF class, no title. The titled windows are
        // the real UI ("RaiDrive Mount", "Hidden Window", notification helpers).
        private static bool IsShellCandidate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (GetParent(hwnd) != IntPtr.Zero) return false;

            var cls = new StringBuilder(512);
            if (GetClassNameW(hwnd, cls, 512) <= 0) return false;
            if (!cls.ToString().StartsWith(TargetClassPrefix)) return false;

            var title = new StringBuilder(512);
            if (GetWindowTextW(hwnd, title, 512) > 0 && title.Length > 0) return false;

            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return false;
            try
            {
                if (!string.Equals(Process.GetProcessById((int)pid).ProcessName, TargetProcess,
                    StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch { return false; }

            return true;
        }

        // Wait for the WebView2 host child to materialise. Ads always get one; any
        // transient helper that does not is handed back with SW_SHOW.
        private static void Resolve(IntPtr hwnd)
        {
            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(60);
                if (!IsWindow(hwnd)) return;

                if (HasWebView(hwnd))
                {
                    RECT r;
                    GetWindowRect(hwnd, out r);
                    ShowWindow(hwnd, SW_HIDE);
                    PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    Log("closed hwnd=" + hwnd + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) +
                        " after " + (i * 60) + "ms");
                    return;
                }
            }

            if (IsWindow(hwnd))
            {
                ShowWindow(hwnd, SW_SHOW);
                Log("restored hwnd=" + hwnd);
            }
        }

        private static int Sweep()
        {
            int n = 0;
            try
            {
                EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    if (IsShellCandidate(h) && IsWindowVisible(h) && HasWebView(h))
                    {
                        RECT r;
                        GetWindowRect(h, out r);
                        ShowWindow(h, SW_HIDE);
                        PostMessageW(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        Log("closed hwnd=" + h + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + " via sweep");
                        n++;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return n;
        }

        private static bool HasWebView(IntPtr hwnd)
        {
            bool found = false;
            EnumChildWindows(hwnd, delegate(IntPtr ch, IntPtr l)
            {
                var cc = new StringBuilder(256);
                if (GetClassNameW(ch, cc, 256) > 0)
                {
                    if (cc.ToString().IndexOf("Chrome_", StringComparison.Ordinal) >= 0)
                    {
                        found = true;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
