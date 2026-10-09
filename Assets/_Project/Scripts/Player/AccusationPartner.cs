using UnityEngine;
using UnityEngine.SceneManagement;
using ForTheCompany.Core;
using ForTheCompany.Managers;
using ForTheCompany.Systems;

namespace ForTheCompany.Player
{
    /// <summary>
    /// 보안통제실 AI로봇 한세 NPC.
    /// 평소엔 일반 대화, Accusation 단계 + 단서 충분(>=3)하면 산업스파이 지목 메뉴 활성.
    /// 보안통제실 내부에서 어슬렁(patrol). Kenney character-g 모델 사용.
    /// 기존 AccusationConsole 큐브 대체 — 같은 GameObject에 자식 모델 + 이 컴포넌트.
    /// </summary>
    public class AccusationPartner : MonoBehaviour, IInteractable
    {
        public static AccusationPartner Instance { get; private set; }

        [Header("Identity")]
        public string displayName = "AI로봇 한세";

        [Header("Interaction")]
        public float interactRadius = 2.5f;
        public int minimumClues = 3;

        [Header("Patrol")]
        public Vector3 patrolCenter = new Vector3(16f, 1.2f, 11f);
        public float patrolRadius = 2.5f;
        public float patrolSpeed = 1.2f;
        public float waitTimeAtPoint = 1.5f;

        public string LastResult { get; private set; }
        public bool IsMenuOpen { get; private set; }
        // 지목 직전 최종 대화를 한 번이라도 봤는지 — 봤으면 다음 인터랙트 시 바로 메뉴
        private bool hasShownFinalDialogue;

        // IInteractable
        public Vector3 InteractPosition => transform.position;
        public float InteractRadius => interactRadius;
        public bool CanInteract => GameSession.Instance == null
            || GameSession.Instance.Outcome == RunOutcome.Ongoing;

        public string PromptText
        {
            get
            {
                var s = GameSession.Instance;
                int c = s != null ? s.totalClues : 0;
                if (!IsStoryReady())
                    return $"Space: {displayName}과 대화";
                if (c < minimumClues)
                    return $"Space: {displayName} — 단서 {c}/{minimumClues} (지목 불가)";
                return IsMenuOpen ? "ESC: 닫기" : "Space: 산업스파이 지목 절차 시작";
            }
        }

        private bool IsStoryReady()
        {
            var quest = QuestManager.Instance;
            if (quest == null) return false;
            return quest.CurrentStage == QuestManager.Stage.Accusation;
        }

        // Patrol state
        private Vector3 currentTarget;
        private float waitUntil;
        private bool waiting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            EnsureSpawned();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureSpawned();

        /// <summary>
        /// 씬에 직접 배치된 인스턴스가 있으면 그것 유지 (Editor 메뉴로 character-g 모델 부착 가능).
        /// 없으면 빈 GameObject로 fallback spawn.
        /// </summary>
        private static void EnsureSpawned()
        {
            if (SceneManager.GetActiveScene().name != "FacilityScene") return;
            if (FindFirstObjectByType<AccusationPartner>() != null) return;
            var go = new GameObject("AccusationPartner");
            go.transform.position = new Vector3(16f, 1.2f, 11f);
            go.AddComponent<AccusationPartner>();
            Debug.Log("[AccusationPartner] 보안통제실에 동적 spawn (모델 없음 — Editor 메뉴로 character-g 부착 필요)");
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            ChoosePatrolTarget();
        }

        private void Update()
        {
            // 다른 모달/대화 떠있으면 patrol 정지
            if (IsBlockedForPatrol()) return;

            if (waiting)
            {
                if (Time.time >= waitUntil)
                {
                    waiting = false;
                    ChoosePatrolTarget();
                }
                return;
            }

            Vector3 toTarget = currentTarget - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist < 0.15f)
            {
                waiting = true;
                waitUntil = Time.time + waitTimeAtPoint;
                return;
            }

            Vector3 dir = toTarget.normalized;
            transform.position += dir * patrolSpeed * Time.deltaTime;

