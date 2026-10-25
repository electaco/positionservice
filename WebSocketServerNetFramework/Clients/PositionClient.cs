using Serilog;
using System;
using System.Diagnostics;
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
        // Monotonic clock for Gw2Data.sampleTime.
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        // Window overlap is checked this often (not on every 1 ms poll), and a change must hold for
        // CoverDebounce before it is sent, so a window dragged across the game doesn't flicker.
        private static readonly TimeSpan CoverCheckEvery = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan CoverDebounce = TimeSpan.FromMilliseconds(150);

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
            var tickTime = Clock.Elapsed.TotalMilliseconds;
            var loading = false;
            var cover = new GameWindowCover();
            // null until the first check, so the state is always sent once after connecting.
            bool? sentCovered = null;
            var pendingCovered = false;
            var pendingSince = DateTime.UtcNow;
            var lastCoverCheck = DateTime.MinValue;
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
                            tickTime = Clock.Elapsed.TotalMilliseconds;
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

                        // Game covered by another program's window: {"Type":"gamecovered","Data":"True"|"False"}.
                        if (now - lastCoverCheck >= CoverCheckEvery)
                        {
                            lastCoverCheck = now;
                            var covered = cover.IsCovered(ndata.context.ProcessId);
                            if (covered != pendingCovered)
                            {
                                pendingCovered = covered;
                                pendingSince = now;
                            }
                            if (sentCovered != pendingCovered && (sentCovered == null || now - pendingSince >= CoverDebounce))
                            {
                                sentCovered = pendingCovered;
                                await SendJson(new Command { Type = "gamecovered", Data = pendingCovered.ToString() });
                            }
                        }

                        if (ndata.Equals(data))
                        {
                            continue;
                        }

                        data = ndata;
                        // The game frame these coordinates belong to started when its tick was seen.
                        ndata.sampleTime = Math.Round(tickTime, 2);
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

