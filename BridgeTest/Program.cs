using QuikBridge;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace BridgeTest
{

    class Program
    {
        static void Main(string[] args)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            JsonProtocolHandler pHandler = new JsonProtocolHandler(socket);
            QBridge bridge = new QBridge(pHandler);

            bridge.getClassesList();
            Console.Read();
        }
    }
}
