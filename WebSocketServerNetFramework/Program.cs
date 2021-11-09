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


        static async Task Main(string[] args)
        {
            var log = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger(); 
            Log.Logger = log;
            //SendKeys(16, 17, 113);
            Gw2WebSocketServer.Start(ListenerAddress);
            await Gw2WebSocketServer.WaitForClose();
        }
    }
}
