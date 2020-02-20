using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using NLog;

namespace Pixie.Common
{
    internal class StreamReader
    {
        private readonly Logger logger_ = LogManager.GetLogger(nameof(StreamReader));
        private readonly Queue<byte> byteQueue_ = new Queue<byte>();
        private int? messageSize_;
        private readonly BinaryFormatter binaryFormatter_ = new BinaryFormatter();
        private readonly object queueLock_ = new object();
            

        public bool HasMessage { get; private set; }
        public const int HeaderLength = 4;
            

        public void Append(int bytesRead, byte[] buffer)
        {
            lock (queueLock_)
            {
                for (int i = 0; i < bytesRead; i++) 
                    byteQueue_.Enqueue(buffer[i]);
                
                if (messageSize_ == null && byteQueue_.Count > HeaderLength)
                    messageSize_ = ReadMessageSize_();
                HasMessage = byteQueue_.Count >= messageSize_;
            }
        }
            

        public object DeserializeMessage()
        {
            if (messageSize_ == null)
            {
                logger_.Debug("Can't extract message, Unknown messages size");
                return null;
            }

            var buffer = new byte[messageSize_.Value];
            lock (queueLock_)
            {
                for (int i = 0; i < messageSize_.Value; i++)
                    buffer[i] = byteQueue_.Dequeue();
                messageSize_ = byteQueue_.Count < HeaderLength ? (int?)null : ReadMessageSize_();
                HasMessage = false;
            }

            return binaryFormatter_.Deserialize(new MemoryStream(buffer));
        }
            
            
        private int ReadMessageSize_()
        {
            var lengthBuffer = new byte[HeaderLength];
            lock (queueLock_)
            {
                for (int i = 0; i < HeaderLength; i++)
                    lengthBuffer[i] = byteQueue_.Dequeue();
            }

            var size = BitConverter.ToInt32(lengthBuffer);
            return size;
        }
    }
}