using System;
using System.Net;
using System.Threading;
using Pixie;

namespace Client
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            TestClient client = new TestClient();
            bool isExiting = false;
            while (true)
            {
                var input = Console.ReadLine();
                if (input == "exit")
                    return;
                client.client_.Send(input);
            }
        }
    }

    public class TestClient
    {
        public Pixie.PixieClient<string> client_;

        public TestClient()
        {
            client_ = new PixieClient<string>(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8000));
            client_.ConnectionStateChanged += (state) => Console.WriteLine(state);
            client_.MessageReceived += Console.WriteLine;
            Thread.Sleep(3000);
            client_.Connect();
        }
    }
    
}