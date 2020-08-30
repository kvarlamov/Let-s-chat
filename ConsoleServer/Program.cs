using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleServer
{
    public class StateObject
    {
        public ManualResetEvent receiveDone = new ManualResetEvent(false);
        public Socket workSocket = null;
        public string Name { get; set; }

        public const int BufferSize = 1024;

        //Buffer for receive data
        public byte[] buffer = new byte[BufferSize];

        public StringBuilder sb = new StringBuilder();
    }


    class ConsolServer
    {
        public static ManualResetEvent allDone = new ManualResetEvent(false);

        private static List<Socket> connectedClients = new List<Socket>();


        public ConsolServer()
        {

        }

        public void StartListening()
        {
            IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress iPAddress = IPAddress.Parse("192.168.0.102");//ipHostInfo.AddressList[0]; //192.168.0.102
            IPEndPoint iPEndPoint = new IPEndPoint(iPAddress, 11000);

            Socket listener = new Socket(iPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(iPEndPoint);
                listener.Listen(100);

                while (true)
                {
                    allDone.Reset();

                    //Start listen for new connections. Async
                    Console.WriteLine("Ждем новых...");
                    listener.BeginAccept(new AsyncCallback(AcceptCallBack), listener);

                    //wait while connection isn't made
                    allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                throw new Exception($"{e.Message}\n\n{e.InnerException}");
            }

            Console.WriteLine("Press ENTER to continue..");
            Console.ReadLine();
        }

        private static void AcceptCallBack(IAsyncResult ar)
        {
            allDone.Set();

            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);
            
            connectedClients.Add(handler);
            Console.WriteLine("новое подключение!...");

            //Get name of client in first message
            StateObject client = new StateObject();
            client.workSocket = handler;
            int bytesRead = handler.Receive(client.buffer, 0, StateObject.BufferSize, 0);
            client.Name = Encoding.UTF8.GetString(client.buffer, 0, bytesRead);
            SendBroadcastMessage($"{client.Name} зашёл в чат", handler);

            CheckForNewMessages(handler, client);
        }


        private static void CheckForNewMessages(Socket handler, StateObject client)
        {
            //Create client object
            //StateObject client = state;
            //client.workSocket = handler;

            while (true)
            {
                client.receiveDone.Reset();
                try
                {
                    handler.BeginReceive(client.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), client);
                    client.receiveDone.WaitOne();
                }
                catch (Exception e)
                {
                    break;
                }
                
                Thread.Sleep(5);
            }

            SendBroadcastMessage($"{client.Name} вышел из чата", handler);
            connectedClients.Remove(handler);
            handler.Shutdown(SocketShutdown.Both);
            handler.Close();
        }

        private static void ReadCallback(IAsyncResult ar)
        {
            try
            {
                string msg = string.Empty;
                StateObject state = (StateObject)ar.AsyncState;
                Socket handler = state.workSocket;

                int bytesRead = handler.EndReceive(ar);

                if (bytesRead >= StateObject.BufferSize)
                {
                    state.sb.Append(Encoding.UTF8.GetString(state.buffer, 0, bytesRead));
                    handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
                }
                else
                {
                    state.sb.Append(Encoding.UTF8.GetString(state.buffer, 0, bytesRead));
                    if (state.sb.Length > 1)
                    {
                        msg = state.sb.ToString();
                        Console.WriteLine(msg);
                        SendBroadcastMessage($"{state.Name}: " + msg, handler);
                        state.sb.Clear();
                        state.receiveDone.Set();
                    }
                }
            }
            catch (Exception)
            {
                ((StateObject)ar.AsyncState).receiveDone.Set();
            }
        }

        /// <summary>
        /// Message to all connected clients
        /// </summary>
        /// <param name="data"></param>
        private static void SendBroadcastMessage(string data, Socket sender)
        {
            byte[] byteData = Encoding.UTF8.GetBytes(data);

            //добавить отправителя сообщений, исключить его из общей рассылки

            foreach (var clinentSocket in connectedClients)
            {
                if (clinentSocket != sender)
                    clinentSocket.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallBack), clinentSocket);
            }
        }

        private static void SendCallBack(IAsyncResult ar)
        {
            try
            {
                Socket client = (Socket)ar.AsyncState;
                int bytesSent = client.EndSend(ar);

                Console.WriteLine("Sent {0} bytes to client.", bytesSent);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
    class Program
    {
        static void Main(string[] args)
        {
            var server = new ConsolServer();
            server.StartListening();
        }
    }
}
