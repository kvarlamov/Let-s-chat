using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    public class StateObject
    {
        public const int BufferSize = 1024;
        public byte[] buffer = new byte[BufferSize];

        public string Name { get; set; }
        public Socket WorkSocket { get; set; } = null;
        public ManualResetEvent ReceiveDone { get; set; } = new ManualResetEvent(false);
        public StringBuilder Sb { get; set; } = new StringBuilder();
    }
    public class ChatServer:INotifyPropertyChanged
    {
        private Task _task;
        private Socket _listener;
        private IPAddress _iPAddress;
        private int _port;
        private bool _isRunning;
        public static ManualResetEvent allDone = new ManualResetEvent(false); 

        #region Properties

        public ObservableCollection<Socket> ConnectedClients { get; set; }
        public ObservableCollection<StateObject> Clients { get; set; }
        public ObservableCollection<string> ChatList { get; set; }

        public int Port
        {
            get => _port;
            set
            {
                if (IsRunning)
                    throw new Exception("Can't change this property when server is running");
                _port = value;
                OnPropertyChanged(nameof(Port));
            }
        }
        public string IpAddress
        {
            get => _iPAddress.ToString();
            set
            {
                if (IsRunning)
                    throw new Exception("Can't change this property when server is running");
                _iPAddress = IPAddress.Parse(value);
                OnPropertyChanged(nameof(Port));
            }
        }

        public bool IsRunning 
        { 
            get => _isRunning;
            set 
            {
                _isRunning = value;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsStopped));
            } 
        }

        public bool IsStopped => !IsRunning;

        private IPEndPoint _iPEndPoint => new IPEndPoint(_iPAddress, _port);

        #endregion

        /// <summary>contructor</summary>
        public ChatServer()
        {
            ConnectedClients = new ObservableCollection<Socket>();
            ChatList = new ObservableCollection<string>();
            Clients = new ObservableCollection<StateObject>();

            IpAddress = "192.168.0.102";
            Port = 11000;
        }

        /// <summary>try start server.</summary>
        public void StartServer()
        {
            if (IsRunning) return;

            _listener = new Socket(_iPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                _listener.Bind(_iPEndPoint);
                _listener.Listen(100);
                IsRunning = true;
                _task = Task.Factory.StartNew(() => Run());
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + "\n\n" + ex.InnerException);
            }
        }

        /// <summary>processing</summary>
        public void Run()
        {
            StartServer();

            while (IsRunning)
            {
                allDone.Reset();
                try
                {
                    _listener.BeginAccept(new AsyncCallback(AcceptCallBack), _listener);
                }
                catch (Exception e)
                {
                    IsRunning = false;
                }

                //wait while connection isn't made
                allDone.WaitOne();
            }
        }

        private void AcceptCallBack(IAsyncResult ar)
        {
            allDone.Set();

            if (IsStopped)
                return;

            try
            {
                Socket listener = (Socket) ar.AsyncState;
                Socket handler = listener.EndAccept(ar);
                ConnectedClients.Add(handler);

                StateObject client = new StateObject();

                //получаем первым сообщением имя клиента
                client.WorkSocket = handler;
                int bytesRead = handler.Receive(client.buffer, 0, StateObject.BufferSize, 0);
                client.Name = Encoding.UTF8.GetString(client.buffer, 0, bytesRead);

                bool uniqName = Clients.Select(n => n.Name).Contains(client.Name);
                if (uniqName)
                {
                    byte[] byteData = Encoding.UTF8.GetBytes("NAMEISEXIST");
                    handler.Send(byteData, 0, byteData.Length, 0);
                    ConnectedClients.Remove(handler);
                    handler.Shutdown(SocketShutdown.Both);
                    handler.Close();
                    return;
                }

                App.Current.Dispatcher.Invoke((Action)delegate
                {
                    Clients.Add(client);
                });
                SendBroadcastMessage($"NEWCLIENTCONNECT{client.Name} зашёл в чат", handler);
                //отправляем новому клиенту список подключенных
                SendAllConnected(handler);

                CheckForNewMessages(handler, client);
            }
            catch
            {
                return;
            }
        }

        /// <summary>send all connected clients to new member in chat</summary>
        /// <param name="handler"></param>
        private void SendAllConnected(Socket handler)
        {
            StringBuilder connectedToString = new StringBuilder();
            foreach (var member in Clients)
            {
                connectedToString.Append(member.Name + "/*AM==-*/");
            }
            byte[] connectedClients = Encoding.UTF8.GetBytes(connectedToString.ToString());
            handler.Send(connectedClients, 0, connectedClients.Length, 0);
        }

        private void CheckForNewMessages(Socket handler, StateObject client)
        {
            while (true)
            {
                client.ReceiveDone.Reset();

                try
                {
                    handler.BeginReceive(client.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), client);
                    client.ReceiveDone.WaitOne();
                }
                catch
                {
                    DisconnectClient(handler, client);
                    break;
                }

                Thread.Sleep(5);
            }
            if (IsStopped)
            {
                return;
            }
        }

        private void DisconnectClient(Socket handler, StateObject client)
        {
            ConnectedClients.Remove(handler);
            App.Current.Dispatcher.Invoke((Action)delegate
            {
                Clients.Remove(client);
            });
            

            if (handler.Connected == false)
            {
                SendBroadcastMessage($"CLIENTDISCONNECT{client.Name} вышел из чата", handler);
                return;
            }
           
            handler.Shutdown(SocketShutdown.Both);
            handler.Close();
        }

        private void ReadCallback(IAsyncResult ar)
        {
            try
            {
                string msg = string.Empty;
                StateObject state = (StateObject)ar.AsyncState;
                Socket handler = state.WorkSocket;

                int bytesRead = handler.EndReceive(ar);

                if (bytesRead >= StateObject.BufferSize)
                {
                    state.Sb.Append(Encoding.UTF8.GetString(state.buffer, 0, bytesRead));
                    handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
                }
                else
                {
                    state.Sb.Append(Encoding.UTF8.GetString(state.buffer, 0, bytesRead));
                    if (state.Sb.Length > 1)
                    {
                        msg = state.Sb.ToString();
                        if (msg.StartsWith("LEAVECHAT"))
                        {
                            DisconnectClient(handler, state);
                        }
                        else
                        {
                            SendBroadcastMessage($"{state.Name}: " + msg, handler);
                        }
                        state.Sb.Clear();
                        state.ReceiveDone.Set();
                    }
                }
            }
            catch
            {
                ((StateObject)ar.AsyncState).ReceiveDone.Set();
            }
        }

        /// <summary>stop server</summary>
        public void StopServer()
        {
            if (IsStopped) return;

            SendBroadcastMessage("SERVERGOOFFLINE", _listener);
            for (int i = 0; i < ConnectedClients.Count; i++)
            {
                ConnectedClients[i].Shutdown(SocketShutdown.Both);
                ConnectedClients[i].Close();
            }

            ConnectedClients.Clear();
            Clients.Clear();
            _listener.Close();
            IsRunning = false;
        }

        public void SwitchServerState()
        {
            if (!IsRunning)
            {
                StartServer();
                return;
            }

            StopServer();
        }


        private void SendBroadcastMessage(string data, Socket sender)
        {
            //string log = string.Format("{0} >> {1} : {2}", from.UserName, to.UserName, msg);
            
            App.Current.Dispatcher.Invoke((Action)delegate
            {
                if (data.StartsWith("NEWCLIENTCONNECT") || data.StartsWith("CLIENTDISCONNECT"))
                {
                    ChatList.Add(data.Substring(16));
                }
                else
                    ChatList.Add(data);
            });

            byte[] byteData = Encoding.UTF8.GetBytes(data);

            foreach (var clinentSocket in ConnectedClients)
            {
                if (clinentSocket != sender)
                {
                    try
                    {
                        clinentSocket.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallBack), clinentSocket);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
        }

        private void SendCallBack(IAsyncResult ar)
        {
            try
            {
                Socket client = (Socket)ar.AsyncState;
                int bytesSent = client.EndSend(ar);
            }
            catch (Exception e)
            {
                ConnectedClients.Remove((Socket) ar.AsyncState);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propName) => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
