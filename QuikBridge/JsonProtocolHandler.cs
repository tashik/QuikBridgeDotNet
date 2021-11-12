using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json;
using System.Threading;

namespace QuikBridge
{
    public class JsonReqMessage
    {
        public int id { get; set; }
        public string type { get; set; }
        public JsonReqData data { get; set; }
    }

    public class JsonReqData
    {
        public string method { get; set; }
        public string function { get; set; }
        public string[] arguments { get; set; }
    }

    public class JsonVersionMessage
    {
        public int id { get; set; }
        public string type { get; set; }
        public string version { get; set; }
    }

    public class JsonMessage
    {
        public int id { get; set; }
        public string type { get; set; }
        public JsonElement body { get; set; }
    }

    public class JsonFinishMessage
    {
        public int id { get; set; }
        public string type { get; set; }
    }

    public class JsonProtocolHandler
    {
        private Socket Sock;
        private int _bufSz = 256;
        private int _filledSz;
        private byte[] _incommingBuf = new byte[256];
        private bool _peerEnded;
        private bool _weEnded;
        private int _expectedResponseId = -1;
        private ManualResetEvent _responseReceived = new ManualResetEvent(false);

        public event Action<JsonMessage> ReqArrived;
        public event Action<JsonMessage> RespArrived;
        public event Action ConnectionClose;


        private int _attempts;

        public const string MsgTypeReq = "req";
        public const string MsgTypeResp = "ans";
        public const string MsgTypeEnd = "end";
        public const string MsgTypeVersion = "ver";

        public JsonProtocolHandler(Socket socket)
        {
            Sock = socket;
            _attempts = 0;
        }

        public bool Connected
        {
            get
            {
                if (Sock != null)
                    return Sock.Connected;
                return false;
            }
        }

