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

namespace Client
{
    public class Member
    {
        public string Name { get; set; }
    }

    public class StateObject
    {
        public Socket workSocket = null;

        public const int BufferSize = 256;

        public byte[] buffer = new byte[BufferSize];

        public StringBuilder sb = new StringBuilder();
    }
    public class ChatClient:INotifyPropertyChanged
    {
        private static ManualResetEvent connectDone = new ManualResetEvent(false);
        private static ManualResetEvent sendDone = new ManualResetEvent(false);
        private static ManualResetEvent receiveDone = new ManualResetEvent(false);
        private bool _isRunning;
        private bool _notEmptyFields = false;
        

        private string _name;
        private string _message;

        private Socket _server;
        private Socket _client;
        private IPAddress _iPAddress;
        private int _port;

        public string Name 
        {
            get => _name;
            set 
            {
                _name = value;
                CheckFields();
                OnPropertyChanged(nameof(Name));
            } 
        }

        /// <summary>IP, PORT and NAME cant be empty</summary>
        public bool NotEmptyFields
        {
            get => _notEmptyFields;
            set
            {
                _notEmptyFields = value;
                OnPropertyChanged(nameof(NotEmptyFields));
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

        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                OnPropertyChanged(nameof(Message));
            }
        }


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

        public bool IsStopped => !IsRunning;

        public ObservableCollection<string> ChatList { get; set; }
        public ObservableCollection<Member> Clients { get; set; }

        private IPEndPoint _remoteEP => new IPEndPoint(_iPAddress, _port);

        public ChatClient()
        {
            ChatList = new ObservableCollection<string>();
            Clients = new ObservableCollection<Member>();

            IpAddress = "192.168.0.102";
            Port = 11000;
        }

        public void SwitchClientState()
        {
            if (!IsRunning)
            {
                StartClient();
                return;
            }

            Disconnect();
        }


        public void StartClient()
        {
            if (IsRunning)
                return;

            CheckFields();

            //Если не заполнены IP, POrt или Имя
            if (!NotEmptyFields)
                return;

            ChatList.Clear();
            Clients.Clear();

            try
            {
                connectDone.Reset();
                _server = new Socket(_iPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _server.BeginConnect(_remoteEP, new AsyncCallback(ConnectCallback), _server);
                connectDone.WaitOne();

                //отсылаем серверу имя
                byte[] byteData = Encoding.UTF8.GetBytes(Name);                
                _server.Send(byteData, 0, byteData.Length, 0);

                Task forReceive = Task.Factory.StartNew(() => StartReceive(_server));
            }
            catch
            {
                IsRunning = false;
                connectDone.Set();
                App.Current.Dispatcher.Invoke((Action)delegate
                {
                    ChatList.Add("Не удается подключиться к серверу. Проверьте параметры подключения");
                });
                //Disconnect();
            }
        }


        /// <summary>получаем всех подключенных к чату</summary>
        private void GetConnectedToChat()
        {
            byte[] connectedMembers = new byte[1024];
            int bytesRead = _server.Receive(connectedMembers, 0, connectedMembers.Length, 0);

            if (bytesRead < 1)
            {
                return;
            }

            string connectedMembersString = Encoding.UTF8.GetString(connectedMembers, 0, bytesRead);

            if (connectedMembersString == "NAMEISEXIST")
            {
                App.Current.Dispatcher.Invoke((Action)delegate
                {
                    ChatList.Add("Ваше имя не уникально, пожалуйста поменяйте");
                });
                Disconnect();
                return;
            }

            var members = connectedMembersString.Split(new[] { "/*AM==-*/"}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var member in members)
            {
                if(!string.IsNullOrWhiteSpace(member))
                    App.Current.Dispatcher.Invoke((Action)delegate
                    {
                        Clients.Add(new Member { Name = member });
                    });
            }
        }

        private void CheckFields()
        {
            if ((Name == null) || (IpAddress == null) || (Port == 0))
            {
                NotEmptyFields = false;
                return;
            }

            NotEmptyFields = true;
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                _client = (Socket)ar.AsyncState;

                _client.EndConnect(ar);
                IsRunning = true;
                
                connectDone.Set();
            }
            catch
            {
                connectDone.Set();
            }
        }

        private void StartReceive(Socket server)
        {
            try
            {
                StateObject state = new StateObject();
                state.workSocket = server;
                
                GetConnectedToChat();

                while (IsRunning)
                {
                    receiveDone.Reset();
                    server.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
                    receiveDone.WaitOne();
                }
            }
            catch
            {
                IsRunning = false;
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
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
                    handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
                }
                else
                {
                    state.sb.Append(Encoding.UTF8.GetString(state.buffer, 0, bytesRead));
                    if (state.sb.Length > 1)
                    {
                        msg = state.sb.ToString();

                        //if new connected
                        if (msg.StartsWith("NEWCLIENTCONNECT"))
                        {
                            string name = msg.Substring(0, msg.Length - 12);
                            name = name.Substring(16);

                            App.Current.Dispatcher.Invoke((Action)delegate
                            {
                                ChatList.Add(msg.Substring(16));
                                Clients.Add(new Member { Name = name });
                            });
                        }
                        //someone quit
                        else if (msg.StartsWith("CLIENTDISCONNECT"))
                        {
                            string name = msg.Substring(0, msg.Length - 14);
                            name = name.Substring(16);

                            var member = Clients.Where(cl => cl.Name == name).FirstOrDefault();

                            App.Current.Dispatcher.Invoke((Action)delegate
                            {
                                ChatList.Add(msg.Substring(16));
                                Clients.Remove(member);
                            });
                        }
                        else if (msg == "SERVERGOOFFLINE")
                        {
                            App.Current.Dispatcher.Invoke((Action) delegate
                            {
                                ChatList.Add("СЕРВЕР ПРЕРВАЛ СОЕДИНЕНИЯ. Вы были отключены");
                            });
                            Disconnect();
                        }
                        else
                        {
                            App.Current.Dispatcher.Invoke((Action)delegate
                            {
                                ChatList.Add(msg);
                            });
                        }
                        
                        state.sb.Clear();
                        receiveDone.Set();
                    }
                }
            }
            catch
            {
                receiveDone.Set();
            }
        }

        public void StartSend()
        {
            if (string.IsNullOrWhiteSpace(Message))
            {
                return;
            }

            ChatList.Add(Message);

            byte[] byteData = Encoding.UTF8.GetBytes(Message);
            _server.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), _server);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                Socket server = (Socket)ar.AsyncState;

                int bytesSent = server.EndSend(ar);
                sendDone.Set();
                Message = "";
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
        }

        private void Disconnect()
        {
            byte[] byteData = Encoding.UTF8.GetBytes("LEAVECHAT" + Name);
            try
            {
                _server.Send(byteData, 0, byteData.Length, 0);
                _server.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                //ADD LOGGING
            }
            finally
            {
                Clients.Clear();
                IsRunning = false;
            }
        }

        private void OnPropertyChanged(string propName) => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
