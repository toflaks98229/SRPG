# Bad North 및 SRPG 장르 조사 보고서

> 작성일: 2026-08-07
> 대상 프로젝트: SRPG (실시간 전략 + 고수준 전투 AI)
> 목적: Bad North류 실시간 전술(RTT)과 SRPG의 전투 AI를 결합한 게임의 설계 근거 확보

---

## 1. 프로젝트 목표 요약

본 프로젝트는 다음 두 축의 교집합을 노린다.

| 축 | 참조 | 취할 것 |
|---|---|---|
| **실시간 전술 (RTT)** | Bad North | 미니멀 조작, 소수 분대, 지형 기반 전투, 슬로우모션 명령 |
| **고수준 전투 AI** | XCOM / Into the Breach / RTS AI 연구 | 유틸리티 기반 의사결정, 영향력 맵, 계층적 지휘 AI |

핵심 설계 명제: **"플레이어의 입력은 최소화하되, 그 입력이 만들어내는 전술적 결과는 최대화한다."**
이를 성립시키는 것은 결국 AI의 품질이다. 플레이어가 "이동" 하나만 지시해도 유닛이 알아서 진형을 잡고 교전하려면, 유닛 AI가 뛰어나야 한다. 반대로 적이 플레이어의 배치를 읽고 협공·우회·분산 상륙을 해야 긴장감이 생긴다. 즉 **본 프로젝트에서 AI는 "적 난이도 장치"가 아니라 "조작 체계를 성립시키는 핵심 시스템"이다.**

---

## 2. Bad North 심층 분석

### 2.1 기본 정보

- 개발: Plausible Concept (Richard Meredith - 조작/UX/진행/밸런스/UI, Oskar Stålberg - 절차적 생성/기술), 오디오 Martin Kvale
- 출시: 2018년 (Switch 선행) → PC/콘솔/모바일, 2019년 7월 무료 확장 *Jotunn Edition*
- 장르 자칭: **"micro-strategy"** — RTS·턴제 전술·타워디펜스의 혼합

### 2.2 코어 게임플레이 루프

```
캠페인 맵(북쪽에서 바이킹 남하) → 섬 선택 → 상륙 방어전 → 보상(코인/지휘관/아이템) → 업그레이드 → 다음 섬
                                        ↑                                                    │
                                        └────────────── 실패 시 지휘관 영구 소실 ←───────────┘
```

- 섬은 **절차적으로 생성**되며 **타일로 분할**된다.
- 바이킹은 배를 타고 **여러 방향에서 동시 상륙**한다. 방어 지점이 분산되는 것이 긴장의 원천.
- 적이 **집에 횃불을 던져 불태우면 그 집에서 나오는 코인을 잃는다** → 단순 생존이 아니라 "얼마나 지켜냈는가"가 보상에 직결.
- 난이도: Easy / Normal / Hard, Hard 클리어 시 **Very Hard** 해금. Jotunn Edition에서 **영웅 특성(hero traits)** 추가.

### 2.3 조작 체계 — 미니멀리즘의 핵심

이 부분이 본 프로젝트가 가장 직접적으로 참조해야 할 지점이다.

- **플레이어가 동시에 지휘하는 분대는 최대 4개** 수준.
- **주 조작은 단 하나: "분대를 타일로 보낸다."** 개발자 Meredith 曰 —
  > "Your primary action in the game is to tell a unit to go to a grid space... really limiting the amount of micromanagement."
- **명령 중에는 게임이 완전히 멈추지 않고 '느려진다'(slow to a crawl).** 완전 일시정지가 아니라 슬로우모션이라는 점이 중요하다. 판단할 여유는 주되 긴박감은 유지한다.
- 나머지는 유닛이 알아서 한다: 경로 탐색, 교전 판단, 진형 유지.
  > "You command the broad strokes of your defences and monitor positioning — your soldiers do the rest, navigating and engaging intuitively in response to the situation at hand."
