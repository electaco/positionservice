using Serilog;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TacoLib.GameInteract;
using WindowsInput;
using WindowsInput.Events;
using WindowsInput.Events.Sources;

namespace WebSocketServerNetFramework.Clients
{
    public class Command
    {
        public string Type { get; set; }
        public string Data { get; set; }
    }

    public class ControlClient : BaseClient, ISocketClient
    {

        [DllImport("User32.dll")]
        static extern int SetForegroundWindow(IntPtr point);

        [DllImport("User32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("User32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("User32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("Kernel32.dll")]
        static extern uint GetCurrentThreadId();

        // How long to wait for the game to come to the front before giving up.
        const int ForegroundTimeoutMs = 500;
        // Once in front, give the game a moment before sending input: a fullscreen game resets its
        // keyboard state on activation, and keys arriving during that can be dropped.
        const int ActivationSettleMs = 150;

        public static void SendKeys(params int[] keys)
        {
            var process = GameInstance.GetGameProcess();
            if (process == null || process.HasExited)
            {
                return;
            }
            var window = process.MainWindowHandle;

            if (GetForegroundWindow() != window)
            {
                var waited = Stopwatch.StartNew();
                var how = BringToForeground(window);
                // The switch is asynchronous: wait until the game really is in front.
                while (GetForegroundWindow() != window && waited.ElapsedMilliseconds < ForegroundTimeoutMs)
                {
                    Thread.Sleep(5);
                }
                Console.WriteLine(GetForegroundWindow() == window
                    ? $"Game brought to front ({how}) in {waited.ElapsedMilliseconds} ms"
                    : $"Could not bring the game to front ({how}); sending keys anyway");
                Thread.Sleep(ActivationSettleMs);
            }

            var mkeys = keys.Select(x => (KeyCode)x).ToArray();
            Simulate.Events().ClickChord(mkeys).Invoke();
        }

        [DllImport("User32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        const byte VK_F24 = 0x87;
        const uint KEYEVENTF_KEYUP = 0x0002;

        // Windows refuses SetForegroundWindow from a background process right after the player
        // used another window (the foreground lock): after a click on an Electaco window it
        // returns 0 every time, which is why the first mount click did nothing. Later clicks
        // worked only because the first chord was injected keyboard input, and a process that
        // produced the last input may take the foreground. So produce some first: a tap of F24,
        // which no keyboard has, goes to the window currently in front (not the game) and does
        // nothing. Returns which way was used.
        private static string BringToForeground(IntPtr window)
        {
            if (SetForegroundWindow(window) != 0)
            {
                return "direct";
            }

            keybd_event(VK_F24, 0, 0, UIntPtr.Zero);
            keybd_event(VK_F24, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(20);
            if (SetForegroundWindow(window) != 0)
            {
                return "after an F24 tap";
            }

            // Last resort: attach to the foreground window's input queue.
            var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            var thisThread = GetCurrentThreadId();
            AttachThreadInput(thisThread, foregroundThread, true);
            SetForegroundWindow(window);
            AttachThreadInput(thisThread, foregroundThread, false);
            return "via AttachThreadInput";
        }

        public ControlClient(int socketId, WebSocket socket) : base(socketId, socket) 
        {
        }

        private void RegisterListeners()
        {
            Gw2WebSocketServer.InputHook.ButtonDown += OnButtonDown;
            Gw2WebSocketServer.InputHook.ButtonUp += OnButtonUp;
            Gw2WebSocketServer.InputHook.ButtonClick += InputHook_ButtonClick;
        }

        private void UnregisterListeners()
        {
            Gw2WebSocketServer.InputHook.ButtonDown -= OnButtonDown;
            Gw2WebSocketServer.InputHook.ButtonUp -= OnButtonUp;
            Gw2WebSocketServer.InputHook.ButtonClick -= InputHook_ButtonClick;
        }

        private void InputHook_ButtonClick(object sender, EventSourceEventArgs<ButtonClick> e)
        {
            
            SendJson(new Command { Type="mouseclick", Data= e.Data.Button.ToString()}).Wait();
            Console.WriteLine("Mouse click detected");
        }

        public void OnButtonDown(object sender, EventSourceEventArgs<ButtonDown> e)
        {
            Console.WriteLine("ButtonDown");
        }

        public void OnButtonUp(object sender, EventSourceEventArgs<ButtonUp> e)
        {
            Console.WriteLine("ButtonUp");
        }

        public void HandleIncomingData(ArraySegment<byte> buffer, WebSocketReceiveResult receiveResult)
        {
            var command = GetResult<Command>(buffer, receiveResult);
            if (command.Type == "keys")
            {
                HandleSendKeys(command.Data);
            }
        }

        public void HandleSendKeys(string data)
        {
            var arrdata = data.Split(',').Select(x => int.Parse(x)).ToArray();
            SendKeys(arrdata);
        }

        public async Task SendLoopAsync()
        {
            RegisterListeners();
            bool state = false;
            bool nstate = false;
            var cancellationToken = SendLoopTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    while (webSocket.State == WebSocketState.Open)
                    {
                        await Task.Delay(100);
                        var process = GameInstance.GetGameProcess();
                        nstate = process != null && !process.HasExited;
                        if (nstate != state)
                        {
                            await SendJson(new Command { Type = "gamerunning", Data = nstate.ToString() });
                        }
                        state = nstate;
                    }
                }
                catch (OperationCanceledException)
                {
                    // normal upon task/token cancellation, disregard
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "An error happened when running the web socket");
                }
            }
            UnregisterListeners();
        }
    }
}
