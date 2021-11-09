using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketServerNetFramework.Clients
{
    public interface ISocketClient
    {
        int SocketId { get; }
        WebSocket webSocket { get; }
        CancellationTokenSource SendLoopTokenSource { get; }

        Task SendLoopAsync();
        void HandleIncomingData(ArraySegment<byte> buffer, WebSocketReceiveResult receiveResult);
    }

    public class BaseClient
    {
        public int SocketId { get; set; }

        public WebSocket webSocket { get; set; }
        public CancellationTokenSource SendLoopTokenSource { get; set; } = new CancellationTokenSource();

        protected string GetResultText(ArraySegment<byte> buffer, WebSocketReceiveResult receiveResult)
        {
            return Encoding.UTF8.GetString(buffer.Array, 0, receiveResult.Count);
        }

        public T GetResult<T>(ArraySegment<byte> buffer, WebSocketReceiveResult receiveResult)
        {
            var text = GetResultText(buffer, receiveResult);
            return JsonConvert.DeserializeObject<T>(text);
        }

       public async Task SendJson(object data)
        {
            var jdata = new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data)));
            await webSocket.SendAsync(jdata, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public BaseClient(int socketId, WebSocket socket)
        {
            SocketId = socketId;
            webSocket = socket;
        }
    }

}