- **UI/스탯/명시적 규칙을 거의 노출하지 않는다.** 시스템은 숨겨져 있고 플레이어가 플레이하며 암묵적으로 발견한다.

> **설계 시사점**: "복잡한 시스템 + 단순한 인터페이스"가 원칙이다. 시스템을 단순화하는 게 아니라, **시스템의 복잡성을 AI가 흡수**하게 만든다.

### 2.4 유닛 구성

| 유닛 | 역할 | 특성 |
|---|---|---|
| **Militia (민병)** | 기본/무보직 | 업그레이드 전 상태 |
| **Infantry (보병)** | 탱커·추격 | 방패로 **투사체 거의 무효화**, 기동성 우수, 아군(궁수/지휘관) 보호 |
| **Archers (궁수)** | 원거리 딜러 | **적이 아직 배에 있을 때** 최대 효율, 방패병을 배 밖으로 밀어냄 |
| **Pikes (창병)** | 라인 홀더 | **이동 중 공격 불가**, 초크포인트·경사면에서 최대 성능 |

특히 주목할 밸런스 디테일:
- **궁수는 인원이 많을수록 피격 면적이 커져 오히려 취약해진다.** 소규모 적이 대규모 궁수 부대에 피해를 줄 수 있다. → **부대 규모 자체가 전술 변수**가 되는 구조. 단순 스탯 합산이 아니라 물리적 점유가 밸런스에 개입한다.
- 창병의 "이동 중 공격 불가"는 **플레이어가 위치를 미리 결정하도록 강제**하는 장치다. 실시간에서 사전 계획을 유도하는 좋은 사례.

### 2.5 지휘관(Flag Bearer) 시스템 — 로그라이트의 핵심

- 각 분대에는 **깃발병(지휘관)** 이 있다.
- **개별 병사가 죽어도 분대는 유지**되며, 집(house)에서 **회복(충원)** 이 가능하다.
- 그러나 **지휘관이 죽으면 분대 전체와 그 분대가 획득한 모든 업그레이드가 영구 소실**된다.
- 코인은 유닛 업그레이드와 지휘관 레벨업(방어)에 사용된다.

> **설계 시사점**: 손실의 층위가 두 개다 — **회복 가능한 손실(병사)** vs **영구 손실(지휘관)**. 이 이중 구조가 "물러날 것인가 버틸 것인가"라는 실시간 판단을 매 순간 만들어낸다. 본 프로젝트도 반드시 이 이중 손실 구조를 가져가야 한다.

### 2.6 개발 배경

- 시작점은 **절차적 생성 알고리즘**과 **군중 내 유닛 이동에 대한 아이디어**였다. 게임 콘셉트가 먼저가 아니라 **기술이 먼저**였고, 여러 반복을 거쳐 "작은 섬 + 바이킹 침공"으로 수렴했다.
  > "Exploring the technology and letting the game tell them what it wanted to be."
- 목표 관객: **전략 게임에 관심 있지만 전형적 RTS의 복잡도에 질린 층.**

---

## 3. 타 SRPG 조사

### 3.1 Fire Emblem — 규칙 기반(Rule-based) AI

**의사결정 구조 (탐욕적/greedy)**
1. 공격 범위 내에 적(플레이어 유닛)이 있으면 **반드시 공격**한다.
2. 대상이 복수면 **가장 큰 피해를 줄 수 있는 유닛**을 선택한다.
3. 범위 내에 없으면 **제자리 유지**(방어형) 또는 **접근**(blitz형).

**AI 유형**
- **Defensive**: 사거리 내 적만 공격
- **Blitz**: 플레이어 쪽으로 공격적 이동
- **Impatient**: 이동+공격 범위 내 **방어력이 가장 낮은 유닛**을 최단 경로로 노림

**행동 순서**: 근접이 원거리보다 우선 → 같은 사거리면 가장 빨리 도달 가능한 유닛 → 모두 동일하면 **맵 좌측 유닛부터**.

