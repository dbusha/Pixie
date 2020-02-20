using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Pixie
{
    public interface IPixieServer<out TSession, in TMessage> where TSession : class
    {
        Task Start();
        void Stop();
        void AcceptSession(Guid id);
        void Send(Guid id, TMessage message);
        
        event Action ServerStopped;
        event Action<TSession> ConnectionReceived;
    }
    
    
    public class PixieServer<TSession,TMessage> : IPixieServer<TSession, TMessage>
        where TSession : class, IPixieSession
    {
        private static readonly Logger Logger = LogManager.GetLogger(nameof(PixieServer<TSession, TMessage>));
        private readonly IPAddress ipAddress_;
        private readonly int port_;
        private volatile bool isRunning_ = true;
        private Dictionary<Guid, TSession> sessions_ = new Dictionary<Guid,TSession>();
        private Dictionary<Guid, TSession> pendingSessions_ = new Dictionary<Guid,TSession>();
        private readonly ReaderWriterLockSlim sessionLock_ = new ReaderWriterLockSlim();
        private readonly ReaderWriterLockSlim pendingSessionLock_ = new ReaderWriterLockSlim();


        public PixieServer(IPAddress address, int port)
        {
            port_ = port;
            ipAddress_ = address;
        }


        public event Action ServerStopped;
        public event Action<TSession> ConnectionReceived;
        public event Action<Guid, TMessage> MessageReceived;
        

        public async Task Start()
        {
            var listener = new TcpListener(ipAddress_, port_);
            listener.Start();
            while (isRunning_)
            {
                var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                IPixieSession session = new PixieSession(client);
                session.MessageReceived += OnMessageReceived_;
                QueuePendingSession((TSession) session);
            }
        }


        private void OnMessageReceived_(IPixieSession session)
            => MessageReceived?.Invoke(session.SessionId, session.GetMessage<TMessage>());


        private void QueuePendingSession(TSession session)
        {
            CloseExistingSession(session);
            
            pendingSessionLock_.EnterWriteLock();
            if (isRunning_)
                pendingSessions_[session.SessionId] = session;

            pendingSessionLock_.ExitWriteLock();
            
            Task.Run(() => ConnectionReceived?.Invoke(session));
        }


        private void CloseExistingSession(TSession session)
        {
            CloseSession(session, sessionLock_, sessions_);
            CloseSession(session, pendingSessionLock_, pendingSessions_);
        }


        private void CloseSession(TSession session, ReaderWriterLockSlim rwLock, IDictionary<Guid, TSession> sessions)
        {
            TSession existingSession = null;
            rwLock.EnterUpgradeableReadLock();
            existingSession = sessions.Values.FirstOrDefault(s => s.RemoteAddress.Equals(session.RemoteAddress));
            if (existingSession != null)
            {
                rwLock.EnterWriteLock();
                sessions.Remove(existingSession.SessionId);
                rwLock.ExitWriteLock();
            }
            rwLock.ExitUpgradeableReadLock();
            
            if (existingSession == null)
                return;

            try
            {
                Logger.Debug($"Received a new session request from an existing IP:Port {session.RemoteAddress}");
                existingSession.Close();
            }
            catch (Exception err) { Logger.Error(err, $"Error while closing session for {session.RemoteAddress}"); }
        }
        
        
        public void Stop()
        {
            isRunning_ = false;
            CloseAllSessions(pendingSessionLock_, pendingSessions_);
            pendingSessions_ = null;

            CloseAllSessions(sessionLock_, sessions_);
            sessions_ = null;
            ServerStopped?.Invoke();
        }

        
        public void AcceptSession(Guid id)
        {
            pendingSessionLock_.EnterWriteLock();
            pendingSessions_.TryGetValue(id, out var session);
            pendingSessions_.Remove(id);
            pendingSessionLock_.ExitWriteLock();
            
            if (session == null)
                throw new NullReferenceException("session not found among pending sessions");
            
            sessionLock_.EnterWriteLock();
            sessions_[id] = session;
            sessionLock_.ExitWriteLock();
        }
        

        public void Send(Guid id, TMessage message)
        {
            sessionLock_.EnterReadLock();
            if (!sessions_.TryGetValue(id, out var session))
            {
                Logger.Error($"Failed to send message to session {id}");
                return;
            }
            sessionLock_.ExitReadLock();
            session.Send(message);
        }


        private void CloseAllSessions(ReaderWriterLockSlim rwLock, IDictionary<Guid, TSession> sessions)
        {
            rwLock.EnterWriteLock();
            try 
            {
                foreach (var session in sessions.Values)
                    session.Close();
                sessions.Clear();
            }
            catch (Exception err) { Logger.Error(err); }
            finally { rwLock.ExitWriteLock(); }
        }
    }
}