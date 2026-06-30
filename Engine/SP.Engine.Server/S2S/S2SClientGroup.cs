using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SP.Engine.Server.S2S;

public sealed class S2SClientGroup(string name) : IDisposable
{
    private readonly List<S2SClient> _clients = [];
    private volatile S2SClient[] _activeClients = [];
    private int _rrCursor;
    
    public string Name { get; } = name;

    public void AddClient(S2SClient client)
    {
        lock (_clients)
        {
            _clients.Add(client);

            client.Connected += _ => OnClientStateChanged();
            client.Disconnected += _ => OnClientStateChanged();
        }
    }
    
    private void OnClientStateChanged()
    {
        lock (_clients)
        {
            _activeClients = _clients.Where(client => client.IsConnected).ToArray();
        }
    }

    public void Start()
    {
        foreach (var client in _clients) client.Connect();
    }

    public IS2SClient GetAvailableClient()
    {
        var clients = _activeClients;
        if (clients.Length == 0) return null;
        
        var index = (uint)Interlocked.Increment(ref _rrCursor) % clients.Length;
        return clients[index];
    }

    public IEnumerable<IS2SClient> GetAllAvailableClients() => _activeClients;

    public void Dispose()
    {
        foreach (var client in _clients) client.Dispose();
    }
}