**알려진 한계**: 대상 우선순위 방식 때문에 **피해를 줄 수 없는 유닛을 노리거나, Lord 같은 핵심 표적을 두고 덜 중요한 유닛을 공격**하는 부자연스러운 판단이 자주 발생한다.

> **교훈**: 규칙 기반은 구현·디버깅이 쉽지만 **"왜 저기로 가지?"** 하는 순간 몰입이 깨진다. 본 프로젝트의 "높은 수준의 AI" 목표에는 부족하다. 다만 **폴백(fallback) 계층**으로는 유용하다.

### 3.2 Into the Breach — 완전 정보 + 텔레그래핑

- **모든 적 공격이 사전에 완전히 예고된다.** 어느 타일을 칠지, 피해량이 얼마인지 플레이어 턴에 UI로 표시.
- **명중률(to-hit) 개념이 없다.** 무작위성을 최소화.
- 설계 의도:
  > "모든 죽음이 플레이어 자신의 잘못으로 느껴지게 만들고 싶었다."
- 효과: 플레이어는 **게임이 어떻게 돌아가는지 파악하는 데 시간을 덜 쓰고, 이기는 방법을 궁리하는 데 시간을 더 쓴다.**
- **AI 난이도**: 무작위성의 도움이 전혀 없기 때문에, AI는 매 턴 모든 적을 배치하고 공격을 구성하되 **플레이어가 현재 자원으로 최소 피해로 넘길 수 있는 수준**을 유지해야 한다. 이는 오히려 매우 정교한 AI를 요구한다.

> **본 프로젝트 적용**: 실시간이므로 턴 단위 완전 예고는 불가능하다. 그러나 **"적의 의도를 읽을 수 있게 만든다"** 는 원칙은 이식 가능하다. 예: 상륙정의 진행 방향/도착 예정 지점 표시, 적 분대의 목표 타일 하이라이트, 돌격 직전 모션 예비동작(wind-up). **실시간에서의 텔레그래핑 = 예비동작과 의도 시각화.**

### 3.3 장르 비교

| 항목 | Bad North | Fire Emblem | XCOM | Into the Breach | **본 프로젝트(제안)** |
|---|---|---|---|---|---|
| 시간 모델 | 실시간 + 슬로우모 | 턴제 | 턴제 | 턴제 | **실시간 + 슬로우모** |
| 조작 단위 | 분대(4개) | 개별 유닛 | 개별 유닛 | 개별 메크 | **분대** |
| 명령 종류 | 이동(사실상 1개) | 이동+행동 | 이동+행동 | 이동+행동 | **이동 + 소수 능력** |
| 무작위성 | 낮음 | 명중률/크리 | 명중률 중심 | **없음** | **낮게 유지** |
| 정보 공개 | 부분 | 부분 | 안개 | **완전** | **의도 텔레그래핑** |
| 영구 손실 | 지휘관 | 유닛(클래식) | 병사 | 조종사 | **지휘관 + 업그레이드** |
| AI 방식 | 군중/스티어링 | 규칙 기반 | 유틸리티+스코어링 | 제약 기반 | **계층적 유틸리티** |

---

## 4. 전투 AI 기술 조사

본 프로젝트의 요구는 **"높은 수준의 전투 AI"** 이므로, 이 절이 실질적 기술 근간이다.

### 4.1 유틸리티 AI / IAUS (Infinite Axis Utility System)

Dave Mark & Mike Lewis가 GDC AI Summit("Building a Better Centaur: AI at Massive Scale")에서 발표한 방식.

**구조**
```
Action(행동) ─┬─ Consideration 1: input → 정규화[0,1] → 응답곡선 → score1
              ├─ Consideration 2: ...                            → score2
              └─ Consideration N: ...                            → scoreN
                                                     최종점수 = score1 × score2 × ... × scoreN
                                            (모든 Action 중 최고점 Action 실행)
```

