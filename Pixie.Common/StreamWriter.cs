using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Pixie.Common
{
    public class StreamWriter
    {
        
        private readonly object outgoingLock_ = new object();
        private readonly Queue<byte> outgoingByteQueue_ = new Queue<byte>();
        
        
        public void QueueMessage(object message)
        {
            var stream = new MemoryStream();
            var writer = new BinaryFormatter();
            writer.Serialize(stream, message);
            stream.Seek(0, SeekOrigin.Begin);
            
            lock (outgoingLock_)
            {
                EnqueueHeader_(stream.Length);
                for (int i = 0; i < stream.Length; i++)
                    outgoingByteQueue_.Enqueue((byte) stream.ReadByte());
            } 
        }

        private void EnqueueHeader_(long length)
        {
            var bytes = BitConverter.GetBytes(length);
            for (int i = 0; i < StreamReader.HeaderLength; i++)
                outgoingByteQueue_.Enqueue(bytes[i]);
        }


        public (byte[] buffer, int length) GetByteArray()
        {
            int queueLength;
            byte[] buffer;
            lock (outgoingLock_)
            {
                queueLength = outgoingByteQueue_.Count;
                if (queueLength == 0)
                    return (null, 0);

                buffer = ArrayPool<byte>.Shared.Rent(queueLength);
                outgoingByteQueue_.CopyTo(buffer, 0);
                outgoingByteQueue_.Clear();
                outgoingByteQueue_.TrimExcess();
            }

            return (buffer, queueLength);
        }
    }
}