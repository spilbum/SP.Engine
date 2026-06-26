using Common.Protocol.EC2ES;
using SP.Engine.Client;

namespace EchoClient;

public class EchoClient : NetPeerBase
{
    private CancellationTokenSource? _cts;
    private int _pendingCount;
    private int _batchCount;
    private string? _sendType;
    
    public bool IsEchoing { get; private set; }

    public EchoClient()
    {
        Connected += OnConnected;
        Disconnected += OnDisconnected;
        Offline += OnOffline;
        StateChanged += OnStateChanged;
        Error += OnError;
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        Logger.Error("[OnError] An error occurred: {0}\nStacktrace: {1}", ex.Message, ex.StackTrace);
    }

    private void OnStateChanged(object? sender, StateChangedEventArgs e)
    {
        Logger.Debug("[OnStateChanged] {0} -> {1}", e.OldState, e.NewState);
    }

    private void OnOffline(object? sender, EventArgs e)
    {
        Logger.Debug("[OnOffline] PeerId: {0}", PeerId);
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Logger.Debug("[OnDisconnected] PeerId: {0}", PeerId);
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        Logger.Debug("[OnConnected] PeerId: {0}", PeerId);
    }
    
    public void StartEcho(string type, int period, int batchCount)
    {
        if (IsEchoing) return;
        
        IsEchoing = true;
        _sendType = type;
        _batchCount = batchCount;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => EchoScheduler(period, _cts.Token));
    }

    public void StopEcho()
    {
        if (!IsEchoing) return;
        IsEchoing = false;
        
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        _pendingCount = 0;
    }

    private async Task EchoScheduler(int period, CancellationToken ct)
    {
        Logger.Debug("[EchoScheduler Started] Type: {0}, Period: {1}ms", _sendType, period);
        
        while (!ct.IsCancellationRequested)
        {
            // 원자적으로 발송해야 할 개수만 증가시킵니다. (Send를 직접 호출하지 않음)
            Interlocked.Add(ref _pendingCount, _batchCount);
            
            if (period > 0) 
                await Task.Delay(period, ct);
            else
            {
                await Task.Yield();
            }
        }
    }

    public void ProcessPendingEcho()
    {
        // 원자적으로 현재 쌓인 발송 요청 개수를 땡겨옵니다.
        var count = Interlocked.Exchange(ref _pendingCount, 0);
        if (count <= 0) return;

        for (var i = 0; i < count; i++)
        {
            switch (_sendType)
            {
                case "tcp":
                    SendTcp(128);
                    break;
                case "udp":
                    SendUdp(5000);
                    break;
                case "both":
                    SendTcp(128);
                    SendUdp(128);
                    break;
            }
        }
    }

    private void SendTcp(int bytes)
    {
        Send(new TcpEchoReq
        {
            SentTicks = DateTime.UtcNow.Ticks,
            Data = GetRandomBytes(bytes)
        });
    }

    private void SendUdp(int bytes)
    {
        Send(new UdpEchoReq
        {
            SentTicks = DateTime.UtcNow.Ticks,
            Data = GetRandomBytes(bytes)
        });
    }
    
    private static byte[] GetRandomBytes(int count)
    {
        var bytes = new byte[count];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 256);
        }
        return bytes;
    }
}