**핵심 특성**
- **Consideration(고려사항)** 이 최소 단위: 입력값을 응답 곡선(response curve)에 매핑해 점수를 낸다.
- 입력은 **[0,1]로 정규화**, 곡선은 **4개 파라미터(m, k, b, c)** 로 형태를 조절(선형/지수/로지스틱/로지트).
- 점수를 **곱셈**으로 합성 → 결과가 항상 [0,1]에 유지되므로 고려사항을 무한히 추가 가능. 이것이 "Infinite Axis"의 의미.
- **주의**: 곱셈이므로 고려사항이 많아질수록 점수가 0에 수렴한다. → **보정(compensation factor)** 이 필요하다. 통상 기하평균 기반 보정을 적용한다.

**본 프로젝트 적합성: 매우 높음**
- 실시간에서 매 프레임이 아니라 **일정 주기(예: 0.2~0.5초)로 재평가**하면 비용이 감당된다.
- 디자이너가 곡선만 조정해 AI 성격을 바꿀 수 있다 → **ScriptableObject로 데이터화**하기 좋다.
- "공격적/방어적/기회주의적" 같은 **AI 페르소나를 곡선 세트로 표현** 가능.

관련 도구/사례: Curvature(유틸리티 AI 에디터), Tactical Troops: Anthracite Shift는 **MCTS + Utility AI 계층 결합**을 상용 적용.

### 4.2 영향력 맵 (Influence Map)

**개념**: 맵의 각 타일/셀에 **전략적 가치 점수**를 부여한다. 아군 전력, 적 전력, 위협, 엄폐, 목표 근접도 등을 레이어로 쌓는다.

**계산**: Dijkstra 변형으로 확산. 지형별 이동 비용과 복수 목적지를 고려.

**활용**
- 이동 목표 선정: "방어 유리 타일을 선호하되 그 근처에 머문다"
- **장기 계획의 착시(illusion of long-term planning)** 생성 — 실제로는 지역 탐색인데 전략적으로 보인다.
- 위협 맵(threat map)으로 궁수 사거리 회피, 측면 우회 경로 산출.

**본 프로젝트 적합성: 매우 높음.** Bad North식 타일 분할 섬과 직결된다. 레이어 예시:
```
- FriendlyStrength   (아군 전력 확산)
- EnemyThreat        (적 위협/사거리 확산)
- ChokePointValue    (초크포인트 가중 — 창병 배치 판단)
- HighGroundValue    (고지/경사 — 궁수·창병 보정)
- ObjectiveValue     (집/거점 보호 우선도)
- LandingPressure    (상륙 예상 지점 압력)
```

### 4.3 계층적 AI (Hierarchical / Commander-Squad-Unit)

GDC "Believable Tactics for Squad AI"(Champandard, Jack, Dunstan) 및 Days Gone의 "Squad Coordination" 사례가 근거.

**3계층 제안**
```
┌─ Commander AI (전략, 1~2초 주기)
│    · 어느 해안에 몇 척을 보낼지, 언제 압박할지
│    · 영향력 맵 기반 전체 전선 판단, 분대 생성/해체
│
├─ Squad AI (전술, 0.2~0.5초 주기)
│    · 유틸리티 평가로 목표 타일/교전 대상 결정
│    · 진형(Formation) 선택, 협공·우회·후퇴
│
└─ Unit AI (실행, 매 프레임)
     · 플로우 필드 추종 + 스티어링(분리/정렬/응집)
     · 근접 회피, 공격 타이밍, 애니메이션 상태
```

**중앙집중 vs 분산**: GDC 세션은 두 방식을 비교한다. 본 프로젝트는 **분대 단위 중앙집중 + 유닛 단위 분산**의 하이브리드를 권장한다. 분대 내 동기화(동시 돌격 등)는 중앙에서, 개별 충돌 회피는 분산으로.

