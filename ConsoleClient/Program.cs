using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleClient
{
    public class StateObject
    {
        public Socket workSocket = null;

        public const int BufferSize = 256;

        public byte[] buffer = new byte[BufferSize];

        public StringBuilder sb = new StringBuilder();
    }
    public class Client
    {
        private const int port = 11000;

        private static ManualResetEvent connectDone = new ManualResetEvent(false);
        private static ManualResetEvent sendDone = new ManualResetEvent(false);
        private static ManualResetEvent receiveDone = new ManualResetEvent(false);
        private readonly IPAddress iPAddress = IPAddress.Parse("192.168.0.102");

        private static string response = string.Empty;

        public void StartClient()
        {
            Console.WriteLine("Enter your name");
            string name = Console.ReadLine();
            
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(iPAddress, port);

                Socket server = new Socket(iPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                server.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), server);
                connectDone.WaitOne();

                //send first message to server - client name
                byte[] byteData = Encoding.UTF8.GetBytes(name);
                server.Send(byteData, 0, byteData.Length, 0);

                Task forReceive = Task.Factory.StartNew(() => Receive(server));
                Send(server);
                
                server.Shutdown(SocketShutdown.Both);
                server.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                Socket client = (Socket)ar.AsyncState;

                client.EndConnect(ar);

                Console.WriteLine("Socket connected to {0}", client.RemoteEndPoint.ToString());
                connectDone.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void Receive(Socket client)
        {
            try
            {
                StateObject state = new StateObject();
                state.workSocket = client;

                while (true)
                {
                    receiveDone.Reset();
                    //begin receiving data from server
                    client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
                    receiveDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                string msg = string.Empty;
                StateObject state = (StateObject)ar.AsyncState;
                Socket client = state.workSocket;

                int bytesRead = client.EndReceive(ar);

                if (bytesRead >= StateObject.BufferSize)
                {
                    state.sb.Append(Encoding.UTF8.GetString(state.buffer, 0, bytesRead));
                    client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
                }
                else
                {
                    state.sb.Append(Encoding.UTF8.GetString(state.buffer, 0, bytesRead));
                    if (state.sb.Length > 1)
                    {
                        response = state.sb.ToString();
                        Console.WriteLine(response);
                        state.sb.Clear();
                        receiveDone.Set();
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                receiveDone.Set();
            }
        }

        private void Send(Socket server)
        { 
            while (true)
            {
                Console.Write("> ");
                string data = Console.ReadLine();
                byte[] byteData = Encoding.UTF8.GetBytes(data);

                server.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), server);
                sendDone.WaitOne();
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                Socket server = (Socket)ar.AsyncState;

                int bytesSent = server.EndSend(ar);
                Console.WriteLine("Sent {0} bytes to server.", bytesSent);

                sendDone.Set();
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
            Client client = new Client();
            client.StartClient();
            Console.ReadKey();
        }
    }
}
