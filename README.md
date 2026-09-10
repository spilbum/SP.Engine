# SP.Engine

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

SP.Engine은 .NET 8 인프라를 기반으로 구축된 고성능 실시간 멀티플레이어 게임 서버 엔진입니다.
본 프로젝트는 13년 간의 대규모 글로벌 게임 서버 설계 및 라이브 서비스 경험을 바탕으로, **실시간 분산 환경에서 발생하는 로우레벨 최적화 이슈와 동시성 제어 기술을 집대성한 '고성능 게임 서버 레퍼런스 아키텍처'**입니다. 
대규모 동시 접속 환경에서 서버의 가용성과 처리량(Throughput)을 제한하는 두 가지 최대 요인인 **GC(Garbage Collection) 스파이크**와 **스레드 간 락 경합(Lock Contention)**을 프레임워크 런타임 레벨에서 원천적으로 차단하는 것을 최우선 사상으로 설계되었습니다.

---

## 🚀 Key Features

SP.Engine은 복잡한 로우레벨 제어(스레드 동기화, GC 메모리 관리, 네트워크 예외 처리)를 프레임워크 런타임으로 철저히 은닉했습니다. 콘텐츠 개발자는 인프라 고민 없이 오직 게임 비즈니스 로직 구현에만 집중할 수 있습니다.

* **동기화 비용이 없는(Lock-Free) 비즈니스 로직 런타임**
  * 다수의 클라이언트 요청을 코어 수에 맞춘 **Fiber 기반의 가상 스레드 큐**에 안전하게 샤딩합니다.
  * 콘텐츠 개발자는 멀티스레드 환경의 고질적인 문제인 데드락(Deadlock)이나 `lock` 경합에 대한 피로감 없이, 방(Room) 단위 로직이나 전투 시스템을 단일 스레드처럼 직관적이고 안전하게 작성할 수 있습니다.
    
* **상태 유실 없는 무중단 세션 복구 (Seamless Resumption)**
  * Wi-Fi와 LTE/5G 전환이 잦은 모바일 네트워크 환경에 특화된 방어 로직을 제공합니다.
  * 물리적인 소켓 단선이 발생해도 논리적인 유저 세션과 송신 윈도우(Sliding Window)를 즉시 파괴하지 않고 동결합니다. 재접속 시 유실된 패킷 구간만 정밀하게 재전송하여 플레이 경험의 단절을 막습니다.
    
* **목적형 하이브리드 네트워크 스택 (TCP / UDP)**
  * 데이터의 절대적 무결성이 필요한 로직(인증, 결제 등)은 TCP 채널로, 약간의 유실을 감수하더라도 실시간 레이턴시가 최우선인 로직(이동 동기화, 액션 등)은 UDP 채널로 분리하여 전송합니다.
  * 자체적인 패킷 파편화 조립(Fragment Assembly) 메커니즘을 내장하여 가변 MTU 환경에서도 대용량 UDP 데이터를 안전하게 처리합니다.
    
* **분산 아키텍처 확장을 위한 S2S (Server-to-Server) 커넥터**
  * 단일 서버의 한계를 넘어 전역 매치메이킹, 랭킹, 채팅 등 다양한 마이크로서비스와 유기적으로 연동할 수 있는 통신 인프라를 제공합니다.
  * 자체 암호화, 가변 압축, Zero-Allocation 직렬화 등 클라이언트와 동일한 고성능 스택을 서버 간 통신에도 대칭 적용하여 대용량 상태 동기화와 유연한 스케일아웃(Scale-out)을 지원합니다.
    
* **동적 IL(Emit) 기반의 Zero-Reflection 직렬화**
  * 성능 저하와 GC 스파이크의 주범인 C# 기본 리플렉션과 박싱(Boxing) 연산을 완벽히 배제했습니다.
  * 프레임워크 초기화 시점에 컴파일되는 동적 IL(Intermediate Language)과 정적 메타데이터를 활용하여, 개발자가 패킷 구조체만 정의하면 즉시 런타임 오버헤드 없는 초고속 직렬화 통신이 가능합니다.
    
---

## 🚀 Getting Started

### Prerequisites
* .NET 8.0 SDK 이상