**중요**: 이 계층은 **플레이어 유닛에도 동일 적용**된다. Bad North에서 "이동만 지시하면 나머지는 알아서"가 성립하는 이유가 바로 이것이다. 플레이어 분대의 Squad AI는 **플레이어가 지정한 목표 타일을 최우선 제약으로 받는** 동일 엔진이면 된다.

### 4.4 플로우 필드 패스파인딩 + 스티어링

**근거**: Game AI Pro Ch.23 "Crowd Pathfinding and Steering Using Flow Field Tiles"(Elijah Emerson), Planetary Annihilation 사례.

**원리**
1. 목표 셀에서 시작해 **Dijkstra로 비용 필드(cost field)** 를 채운다.
2. 각 셀에서 비용 기울기로 **방향 벡터**를 계산 → **플로우 필드**.
3. 유닛은 자기가 밟은 셀의 벡터를 속도에 적용한다.

**장점**: 유닛 수에 무관하게 **경로 계산은 목표당 1회**. 대군 이동에 압도적으로 유리하다. A*를 유닛마다 돌리는 것과 비교 불가.

**스티어링 결합**: Craig Reynolds의 Boids 3원칙 — **분리(separation) / 정렬(alignment) / 응집(cohesion)** 을 플로우 필드 위에 얹어 자연스러운 군중 이동을 만든다.

**본 프로젝트 적합성: 높음.** 단, 섬이 작고(타일 수십~수백) 유닛이 수십 규모라면 **NavMesh + 로컬 회피로도 충분**할 수 있다. 다음 기준으로 판단한다:

| 조건 | 권장 |
|---|---|
| 동시 유닛 < 50, 섬 소형 | Unity NavMesh(`com.unity.ai.navigation` 이미 설치됨) + 커스텀 스티어링 |
| 동시 유닛 > 100, 대군 연출 | 플로우 필드 + Job System/Burst |

초기에는 NavMesh로 시작하고, `02.Scripts/Systems/Pathfinding/` 하위에 **FlowField / NavGrid / Steering** 을 분리해 두어 교체 가능하게 설계했다.

### 4.5 기타 검토 기법

| 기법 | 평가 | 판단 |
|---|---|---|
| **Behavior Tree** | 실행 흐름 제어에 우수, 의사결정에는 조건 폭발 | Unit AI 실행 계층에 부분 채택 |
| **GOAP** | 목표→행동 역방향 계획. 계획 비용이 실시간에 부담 | 보류 |
| **HTN** | 계층적 작업 분해. Commander 계층에 이론적 적합 | 후보 (유틸리티 우선) |
| **MCTS** | 강력하나 실시간·연속공간에서 비용 과다 | 보류 (턴제였다면 1순위) |
| **강화학습** | Fire Emblem "Mirror Mode" 연구 등 사례 존재 | 범위 밖 |

**결론: 유틸리티 AI(의사결정) + 영향력 맵(공간 평가) + BT(실행) + 플로우 필드/스티어링(이동)** 의 조합을 채택한다.

---

## 5. 본 프로젝트 설계 제안

### 5.1 시간 모델

Bad North의 **슬로우모션 명령**을 채택한다. 완전 정지가 아닌 이유:
- 완전 정지는 최적해 탐색을 유도해 **턴제화**되고 긴박감이 사라진다.
- 슬로우모션은 "생각할 시간은 주되 무한하지 않다"는 압박을 유지한다.

구현: `Systems/Time` 에 `TimeScaleController` 를 두고, 명령 입력 상태에서 `Time.timeScale ≈ 0.1~0.2`. **AI 평가 주기는 스케일되지 않은 시간 기준**으로 돌려야 슬로우모션 중 AI가 멈춘 것처럼 보이지 않는다(`unscaledDeltaTime` 사용).

### 5.2 AI 아키텍처 → 폴더 매핑

