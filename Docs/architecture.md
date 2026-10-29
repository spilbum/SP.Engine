# SP.Engine Architecture Specification

SP.Engine은 .NET 8 인프라를 기반으로 구축된 고성능 실시간 멀티플레이어 게임 서버 엔진입니다. 본 프로젝트는 상용 엔터프라이즈 솔루션이 아닌, 고도의 로우레벨 최적화와 동시성 제어 기술을 증명하기 위해 설계된 **상용 서비스 수준의 아키텍처를 지향하는 포트폴리오/토이 프로젝트**입니다. 

서버 가용성과 처리량(Throughput)을 제한하는 두 가지 최대 요인인 **GC(Garbage Collection) 스파이크**와 **스레드 간 락 경합(Lock Contention)**을 프레임워크 레벨에서 원천 차단하는 것을 최우선 사상으로 구현되었습니다.

본 문서는 최하단 버퍼 관리부터 최상단 분산 모듈 시스템까지 엔진의 현재 구조와 유기적 패킷 흐름, 그리고 동시성 통제 정책을 정밀 정의합니다.

---

## 1. 프로젝트 레이어 아키텍처 (Project Structure)

SP.Engine은 도메인 로직과 코어 엔진 로직의 물리적 분리를 강제하여 확장성과 유지보수성을 극대화합니다. 전체 솔루션은 크게 세 가지 주요 그룹으로 나뉩니다.

### A. SP.Core (기반 유틸리티)
도메인에 종속되지 않는 최하단 로우레벨 유틸리티 레이어입니다.
*   **Fibers**: 액터 모델 기반의 스레드 추상화 및 비동기 작업 큐.
*   **Buffers**: 네트워크 통신을 위한 고속 순환 메모리 구조체 (`ByteRingBuffer` 등).
*   **Serialization**: 박싱(Boxing)이나 임시 힙 배열 할당 없이 `Span<byte>` 메모리에 직접 데이터를 쓰고 읽는 무할당(Zero-allocation) 직렬화 엔진 (`NetWriter`, `NetReader`).

### B. SP.Engine (엔진 런타임 및 프레임워크)
실시간 멀티플레이어 통신을 위한 핵심 네트워크 및 런타임 레이어입니다.
*   **SP.Engine.Common**: 전역적으로 공유되는 엔진 내부 프로토콜 ID 및 패킷 데이터 정의.
*   **SP.Engine.Runtime**: 클라이언트와 서버가 모두 공유하는 크로스 플랫폼 네트워크 공통 로직. 프로토콜 핸들러/정책 추상화, 객체 풀(`ProtocolPool`, `ProtocolScope`), 패킷 조립을 위한 `MessageReadBuffer` 및 TCP/UDP 헤더 파싱 제어.
*   **SP.Engine.SourceGenerator**: Roslyn 증분 소스 생성기(Incremental Source Generator). 컴파일 타임에 `[ProtocolData]`, `[NetSerializable]`, `[ProtocolHandler]` 어트리뷰트를 분석하여 직렬화/역직렬화/초기화 코드와 핸들러 및 정책 레지스트리를 자동 생성.
*   **SP.Engine.Client**: 클라이언트 전용 라이브러리. `NetPeerBase`를 통한 접속 관리, `TickTimer`, 레이턴시 및 지터를 추정하는 `LatencyEstimator` 탑재.
*   **SP.Engine.Server**: 고성능 서버 구축을 위한 백엔드 프레임워크. 고도화된 `EngineBase`, 범용 객체 풀(`ExpandablePool`), IOCP 기반의 네트워크 리스너 및 세션 관리자 포함.

### C. Modules (플러그인 확장 모듈)
엔진 코어 루프와 결합도가 낮게 설계된 독립적인 플러그인 시스템입니다.
*   **SP.Database**: 추상화된 비동기 DB 프록시 인프라 및 마이크로 ORM 지원.
*   **SP.Rank**: Redis 기반 혹은 초고속 인메모리 기반의 실시간 랭킹 및 리더보드 시스템.
*   **SP.Resource**: 코드 생성기(CodeGen) 및 Zip 기반의 게임 정적 데이터 관리 및 로컬라이제이션 시스템.

---

## 2. 스레딩 및 동시성 제어 모델 (Threading & Concurrency Model)

SP.Engine은 무분별한 멀티스레드 레이스 컨디션을 배제하고 컨텍스트 스위칭 비용을 최소화하기 위해 **액터(Actor) 모델 기반의 Fiber 아키텍처**를 채택합니다.

* **가상 스레드 기반 로직 샤딩 (LogicFiber Pool):** `EngineBase` 구동 시 환경의 프로세서 코어 수를 기반으로 전용 `LogicFiber` 배열이 생성됩니다. 접속한 각 클라이언트(`Peer`) 객체는 고유 `PeerId`의 비트 마스킹 연산을 통해 특정 `LogicFiber`에 영구적으로 바인딩(Sharding)됩니다.
* **락 프리(Lock-free) 비즈니스 로직 실행:** 특정 유저의 작업과 메시지는 항상 지정된 단일 Fiber 큐에 순차적으로 진입합니다. 따라서 상위 비즈니스 로직 전반에서 스레드 동기화 락(`lock`)을 완전히 배제하고 단일 스레드와 동일한 무락 환경을 제공합니다.
* **글로벌 스케줄러 연동:** `GlobalScheduler`는 스레드를 직접 차단하지 않고, 일정 주기의 틱(Session Tick, Simulation Tick) 이벤트를 발생시킵니다. 이 타이머 이벤트 역시 대상 Fiber의 큐에 비동기적으로 삽입되어 안전하게 동시성을 통제합니다.

