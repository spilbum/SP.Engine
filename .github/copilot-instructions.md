# Copilot / AI Agent Instructions for SP.Engine

간결한 목적: 이 파일은 AI 코딩 에이전트가 SP.Engine 저장소에서 즉시 생산적으로 작업할 수 있도록, 핵심 구조·패턴·작업 흐름·예시 파일을 요약합니다.

Quick start
- **빌드:** `dotnet build SP.Engine.sln -c Release`
- **샘플 실행(예):** `dotnet run --project Samples/GameServer/GameServer.csproj`

Big picture (한눈 요약)
- **Core**: 재사용 유틸리티(메모리 풀, 동시성 버퍼, 로깅, Fiber 스케줄러). 예: `Core/SP.Core/PooledBuffer.cs`, `Core/SP.Core/SwapBuffer.cs`.
- **Engine**: 클라이언트/프로토콜/런타임/서버 컴포넌트로 분리되어 있음. `Engine/SP.Engine.Protocol`에 프로토콜 정의, `Engine/SP.Engine.Server`에 서버 라이프사이클·세션 로직이 집중.
- **Samples**: 실제 사용 예시(서버/클라이언트), 빠른 기능 검증용.

Key patterns & conventions (프로젝트에 특화된 규칙)
- **프로토콜 등록:** Protocol 메시지와 정책은 어트리뷰트로 정의합니다. 예: `Engine/SP.Engine.Protocol/EngineProtocol.cs` 내의 `[Protocol(id, encrypt: ..., compress: ...)]` 클래스.
- **핸들러 형태:** 프로토콜 핸들러는 `BaseCommand<TPeer, TProtocol>` 형태로 구현되고 `[ProtocolCommand(id)]` 어트리뷰트를 사용(레퍼런스: README 사용 예제).
- **채널 분리:** `ChannelKind.Reliable`(TCP) vs `ChannelKind.Unreliable`(UDP)을 명확히 구분하여 메시지 직렬화/전송을 선택합니다. (참조: `Engine/SP.Engine.Server/Session.cs`의 `InternalSend`)
- **메모리 관리:** `Span<T>`, `ArrayPool<T>.Shared`, `PooledBuffer` 등을 사용해 GC 부담을 최소화합니다. 변경 시 '복사/반환' 규칙 준수 필요(`Core/SP.Core/PooledBuffer.cs`).
- **동시성:** 경량 스레드/작업 스케줄러(`FiberScheduler`)와 lock-free 스타일 `SwapBuffer<T>`를 사용합니다. 변경 시 `Pending/Claimed/Published` 플로우를 이해해야 합니다.
- **보안·압축 토글:** 프로토콜별로 `encrypt`/`compress` 옵션을 지정합니다(예: 세션 인증 메시지는 암호화 비활성화). 관련 설정은 `Engine/SP.Engine.Runtime/Security`와 `Engine/SP.Engine.Runtime/Compression`에서 확인.

Integration points & external deps
- .NET 8 런타임 기반.
- 네트워킹은 `Socket`/`TcpNetworkSession`/`UdpSocket` 추상층을 통해 구현(참조: `Engine/SP.Engine.Server/SocketServer.cs`, `Engine/SP.Engine.Client/UdpSocket.cs`).
- 암호화/키교환은 런타임의 Security 모듈에 위치(예: DH 키 교환, AES-GCM 사용 패턴을 찾아 수정).

How to add a new protocol + handler (minimal example)
- 1) `Engine/SP.Engine.Protocol/EngineProtocol.cs`에 `[Protocol(<id>)] public class Xxx : BaseProtocolData { ... }` 추가
- 2) 핸들러: `[ProtocolCommand(<id>)] public class XxxHandler : BaseCommand<GamePeer, Xxx> { protected override Task ExecuteCommand(...) { ... } }`
- 3) 네트워크 정책(암호화/압축)이 필요하면 `GetNetworkPolicy`/설정에서 반영

Files to inspect first (빠른 참조)
- `Engine/SP.Engine.Protocol/EngineProtocol.cs` — 프로토콜 정의와 어트리뷰트 예시
- `Engine/SP.Engine.Server/Session.cs` — 세션 핸드쉐이크/전송 흐름 예시
- `Engine/SP.Engine.Server/BaseEngine.cs` — 서버 초기화·리스너·타이머·셧다운 패턴
- `Core/SP.Core/PooledBuffer.cs` — ArrayPool 반환 규칙
- `Core/SP.Core/SwapBuffer.cs` — 배칭·동시성 플로우

Notes for AI edits
- 변경은 작게, 명확한 단위로: 프로토콜 ID 충돌이나 네트워크 정책 변경은 기존 세션 핸드쉐이크/호환성에 즉시 영향을 줍니다.
- 메모리풀/스팬 관련 코드를 수정할 때는 `ArrayPool.Return` 누락이나 `Span`의 유효범위(생명주기)를 반드시 검증하세요.
- 동시성 구조(SwapBuffer, FiberScheduler)를 고칠 때는 기존 테스트 또는 샘플을 통해 성능·경합 시나리오를 재검증하세요.

수정했으면 알려주세요 — 불명확한 부분(예: 샘플 실행 방법, CI 명령어 등)을 더 채워서 반복하겠습니다.
