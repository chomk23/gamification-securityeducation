using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ForTheCompany.Core;
using ForTheCompany.Managers;
using ForTheCompany.Player;

namespace ForTheCompany.Systems
{
    public class SecurityQuizController : MonoBehaviour
    {
        public static SecurityQuizController Instance { get; private set; }

        public ClueObject ActiveClue { get; private set; }
        public bool IsOpen => ActiveClue != null;
        public string LastResultText { get; private set; }
        public float LastResultTime { get; private set; }
        public bool LastWasCorrect { get; private set; }

        // ── 해설/힌트 상태 ──
        public bool ShowingExplanation { get; private set; }    // 정답 직후 해설 표시 중 ([다음] 버튼으로만 진행)
        public bool HintRevealed { get; private set; }           // [힌트 보기]를 눌렀는지
        public bool CurrentQuestionMissed { get; private set; }  // 현재 문제에서 한 번이라도 오답 냈는지
        public string CurrentExplanation => ActiveClue != null && ActiveClue.data != null
            ? ActiveClue.data.quizExplanation : "";
        public string CurrentHint => ActiveClue != null && ActiveClue.data != null
            ? ActiveClue.data.quizHint : "";

        // ── 연속 3문제 세션 진행 ──
        private List<QuizPool.QuizVariant> sessionQuizzes;
        private int sessionCorrectCount;
        private readonly List<string> sessionSuccessClues = new List<string>();
        public int SessionIndex { get; private set; }       // 0-based 현재 문제
        public int SessionTotal => sessionQuizzes?.Count ?? 0;
        public int SessionCurrent => SessionIndex + 1;      // 1-based 표시용
        public const int QuestionsPerSession = 3;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            EnsureSpawned();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureSpawned();
        }

