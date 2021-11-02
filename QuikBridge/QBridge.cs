using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using Newtonsoft.Json;
using System.Threading;
using QuikBridge.Entities;

namespace QuikBridge
{
    public enum SubscriptionType
    {
        OrderBook,
        Quotes,
        SecurityInfo
    }

    public class Subscription
    {
        public SubscriptionType MessageType { get; set; }
        public string Ticker { get; set; }
        public string ClassCode { get; set; }
    }

    public class Message
    {
        public int Id { get; set; }
        public SubscriptionType MessageType { get; set; }
        public string Method { get; set; }
        
        public string Ticker { get; set; }
    }
    
    public class QBridge
    {
        private readonly JsonProtocolHandler _pHandler;

        private readonly Timer _orderBookTimer;

        public event Action<string, SubscriptionType, OrderBook> OrderBookUpdate;

        private readonly MessageIndexer _msgIndexer;

        private List<Subscription> Subscriptions { get; set; }

        private readonly Dictionary<int, Message> _messageRegistry;

        public QBridge(string host, int port)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _pHandler = new JsonProtocolHandler(socket);
            _pHandler.Connect(host, port);

            if (!IsConnected())
            {
                _pHandler.Finish();
            }

            _pHandler.RespArrived += OnResp;
            _pHandler.ReqArrived += OnReq;
            _pHandler.ConnectionClose += OnDisconnect;

            Subscriptions = new List<Subscription>();
            _messageRegistry = new Dictionary<int, Message>();
            
            _msgIndexer = new MessageIndexer();
            
            _orderBookTimer = new Timer(Timer_Tick, new AutoResetEvent(false), 0, 5000);
        }

        public bool IsConnected()
        {
            return _pHandler.Connected;
        }

        private Subscription IsSubscribed(SubscriptionType msgType, string ticker)
        {
            return Subscriptions.FindLast(item => item.Ticker == ticker && item.MessageType == msgType);
        }

        public void SubscribeOrderBook(string ticker, string classCode)
        {
            if (IsSubscribed(SubscriptionType.OrderBook, ticker) != null)
            {
                return;
            }

            int msgId = _msgIndexer.GetIndex();
            
            string[] args = {classCode, ticker};
            
            var req = new JsonReqMessage()
            {
                id = msgId,
                type = "req",
                data = new JsonReqData()
                {
                    method = "invoke",
                    function = "Subscribe_Level_II_Quotes",
                    arguments = args
                }
            };
            
            var newSubscription = new Subscription()
            {
                MessageType = SubscriptionType.OrderBook,
                Ticker = ticker,
                ClassCode = classCode
            };
            
            Subscriptions.Add(newSubscription);
            RegisterRequest(req.id, req.data.function, newSubscription);
            
            _pHandler.SendReq(req);
        }

        private void RegisterRequest(int id, string methodName, Subscription data)
        {
            if (_messageRegistry.ContainsKey(id)) return;
            
            _messageRegistry.Add(id, new Message()
            {
                Id = id,
                Method = methodName,
                MessageType = data.MessageType,
                Ticker = data.Ticker
            });
        }

        public void Unsubscribe(SubscriptionType msgType, string ticker)
        {
            var subscription = IsSubscribed(msgType, ticker);
            if (subscription == null) return;
            
            string[] args = {subscription.ClassCode, ticker};
            
            var req = new JsonReqMessage()
            {
                id = _msgIndexer.GetIndex(),
                type = "req",
                data = new JsonReqData()
                {
                    method = "invoke",
                    function = "Unsubscribe_Level_II_Quotes",
                    arguments = args
                }
            };
            RegisterRequest(req.id, req.data.function, subscription);
            _pHandler.SendReq(req);
            Subscriptions.Remove(subscription);
        }

        private void OnReq(JsonMessage msg)
        {
            Console.WriteLine("msg arrived with message id " + msg.id);
        }

        private void OnResp(JsonMessage msg)
        {
            if (!_messageRegistry.ContainsKey(msg.id)) return;

            var newMessage = _messageRegistry[msg.id];
            Console.WriteLine("resp arrived with message id {0} to function {1} for ticker {2}", newMessage.Id, newMessage.Method, newMessage.Ticker);

            switch (newMessage.Method)
            {
                case "getQuoteLevel2":
                {
                    try
                    {
                        msg.body.TryGetProperty("result", out var result);
                        foreach (var res in result.EnumerateArray())
                        {
                            string jsonStr = res.GetRawText();
                            var snapshot = JsonConvert.DeserializeObject<OrderBook>(jsonStr);
                            double bidsCount = 0;
                            double offersCount = 0;

                            if (snapshot != null && snapshot.bid_count != "" && snapshot.offer_count != "")
                            {
                                bool isBidParsed = double.TryParse(snapshot.bid_count, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out bidsCount);
                                bool isOfferParsed = double.TryParse(snapshot.offer_count, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out offersCount);
                                if (bidsCount > 0 && offersCount > 0)
                                    OrderBookUpdate?.Invoke(newMessage.Ticker, newMessage.MessageType, snapshot);
                            }
                            
                        }
                        
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error on Json Convert {0}", e.Message);
                    }

                    break;
                }
                default:
                    return;
            }
        }

        private void OnDisconnect()
        {
            _orderBookTimer.Dispose();
        }

        private void Timer_Tick(object state)
        {
            if (!Subscriptions.Any()) return;

            foreach (var entry in Subscriptions)
            {
                switch (entry.MessageType)
                {
                    case SubscriptionType.OrderBook:
                    {
                        string[] args = { entry.ClassCode, entry.Ticker };

                        var req = new JsonReqMessage()
                        {
                            id = _msgIndexer.GetIndex(),
                            type = "req",
                            data = new JsonReqData()
                            {
                                method = "invoke",
                                function = "getQuoteLevel2",
                                arguments = args
                            }
                        };
                        RegisterRequest(req.id, req.data.function, entry);
                        _pHandler.SendReq(req);
                        return;
                    }
                    default:
                        return;
                }
            }
        }
    }
}