---

## 3. 고성능 메모리 매니지먼트 (Zero-Allocation Memory Management)

C# 실시간 서버 성능 최적화의 최대 취약점인 JIT 컴파일러 런타임의 GC 압박을 인프라 레벨에서 은닉 및 제거했습니다. 

* **동적 객체 풀링 (`ExpandablePool` & `MessageReadPoolFactory`):** 런타임 동적 할당과 락 경합을 없애기 위해 고성능 객체 풀링 메커니즘을 사용합니다. `SP.Engine.Server` 내에서 객체를 동적으로 생성하고 소멸시키는 대신 `ExpandablePool`에서 재사용(Rent/Return)하여 할당 오버헤드를 제로에 가깝게 유지합니다.
* **통합 링 버퍼 시스템 (`ByteRingBuffer` & `MessageReadBuffer`):** 
  - 순수 바이트 스트림 수신 및 송신 제어는 `ByteRingBuffer` (`SP.Core.Buffers`)를 통해 이루어집니다. 2의 거듭제곱 크기 제어와 비트 연산 마스크를 결합해 모듈러(`%`) 연산 비용을 지웁니다.
  - L7 네트워크 패킷 조립은 `MessageReadBuffer` (`SP.Engine.Runtime`) 로 통합되었습니다. 패킷 조립 시 성공, 데이터 부족, 헤더 오류 등을 `MessageReadResult` Enum으로 명시적으로 관리하여 수신 파이프라인의 견고함을 더했습니다.
* **Roslyn Source Generator 기반 Zero-Allocation 직렬화:** 컴파일 타임에 패킷(`ProtocolData`) 및 서브 타입(`NetSerializable`)의 직렬화/역직렬화/리셋 코드를 정적으로 생성합니다. 런타임 리플렉션과 값 타입 박싱(Boxing)을 원천 차단하며, `NetWriter`와 `NetReader`(`ref struct`)를 통해 `Span<byte>` 메모리에 직접 데이터를 읽고 써서 GC 발동 인자를 제거합니다.

---

## 4. 하이브리드 트랜스포트 레이어 (Hybrid Transport Layer)

데이터 성격에 따라 전송 파이프라인의 신뢰성과 반응성을 극대화하기 위해 물리 프로토콜을 이원화하여 제어합니다.

### A. TCP 채널 (Reliable Channel)
* 인증, 결제, 아이템 조작 등 데이터의 무결성과 절대적 전송 신뢰성이 보장되어야 하는 비즈니스 패킷을 전담합니다. 메시지 유실 없는 순차적 통신을 보장합니다.

### B. UDP 채널 (Unreliable / Fast Channel)
* 실시간 레이텐시(Latency)가 최우선인 이동 동기화, 타격 액션 패킷을 전담합니다.
* **가변 MTU 및 조립 (`FragmentAssembler`):** 패킷 크기가 인터넷 망의 MTU를 초과할 경우 투명하게 데이터그램을 쪼개어 송신합니다. 수신단에서는 `FragmentAssembler`가 파편화된 UDP 패킷들을 커널 버퍼링 오버헤드 없이 고속 스트림으로 단편화 조립(Reassembly)한 후 시퀀스 넘버에 따라 Fiber로 안전하게 전달합니다.

---

## 5. 플러그인 모듈 및 스케일아웃 확장성 (Pluggable Modules)

SP.Engine은 단일 물리 서버 스펙에 종속되지 않고, 백엔드 분산 아키텍처로 유연하게 스케일아웃할 수 있도록 `Modules` 인프라를 탑재하고 있습니다. 각 모듈은 메인 게임 루프를 지탱하는 비즈니스 샤드 스레드 풀에 간섭(Interference)이나 부하를 주지 않도록 철저히 격리 구동됩니다.

* **SP.Database**: 비동기 DB 프록시 인프라를 제공합니다. 추상화된 `IDbProvider`와 `DbSession`을 통해 멀티스레드 환경에서 안전하게 쿼리를 실행하며, 내부 마이크로 ORM(`DbEntityBinder`)을 통해 고속 데이터 바인딩을 지원합니다.
* **SP.Rank**: 대규모 유저 경쟁을 위한 실시간 리더보드 시스템입니다. Redis와 같은 외부 분산 캐시와 연동하거나, 인메모리 기반(`MemoryRankBoard`)의 초고속 순위 산정, 비동기 시즌 초기화(`RankSeason`) 구조를 완벽하게 지원합니다.
* **SP.Resource**: 게임 기획 및 정적 데이터(테이블) 관리를 담당합니다. CodeGen 기반의 파서, Zip 압축 해제 유틸리티, 그리고 다국어(Localization) 리소스 캐싱 기능을 통해 서버 런타임에 즉각적인 데이터 주입을 지원합니다.