        private static void EnsureSpawned()
        {
            // FacilityScene 이외 씬에서는 ClueObject 큐브들이 불필요
            if (SceneManager.GetActiveScene().name != "FacilityScene") return;
            if (FindFirstObjectByType<SecurityQuizController>() != null) return;
            var go = new GameObject("SecurityQuizController");
            var ctrl = go.AddComponent<SecurityQuizController>();
            ctrl.SpawnDefaultClues();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        // 보안 교육 미션은 ESC로 취소 불가 — 무조건 정답을 맞춰야 닫힘.
        // (Answer()에서 정답 시 ActiveClue=null로 자동 닫음)

        public void Open(ClueObject clue)
        {
            if (clue == null || clue.Resolved) return;
            var gs = GameSession.Instance;
            if (gs != null && gs.Outcome != RunOutcome.Ongoing) return; // 결과창 뒤에서 열리지 않게

            // 풀에서 중복 없이 3개 랜덤 선택 → 첫 문제 적용
            sessionQuizzes = QuizPool.GetRandomBatch(clue.data.id, QuestionsPerSession);
            sessionCorrectCount = 0;
            sessionSuccessClues.Clear();
            SessionIndex = 0;

            if (sessionQuizzes.Count > 0)
                QuizPool.ApplyTo(clue.data, sessionQuizzes[0]);
            else
                QuizPool.ApplyRandomTo(clue.data); // 풀 없으면 fallback (기존 단일 quiz)

            ShowingExplanation = false;
            HintRevealed = false;
            CurrentQuestionMissed = false;
            LastResultText = "";

            ActiveClue = clue;
            SfxManager.PlayModalOpen();
            Debug.Log($"[Quiz] {clue.data.id} 세션 시작 ({sessionQuizzes.Count}문제 출제, 풀 {QuizPool.GetPoolSize(clue.data.id)})");
        }

        /// <summary>내부 전용 — 외부에서는 호출 금지. Answer 정답 시에만 사용.</summary>
        public void Close()
        {
            ActiveClue = null;
            sessionQuizzes = null;
        }

        /// <summary>게임이 끝났을 때(결과창) 진행 중인 세션을 보상·통계 없이 닫는다.
        /// 열린 채 두면 결과창 밑에 숨은 선택지가 결과창 클릭을 먼저 가져간다.</summary>
        public void ForceCloseForRunEnd()
        {
            if (ActiveClue == null) return;
            ActiveClue = null;
            sessionQuizzes = null;
            sessionSuccessClues.Clear();
            ShowingExplanation = false;
            HintRevealed = false;
            CurrentQuestionMissed = false;
            LastResultText = "";
        }

        public void Answer(int index)
        {
            if (ActiveClue == null || ActiveClue.data == null) return;
            if (ShowingExplanation) return; // 해설 표시 중엔 답변 무시 — [다음] 버튼으로만 진행
            var data = ActiveClue.data;
            bool correct = index == data.correctIndex;
            LastWasCorrect = correct;
            LastResultTime = Time.time;

            if (!correct)
            {
                // 오답 — 같은 문제 재시도. 첫 오답이면 통계 기록(결과창 오답 노트용)
                LastResultText = "× 오답. 다시 시도하세요.";
                SfxManager.PlayWrong();
                if (!CurrentQuestionMissed)
                {
                    CurrentQuestionMissed = true;
                    var gs = GameSession.Instance;
                    if (gs != null)
                    {
                        gs.quizWrongCount++;
                        gs.WrongQuizzes.Add(new GameSession.WrongQuizEntry
                        {
                            question = data.quizQuestion,
                            explanation = data.quizExplanation
                        });
                    }
                }
                Debug.Log($"[Quiz] {data.id} #{SessionCurrent}/{SessionTotal} 오답");
                return;
            }

            // ── 정답 처리 — 즉시 넘어가지 않고 해설을 먼저 보여준다 ──
            sessionCorrectCount++;
            sessionSuccessClues.Add(data.successClue);  // 통합 단서용 모음
            SfxManager.PlayCorrect();

            // 보상 카운트만 누적 (인벤토리 단서는 세션 완료 시 1개로 통합 추가)
            if (GameSession.Instance != null)
            {
                GameSession.Instance.totalClues += data.clueReward;
                GameSession.Instance.LastEncounterRewardClues = data.clueReward;
                if (!CurrentQuestionMissed)
                    GameSession.Instance.quizFirstTryCorrect++; // 첫 시도 정답 통계
            }

            LastResultText = "";
            ShowingExplanation = true;
            Debug.Log($"[Quiz] {data.id} #{SessionCurrent}/{SessionTotal} 정답 — 해설 표시");
        }

        /// <summary>오답 후 [힌트 보기] 버튼 — 현재 문제의 힌트 공개</summary>
        public void RevealHint()
        {
            if (ShowingExplanation) return;
            HintRevealed = true;
        }

        /// <summary>해설 확인 후 [다음 문제/교육 완료] 버튼 — 진행 또는 세션 종료</summary>
        public void ProceedAfterExplanation()
        {
            if (!ShowingExplanation || ActiveClue == null || ActiveClue.data == null) return;
            var data = ActiveClue.data;

            ShowingExplanation = false;
            HintRevealed = false;
            CurrentQuestionMissed = false;

            SessionIndex++;
            bool hasNext = sessionQuizzes != null && SessionIndex < sessionQuizzes.Count;
            if (hasNext)
            {
                // 다음 문제로 전환 — 모달 유지, quiz 내용만 바뀜
                QuizPool.ApplyTo(data, sessionQuizzes[SessionIndex]);
                LastResultText = "";
                SfxManager.PlayDialogue();
            }
            else
            {
                FinalizeSession(data);
            }
        }

        /// <summary>세션 완료 — 통합/모순 단서를 인벤토리에 추가 + 모달 닫기</summary>
        private void FinalizeSession(ClueData data)
        {
            int totalReward = data.clueReward * sessionCorrectCount;
            if (GameSession.Instance != null && sessionSuccessClues.Count > 0)
            {
                // 이 장소의 담당 용의자(relatedRole)가 실제 스파이라면, 일반 단서 대신
                // 알리바이를 깨는 결정적 단서를 수집한다 (보드에서 ⚠ 모순 표시).
                int spyRole = NPCRoster.Instance != null && NPCRoster.Instance.Spy != null
                    && NPCRoster.Instance.Spy.data != null
                    ? (int)NPCRoster.Instance.Spy.data.role : -1;

                if (data.relatedRole > 0 && data.relatedRole == spyRole)
                {
                    GameSession.Instance.AddClue(
                        "▲ 결정적 단서 — 알리바이 모순",
                        GameSession.GetContradictionClue(data.relatedRole),
                        ClueSource.Environment, data.relatedRole, "ALIBI", true);
                }
                else
                {
                    string combinedText = string.Join("\n\n", sessionSuccessClues);
                    string clueTitle = $"[{data.roomName}] {data.objectLabel}";
                    GameSession.Instance.AddClue(clueTitle, combinedText,
                        ClueSource.Environment, data.relatedRole, data.tag);
                }
            }
            LastResultText = $"✓ 보안 교육 완료! {sessionCorrectCount}/{SessionTotal} 정답\n+{totalReward} 단서 획득";
            LastResultTime = Time.time; // 완료 토스트 타이머
            LastWasCorrect = true;
            ActiveClue.MarkResolved();
            ActiveClue = null;
            sessionQuizzes = null;
            sessionSuccessClues.Clear();
            Debug.Log($"[Quiz] {data.id} 세션 완료 ({sessionCorrectCount}/{QuestionsPerSession} 정답, +{totalReward} 단서)");
        }

        /// <summary>
        /// 환경 단서 — 시각적 큐브는 안 만들고 invisible GameObject로만 등록.
        /// NPC 대화 → 자동 트리거 흐름으로 보안 교육 모듈이 뜨므로 환경에 큐브가 떠있을 필요 없음.
        /// ClueObject 인스턴스는 유지해야 NPCInteractable이 FindObjectsByType으로 찾아서 sqc.Open() 호출 가능.
        /// </summary>
        private void SpawnDefaultClues()
        {
            var allClues = GetDefaultClues();
            foreach (var data in allClues)
            {
                var go = new GameObject("Clue_" + data.id);
                go.transform.position = data.worldPos;
                // MeshRenderer/Collider 없음 — 보이지도, 막히지도 않음
                var co = go.AddComponent<ClueObject>();
                co.data = data;
            }
        }

        public static List<ClueData> GetDefaultClues()
        {
            return new List<ClueData>
            {
                new ClueData
                {
                    id = "research_usb",
                    stageRequired = (int)QuestManager.Stage.MeetResearcher,
                    unlockHint = "연구원 만난 후",
                    roomName = "연구실",
                    worldPos = new Vector3(-20f, 0.6f, 14f),
                    objectLabel = "정체불명 USB",
                    color = new Color(0.95f, 0.85f, 0.3f),
                    promptVerb = "조사",
                    relatedRole = 1, tag = "DATA",
                    quizQuestion = "신원 미상의 USB가 발견됐다. 가장 안전한 대응은?",
                    quizOptions = new[]
                    {
                        "내 컴퓨터에 꽂아 내용을 확인한다",
                        "신고 후 보안팀에 전달한다",
                        "그냥 무시한다",
                        "다른 직원에게 준다"
                    },
                    correctIndex = 1,
                    successClue = "USB에 새벽 2시 연구실 서버 백업 데이터가 들어있다. 누군가 빼돌리려 했다.",
                    clueReward = 2
                },
                new ClueData
                {
                    id = "server_log",
                    stageRequired = (int)QuestManager.Stage.MeetNetworkAdmin,
                    unlockHint = "네트워크관리자 만난 후",
                    roomName = "서버실",
                    worldPos = new Vector3(3f, 0.6f, 14f),
                    objectLabel = "서버실 모니터",
                    color = new Color(0.95f, 0.3f, 0.3f),
                    promptVerb = "로그 확인",
                    relatedRole = 2, tag = "NET",
                    quizQuestion = "관리자 권한으로 접근한 비정상 로그를 발견했다. 우선 조치는?",
                    quizOptions = new[]
                    {
                        "무시하고 일을 계속한다",
                        "로그를 삭제해서 정리한다",
                        "로그 보존 후 보안팀에 즉시 보고",
                        "본인이 해당 사용자에게 직접 묻는다"
                    },
                    correctIndex = 2,
                    successClue = "비밀번호 평문 저장 + 비정상 관리자 접근. 내부자 소행 가능성이 높다.",
                    clueReward = 2
                },
                new ClueData
                {
                    id = "data_traffic",
                    stageRequired = (int)QuestManager.Stage.MeetNetworkAdmin,
                    unlockHint = "네트워크관리자 만난 후",
                    roomName = "데이터센터",
                    worldPos = new Vector3(3f, 0.6f, -14f),
                    objectLabel = "트래픽 분석 단말",
                    color = new Color(0.3f, 0.7f, 0.85f),
                    promptVerb = "분석",
                    relatedRole = 2, tag = "NET",
                    quizQuestion = "외부 IP로 대용량 데이터 전송이 감지되었다. 적절한 대응은?",
                    quizOptions = new[]
                    {
                        "전송 완료까지 기다린다",
                        "전송을 즉시 차단하고 로그 분석",
                        "본인이 받은 게 아니니 무시",
                        "동료에게 알리고 퇴근"
                    },
                    correctIndex = 1,
                    successClue = "어제 23:47, 미식별 외부 IP로 1.2GB 전송. 발신 단말은 사내 네트워크 단말.",
                    clueReward = 2
                },
                new ClueData
                {
                    id = "lounge_memo",
                    stageRequired = (int)QuestManager.Stage.RacingMission,
                    unlockHint = "보안 레이싱 클리어 후",
                    roomName = "휴게실",
                    worldPos = new Vector3(-15f, 0.6f, -2f),
                    objectLabel = "휘갈긴 메모",
                    color = new Color(0.95f, 0.95f, 0.95f),
                    promptVerb = "읽기",
                    relatedRole = -1, tag = "LOG",
                    quizQuestion = "쓰레기통에서 비밀번호가 적힌 메모를 발견했다. 처리 방법?",
                    quizOptions = new[]
                    {
                        "적힌 비밀번호로 시스템 접근 시도",
                        "분쇄기로 즉시 파기 + 정책 안내",
                        "SNS에 사진을 올린다",
                        "그냥 다시 버린다"
                    },
                    correctIndex = 1,
                    successClue = "메모에 'adm1n_2026' 같은 관리자 패스워드. 누군가 메모를 보고 무단 접속했다.",
                    clueReward = 2
                },
                new ClueData
                {
                    id = "storage_box",
                    stageRequired = (int)QuestManager.Stage.MeetFacilityManager,
                    unlockHint = "시설관리자 만난 후",
                    roomName = "창고",
                    worldPos = new Vector3(-20f, 0.6f, -14f),
                    objectLabel = "수상한 택배",
                    color = new Color(0.65f, 0.45f, 0.25f),
                    promptVerb = "조사",
                    relatedRole = 3, tag = "COMMS",
                    quizQuestion = "발신자 불명의 익명 택배가 도착했다. 적절한 행동?",
                    quizOptions = new[]
                    {
                        "즉시 개봉한다",
                        "보안팀 신고 + X-ray 검사",
                        "책상 위에 그냥 둔다",
                        "본인이 가져간다"
                    },
                    correctIndex = 1,
                    successClue = "위장 USB 충전기 + 키로거가 들어있다. 명백한 사회공학 공격 시도.",
                    clueReward = 2
                },
                new ClueData
                {
                    id = "cardkey_log",
                    stageRequired = (int)QuestManager.Stage.MeetFacilityManager,
                    unlockHint = "시설관리자 만난 후",
                    roomName = "카드키 구역",
                    worldPos = new Vector3(18f, 0.6f, 2f),
                    objectLabel = "카드키 발급 로그",
                    color = new Color(0.85f, 0.2f, 0.2f),
                    promptVerb = "조회",
                    relatedRole = 3, tag = "BADGE",
                    quizQuestion = "내 카드키가 사용된 적 없는 시간대 출입 기록이 발견됐다. 우선 조치?",
                    quizOptions = new[]
                    {
                        "사용 가능성을 무시한다",
                        "카드 분실/도난 가능성 신고 + 재발급",
                        "비밀번호를 바꾼다 (카드와 무관)",
                        "친구에게 빌려준 것이라 추측"
                    },
                    correctIndex = 1,
                    successClue = "복제 카드키가 새벽 1~3시 보안 구역 출입에 사용됐다.",
                    clueReward = 2
                }
            };
        }
    }
}
