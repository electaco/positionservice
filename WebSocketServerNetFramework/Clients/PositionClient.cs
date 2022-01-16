using Serilog;
using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using TacoLib.Gw2MumbleLib;

namespace WebSocketServerNetFramework.Clients
{
    public class PositionClient: BaseClient, ISocketClient
    {
        public PositionClient(int socketId, WebSocket socket) : base(socketId, socket) {}

        public void HandleIncomingData(ArraySegment<byte> buffer, WebSocketReceiveResult receiveResult)
        {
            throw new NotImplementedException();
        }

        public async Task SendLoopAsync()
        {
            Gw2Data data = null;
            var cancellationToken = SendLoopTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    while (webSocket.State == WebSocketState.Open)
                    {
                        await Task.Delay(5);
                        var ndata = TacoLib.TacoLib.GetGw2Data();
                        if (ndata.Equals(data))
                        {
                            continue;
                        }

                        data = ndata;
                        try
                        {
                            await SendJson(ndata);
                        }
                        catch (Exception e)
                        {
                            Log.Error(e, "An error happened when sending data");
                        }
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
        }
    }
}

