using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Pixie.Common
{
    public enum ConnectionState {Unknown, PendingConnect, Connected, PendingDisconnect, Disconnected}
    public interface IPixieSession
    {
        Guid SessionId { get; }
        IPEndPoint RemoteEndPoint { get; }
        string RemoteAddress { get; }
        ConnectionState ConnectionState { get; }
        
        event Action<ConnectionState> ConnectionStateChanged;
        event Action<IPixieSession> MessageReceived;
        
        T GetMessage<T>();
        void Send(object message);
        void Open();
        void Close();
    }
    
    
    public class PixieSession : IPixieSession
    {
        private readonly TcpClient client_;
        private NetworkStream networkStream_;
        private bool isWriting_;
        private readonly object writeLock_ = new object();
        private readonly Logger logger_ = LogManager.GetLogger(nameof(PixieSession));
        private ConnectionState connectionState_;
        private readonly StreamReader streamReader_ = new StreamReader();
        private readonly StreamWriter streamWriter_ = new StreamWriter();
        private readonly AutoResetEvent resetEvent_ = new AutoResetEvent(false);
        

        public PixieSession(TcpClient client)
        {
            client_ = client;
            RemoteEndPoint = (IPEndPoint)client_.Client.RemoteEndPoint;
            networkStream_ = client_.GetStream();
            ConnectionState = client_.Connected ? ConnectionState.Connected : ConnectionState.Disconnected;
            Task.Run(new Action(async () => await ListenForMessagesAsync_()));
            Task.Run(new Action(async () => await WriteOutgoing_()));
        }
        

        public PixieSession(IPEndPoint endPoint)
        {
            RemoteEndPoint = endPoint;
            ConnectionState = ConnectionState.Disconnected;
            client_ = new TcpClient();
            Task.Run(new Action(async () => await ListenForMessagesAsync_()));
            Task.Run(new Action(async () => await WriteOutgoing_()));
        }

        public event Action<ConnectionState> ConnectionStateChanged;
        public event Action<IPixieSession> MessageReceived;
        
        public IPEndPoint RemoteEndPoint { get; }
        public string RemoteAddress => $"{RemoteEndPoint}:{RemoteEndPoint.Port}";
        public Guid SessionId { get; } = Guid.NewGuid();

        
        public ConnectionState ConnectionState
        {
            get => connectionState_;
            private set 
            {
                connectionState_ = value;
                Task.Run(() => ConnectionStateChanged?.Invoke(value));
            }
        }


        public void Send(object message)
        {
            if (ConnectionState != ConnectionState.Connected)
                throw new InvalidOperationException("Connection isn't open");
            streamWriter_.QueueMessage(message);
            resetEvent_.Set();
        }


        public void Open()
        {
            if (!client_.Connected)
            {
                ConnectionState = ConnectionState.PendingConnect;
                client_.Connect(RemoteEndPoint);
                networkStream_ = client_.GetStream();
            }
            ConnectionState = ConnectionState.Connected;
        }
        
        
        public void Close()
        {
            ConnectionState = ConnectionState.PendingDisconnect;
            client_.Close();
            ConnectionState = ConnectionState.Disconnected;
        }


        public T GetMessage<T>() => (T)streamReader_.DeserializeMessage();
        
        
        private async Task WriteOutgoing_()
        {
            while (!ConnectionState.IsExiting())
            {
                resetEvent_.WaitOne();
                lock (writeLock_)
                {
                    if (connectionState_.IsExiting() || isWriting_)
                        continue;
                    isWriting_ = true;
                }

                var (buffer, length) = streamWriter_.GetByteArray();
                if (buffer == null)
                {
                    isWriting_ = false;
                    continue;
                }
                try
                {
                    await networkStream_.WriteAsync(buffer, 0, length).ConfigureAwait(false);
                    isWriting_ = false;
                    resetEvent_.Set();
                }
                catch (Exception err)
                {
                    logger_.Error(err, "Failed to start write");
                    Close();
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
        }
        

        private async Task ListenForMessagesAsync_()
        {
            while (!ConnectionState.IsExiting())
            {
                const int OneKb = 1024;
                var buffer = ArrayPool<byte>.Shared.Rent(OneKb);
                try
                {
                    var bytesRead = await networkStream_.ReadAsync(buffer, 0, OneKb).ConfigureAwait(false);

                    if (bytesRead > 0)
                        streamReader_.Append(bytesRead, buffer);

                    if (streamReader_.HasMessage)
                        Task.Run(() => MessageReceived?.Invoke(this));
                }
                catch (Exception err)
                {
                    logger_.Error(err);
                    Close();
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
        }
    }
}