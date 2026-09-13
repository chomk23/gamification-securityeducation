# For The Company

산업보안 1인 탐정 게임

Unity 6 + URP로 만든 **사이버펑크 / 다크 네온 터미널 톤**의 단편 미스터리 게임
사내 보안 교육 콘텐츠를 게이미피케이션한 1인 시점 추리 게임으로, 환경 조사 · NPC 대화 · 미니게임을 통해 진짜 산업스파이를 추적

---

## 스토리

대기업 연구시설 **'For The Company'** — 차세대 보안 칩 설계도가 외부로 유출
내부 감사 결과 시설 안의 누군가가 정보를 빼돌리고 있다.

용의자는 셋:
-  **연구원** — 칩 설계 핵심 인력
-  **네트워크관리자** — 외부 통신 권한 보유
-  **시설관리자** — 카드키 발급 담당
---

##  핵심 기능

###  시네마틱 오프닝
- 검은 화면에서 페이드 인 + 보안조사관이 시작 위치까지 워크
- 동시에 1인칭 속마음 5라인 자동 재생
- 컷씬 중 인풋 차단 (몰입감 강화)

###  SecureSense 디자인 시스템
- **다크 베이스 + 네온 액센트** (Green / Cyan / Magenta / Violet / Yellow)
- 가짜 OS 윈도우 chrome · 스캔라인 · 글리치 효과
- 네온사인 깜빡이는 메인 타이틀 ("FOR THE COMPANY")
- 사이버펑크 네온 십자 마우스 커서
- 모든 UI hover 효과 (버튼 · 텍스트 · 카드)

###  대화 시스템
- Typewriter 효과로 글자 하나씩 등장 (Undertale 스타일 비프 사운드)
- 화자 라벨 자동 분리 (NPC ↔ "나")
- 선택지 분기 → 응답 → 자연스러운 트랜지션
- NPC별 동적 대화 (스파이 본인은 회피적 톤, 무고한 NPC는 적극적)
- 대화 시 사선 카메라 시점 자동 전환

###  7단계 선형 스토리
```
1. 경비원 첫 브리핑       (선택지 3개 분기)
2. 연구원 대화 + 보안교육 (자동 트리거)
3. 경비원 중간 브리핑     (스파이 힌트)
4. 보안 레이싱 미니게임
5. 네트워크관리자 + 보안교육
6. 시설관리자 + 보안교육
7. 보안통제실에서 산업스파이 지목
```

###  보안 교육 모듈 (실전 보안 콘텐츠)
- **20개 문제 풀** — 3개 카테고리 (외부 매체 / 네트워크 / 출입 보안)
- NPC 대화 종료 시 **연속 3문제 자동 출제** (풀에서 중복 없이 랜덤)
- 매 출제마다 선택지 순서 셔플 (정답 위치 매번 다름)
- 정답 시 해당 사건의 추리 단서 획득 → 인벤토리 연결 (게임 추리와 학습 결합)

#### 문제 출처 — 개인정보보호 실무교육 자료 기반
실제 KISA/개인정보보호위원회 실무교육 3개 자료의 정보보안 내용을 문항화했습니다.
각 문항 코드에 출처 주석 표기 ([QuizPool.cs](Assets/_Project/Scripts/Systems/QuizPool.cs)).

| 카테고리 | 문항 수 | 다룬 주제 (출처) |
|---|---|---|
|  외부 매체 / 자료 관리 (`research_usb`) | 7 | 보조매체 암호화·반출입 통제, 퇴사자 USB 반출(고의유출), 외부접속 VPN, 출력물 파쇄, 화면 잠금(세션 차단), 클라우드 노출 |
|  네트워크 / 접근권한 / 암호화 (`server_log`) | 7 | 접속기록 보존·점검, 임원 사칭 피싱(스피어피싱), **크리덴셜 스터핑**, 2차 인증(OTP), 무차별 대입 계정 잠금, 비밀번호 일방향 암호화(SHA-256) |
|  출입 / 물리 보안 / 유출 대응 (`cardkey_log`) | 6 | 인사이동 시 권한 말소, 최소권한 차등 부여, **테일게이팅**, 유출 통지·신고(72시간), 수탁자 사고 위탁자 책임, 엑셀 숨김 데이터 점검 |

