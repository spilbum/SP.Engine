using Common.Protocol.EC2ES;
using SP.Engine.Client;

namespace EchoClient;

public class UserPeer : NetPeerBase
{
    private CancellationTokenSource? _cts;
    private int _pendingCount;
    private int _batchCount;
    private string? _sendType;
    
    public bool IsRunning { get; private set; }

    public UserPeer()
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

    public void StartTest(string sendType, int period, int batchCount)
    {
        if (IsRunning) return;
        
        IsRunning = true;
        _sendType = sendType;
        _batchCount = batchCount;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => TestScheduler(period, _cts.Token));
    }

    public void StopTest()
    {
        if (!IsRunning) return;
        IsRunning = false;
        
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        
        _pendingCount = 0;
        _sendType = null;
        _batchCount = 0;
    }

    private async Task TestScheduler(int period, CancellationToken ct)
    {
        Logger.Debug("[TestScheduler Started] Type: {0}, Period: {1}ms, Batch: {2}", _sendType, period, _batchCount);
        
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

    public void ProcessSend()
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
                case "heavy":
                    SendHeavy();
                    break;
            }
        }
    }

    private void SendHeavy()
    {
        var delayMs = new Random().Next(10, 20);
        Send(new HeavyLoadNotify { DelayMs = delayMs });
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
