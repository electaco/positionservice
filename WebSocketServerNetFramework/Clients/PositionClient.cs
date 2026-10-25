using Serilog;
using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using TacoLib.Gw2MumbleLib;

namespace WebSocketServerNetFramework.Clients
{
    public class PositionClient: BaseClient, ISocketClient
    {
        // GW2 doesn't update MumbleLink during loading screens, and has no flag for them; uiTick
        // advances every game frame otherwise. When it stands still this long, a loading screen
        // has started.
        private static readonly TimeSpan LoadingScreenAfter = TimeSpan.FromMilliseconds(750);

        public PositionClient(int socketId, WebSocket socket) : base(socketId, socket) {}

        public void HandleIncomingData(ArraySegment<byte> buffer, WebSocketReceiveResult receiveResult)
        {
            throw new NotImplementedException();
        }

        public async Task SendLoopAsync()
        {
            Gw2Data data = null;
            uint lastTick = 0;
            var lastTickChange = DateTime.UtcNow;
            var loading = false;
            var cancellationToken = SendLoopTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    while (webSocket.State == WebSocketState.Open)
                    {
                        // ~1 ms with the 1 ms timer resolution set in Program.Main, so each game
                        // frame is picked up within a millisecond or two of being written.
                        await Task.Delay(1);
                        var ndata = TacoLib.TacoLib.GetGw2Data();

                        // Loading screen events: {"Type":"loadingscreen","Data":"True"|"False"}.
                        var now = DateTime.UtcNow;
                        if (ndata.uiTick != lastTick)
                        {
                            lastTick = ndata.uiTick;
                            lastTickChange = now;
                            if (loading)
                            {
                                loading = false;
                                await SendJson(new Command { Type = "loadingscreen", Data = loading.ToString() });
                            }
                        }
                        else if (!loading && now - lastTickChange > LoadingScreenAfter)
                        {
                            loading = true;
                            await SendJson(new Command { Type = "loadingscreen", Data = loading.ToString() });
                        }

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

