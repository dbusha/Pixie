using System;
using System.Net;
using System.Net.Sockets;
using NLog;

namespace Pixie
{

    public interface IPixieClient<TMessage>
    {
        void Connect();
        void Disconnect();
        void Send(TMessage message);

        event Action<TMessage> MessageReceived;
        event Action<ConnectionState> ConnectionStateChanged;
    }


    public class PixieClient<TMessage> : IPixieClient<TMessage>
    {
        private readonly IPEndPoint endPoint_;
        private IPixieSession session_;
        private static readonly Logger logger_ = LogManager.GetLogger(nameof(PixieClient<TMessage>));


        public PixieClient(IPEndPoint endPoint)
        {
            endPoint_ = endPoint;
        }


        public event Action<TMessage> MessageReceived;
        public event Action<ConnectionState> ConnectionStateChanged;


        public void Connect()
        {
            session_ = new PixieSession(endPoint_);
            session_.ConnectionStateChanged += (state) => ConnectionStateChanged?.Invoke(state);
            session_.MessageReceived += (message) => MessageReceived?.Invoke(session_.GetMessage<TMessage>());
            session_.Open();
        }

        
        public void Disconnect()
        {
            try { session_.Close(); }
            catch (Exception err) { logger_.Error(err, "Error while closing session"); }
        }

        
        public void Send(TMessage message) => session_.Send(message);
    }
}