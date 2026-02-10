# SP.Engine

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

**SP.Engine**은 **실시간 멀티플레이어 게임**을 위해 설계된 고성능 TCP/UDP 서버 엔진입니다.

개인 프로젝트로 시작하여 실무 수준의 요구사항을 반영해 발전시킨 이 엔진은, 
**안정적인 비동기 메시지 처리**, **모바일 환경을 고려한 세션 복원**, 그리고 **강력한 보안 통신** 기능을 제공합니다. 
복잡한 네트워크 로직을 추상화하여, 개발자가 게임 로직에만 집중할 수 있는 유연하고 유지보수하기 쉬운 아키텍처를 지향합니다.

## ✨ 주요 기능 (Key Features)

### ⚡ High Performance TCP
* **High Performance**: .NET 8의 **Non-Blocking I/O 모델**을 기반으로, 최소한의 리소스로 수천 개의 동시 연결을 처리합니다.
* **Reliability**: 어플리케이션 레벨의 **시퀀스 추적(Sequence Tracking)**을 통해 논리적 패킷 순서를 보장하고 중복 처리 방지 및 패킷 누락을 감지합니다.

### 🔄 Session Restoration
* **Seamless Reconnection**: 불안정한 모바일 네트워크 환경(Wi-Fi ↔ LTE 전환)이나 일시적인 연결 끊김 시에도 유저 상태를 유지하며 세션을 매끄럽게 복구합니다.

### 🔐 End-to-End Security & Optimization
* **Authenticated Encryption**: **Diffie-Hellman** 키 교환 후, **AES-256-GCM**을 사용하여 데이터의 **기밀성**과 **무결성**을 모두 보장합니다.
* **LZ4 Compression**: 대용량 패킷 전송 시 LZ4 알고리즘을 자동 적용하여 대역폭 비용을 절감하고 전송 속도를 높입니다.

### 📦 Lightweight Protocol
* **Memory Efficient**: `Span<T>`과 `ArrayPool`을 적극 활용하여 직렬화/역직렬화 과정에서 **GC(Garbage Collection) 부하를 최소화**했습니다.
* **Easy Expansion**: C# Attribute를 사용하여 핸들러와 프로토콜을 손쉽게 등록할 수 있습니다.

### ⏱ Async Job Scheduler
* **Game Loop Optimized**: 게임 서버 전용 **비동기 작업 처리기**를 내장하고 있습니다.
* **Timing Control**: **타이머 기반** 반복 작업 실행과 **신호 기반 큐** 처리를 통해 지연을 최소화합니다.

### 📡 UDP Support
* **Real-time Optimization**: 빠른 반응성이 필요한 인게임 데이터(이동, 좌표 동기화 등)를 위한 UDP 전송을 지원합니다.
* **Fragmentation & Reassembly**: MTU 크기를 초과하는 데이터도 자동으로 단편화(Fragmentation) 및 재조립하여 전송합니다.

## 💻 사용 예제 (Usage)

```csharp
// 1. 서버 및 피어 정의
public class GameServer : Engine
{
    protected override IPeer CreatePeer(ISession session)
    {
        return new GamePeer(sessoin)
    }
}

public class GamePeer : BasePeer
{
    public GamePeer(ISession session)
      : base(PeerKind.User, session)
    {
    }
}

// 2. 프로토콜 정의
[Protocol(100)]
public class Login : BaseProtocolData
{
}

// 3. 프로토콜 핸들러 정의
[ProtocolCommand(100)]
public class Login : BaseCommand<GamePeer, Login>
{
    protected override async Task ExecuteCommand(GamePeer peer, Login protocol)
    {
    }
}

// 4. 서버 구동
var builder = EngineConfigBuilder.Create()
    .WithNetwork(n => n with
    {
    })
    .WithSession(s => s with
    {
    })
    .WithPerf(r => r with
    {
    })
    .AddListener(new ListenerConfig { Ip = "Any", Port = 10000 });

var config = builder.Build();

var server = new GameServer();
server.Initialize("Game", config);
server.Start();

```

## 설치 (Installation)

git clone [https://github.com/spilbum/sp.engine.git](https://github.com/spilbum/sp.engine.git)

## 📄 라이선스 (License)
이 프로젝트는 MIT 라이선스 하에 배포됩니다.

Contact GitHub: @spilbum