| 계층 | 위치 |
|---|---|
| Commander AI | `02.Scripts/Systems/AI/Commander/` |
| Squad AI | `02.Scripts/Systems/AI/SquadAI/` |
| 유틸리티 평가 | `02.Scripts/Systems/AI/Considerations/` |
| 응답 곡선 데이터 | `03.DataAssets/AI/Curves/`, `03.DataAssets/AI/Considerations/` |
| AI 페르소나 | `03.DataAssets/AI/Profiles/` |
| 영향력 맵 | `02.Scripts/Systems/AI/InfluenceMap/` |
| 블랙보드 | `02.Scripts/Systems/AI/Blackboard/` |
| 실행 BT | `02.Scripts/Systems/AI/BehaviorTree/` |
| AI 디버그 시각화 | `02.Scripts/Systems/AI/Debug/` |
| 이동 | `02.Scripts/Systems/Pathfinding/{FlowField,NavGrid,Steering}/` |
| 진형 | `02.Scripts/Systems/Formation/` |
| 위협 평가 | `02.Scripts/Systems/Combat/Threat/` |

**AI 디버그 시각화는 선택이 아니라 필수다.** 유틸리티 점수와 영향력 맵은 눈으로 보지 않으면 튜닝이 불가능하다. 각 Consideration의 점수 기여도를 런타임에 표시하는 오버레이를 초기부터 만들 것을 강력히 권한다.

### 5.3 데이터 주도 설계

Bad North의 밸런스 디테일(궁수 인원수 = 피격 면적)처럼, **수치가 아니라 구조에서 나오는 밸런스**를 지향한다. 그러려면 수치는 전부 데이터화되어 실험 가능해야 한다. `03.DataAssets/` 하위를 ScriptableObject 중심으로 구성했고, SCPPJ와 동일하게 `CSV/` 파이프라인 자리를 남겨두었다.

### 5.4 검증 우선순위 (프로토타입 순서)

1. **타일 분할된 섬 + 분대 이동 명령 + 슬로우모** — 조작 감각이 성립하는지
2. **Unit AI 자동 교전** — "이동만 지시해도 알아서 싸운다"가 되는지
3. **영향력 맵 + Squad AI** — 적이 똑똑해 보이는지
4. **지휘관 영구 사망 + 업그레이드** — 긴장이 생기는지
5. **Commander AI 다방향 상륙** — 전선 분산 압박이 작동하는지

1~2번이 성립하지 않으면 나머지는 의미가 없다. **가장 먼저 검증할 것은 AI가 아니라 조작 감각이다.**

---

## 6. 리스크

| 리스크 | 영향 | 대응 |
|---|---|---|
| 유틸리티 AI 튜닝 난이도 | 높음 | 디버그 오버레이 선행 구축, 곡선 데이터화 |
| "알아서 잘 싸우는" 유닛 AI 미달 | 치명적 | 프로토타입 2단계에서 조기 검증, 미달 시 명령 종류 추가 검토 |
| 실시간 + 다수 유닛 성능 | 중간 | AI 평가 주기 분산(시간 분할), 필요 시 Job System 전환 |
| 미니멀 UI vs 정보 부족 | 중간 | Into the Breach식 의도 텔레그래핑으로 보완 |
| 절차적 섬 생성 품질 | 중간 | 초기엔 수제작 맵으로 전투 검증, 생성은 후순위 |

---

## 7. 참고 자료

