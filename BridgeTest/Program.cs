using QuikBridge;
using System;

namespace BridgeTest
{

    class Program
    {
        static void Main(string[] args)
        {
            //@todo from outer config
            string host = "127.0.0.1";
            int port = 57777;
            
            QBridge bridge = new QBridge(host, port);

            bridge.SubscribeOrderBook("RIZ1", "SPBFUT");
            bridge.SubscribeOrderBook("SiZ1", "SPBFUT");
            //bridge.SubscribeOrderBook("BRZ1", "SPBFUT");
            //bridge.SubscribeOrderBook("SRZ1", "SPBFUT");
            //bridge.SubscribeOrderBook("GDZ1", "SPBFUT");
            //bridge.SubscribeOrderBook("GZZ1", "SPBFUT");
            
            Console.Read();
        }
    }
}
