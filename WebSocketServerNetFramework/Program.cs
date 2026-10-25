using Newtonsoft.Json;
using Serilog;
using System;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TacoLib.GameInteract;
using TacoLib.Gw2MumbleLib;
using WindowsInput;
using WindowsInput.Events;
using System.Linq;

namespace WebSocketServerNetFramework
{
    class Program
    {


        const string ListenerAddress = "http://localhost:19939/";

        // Windows timers tick every ~15.6 ms by default, so the position loop's short delays
        // really waited ~15 ms: it saw game frames up to 15 ms late, unevenly, and missed frames
        // above ~65 fps, which made overlay markers jitter. 1 ms lets it see every frame promptly.
        [DllImport("winmm.dll")]
        static extern uint timeBeginPeriod(uint period);


        // Window rectangles (GameWindowCover) must be in physical pixels, like the game's client area;
        // a DPI-unaware process gets them scaled ("virtualized") on scaled displays.
        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        static void MakeDpiAware()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (Windows 10 1703+).
                if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
                {
                    return;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
            SetProcessDPIAware();
        }

        static async Task Main(string[] args)
        {
            MakeDpiAware();
            var log = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger(); 
            Log.Logger = log;
            timeBeginPeriod(1);
            //SendKeys(16, 17, 113);
            Gw2WebSocketServer.Start(ListenerAddress);
            await Gw2WebSocketServer.WaitForClose();
        }
    }
}
