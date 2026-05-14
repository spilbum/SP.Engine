using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SP.Engine.Server;

public sealed class SessionManager
{
    private readonly Session[] _sessions;
    private readonly Stack<int> _freeIndices;
    private readonly object _lock = new();
    private readonly int _maxCapacity;
    
    public SessionManager(int capacity)
    {
        _maxCapacity = capacity;
        _sessions = new Session[capacity];
        _freeIndices = new Stack<int>(capacity);

        for (var i = capacity - 1; i >= 0; i--)
        {
            _freeIndices.Push(i);
        }
    }

    public Session[] GetActiveSnapshot()
    {
        lock (_lock)
        {
            var activeCount = _maxCapacity - _freeIndices.Count;
            if (activeCount <= 0) return [];
            
            var snapshot = new Session[activeCount];
            var cursor = 0;
            for (var i = 0; i < _maxCapacity; i++)
            {
                if (_sessions[i] == null || _sessions[i].SessionId == 0)
                    continue;
                
                snapshot[cursor++] = _sessions[i];
                if (cursor >= activeCount) break;
            }
            return snapshot;
        }
    }

    public Session CreateSession(EngineCore engine, TcpNetworkSession tcpSession)
    {
        lock (_lock)
        {
            if (_freeIndices.Count == 0) return null;
            
            var index = _freeIndices.Pop();
            var salt = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
            
            var sessionId = ((long)salt << 32) | (uint)index;

            var session = new Session();

            try
            {
                session.Initialize(sessionId, engine, tcpSession);
            }
            catch (Exception ex)
            {
                engine.Logger.Error(ex);
                return null;
            }
      
            _sessions[index] = session;
            return session;
        }
    }

    public Session GetSession(long sessionId)
    {
        var index = (int)(sessionId & 0xFFFFFFFF);
        if (index < 0 || index >= _maxCapacity) return null;

        lock (_lock)
        {
            var session = _sessions[index];
            if (session != null && session.SessionId == sessionId)
                return session;
        }
        
        return null;
    }

    public void RemoveSession(long sessionId)
    {
        lock (_lock)
        {
            var index = (int)(sessionId & 0xFFFFFFFF);
            if (index < 0 || index >= _maxCapacity) return;
            
            var session = _sessions[index];
            if (session == null) return;

            session.Dispose();
            
            _sessions[index] = null;
            _freeIndices.Push(index);
        }
    }
}
