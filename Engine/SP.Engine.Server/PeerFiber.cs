using System;
using System.Collections.Generic;
using SP.Core.Fiber;
using SP.Core.Logging;

namespace SP.Engine.Server;

public class PeerFiber(ILogger logger, string name) : ThreadFiber(logger, name)
{
    private readonly List<Session> _sessions = [];

    public void AddSession(Session session)
    {
        Enqueue(() =>
        {
            _sessions.Add(session);
        });
    }

    public void RemoveSession(Session session)
    {
        Enqueue(() =>
        {
            var index = _sessions.IndexOf(session);
            if (index == -1) return;

            var lastIndex = _sessions.Count - 1;
            if (index != lastIndex)
            {
                _sessions[index] = _sessions[lastIndex];
            }

            _sessions.RemoveAt(lastIndex);
        });
    }

    public void Tick()
    {
        for (var i = _sessions.Count - 1; i >= 0; i--)
        {
            var session = _sessions[i];
            try
            {
                if (session.IsConnected && session.Peer != null)
                    session.Peer.Tick();
            }
            catch (Exception e)
            {
                Logger.Error($"Error tick session {session.Id}: {e.Message}");
            }
        }
    }
}