            // 부드러운 회전 (CharacterBobbing이 이 회전 기반으로 걷기 표현)
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 8f * Time.deltaTime);
        }

        private void ChoosePatrolTarget()
        {
            Vector2 r = Random.insideUnitCircle * patrolRadius;
            currentTarget = patrolCenter + new Vector3(r.x, 0f, r.y);
            currentTarget.y = transform.position.y; // 높이 유지
        }

        private bool IsBlockedForPatrol()
        {
            var ds = DialogueSystem.Instance;
            if (ds != null && ds.IsActive) return true;
            if (IsMenuOpen) return true;
            var sqc = SecurityQuizController.Instance;
            if (sqc != null && sqc.IsOpen) return true;
            return false;
        }

        public void Interact()
        {
            FacePlayer();

            // 지목 단계 + 단서 충분 → 최초 1회는 최종 대화 → 그 후 메뉴 자동
            if (IsStoryReady())
            {
                var s = GameSession.Instance;
                if (s != null && s.totalClues >= minimumClues)
                {
                    if (!hasShownFinalDialogue)
                    {
                        hasShownFinalDialogue = true;
                        StartFinalDialogue();
                        return;
                    }
                    // 이미 최종 대화 본 적 있음 → 메뉴 토글
                    IsMenuOpen = !IsMenuOpen;
                    return;
                }
            }

            // 일반 대화 (지목 불가 상태) 또는 동료 잡담
            StartGeneralDialogue();
        }

        /// <summary>지목 직전 최종 대화 — 끝나면 자동으로 지목 메뉴 오픈</summary>
        private void StartFinalDialogue()
        {
            var ds = DialogueSystem.Instance;
            if (ds == null) { IsMenuOpen = true; return; }

            var s = GameSession.Instance;
            int c = s != null ? s.totalClues : 0;
            string spyName = NPCRoster.Instance != null && NPCRoster.Instance.Spy != null
                ? NPCRoster.Instance.Spy.DisplayName
                : "?";

            var lines = new[]
            {
                $"조사관님, 돌아오셨군요. 단서를 {c}건이나 모으셨네요.",
                "제가 수집된 정보를 다시 분석해봤습니다. 패턴이 명확해지고 있어요.",
                "수사 보드(I 키)를 열어 세 용의자의 알리바이를 수집한 증거와 대조해 보세요.",
                "딱 한 명의 알리바이만 증거와 시간대가 어긋납니다. 그 사람이 범인이에요.",
                "...하지만 마지막 판단은 인간 조사관이 직접 내려야 한다는 게 회사 규정이에요.",
                "잘못된 지목은 되돌릴 수 없습니다. 한 번에 끝내야 합니다.",
                "준비되셨으면 지목 절차 시작하겠습니다. 화면에 용의자 목록이 표시됩니다."
            };

            ds.StartDialogue(displayName, lines, transform, () =>
            {
                // 대화 종료 → 자동으로 지목 메뉴 오픈
                IsMenuOpen = true;
            });
        }

        public void Close() => IsMenuOpen = false;

        public void Accuse(int npcIndex)
        {
            var s = GameSession.Instance;
            var roster = NPCRoster.Instance;
            if (s == null || roster == null) return;
            if (s.Outcome != RunOutcome.Ongoing) return;
            if (npcIndex < 0 || npcIndex >= roster.npcs.Count) return;
            IsMenuOpen = false;

            var accused = roster.npcs[npcIndex];
            string chosenName = accused != null ? accused.DisplayName : "?";

            if (accused != null && accused.isSpy)
            {
                s.DeclareWin($"정확히 지목 — {chosenName}가 산업스파이였다.");
            }
            else
            {
                string actual = roster.Spy != null ? roster.Spy.DisplayName : "?";
                s.DeclareLose($"오인 — {chosenName}는 무고했다. 실제 스파이는 {actual}.");
            }
            Debug.Log($"[Accuse] chose={chosenName}, actualSpy={(roster.Spy != null ? roster.Spy.DisplayName : "?")}, result={s.Outcome}");
        }

        private void StartGeneralDialogue()
        {
            var ds = DialogueSystem.Instance;
            if (ds == null) return;

            var quest = QuestManager.Instance;
            var s = GameSession.Instance;
            int c = s != null ? s.totalClues : 0;
            int curStage = quest != null ? (int)quest.CurrentStage : 0;

            string[] lines;

            if (quest != null && quest.CurrentStage == QuestManager.Stage.Accusation
                && c >= minimumClues)
            {
                // 이미 지목 가능한 상태 (이론상 안 옴 — Interact가 메뉴 먼저 띄움)
                lines = new[]
                {
                    "준비되시면 말씀해주세요. 지목 절차 바로 시작합니다."
                };
            }
            else if (quest != null && quest.CurrentStage == QuestManager.Stage.Accusation)
            {
                // 지목 단계인데 단서 부족
                lines = new[]
                {
                    "조사관님, 단서는 충분히 모으셨나요?",
                    $"현재 수집된 단서가 {c}건이군요. 최소 {minimumClues}건은 있어야 정확한 지목이 가능합니다.",
                    "용의자 대화와 보안 교육으로 단서를 더 모으고 오세요. 잘못된 지목은 되돌릴 수 없으니까요."
                };
            }
            else if (curStage <= (int)QuestManager.Stage.MeetResearcher)
            {
                // 게임 초반 — 자기소개 (AI로봇)
                lines = new[]
                {
                    "안녕하세요, 조사관님. 저는 AI로봇 한세입니다.",
                    "이번 사건 분석 보조로 배정됐어요. 보안통제실은 제가 지키고 있을게요.",
                    "단서 수집 패턴 분석과 최종 지목 절차 진행이 제 주요 업무입니다.",
                    "용의자 셋 다 만나보시고 단서 충분히 모으신 다음 저에게 다시 와주세요. 산업스파이 지목 절차는 제가 직접 진행해드립니다."
                };
            }
            else
            {
                // 진행 중 — AI 분석 톤
                lines = new[]
                {
                    "조사관님, 수사 잘 진행되고 계신가요?",
                    $"현재까지 단서 {c}건 — 분석 결과 패턴이 점점 좁혀지고 있습니다.",
                    "나머지 용의자들도 다 만나 보안 교육까지 마치신 다음 여기로 다시 오세요. 그때 종합 분석 결과 보여드릴게요."
                };
            }

            ds.StartDialogue(displayName, lines, transform, null);
        }

        private void FacePlayer()
        {
            var p = PlayerInteractor.Instance;
            if (p == null) return;
            Vector3 toPlayer = p.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.LookRotation(toPlayer.normalized);
            p.transform.rotation = Quaternion.LookRotation(-toPlayer.normalized);
        }
    }
}