> 참고 자료: ①개인정보보호법 개정 주요내용 및 안전관리 ②개인정보 관련 판례 및 사례 ③개인정보 유·노출 사례 및 대응방법


###  Investigation Board 인벤토리
- 단서 카드 그리드 자동 배치 (태그별 색 분류: DATA · NET · BADGE · COMMS · LOG ...)
- **3명 용의자 카드 클릭 → 관련 단서로 네온 점선 자동 연결**
- 우측 SUSPECT FILE: 의심도 막대 · 연결 단서 수 · 동적 안내
- 카드 마우스 hover → 전체 내용 툴팁 표시


###  엔드 화면
- VICTORY / DEFEAT 큰 라벨
- **통계 패널**: 사용 시간 · 단서 수집 · 출처별 분류 (NPC 증언 / 보안교육 / 미니게임)
- **MOTIVE 박스**: NPC별 유출 동기 서사 (3가지 패턴 분기)

---

##  조작 방법

| 키 | 동작 |
|---|---|
| `WASD` | 이동 |
| `Shift` | 달리기 (1.8배 속도) |
| `마우스 휠` | 카메라 줌인/아웃 |
| `Space` | NPC 대화 · 단서 조사 · 지목 · 대화 진행 |
| `I` | 인벤토리 (Investigation Board) |
| `ESC` | 일시정지 메뉴 |
| `R` | 게임 종료 후 재시작 |
| `M` | 메인 메뉴 |

---

## 기술 스택

### 엔진 · 언어
- **Unity 6** (6000.4.7f1) + URP (Universal Render Pipeline)
- **C#** + .NET Standard 2.1
- **New Input System** (Keyboard.current API)
- **IMGUI / OnGUI** (UI 렌더링)

### 패키지 · 에셋
- **UnityWebBrowser** (CEF 엔진) — HTML5 미니게임 임베드
- **Kenney Blocky Characters** — NPC 모델 (절차적 걷기/제스처 애니메이션)
- **ScifiOfficeLite** (Terresquall) — Sci-Fi 가구/벽/바닥
- **Synty Polygon Office** — 추가 환경 에셋

### 개발 도구
- Git + Git LFS (대용량 에셋 관리)
- Unity MCP for Claude (자동 코드/씬 조작)

---

##  프로젝트 구조

```
Assets/_Project/
├─ Scenes/
│  ├─ MainMenuScene.unity      ← 시작 화면 + 인트로
│  └─ FacilityScene.unity      ← 메인 게임플레이
├─ Scripts/
│  ├─ Core/                    ← GameSession, FollowCamera
│  ├─ Player/                  ← Player·NPC·인터랙션
│  ├─ Systems/                 ← UI·대화·퀴즈·BGM·SFX·인벤토리
│  ├─ Managers/                ← NPCRoster·Encounter
│  └─ Editor/                  ← 자동 빌드 스크립트
├─ Audio/Resources/BGM/        ← 메인메뉴·게임플레이·엔딩 트랙
└─ Prefabs/                    ← SciFiFacility 등
```

---

##  개발 컨셉

기업 보안교육 콘텐츠를 **게임으로 풀어낸 사이드 프로젝트**입니다.
실제 보안 사례(피싱·USB 분실·카드키 도난·VPN 등)를 게임 메커니즘에 녹여,
플레이어가 자연스럽게 보안 의식을 학습할 수 있도록 설계했습니다.

**디자인 컨셉**: Claude Design ([claude.ai/design](https://claude.ai/design))으로 작성한
`security-education-ui-remix` ("SecureSense") 디자인 시스템을 Unity OnGUI로 1:1 구현.

---

##  라이센스

- **코드**: 개인 학습용 · 자유 사용
- **외부 에셋**: 각 에셋 라이센스 따름
  - Kenney Blocky Characters: CC0
  - ScifiOfficeLite: Terresquall 무료 라이센스
  - BGM: pixabay CC0

---
