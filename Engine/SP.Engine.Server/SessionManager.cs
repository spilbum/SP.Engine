using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;

namespace SP.Engine.Server;

public sealed class SessionManager
{
    private readonly Session[] _sessions;
    private readonly Stack<int> _freeIndices;
    private readonly object _lock = new();
    private readonly int _maxCapacity;
    private volatile Session[] _activeSnapshot = [];
    
    public SessionManager(int capacity)
    {
        _maxCapacity = capacity;
        _sessions = new Session[capacity];
        _freeIndices = new Stack<int>(capacity);

        for (var index = capacity - 1; index >= 0; index--)
        {
            _freeIndices.Push(index);
        }
    }

    public Session[] GetActiveSnapshot()
    {
        return _activeSnapshot;
    }

    public Session CreateSession(EngineCore engine, TcpNetworkSession ns, ReadWriteBuffer readWriteBuffer)
    {
        lock (_lock)
        {
            if (_freeIndices.Count == 0) return null;
            
            var index = _freeIndices.Pop();
            var salt = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
            var sessionId = ((long)salt << 32) | (uint)index;

            var session = new Session(sessionId);

            try
            {
                session.Initialize(engine, ns, readWriteBuffer);
            }
            catch (Exception ex)
            {
                engine.Logger.Error(ex);
                _freeIndices.Push(index);
                return null;
            }
      
            Volatile.Write(ref _sessions[index], session);
            UpdateSnapshot();
            return session;
        }
    }

    public Session GetSession(long sessionId)
    {
        var index = (int)(sessionId & 0xFFFFFFFF);
        if (index < 0 || index >= _maxCapacity) return null;

        var session = Volatile.Read(ref _sessions[index]);
        
        if (session != null && session.SessionId == sessionId)
            return session;
        
        return null;
    }

    public void RemoveSession(long sessionId)
    {
        lock (_lock)
        {
            var index = (int)(sessionId & 0xFFFFFFFF);
            if (index < 0 || index >= _maxCapacity) return;
            
            var session = _sessions[index];
            if (session == null || session.SessionId != sessionId) return;
            
            Volatile.Write(ref _sessions[index], null);
            
            session.Dispose();
            _freeIndices.Push(index);
            
            UpdateSnapshot();
        }
    }

    private void UpdateSnapshot()
    {
        var activeCount = _maxCapacity - _freeIndices.Count;
        if (activeCount <= 0)
        {
            _activeSnapshot = [];
            return;
        }
        
        var newSnapshot = new Session[activeCount];
        var cursor = 0;
        for (var i = 0; i < _maxCapacity; i++)
        {
            if (_sessions[i] == null || _sessions[i].SessionId == 0)
                continue;
            
            newSnapshot[cursor++] = _sessions[i];
            if (cursor >= activeCount) break;
        }
        
        _activeSnapshot = newSnapshot;
    }
}
