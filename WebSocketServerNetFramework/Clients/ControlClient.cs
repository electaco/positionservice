using Serilog;
using System;
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

        public static void SendKeys(params int[] keys)
        {
            var process = GameInstance.GetGameProcess();
            SetForegroundWindow(process.MainWindowHandle);
            Thread.Sleep(10);
            var mkeys = keys.Select(x => (KeyCode)x).ToArray();
            Simulate.Events().ClickChord(mkeys).Invoke();
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
