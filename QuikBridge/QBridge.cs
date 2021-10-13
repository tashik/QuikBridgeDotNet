using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace QuikBridge
{
    public class QBridge
    {
        private JsonProtocolHandler _pHandler;

        private Timer OrderBookTimer;

        public QBridge(JsonProtocolHandler pHandler)
        {
            // будем брать настройки из конфига (хост и порт сокета)
            string host = "127.0.0.1";
            int port = 57777;

            _pHandler = pHandler;

            _pHandler.connect(host, port);

            if (!_pHandler.Connected)
            {
                _pHandler.finish();
            }

            _pHandler.respArrived += onResp;
            _pHandler.reqArrived += onReq;
            _pHandler.connectionClose += onDisconnect;

            var autoresetEvent = new AutoResetEvent(false);
            OrderBookTimer = new Timer(Timer_Tick, autoresetEvent, 0, 500);
        }

        public void onReq(JsonMessage msg)
        {
            Console.WriteLine("msg arrived with message id " + msg.id);
            int msgId = 2;
            string[] args = { "SPBFUT", "RIZ1" };

            JsonReqData reqData = new JsonReqData()
            {
                method = "invoke",
                function = "getQuoteLevel2",
                arguments = args
            };
            JsonReqMessage req = new JsonReqMessage()
            {
                id = msgId,
                type = "req",
                data = reqData
            };

            _pHandler.sendReq(req);
        }

        public void onResp(JsonMessage msg)
        {
            Console.WriteLine("resp arrived with message id " + msg.id);
        }

        public void onDisconnect()
        {
            OrderBookTimer.Dispose();
        }

        private void Timer_Tick(Object state)
        {
            
        }

        public void getClassesList()
        {
            int msgId = 1;
            string[] args = {"SPBFUT", "RIZ1"};

            JsonReqData reqData = new JsonReqData()
            {
                method = "invoke",
                function = "Subsribe_Level_II_Quotes",
                arguments = args
            };
            JsonReqMessage req = new JsonReqMessage()
            {
                id = msgId,
                type = "req",
                data = reqData
            };

            _pHandler.sendReq(req);
        }
    }
}