**Bad North**
- [Bad North 공식 사이트](https://www.badnorth.com/)
- [Bad North - Wikipedia](https://en.wikipedia.org/wiki/Bad_North)
- [Getting To Grips With Bad North's Take On Real-Time Strategy - TheSixthAxis](https://www.thesixthaxis.com/2018/04/25/getting-to-grips-with-bad-norths-take-on-real-time-strategy/)
- [Interview: Taking on hordes of invading Vikings in Bad North - Nintendo UK](https://www.nintendo.com/en-gb/News/2018/April/Interview-Taking-on-hordes-of-invading-Vikings-in-Bad-North-1368315.html)
- [Indie Spotlight: Plausible Concept on Bad North - PocketGamer.biz](https://www.pocketgamer.biz/interview/68638/indie-spotlight-plausible-concept-on-bad-north/)
- [Bad North: Jotunn Edition on Steam](https://store.steampowered.com/app/688420/Bad_North_Jotunn_Edition/)
- [Bad North - Unit Summaries (Steam Guide)](https://steamcommunity.com/sharedfiles/filedetails/?id=1566564445)
- [Bad North - Enemies Guide (Steam Guide)](https://steamcommunity.com/sharedfiles/filedetails/?id=1569270576)
- [Beginner Guide to Bad North - Fandom Wiki](https://bad-north.fandom.com/wiki/Beginner_Guide_to_Bad_North)

**SRPG / 턴제 전술**
- [Fire Emblem AI Analysis](https://jchuong.github.io/fire-emblem-ai-analysis)
- [AI - Fire Emblem Heroes Wiki](https://feheroes.fandom.com/wiki/AI)
- [Into the Breach - Subset Games](https://subsetgames.com/itb.html)
- [Into the Breach & Enemy Intentions - Atomic Bob-Omb](https://atomicbobomb.home.blog/2020/05/17/into-the-breach-enemy-intentions/)
- [Into The Breach And Dynamic Puzzles - Blog of Arcane Secrets](https://blogofarcanesecrets.wordpress.com/2018/03/09/into-the-breach-and-dynamic-puzzles/)
- [Road to the IGF: Subset Games' Into the Breach - Game Developer](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-)
- [Designing AI Algorithms For Turn-Based Strategy Games - Game Developer](https://www.gamedeveloper.com/design/designing-ai-algorithms-for-turn-based-strategy-games)
- [Hex Tactics: AI Extension (영향력 맵 실전 사례)](https://ackleyrc.itch.io/hex-tactics/devlog/220323/hex-tactics-ai-extension)
- [The Computational Complexity of Fire Emblem Series and similar TRPGs (arXiv)](https://arxiv.org/pdf/1909.07816)

**전투 AI 기술**
- [Utility Theory Crash Course - Curvature Wiki](https://github.com/apoch/curvature/wiki/Utility-Theory-Crash-Course)
- [Smarter Game AI with Infinite Axis Utility Systems](https://tonogameconsultants.com/infinite-axis-utility-systems/)
- [Considerations - Infinite Axis Utility System 문서](https://uintel-go.utilityworlds.com/Documentation/UtilityIntelligence/Considerations/)
- [Utility system - Wikipedia](https://en.wikipedia.org/wiki/Utility_system)
- [GDC Vault - Believable Tactics for Squad AI](https://gdcvault.com/play/1016076/Believable-Tactics-for-Squad)
- [GDC Vault - AI Summit: Squad Coordination in 'Days Gone'](https://gdcvault.com/play/1027237/AI-Summit-Squad-Coordination-in)
- [Game AI Pro Ch.23 - Crowd Pathfinding and Steering Using Flow Field Tiles (PDF)](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf)
- [How to RTS: Basic Flow Fields](https://howtorts.github.io/2014/01/04/basic-flow-fields.html)
- [RTS Group Movement (스티어링 + 군중 이동 문서)](https://sandruski.github.io/rts-group-movement/)
- [Combining Utility AI and MCTS - Tactical Troops: Anthracite Shift (ResearchGate)](https://www.researchgate.net/publication/358095717_Combining_Utility_AI_and_MCTS_Towards_Creating_Intelligent_Agents_in_Video_Games_with_the_Use_Case_of_Tactical_Troops_Anthracite_Shift)
- [Building Human-Level AI for Real-Time Strategy Games (AAAI PDF)](https://cdn.aaai.org/ocs/4209/4209-17783-1-PB.pdf)
- [A Tactical and Strategic AI Interface for Real-Time Strategy Games (PDF)](https://www.cs.auckland.ac.nz/courses/compsci767s2c/resources/Papers/WS04-04-007.pdf)