        public void Connect(string host, int port)
        {
            IPHostEntry ipHostInfo = Dns.GetHostEntry(host);
            IPAddress ipAddress = null;
            if (ipHostInfo.AddressList.Length == 0)
            {
                Console.WriteLine("Couldn't resolve ip");
                ipAddress = IPAddress.Parse(host.Trim());
            }
            else
            {
                Console.WriteLine($"{host} resolved to {ipHostInfo.AddressList.Length} addresses");
                int i;
                bool resolved = false;
                for (i = 0; i < ipHostInfo.AddressList.Length; i++)
                {
                    if (ipHostInfo.AddressList[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        Console.WriteLine("Address #{0} has correct family", i);
                        ipAddress = ipHostInfo.AddressList[i];
                        resolved = true;
                        break;
                    }
                }
                if (!resolved)
                {
                    Console.WriteLine("Couldn't resolve ip");
                    ipAddress = IPAddress.Parse(host.Trim());
                }
            }
            Console.WriteLine("Create socket {0}:{1}", ipAddress, port);
            Sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Console.WriteLine("Connect socket {0}:{1}", ipAddress, port);
            try
            {
                Sock.Connect(ipAddress, port);
                if (!Sock.Connected) return;
                Console.WriteLine("Socket connected");
                var state = new StateObject
                {
                    WorkSocket = Sock,
                    ProtocolHandler = this
                };
                Sock.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
            }
            catch (SocketException e)
            {
                if (!Sock.Connected)
                {
                    _weEnded = true;
                }
                Console.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// Коллбэк-парсер сообщения
        /// </summary>
        /// <param name="ar"></param>
        private static void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                StateObject state = (StateObject)ar.AsyncState;
                Socket sock = state.WorkSocket;
                JsonProtocolHandler ph = state.ProtocolHandler;

                int bytesRead = sock.EndReceive(ar);

                if (bytesRead > 0)
                {
                    if (ph._bufSz - ph._filledSz < bytesRead)
                    {
                        var narr = new byte[ph._filledSz + bytesRead];
                        ph._incommingBuf.CopyTo(narr, 0);
                        ph._incommingBuf = narr;
                        ph._bufSz = ph._filledSz + bytesRead;
                    }
                    Buffer.BlockCopy(state.Buffer, 0, ph._incommingBuf, ph._filledSz, bytesRead);
                    ph._filledSz += bytesRead;
                    ph.ProcessBuffer();
                    sock.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        /// <summary>
        /// Parse JSON data
        /// </summary>
        private void ProcessBuffer()
        {
            int i;
            //Console.WriteLine("INCOMMING BUFFER: ");
            //Console.WriteLine(Encoding.UTF8.GetString(_incommingBuf));
            for (i = 0; i < _filledSz; i++)
            {
                if (_incommingBuf[i] == '{')
                {
                    if (i > 0)
                    {
                        byte[] trash = new byte[i];
                        Buffer.BlockCopy(_incommingBuf, 0, trash, 0, i);
                        if (_filledSz - i > 0)
                            Buffer.BlockCopy(_incommingBuf, i, _incommingBuf, 0, _filledSz - i);
                        _filledSz -= i;
                        Console.WriteLine("Malformed request when buffer copy");
                        ParseError(trash);
                        i = 0;
                    }
                    break;
                }
            }
            if (i > 0)
                return;

            bool inString = false;
            bool inEsc = false;
            int braceNestingLevel = 0;

            for (i = 0; i < _filledSz; i++)
            {
                byte currCh = _incommingBuf[i];

                if (currCh == '"' && !inEsc)
                {
                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    if (currCh == '{')
                        braceNestingLevel++;
                    else if (currCh == '}')
                    {
                        braceNestingLevel--;
                        if (braceNestingLevel == 0)
                        {
                            byte[] pdoc = new byte[i + 1];
                            Buffer.BlockCopy(_incommingBuf, 0, pdoc, 0, i + 1);
                            Console.WriteLine("PARSED DOC: ");
                            Console.WriteLine(Encoding.UTF8.GetString(pdoc));
                            if (_filledSz - i - 1 > 0)
                            {
                                Buffer.BlockCopy(_incommingBuf, i, _incommingBuf, 0, _filledSz - i - 1);
                                Console.WriteLine("INCOMMING BUFFER AFTER PDOC CUT: ");
                                Console.WriteLine(Encoding.UTF8.GetString(_incommingBuf));
                            }
                                
                            _filledSz -= i + 1;
                            i = -1;
                            inString = false;
                            inEsc = false;
                            braceNestingLevel = 0;
                            JsonDocument jdoc = null;
                            try
                            {
                                jdoc = JsonDocument.Parse(pdoc);
                            }
                            catch (System.Text.Json.JsonException jex)
                            {
                                Console.WriteLine(jex.Message);
                            }
                            if (jdoc == null)
                            {
                                Console.WriteLine("Malformed request when jdoc not parsed");
                                ParseError(pdoc);
                            }
                            else
                            {
                                JsonElement jobj = jdoc.RootElement;
                                if (!jobj.TryGetProperty("id", out var idVal) || !jobj.TryGetProperty("type", out var typeVal))
                                    continue;
                                if (!idVal.TryGetInt32(out var id))
                                    id = -1;
                                if (id >= 0)
                                {
                                    var mtype = typeVal.ToString();
                                    switch (mtype)
                                    {
                                        case MsgTypeEnd:
                                        {
                                            _peerEnded = true;
                                            if (_weEnded)
                                            {
                                                Finish();
                                            }
                                            else
                                                EndArrived();
                                            return;
                                        }
                                        case MsgTypeVersion:
                                        {
                                            int ver = 0;
                                            JsonElement verVal;
                                            if (jobj.TryGetProperty("version", out verVal))
                                            {
                                                if (!verVal.TryGetInt32(out ver))
                                                    ver = 0;
                                            }
                                            VerArrived(ver);
                                            return;
                                        }
                                        case MsgTypeReq:
                                        case MsgTypeResp:
                                        {
                                            JsonElement data;
                                            jobj.TryGetProperty("data", out data);
                                            NewMsgArrived(id, mtype, data);
                                            return;
                                        }
                                        default:
                                            Console.WriteLine("Unknown message type {0}", mtype);
                                            return;
                                    }
                                }
                            }
                            continue;
                        }
                    }
                }
                else
                {
                    if (currCh == '\\' && !inEsc)
                        inEsc = true;
                    else
                        inEsc = false;
                }
            }
        }

        private static void ParseError(byte[] trash)
        {
            Console.WriteLine("Can't parse:");
            Console.WriteLine(Encoding.UTF8.GetString(trash));
        }

        public void SendReq(JsonReqMessage req)
        {
            if (_weEnded) return;

            string reqJson = JsonConvert.SerializeObject(req);
            Sock.Send(Encoding.UTF8.GetBytes(reqJson));
        }

        public void SendResp(int id, JsonReqData data)
        {
            if (_weEnded) return;

            var resp = new JsonReqMessage
            {
                id = id,
                type = "ans",
                data = data
            };

            var respJson = JsonConvert.SerializeObject(resp);
            Sock.Send(Encoding.UTF8.GetBytes(respJson));
        }

        public void SendVer(string ver)
        {
            if (_weEnded) return;

            var req = new JsonVersionMessage
            {
                id = 0,
                type = "ver",
                version = ver
            };

            var reqJson = JsonConvert.SerializeObject(req);
            Sock.Send(Encoding.UTF8.GetBytes(reqJson));
        }

        public void Finish(bool force = false)
        {
            if (_weEnded && ! force)
            {
                return;
            }
            JsonFinishMessage req = new JsonFinishMessage
            {
                id = 0,
                type = "end"
            };

            string reqJson = JsonConvert.SerializeObject(req);
            Sock.Send(Encoding.UTF8.GetBytes(reqJson));

            _weEnded = true;

            if (_peerEnded || force) {
                Sock.Shutdown(SocketShutdown.Both);
                Sock.Close();
            }
        }

        private void NewMsgArrived(int id, string type, JsonElement data)
        {
            JsonMessage jReq = new JsonMessage()
            {
                id = id,
                type = type,
                body = data
            };
            switch (type)
            {
                case MsgTypeReq:
                    ReqArrived?.Invoke(jReq);
                    break;
                case MsgTypeResp:
                    RespArrived?.Invoke(jReq);
                    _responseReceived.Set();
                    break;
                default:
                    Console.WriteLine("Unsupported message type");
                    break;
            }
        }

        private void EndArrived()
        {
            Console.WriteLine("end request received");
            ConnectionClose?.Invoke();
        }

        private void VerArrived(int ver)
        {
            Console.WriteLine($"Version of protocol: {ver}");
        }
    }

    public class StateObject
    {
        // Client socket.
        public Socket WorkSocket;
        public const int BufferSize = 256;
        public JsonProtocolHandler ProtocolHandler;
        public byte[] Buffer = new byte[BufferSize];
    }
}
