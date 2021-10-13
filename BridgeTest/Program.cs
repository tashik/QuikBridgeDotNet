using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BridgeTest
{
    public interface IProtocolConsumer
    {
        void MsgArrived(int id, JsonElement data);
        void RpyArrived(int id, JsonElement data);
        void VerArrived(int ver);
        void EndArrived();
        void Finished();
        void ParseError(byte[] trash);
        void writeToLog(string msg);
    }
    public class StateObject
    {
        // Client socket.
        public Socket workSocket = null;
        public ProtocolHandler pHandler = null;
        public const int BufferSize = 256;
        public byte[] buffer = new byte[BufferSize];
    }
    public class ProtocolHandler
    {
        private Socket sock = null;
        private IProtocolConsumer consumer = null;
        private int bufSz = 256;
        private int filledSz = 0;
        private byte[] incommingBuf = new byte[256];
        private bool peerEnded = false;
        private bool weEnded = false;
        private int expectedResponseId = -1;
        private ManualResetEvent responseReceived = new ManualResetEvent(false);
        private JsonElement answerData;

        public bool Connected
        {
            get
            {
                if (sock != null)
                    return sock.Connected;
                return false;
            }
        }
        /// <summary>
        /// Прилетело сообщение
        /// </summary>
        /// <param name="id"></param>
        /// <param name="data"></param>
        private void MsgArrived(int id, JsonElement data)
        {
            if (consumer != null)
                consumer.MsgArrived(id, data);
        }

        /// <summary>
        /// Прилетел ответ на запрос
        /// </summary>
        /// <param name="id"></param>
        /// <param name="data"></param>
        private void RpyArrived(int id, JsonElement data)
        {
            if (expectedResponseId == id)
            {
                answerData = data;
                responseReceived.Set();
            }
            else
            {
                if (consumer != null)
                    consumer.RpyArrived(id, data);
            }
        }

        /// <summary>
        /// Прилетела версия
        /// </summary>
        /// <param name="ver"></param>
        private void VerArrived(int ver)
        {
            if (consumer != null)
                consumer.VerArrived(ver);
        }

        /// <summary>
        /// Прилетело сообщение о закрытии соединения
        /// </summary>
        private void EndArrived()
        {
            if (consumer != null)
                consumer.EndArrived();
        }

        /// <summary>
        /// Закрытие соединения
        /// </summary>
        private void Finished()
        {
            if (consumer != null)
                consumer.Finished();
        }

        /// <summary>
        /// Ошибка разбора сообщения
        /// </summary>
        /// <param name="trash"></param>
        private void ParseError(byte[] trash)
        {
            if (consumer != null)
                consumer.ParseError(trash);
        }

        /// <summary>
        /// Синхронная отправка сообщения
        /// </summary>
        /// <param name="id"></param>
        /// <param name="data"></param>
        /// <param name="showInLog"></param>
        /// <returns></returns>
        public JsonElement SendMsgAndWaitRpy(int id, JsonElement data, bool showInLog = true)
        {
            expectedResponseId = id;
            SendMsg(id, data, showInLog);
            responseReceived.WaitOne();
            return answerData;
        }

        /// <summary>
        /// Асинхронная отправка сообщения
        /// </summary>
        /// <param name="id"></param>
        /// <param name="data"></param>
        /// <param name="showInLog"></param>
        public void SendMsg(int id, JsonElement data, bool showInLog = true)
        {
            if (weEnded)
                return;
            if (showInLog)
                Console.WriteLine(data.GetRawText());
            sock.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("{{\"id\": {0}, \"type\": \"msg\", \"data\": {1}}}", id, data.GetRawText())));
        }

        /// <summary>
        /// Отправка ответа
        /// </summary>
        /// <param name="id"></param>
        /// <param name="data"></param>
        /// <param name="showInLog"></param>
        public void SendRpy(int id, JsonElement data, bool showInLog = true)
        {
            if (weEnded)
                return;
            if (showInLog)
                Console.WriteLine(data.GetRawText());
            sock.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("{{\"id\": {0}, \"type\": \"rpy\", \"data\": {1}}}", id, data.GetRawText())));
        }

        /// <summary>
        /// Запрос версии
        /// </summary>
        /// <param name="ver"></param>
        public void SendVer(int ver)
        {
            if (weEnded)
                return;
            sock.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("{{\"id\": 0, \"type\": \"ver\", \"version\": {0} }}", ver)));
        }

        /// <summary>
        /// разрыв соединения
        /// </summary>
        /// <param name="force"></param>
        public void End(bool force = false)
        {
            if (weEnded && !force)
                return;
            sock.Send(System.Text.Encoding.UTF8.GetBytes("{\"id\": 0, \"type\": \"end\"}"));
            weEnded = true;
            if (peerEnded || force)
            {
                Close();
            }
        }

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="cons"></param>
        /// <param name="host"></param>
        /// <param name="port"></param>
        public ProtocolHandler(IProtocolConsumer cons, string host, int port)
        {
            cons.writeToLog(String.Format("Create protocol handler for {0}:{1}", host, port));
            consumer = cons;
            IPHostEntry ipHostInfo = Dns.GetHostEntry(host);
            IPAddress ipAddress = null;
            if (ipHostInfo.AddressList.Length == 0)
            {
                cons.writeToLog("Couldn't resolve ip");
                ipAddress = IPAddress.Parse(host.Trim());
            }
            else
            {
                cons.writeToLog(String.Format("{0} resolved to {1} addresses", host, ipHostInfo.AddressList.Length));
                int i;
                bool resolved = false;
                for (i = 0; i < ipHostInfo.AddressList.Length; i++)
                {
                    if (ipHostInfo.AddressList[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        cons.writeToLog(String.Format("Address #{0} has correct family", i));
                        ipAddress = ipHostInfo.AddressList[i];
                        resolved = true;
                        break;
                    }
                }
                if (!resolved)
                {
                    cons.writeToLog("Couldn't resolve ip");
                    ipAddress = IPAddress.Parse(host.Trim());
                }
            }
            cons.writeToLog(String.Format("Create socket {0}:{1}", ipAddress.ToString(), port));
            sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            cons.writeToLog(String.Format("Connect socket {0}:{1}", ipAddress.ToString(), port));
            sock.Connect(ipAddress, port);
            if (sock.Connected)
            {
                cons.writeToLog("Socket connected");
                StateObject state = new StateObject();
                state.workSocket = sock;
                state.pHandler = this;
                sock.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
            }
        }

        /// <summary>
        /// Закрытие сокета
        /// </summary>
        public void Close()
        {
            sock.Shutdown(SocketShutdown.Both);
            sock.Close();
            Finished();
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
                            catch (JsonException jex)
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
                                                Close();
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
                                        if (mtype == "rpy" || mtype == "msg")
                                        {
                                            JsonElement data;
                                            jobj.TryGetProperty("data", out data);
                                            if (mtype == "rpy")
                                                RpyArrived(id, data);
                                            else
                                                MsgArrived(id, data);
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
                ProtocolHandler ph = state.pHandler;

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
    }

    public class QuikConsumer: IProtocolConsumer
    {
        private List<string> subs = new List<string>();
        private int MsgId { get; set; }


        public void EndArrived()
        {
            Console.WriteLine("end request received");
        }

        public void Finished()
        {
            Console.WriteLine("connection closed");
        }

        public void MsgArrived(int id, JsonElement data)
        {
            string method = data.GetProperty("method").GetString();
            Console.Write(method);
        }

        public void ParseError(byte[] trash)
        {
            Console.WriteLine("Can't parse:");
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(trash));
        }

        public void RpyArrived(int id, JsonElement data)
        {
            Console.WriteLine(String.Format("Rpy received: {0}", id));
        }

        public void VerArrived(int ver)
        {
            Console.WriteLine(String.Format("Version of protocol: {0}", ver));
        }

        public void writeToLog(string msg)
        {
            throw new NotImplementedException();
        }
    }


    class Program
    {
        static void Main(string[] args)
        {
        }
    }
}
