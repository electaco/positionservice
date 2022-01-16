using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using WebSocketServerNetFramework.Clients;
using WindowsInput;
using WindowsInput.Events.Sources;

// From https://github.com/MV10/WebSocketExample/blob/master/WebSocketWithBroadcasts/

namespace WebSocketServerNetFramework
{
    public static class Gw2WebSocketServer
    {
        private static HttpListener Listener;

        private static CancellationTokenSource SocketLoopTokenSource;
        private static CancellationTokenSource ListenerLoopTokenSource;

        private static int SocketCounter = 0;

        private static bool ServerIsRunning = true;

        public static IMouseEventSource InputHook;
        // The key is a socket id
        private static ConcurrentDictionary<int, ISocketClient> Clients = new ConcurrentDictionary<int, ISocketClient>();

        public static void Start(string uriPrefix)
        {
            if (InputHook == null)
            {
                Console.WriteLine("Doing hooks");
                InputHook = Capture.Global.MouseAsync();
            }

            SocketLoopTokenSource = new CancellationTokenSource();
            ListenerLoopTokenSource = new CancellationTokenSource();
            Listener = new HttpListener();
            Listener.Prefixes.Add(uriPrefix);
            Listener.Start();
            if (Listener.IsListening)
            {
                Console.WriteLine("Connect browser for a basic echo-back web page.");
                Console.WriteLine($"Server listening: {uriPrefix}");
                // listen on a separate thread so that Listener.Stop can interrupt GetContextAsync
                Task.Run(() => ListenerProcessingLoopAsync().ConfigureAwait(false));
            }
            else
            {
                Console.WriteLine("Server failed to start.");
            }
        }

        public static async Task WaitForClose()
        {
           while (Listener.IsListening)
            {
                await Task.Delay(100);
            }
        }

        public static async Task StopAsync()
        {
            if (Listener?.IsListening ?? false && ServerIsRunning)
            {
                Console.WriteLine("\nServer is stopping.");

                ServerIsRunning = false;            // prevent new connections during shutdown
                await CloseAllSocketsAsync();            // also cancels processing loop tokens (abort ReceiveAsync)
                ListenerLoopTokenSource.Cancel();   // safe to stop now that sockets are closed
                Listener.Stop();
                Listener.Close();
            }
        }

        private static async Task ListenerProcessingLoopAsync()
        {
            var cancellationToken = ListenerLoopTokenSource.Token;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    HttpListenerContext context = await Listener.GetContextAsync();
                    if (ServerIsRunning)
                    {
                        if (context.Request.IsWebSocketRequest)
                        {
                            // HTTP is only the initial connection; upgrade to a client-specific websocket
                            HttpListenerWebSocketContext wsContext = null;
                            try
                            {
                                wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                                int socketId = Interlocked.Increment(ref SocketCounter);
                                ISocketClient client =null;

                                if (context.Request.Url.LocalPath == "/position")
                                    client = new PositionClient(socketId, wsContext.WebSocket);

                                if (context.Request.Url.LocalPath == "/control")
                                    client = new ControlClient(socketId, wsContext.WebSocket);

                                Clients.TryAdd(socketId, client);
                                Console.WriteLine($"Socket {socketId}: New connection for {context.Request.Url.LocalPath }");
                                _ = Task.Run(() => SocketProcessingLoopAsync(client).ConfigureAwait(false));
                            }
                            catch (Exception)
                            {
                                // server error if upgrade from HTTP to WebSocket fails
                                context.Response.StatusCode = 500;
                                context.Response.StatusDescription = "WebSocket upgrade failed";
                                context.Response.Close();
                                return;
                            }
                        }
                        else
                        {
                            context.Response.StatusCode = 400;
                            context.Response.Close();
                        }
                    }
                    else
                    {
                        // HTTP 409 Conflict (with server's current state)
                        context.Response.StatusCode = 409;
                        context.Response.StatusDescription = "Server is shutting down";
                        context.Response.Close();
                        return;
                    }
                }
            }
            catch (HttpListenerException ex) when (ServerIsRunning)
            {
                //Program.ReportException(ex);
            }
        }

        private static async Task SocketProcessingLoopAsync(ISocketClient client)
        {
            _ = Task.Run(() => client.SendLoopAsync().ConfigureAwait(false));

            var socket = client.webSocket;
            var loopToken = SocketLoopTokenSource.Token;
            var sendTokenSource = client.SendLoopTokenSource; // store a copy for use in finally block
            try
            {
                var buffer = WebSocket.CreateServerBuffer(4096);
                while (socket.State != WebSocketState.Closed && socket.State != WebSocketState.Aborted && !loopToken.IsCancellationRequested)
                {
                    var receiveResult = await client.webSocket.ReceiveAsync(buffer, loopToken);
                    // if the token is cancelled while ReceiveAsync is blocking, the socket state changes to aborted and it can't be used
                    if (!loopToken.IsCancellationRequested)
                    {
                        // the client is notifying us that the connection will close; send acknowledgement
                        if (client.webSocket.State == WebSocketState.CloseReceived && receiveResult.MessageType == WebSocketMessageType.Close)
                        {
                            Console.WriteLine($"Socket {client.SocketId}: Acknowledging Close frame received from client");
                            sendTokenSource.Cancel();
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledge Close frame", CancellationToken.None);
                            // the socket state changes to closed at this point
                        }

                        // echo text or binary data to the broadcast queue
                        if (client.webSocket.State == WebSocketState.Open)
                        {
                            client.HandleIncomingData(buffer, receiveResult);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal upon task/token cancellation, disregard
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Socket {client.SocketId}:");
                //Program.ReportException(ex);
            }
            finally
            {
                sendTokenSource.Cancel();
                Console.WriteLine($"Socket {client.SocketId}: Ended processing loop in state {socket.State}");

                // don't leave the socket in any potentially connected state
                if (client.webSocket.State != WebSocketState.Closed)
                    client.webSocket.Abort();

                // by this point the socket is closed or aborted, the ConnectedClient object is useless
                if (Clients.TryRemove(client.SocketId, out _))
                    socket.Dispose();
            }
        }

        private static async Task CloseAllSocketsAsync()
        {
            // We can't dispose the sockets until the processing loops are terminated,
            // but terminating the loops will abort the sockets, preventing graceful closing.
            var disposeQueue = new List<WebSocket>(Clients.Count);

            while (Clients.Count > 0)
            {
                var client = Clients.ElementAt(0).Value;
                Console.WriteLine($"Closing Socket {client.SocketId}");

                client.SendLoopTokenSource.Cancel();
                Console.WriteLine("... ending broadcast loop");


                if (client.webSocket.State != WebSocketState.Open)
                {
                    Console.WriteLine($"... socket not open, state = {client.webSocket.State}");
                }
                else
                {
                    var timeout = new CancellationTokenSource(2500);
                    try
                    {
                        Console.WriteLine("... starting close handshake");
                        await client.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", timeout.Token);
                    }
                    catch (OperationCanceledException ex)
                    {
                        // normal upon task/token cancellation, disregard
                    }
                }

                if (Clients.TryRemove(client.SocketId, out _))
                {
                    // only safe to Dispose once, so only add it if this loop can't process it again
                    disposeQueue.Add(client.webSocket);
                }

                Console.WriteLine("... done");
            }

            // now that they're all closed, terminate the blocking ReceiveAsync calls in the SocketProcessingLoop threads
            SocketLoopTokenSource.Cancel();

            // dispose all resources
            foreach (var socket in disposeQueue)
                socket.Dispose();
        }

    }
}