### Installation & Run Sample
엔진의 정상 동작 여부와 패킷 파이프라인을 검증하기 위해 제공되는 에코 서버 및 클라이언트 샘플 구동 방법입니다.

```bash
# 저장소 클론
git clone [https://github.com/spilbum/SP.Engine.git](https://github.com/spilbum/SP.Engine.git)
cd SP.Engine

# 1. 에코 서버 실행 (터미널 1)
dotnet run --project Samples/EchoServer 10000

# 2. 에코 클라이언트 실행 (터미널 2)
dotnet run --project Samples/EchoClient 127.0.0.1 10000

```

Basic Usage Example
1. Common (Protocol Definition)
클라이언트와 서버가 공유하는 패킷 규격입니다.

```csharp
// 프로토콜 ID 정의
public static class C2S { public const ushort EchoReq = 1000; }
public static class S2C { public const ushort EchoAck = 2000; }

// 요청 패킷
[ProtocolData(C2S.EchoReq)]
public class C2S_EchoReq : ProtocolDataBase<C2S_EchoReq>
{
    public long SentTicks;
}

// 응답 패킷
[ProtocolData(S2C.EchoAck)]
public class S2C_EchoAck : ProtocolDataBase<S2C_EchoAck>
{
    public long SentTicks;
}
```

2. Server Side
엔진을 상속받아 서버를 구성하고 비즈니스 로직(Command)을 연결합니다.

```csharp
// 1. 유저 세션(Peer) 객체 정의
public class UserPeer : PeerBase
{
    public UserPeer(Session session) : base(session) { }
}

// 2. 메인 서버 엔진 구성
public class EchoServer : EngineBase
{
    protected override IPeer CreatePeer(Session session) => new UserPeer(session);

    protected override IConnector CreateConnector(string name) 
        => throw new NotImplementedException("본 예제에서는 S2S 커넥터를 생략합니다.");
}

// 3. 패킷 수신 커맨드 (비즈니스 로직)
[ProtocolCommand(C2S.EchoReq)]
public class EchoReqCommand : CommandBase<UserPeer, C2S_EchoReq>
{
    protected override void ExecuteCommand(UserPeer context, C2S_EchoReq protocol)
    {
        // GC 스파이크 방지를 위해 객체 풀에서 응답 프로토콜을 대여 (Zero-Allocation 지향)
        using var scope = ProtocolScope<S2C_EchoAck>.Rent();
        scope.Protocol.SentTicks = protocol.SentTicks;
        
        context.Send(scope.Protocol);
    }
}

// 4. 서버 빌드 및 구동
var builder = EngineBuilder<EchoServer>.Create()
    .SetName(nameof(EchoServer))
    .Listen(10000, mode: SocketMode.Tcp)
    .Listen(20000, mode: SocketMode.Udp); // TCP/UDP 하이브리드 리슨

var server = builder.Build();
server.Start();

```

3. Client Side
서버와 대칭되는 구조로 클라이언트를 구현하여 에코 응답 레이턴시를 측정합니다.

```csharp
// 1. 클라이언트 피어 정의
public class EchoClient : NetPeerBase
{
    public void SendEcho()
    {
        Send(new C2S_EchoReq { SentTicks = DateTime.UtcNow.Ticks });
    }
}

// 2. 서버 응답 처리 커맨드
[ProtocolCommand(S2C.EchoAck)]
public class EchoAckCommand : CommandBase<EchoClient, S2C_EchoAck>
{
    protected override void ExecuteCommand(EchoClient context, S2C_EchoAck protocol)
    {
        // 왕복 레이턴시(RTT) 계산
        var rtt = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - protocol.SentTicks);
        Console.WriteLine($"[Client] Echo 응답 수신 완료. RTT: {rtt.TotalMilliseconds:F2} ms");
    }
}

// 3. 클라이언트 구동 및 연결 테스트
var logger = new ConsoleLogger("EchoClient");
var client = NetPeerBuilder.Create()
    .WithLogger(logger)
    .WithAutoPing(true, intervalSeconds: 2) // 자동 핑/세션 유지 로직 내장
    .Build<EchoClient>();

client.Connect("127.0.0.1", 10000);
client.SendEcho();
```

## 📄 라이선스 (License)
이 프로젝트는 MIT 라이선스 하에 배포됩니다.

### Contact GitHub: @spilbum
