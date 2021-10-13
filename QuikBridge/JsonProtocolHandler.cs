using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;

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
        protected Socket sock = null;
        private int bufSz = 256;
        private int filledSz = 0;
        private byte[] incommingBuf = new byte[256];
        private bool peerEnded = false;
        private bool weEnded = false;
        private int expectedResponseId = -1;
        private ManualResetEvent responseReceived = new ManualResetEvent(false);

        public event Action<JsonMessage> reqArrived;
        public event Action<JsonMessage> respArrived;
        public event Action connectionClose;


        private int attempts;

        public JsonProtocolHandler(Socket socket)
        {
            sock = socket;
            attempts = 0;
        }

        public bool Connected
        {
            get
            {
                if (sock != null)
                    return sock.Connected;
                return false;
            }
        }

        public void connect(string host, int port)
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
                Console.WriteLine(String.Format("{0} resolved to {1} addresses", host, ipHostInfo.AddressList.Length));
                int i;
                bool resolved = false;
                for (i = 0; i < ipHostInfo.AddressList.Length; i++)
                {
                    if (ipHostInfo.AddressList[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        Console.WriteLine(String.Format("Address #{0} has correct family", i));
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
            Console.WriteLine(String.Format("Create socket {0}:{1}", ipAddress.ToString(), port));
            sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Console.WriteLine(String.Format("Connect socket {0}:{1}", ipAddress.ToString(), port));
            sock.Connect(ipAddress, port);
            if (sock.Connected)
            {
                Console.WriteLine("Socket connected");
                StateObject state = new StateObject();
                state.workSocket = sock;
                state.pHandler = this;
                sock.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
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
                Socket sock = state.workSocket;
                JsonProtocolHandler ph = state.pHandler;

                int bytesRead = sock.EndReceive(ar);

                if (bytesRead > 0)
                {
                    if (ph.bufSz - ph.filledSz < bytesRead)
                    {
                        byte[] narr = new byte[ph.filledSz + bytesRead];
                        ph.incommingBuf.CopyTo(narr, 0);
                        ph.incommingBuf = narr;
                        ph.bufSz = ph.filledSz + bytesRead;
                    }
                    Buffer.BlockCopy(state.buffer, 0, ph.incommingBuf, ph.filledSz, bytesRead);
                    ph.filledSz += bytesRead;
                    ph.processBuffer();
                    sock.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        /// <summary>
        /// Обработка буфера с ответом
        /// </summary>
        private void processBuffer()
        {
            int i;
            for (i = 0; i < filledSz; i++)
            {
                if (incommingBuf[i] == '{')
                {
                    if (i > 0)
                    {
                        byte[] trash = new byte[i];
                        Buffer.BlockCopy(incommingBuf, 0, trash, 0, i);
                        if (filledSz - i > 0)
                            Buffer.BlockCopy(incommingBuf, i, incommingBuf, 0, filledSz - i);
                        filledSz -= i;
                        Console.WriteLine("Malformed request");
                        ParseError(trash);
                        i = 0;
                    }
                    break;
                }
            }
            if (i > 0)
                return;

            bool in_string = false;
            bool in_esc = false;
            int brace_nesting_level = 0;

            for (i = 0; i < filledSz; i++)
            {
                byte curr_ch = incommingBuf[i];

                if (curr_ch == '"' && !in_esc)
                {
                    in_string = !in_string;
                    continue;
                }

                if (!in_string)
                {
                    if (curr_ch == '{')
                        brace_nesting_level++;
                    else if (curr_ch == '}')
                    {
                        brace_nesting_level--;
                        if (brace_nesting_level == 0)
                        {
                            byte[] pdoc = new byte[i + 1];
                            Buffer.BlockCopy(incommingBuf, 0, pdoc, 0, i + 1);
                            if (filledSz - i - 1 > 0)
                                Buffer.BlockCopy(incommingBuf, i, incommingBuf, 0, filledSz - i - 1);
                            filledSz -= i + 1;
                            i = -1;
                            in_string = false;
                            in_esc = false;
                            brace_nesting_level = 0;
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
                                Console.WriteLine("Malformed request");
                                ParseError(pdoc);
                            }
                            else
                            {
                                JsonElement jobj = jdoc.RootElement;
                                JsonElement idVal, typeVal;
                                if (jobj.TryGetProperty("id", out idVal) && jobj.TryGetProperty("type", out typeVal))
                                {
                                    int id = -1;
                                    if (!idVal.TryGetInt32(out id))
                                        id = -1;
                                    if (id >= 0)
                                    {
                                        string mtype = typeVal.ToString();
                                        if (mtype == "end")
                                        {
                                            peerEnded = true;
                                            if (weEnded)
                                            {
                                                finish();
                                            }
                                            else
                                                EndArrived();
                                            return;
                                        }
                                        if (mtype == "ver")
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
                                        if (mtype == "ans" || mtype == "req")
                                        {
                                            JsonElement data;
                                            jobj.TryGetProperty("data", out data);
                                            if (mtype == "rpy")
                                                reqMsgArrived(id, "resp", data);
                                            else
                                                reqMsgArrived(id, "req", data);
                                            return;
                                        }
                                    }
                                }
                            }
                            continue;
                        }
                    }
                }
                else
                {
                    if (curr_ch == '\\' && !in_esc)
                        in_esc = true;
                    else
                        in_esc = false;
                }
            }
        }

        public void ParseError(byte[] trash)
        {
            Console.WriteLine("Can't parse:");
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(trash));
        }

        public void sendReq(JsonReqMessage req)
        {
            if (weEnded) return;

            string reqJson = JsonConvert.SerializeObject(req);
            sock.Send(Encoding.UTF8.GetBytes(reqJson));
        }

        public void sendResp(int id, JsonReqData data)
        {
            if (weEnded) return;

            JsonReqMessage resp = new JsonReqMessage();
            resp.id = id;
            resp.type = "ans";
            resp.data = data;

            string respJson = JsonConvert.SerializeObject(resp);
            sock.Send(Encoding.UTF8.GetBytes(respJson));
        }

        public void sendVer(string ver)
        {
            if (weEnded) return;

            JsonVersionMessage req = new JsonVersionMessage();
            req.id = 0;
            req.type = "ver";
            req.version = ver;

            string reqJson = JsonConvert.SerializeObject(req);
            sock.Send(Encoding.UTF8.GetBytes(reqJson));
        }

        public void finish(bool force = false)
        {
            if (weEnded && ! force)
            {
                return;
            }
            JsonFinishMessage req = new JsonFinishMessage();
            req.id = 0;
            req.type = "end";

            string reqJson = JsonConvert.SerializeObject(req);
            sock.Send(Encoding.UTF8.GetBytes(reqJson));

            weEnded = true;

            if (peerEnded || force) {
                sock.Shutdown(SocketShutdown.Both);
                sock.Close();
            }
        }

        public void reqMsgArrived(int id, string type, JsonElement data)
        {
            JsonMessage jReq = new JsonMessage()
            {
                id = id,
                type = type,
                body = data
            };
            if (type == "req" && reqArrived != null) reqArrived(jReq);
            else if (type == "ans" && respArrived != null) respArrived(jReq);
        }

        public void EndArrived()
        {
            Console.WriteLine("end request received");
            if (connectionClose != null) connectionClose();
        }

        public void VerArrived(int ver)
        {
            Console.WriteLine(String.Format("Version of protocol: {0}", ver));
        }
    }

    public class StateObject
    {
        // Client socket.
        public Socket workSocket = null;
        public const int BufferSize = 256;
        public JsonProtocolHandler pHandler = null;
        public byte[] buffer = new byte[BufferSize];
    }
}
