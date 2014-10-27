using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ProcessServer
{
    public class Client
    {
        public Client(TcpClient tcpClient, byte[] buffer)
        {
            if (tcpClient == null)
                throw new ArgumentNullException("tcpClient");

            if (buffer == null)
                throw new ArgumentNullException("buffer");

            this.TcpClient = tcpClient;
            this.Buffer = buffer;
        }

        public byte[] Buffer { get; set; }
        public string Message { get; set; }
        public TcpClient TcpClient { get; set; }
        public NetworkStream NetworkStream { get { return TcpClient.GetStream(); } }
    }
}
