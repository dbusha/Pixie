using System;
using System.Net;
using Pixie;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            bool isExiting = false;
            
            TestServer server = new TestServer();
            
            while (!isExiting)
            {
                var input = Console.ReadLine();
                if (input == "exit")
                    isExiting = true;
                else
                {
                    server.Send(input);
                }
            }
        }
    }


    public class TestServer
    {
        private readonly PixieServer<PixieSession, string> server_;
        private Guid guid;
        
        public TestServer()
        {
            server_ = new PixieServer<PixieSession, string>(IPAddress.Parse("127.0.0.1"), 8000);
            server_.Start();
            server_.ConnectionReceived += OnSessionReceived_;
            server_.MessageReceived += OnMessageReceived_;
        }


        public void Send(string message)
        {
            server_.Send(guid, message);
        }


        private void OnSessionReceived_(PixieSession session)
        {
            guid = session.SessionId;
            server_.AcceptSession(session.SessionId);
        }


        private void OnMessageReceived_(Guid sessionId, string message)
        {
            Console.WriteLine(message);
            server_.Send(sessionId, $"Received: {message}");
        }
    }
}