using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ForTheCompany.Core;
using ForTheCompany.Managers;
using ForTheCompany.Systems;

namespace ForTheCompany.Player
{
    [RequireComponent(typeof(NPCActor))]
    public class NPCInteractable : MonoBehaviour, IInteractable
    {
        [Header("Interaction")]
        public float interactRadius = 2.5f;
        public int firstTalkClueReward = 2;
        public int repeatTalkClueReward = 1;

        public bool HasBeenTalkedTo { get; private set; }
        public NPCActor Actor { get; private set; }

        public string DisplayName => Actor != null ? Actor.DisplayName : gameObject.name;

        public Vector3 InteractPosition => transform.position;
        public float InteractRadius => interactRadius;
        // 첫 대화 → 보안교육 모듈로 넘어가는 중엔 재대화 불가 (빈틈에 '차례 아님' 대사가 끼어들어 퀴즈와 겹침)
        public bool CanInteract => !quizFlowPending
            && (GameSession.Instance == null || GameSession.Instance.Outcome == RunOutcome.Ongoing);
        private bool quizFlowPending;
        public string PromptText => HasBeenTalkedTo
            ? $"Space: {DisplayName} 다시 대화"
            : $"Space: {DisplayName}와 대화";

        public string LastResult { get; private set; }

        private void Awake() { Actor = GetComponent<NPCActor>(); }

        void IInteractable.Interact() { LastResult = Talk(); }
        public string Interact() => Talk();

        public string Talk()
        {
            // 스토리 모드: 현재 단계에 맞는 NPC가 아니면 안내만
            var questCheck = QuestManager.Instance;
            if (questCheck != null && Actor != null && Actor.data != null
                && !questCheck.IsExpectedNPC(Actor.data.role))
            {
                ShowOutOfTurnGuidance(questCheck);
                return "";
            }

            FacePlayer();
            int reward = HasBeenTalkedTo ? repeatTalkClueReward : firstTalkClueReward;

            if (!HasBeenTalkedTo)
                return StartFirstTalk(reward);
            else
                return StartRepeatTalk(reward);
        }

        // ─────────────────── 첫 대화 (다중 라인 + 선택지) ───────────────────

        private string StartFirstTalk(int reward)
        {
            HasBeenTalkedTo = true;
            int my = Actor != null && Actor.data != null ? (int)Actor.data.role : -1;
            int spy = ResolveSpyRole();
            bool selfIsSpy = Actor != null && Actor.isSpy;

            string[] lines = BuildFirstTalkLines(my, spy, selfIsSpy);
            // 알리바이 진술 한 줄 추가 — 인벤토리 보드 추리의 기준이 된다.
            // 무고한 NPC는 진실, 스파이는 거짓(결정적 단서와 시간대가 어긋남).
            string alibi = GameSession.GetAlibi(my);
            if (!string.IsNullOrEmpty(alibi))
            {
                var withAlibi = new string[lines.Length + 1];
                System.Array.Copy(lines, withAlibi, lines.Length);
                withAlibi[lines.Length] = selfIsSpy
                    ? $"...어제 제 행적이요? {alibi}"
                    : $"아, 어제 제 행적은 — {alibi}";
                lines = withAlibi;
            }
            DialogueChoice[] choices = BuildFirstTalkChoices(my, spy, selfIsSpy);
            // 트랜지션 라인은 선택지 응답 이후에 별도 짧은 대화로 띄움
            // (StartTransitionAndOpenQuiz 코루틴 처리)
            string inventoryText = string.Join("\n", lines);

            var ds = DialogueSystem.Instance;
            if (ds != null)
            {
                int capturedReward = reward;
                ds.StartDialogue(DisplayName, lines, transform,
                    () => ApplyTalkRewards(capturedReward, true, inventoryText),
                    choices);
                Debug.Log($"[Interact] {DisplayName}: 첫 대화 (라인 {lines.Length}, 선택지 {choices?.Length ?? 0})");
                return "";
            }

            ApplyTalkRewards(reward, true, inventoryText);
            return $"{DisplayName}: {lines[0]}";
        }

        // ─────────────────── 반복 대화 (짧은 한 줄) ───────────────────

        private string StartRepeatTalk(int reward)
        {
            int my = Actor != null && Actor.data != null ? (int)Actor.data.role : -1;
            int spy = ResolveSpyRole();
            string msg = BuildRepeatLine(my, spy);

            var ds = DialogueSystem.Instance;
            if (ds != null)
            {
                int capturedReward = reward;
                ds.StartDialogue(DisplayName, msg, transform,
                    () => ApplyTalkRewards(capturedReward, false, msg));
                return "";
            }
            ApplyTalkRewards(reward, false, msg);
            return $"{DisplayName}: {msg}";
        }

        // ─────────────────── 보상/단계 진행 적용 ───────────────────

        private void ApplyTalkRewards(int reward, bool firstTime, string lineText)
        {
            var session = GameSession.Instance;
            if (session != null)
            {
                session.totalClues += reward;
                session.LastEncounterRewardClues = reward;
                if (firstTime && !string.IsNullOrEmpty(lineText))
                {
                    int role = Actor != null && Actor.data != null ? (int)Actor.data.role : -1;
                    session.AddClue($"{DisplayName}의 첫 증언", lineText, ClueSource.NPC, role, "INTERVIEW");
                }
            }

            // (의심도 자동 카운터 폐기 — 범인 식별은 알리바이 모순 단서로 직접 추리한다.
            //  대화만으로 스파이가 자동 지목되던 문제 제거.)

            var quest = QuestManager.Instance;
            if (quest != null && Actor != null && Actor.data != null)
                quest.TryAdvanceByRole(Actor.data.role);

            // 첫 대화(선택지+응답) 종료 후 → 트랜지션 라인 별도 짧은 대화 → 보안교육 모듈
            if (firstTime)
                StartCoroutine(StartTransitionAndOpenQuiz());
        }

        /// <summary>
        /// 첫 대화(선택지 응답 포함)가 모두 끝난 후 진행되는 흐름.
        /// 트랜지션 라인을 별도 짧은 대화로 띄우고 → 그 대화 끝나면 보안교육 모듈 오픈.
        /// 사용자가 선택지를 고르고 응답을 들은 *후* 자연스럽게 마지막에 트랜지션 멘트가 옴.
        /// </summary>
        private IEnumerator StartTransitionAndOpenQuiz()
        {
            string clueId = GetRelatedClueId();
            if (string.IsNullOrEmpty(clueId)) yield break;
            quizFlowPending = true;

            // 첫 대화창 완전히 닫힘 대기
            yield return new WaitForSeconds(0.4f);

            // 다른 창이 떠 있으면 닫힐 때까지 대기 — 여기서 중단하면 이 방의 보안교육(결정적 단서 포함)을 다시 열 방법이 없다
            yield return WaitUntilNoBlockingModal();
            if (IsRunOver()) { quizFlowPending = false; yield break; }

            string[] transitionLines = GetTransitionLines(Actor != null && Actor.isSpy);
            var ds = DialogueSystem.Instance;

            if (ds != null)
            {
                // 트랜지션 라인 (보안교육 안내 + 다음 행동 안내 2줄) → 끝나면 모달 오픈
                ds.StartDialogue(DisplayName, transitionLines, transform,
                    () => StartCoroutine(OpenQuizAfterShortDelay(clueId)));
            }
            else
            {
                // DialogueSystem 없으면 바로 모달
                yield return OpenQuizAfterShortDelay(clueId);
            }
        }

        private IEnumerator OpenQuizAfterShortDelay(string clueId)
        {
            yield return new WaitForSeconds(0.5f);

            yield return WaitUntilNoBlockingModal();
            quizFlowPending = false;
            if (IsRunOver()) yield break;

            var sqc = SecurityQuizController.Instance;
            if (sqc == null) yield break;

            var allClues = FindObjectsByType<ClueObject>(FindObjectsSortMode.None);
            foreach (var c in allClues)
            {
                if (c == null || c.data == null) continue;
                if (c.data.id != clueId) continue;
                if (c.Resolved) yield break;
                if (!c.IsUnlocked) yield break;
                sqc.Open(c);
                Debug.Log($"[Interact] {DisplayName} 트랜지션 종료 → '{c.data.objectLabel}' 보안 교육 모듈 오픈");
                yield break;
            }
        }

        private IEnumerator WaitUntilNoBlockingModal()
        {
            while (IsAnyBlockingModalOpen() && !IsRunOver())
                yield return null;
        }

        private static bool IsRunOver()
        {
            var s = GameSession.Instance;
            return s != null && s.Outcome != RunOutcome.Ongoing;
        }

        private bool IsAnyBlockingModalOpen()
        {
            var sqc = SecurityQuizController.Instance;
            if (sqc != null && sqc.IsOpen) return true;
            var rmc = RacingMissionController.Instance;
            if (rmc != null && rmc.IsOpen) return true;
            var ds = DialogueSystem.Instance;
            if (ds != null && ds.IsActive) return true;
            if (FacilityHUD.IsInventoryOpen) return true;
            var pm = PauseMenu.Instance;
            if (pm != null && pm.IsOpen) return true;
            var partner = AccusationPartner.Instance;
            if (partner != null && partner.IsMenuOpen) return true;
            return false;
        }

        /// <summary>NPC 직업별 트랜지션 안내 멘트 (2줄: 보안교육 + 다음 행동 안내) — 대화 → 퀴즈 → 다음 단계 흐름을 자연스럽게.
        /// 스파이는 회피적 톤, 무고한 NPC는 협조적 톤.</summary>
        private string[] GetTransitionLines(bool selfIsSpy)
        {
            if (Actor == null || Actor.data == null)
                return new[] { "잠시만요, 같이 봐주실 게 있어요." };

            int role = (int)Actor.data.role;
            string transition, nextHint;

            if (selfIsSpy)
            {
                // 스파이 — 회피·모호한 톤
                switch (role)
                {
                    case 1: // 연구원 스파이
                        transition = "보안 교육 자료에도 비슷한 케이스 있긴 한데... 굳이 보시려면 띄워드릴게요.";
                        nextHint = "끝나시면 중앙복도의 경비원에게 가셔서 다음 안내 받으시면 될 겁니다.";
                        break;
                    case 2: // 네트워크관리자 스파이
                        transition = "음... 보안 교육 모듈에도 있긴 합니다. 형식적인 거지만 보시려면...";
                        nextHint = "그 다음엔 전력실의 시설관리자에게 가시면 됩니다. 카드키 발급 권한이 거기 있으니까요.";
                        break;
                    case 3: // 시설관리자 스파이
                        transition = "보안 교육 자료요? 굳이 안 보셔도 되는데... 그래도 띄워는 드릴게요.";
                        nextHint = "끝나시면 보안통제실의 동료 수사관에게 가시면 됩니다. 거기서 지목 절차 진행해주실 거예요.";
                        break;
                    default:
                        return new[] { "잠시만요... 자료 띄워드릴게요." };
                }
                return new[] { transition, nextHint };
            }

            // 무고한 NPC — 협조적, 적극적
            switch (role)
            {
                case 1: // 연구원
                    transition = "잠깐, 보안 교육 모듈에 케이스로 등록돼 있어요. 같이 한번 풀어보시죠.";
                    nextHint = "끝나시면 중앙복도의 경비원에게 돌아가서 중간 보고하세요. 다음 단계를 안내해주실 겁니다.";
                    break;
                case 2: // 네트워크관리자
                    transition = "이 사례는 보안 교육에도 들어가 있어요. 모니터에 띄워드릴게요.";
                    nextHint = "끝나시면 전력실의 시설관리자에게 가보세요. 카드키 발급 기록을 확인해야 합니다.";
                    break;
                case 3: // 시설관리자
                    transition = "출입 기록 분석은 보안 교육 자료로 같이 보시는 게 좋아요. 띄워드릴게요.";
                    nextHint = "끝나시면 보안통제실의 동료 수사관에게 가서 산업스파이를 지목하세요. 마지막 단계입니다.";
                    break;
                default:
                    return new[] { "잠시만요, 같이 봐주실 자료가 있어요." };
            }
            return new[] { transition, nextHint };
        }

        /// <summary>NPC 직업별로 자동 트리거할 환경 단서 ID 매핑</summary>
        private string GetRelatedClueId()
        {
            if (Actor == null || Actor.data == null) return null;
            switch ((int)Actor.data.role)
            {
                case 1: return "research_usb"; // 연구원 → 연구실 USB
                case 2: return "server_log";   // 네트워크관리자 → 서버실 모니터
                case 3: return "cardkey_log";  // 시설관리자 → 카드키 발급 로그
                default: return null;
            }
        }

        // ─────────────────── 차례 안내 ───────────────────

        private void ShowOutOfTurnGuidance(QuestManager quest)
        {
            FacePlayer();
            string msg =
                "잠시만요, 조사관님. 지금은 저와 이야기할 차례가 아닌 것 같습니다.\n\n" +
                $"현재 목표: {quest.CurrentObjective}\n{quest.CurrentLocationHint}";

            var ds = DialogueSystem.Instance;
            if (ds != null)
                ds.StartDialogue(DisplayName, msg, transform, null);
            else
                LastResult = $"{DisplayName}: {msg}";
        }

        private int ResolveSpyRole()
        {
            var spy = NPCRoster.Instance != null ? NPCRoster.Instance.Spy : null;
            if (spy == null || spy.data == null) return -1;
            return (int)spy.data.role;
        }

        private void FacePlayer()
        {
            var p = PlayerInteractor.Instance;
            if (p == null) return;
            Vector3 toPlayer = p.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f) return;
            // NPC가 플레이어 향함 + 플레이어도 NPC 향함 (서로 마주보기)
            transform.rotation = Quaternion.LookRotation(toPlayer.normalized);
            p.transform.rotation = Quaternion.LookRotation(-toPlayer.normalized);
        }

        // ═══════════════════ 대화 콘텐츠 ═══════════════════
        // RoleType: Security=0, Researcher=1, NetworkAdmin=2, FacilityManager=3
        // 캐릭터 톤: 연구원=학자, 네트워크관리자=기술자, 시설관리자=현장

        private string[] BuildFirstTalkLines(int my, int spy, bool selfIsSpy)
        {
            // 자기가 스파이인 경우 — 회피, 모호, 변명
            if (selfIsSpy)
            {
                switch (my)
                {
                    case 1: // 연구원 자기 스파이
                        return new[] {
                            "보안조사관님이시군요. 연구실은... 평소대로입니다.",
                            "USB? 외부 공동연구 자료 받느라 종종 쓰긴 합니다. 다 정식 절차예요.",
                            "어제 새벽요? 마감 직전이라 늦게까지 있었지만, 평범한 야근이었어요.",
                            "제 책상 위 USB 보시려고요? 특별한 거 없을 거예요. 보통 자료뿐이라..."
                        };
                    case 2: // 네트워크관리자 자기 스파이
                        return new[] {
                            "로그요? 다 깨끗합니다. 평소와 다를 게 없어요.",
                            "외부 트래픽도 정상 범위 안이에요. 모니터링 자료 보여드릴 수 있습니다.",
                            "굳이 의심받을 만한 일이 없는데... 왜 저한테 물어보시는 거죠?",
                            "서버실 모니터 보러 가시려고요? 다 정상 로그일 거예요. 굳이 안 보셔도..."
                        };
                    case 3: // 시설관리자 자기 스파이
                        return new[] {
                            "어... 저요? 시설은 평소대로 운영 중입니다. 별 이상 없어요.",
                            "CCTV 점검은 정기적으로 하는 거예요. 시간 좀 걸렸을 수 있지만 정상 업무입니다.",
                            "카드키 발급도 절차대로 처리했어요. 의심받을 만한 일이 뭐가 있는지...",
                            "카드키 발급 단말 보시려고요? 다 정상 절차예요. 따로 보실 필요 없으실 텐데..."
                        };
                }
            }

            // 무고한 NPC — 진짜 스파이를 다양한 방식으로 지목
            switch (my)
            {
                case 1: // 연구원이 말함
                    switch (spy)
                    {
                        case 2: // 스파이=네트워크관리자
                            return new[] {
                                "네... 보안 사건 얘기는 들었어요. 충격이죠.",
                                "어제 네트워크관리자분이 USB 슬롯 점검한다며 오래 머무셨어요. 평소엔 빨리 끝내시는데.",
                                "그리고 새벽에 외부로 큰 데이터 전송이 있었다는 얘기도 있어요. 누군가 의도적으로...",
                                "아! 그리고 제 책상 위 USB도 확인해보세요. 거기 백업 기록 남아있어요."
                            };
                        case 3: // 스파이=시설관리자
                            return new[] {
                                "어제 좀 이상한 일이 있었어요. CCTV가 점검한다고 잠시 꺼졌는데...",
                                "시설관리자분이 직접 점검한다고 했는데, 그 시간이 평소보다 너무 길었어요. 30분 이상.",
                                "보통 5분이면 끝나는 작업이거든요. 그 사이에 누가 뭘 했을지...",
                                "그리고 제 책상 위 USB도 살펴봐 주세요. 새벽 작업 흔적이 거기 있을지도..."
                            };
                    }
                    break;

                case 2: // 네트워크관리자가 말함
                    switch (spy)
                    {
                        case 1: // 스파이=연구원
                            return new[] {
                                "트래픽 로그 보러 오셨죠? 어제 새벽 2시쯤 이상한 거 잡혔어요.",
                                "연구실 서버에서 관리자 권한으로 비정상 접근이 있었거든요. 평소 안 쓰는 권한이에요.",
                                "접근 흔적이 연구원 단말이랑 매칭돼요. 본인이 아니라면 누가 했을지 모르겠네요.",
                                "서버실 모니터에 어제 로그 띄워뒀어요. 직접 보시는 게 빠를 거예요."
                            };
                        case 3: // 스파이=시설관리자
                            return new[] {
                                "이상한 거 하나 잡혔어요. 시설관리자 단말에서 외부 서버 IP로 트래픽이 나갔어요.",
                                "권한상 그분은 외부 통신 안 되는데, 우회한 흔적이 있어요. CCTV 점검 시간이랑 정확히 겹쳐요.",
                                "보안 카드키 발급 권한 가진 분이라 시스템 접근도 쉬웠을 거예요.",
                                "서버실 모니터 로그도 한번 살펴봐 주세요. 외부 IP 흔적이 거기 잡혀요."
                            };
                    }
                    break;

                case 3: // 시설관리자가 말함
                    switch (spy)
                    {
                        case 1: // 스파이=연구원
                            return new[] {
                                "카드키 출입 기록이요? 어제 좀 이상한 패턴이 있긴 했어요.",
                                "연구원분이 새벽 2시쯤 카드키로 연구실 들어가셨는데, 새벽 3시까지 안 나오셨어요.",
                                "보통 야근해도 그 시간엔 다 나가시는데, 그날만 유독... 한 번 더 봐주세요.",
                                "전력실 옆 카드키 발급 단말 확인해보세요. 새벽 발급 이력이 다 남아있어요."
                            };
                        case 2: // 스파이=네트워크관리자
                            return new[] {
                                "네트워크관리자분 출입 기록을 봤는데 좀 특이해요.",
                                "서버실에 평소엔 점검 일정에만 들어가시는데, 최근 며칠은 매일 새벽에 들어가셨어요.",
                                "그것도 짧게 5분이 아니라 30분 이상씩. 안에서 뭘 하셨는지는 CCTV로도 안 잡혔어요.",
                                "카드키 발급 단말도 한번 봐주세요. 누가 자주 출입했는지 다 기록돼요."
                            };
                    }
                    break;
            }

            return new[] { "별다른 단서는 없습니다." };
        }

        private DialogueChoice[] BuildFirstTalkChoices(int my, int spy, bool selfIsSpy)
        {
            // 자기가 스파이일 때 — 회피 응답으로 변경
            if (selfIsSpy)
            {
                switch (my)
                {
                    case 1:
                        return new[] {
                            new DialogueChoice("어제 정확히 몇 시까지 계셨나요?",
                                "음... 2시 반? 3시? 정확히는 기억이 안 나네요."),
                            new DialogueChoice("외부 USB로 받은 자료가 뭐였나요?",
                                "기밀이라 자세히는 말씀드리기 어렵네요. 일반 학술자료요."),
                            new DialogueChoice("협조 감사합니다.",
                                "별 도움 못 드려 죄송하네요.")
                        };
                    case 2:
                        return new[] {
                            new DialogueChoice("어제 서버실에 평소보다 오래 계셨다던데?",
                                "정기 점검이었습니다. 일정 잡혀있던 거예요."),
                            new DialogueChoice("사용하시는 USB 종류가 뭐예요?",
                                "회사 표준 USB만 씁니다. 다른 건 안 가져옵니다."),
                            new DialogueChoice("협조 감사합니다.",
                                "네... 잘 마무리되길 바랍니다.")
                        };
                    case 3:
                        return new[] {
                            new DialogueChoice("어제 CCTV 정확히 몇 분 끄셨어요?",
                                "정확히는 기억이 안 나네요. 일 처리하느라 바빠서요."),
                            new DialogueChoice("발급한 카드키 목록 보여주실 수 있나요?",
                                "공식 요청 거치면 가능합니다. 지금 당장은 어렵네요."),
                            new DialogueChoice("협조해주셔서 감사합니다.",
                                "별 도움 못 드려 죄송하네요.")
                        };
                }
            }

            // 무고한 NPC — 캐릭터별 + 스파이별 구체적 선택지
            switch (my)
            {
                case 1: // 연구원 (학자 톤)
                    switch (spy)
                    {
                        case 2:
                            return new[] {
                                new DialogueChoice("그분이 평소와 달랐던 점이 더 있었나요?",
                                    "최근에 자꾸 야근하시더라고요. 전엔 칼퇴하셨는데."),
                                new DialogueChoice("USB 슬롯에 뭔가 꽂혀있던 거 보셨어요?",
                                    "네, 검은색 작은 USB였어요. 회사 USB는 파란색인데요."),
                                new DialogueChoice("감사합니다, 더 조사해보겠습니다.",
                                    "조심하세요. 디지털 흔적이 잘 안 남는 분이라...")
                            };
                        case 3:
                            return new[] {
                                new DialogueChoice("정확한 시간 기억나세요?",
                                    "새벽 2시 15분부터 47분까지. 단말기 시계로 확인했어요."),
                                new DialogueChoice("시설관리자분이 평소와 다르셨나요?",
                                    "최근 예민해 보이셨어요. 카드키 발급도 막 처리하셨고요."),
                                new DialogueChoice("좋은 정보 감사합니다.",
                                    "꼭 잡으셨으면 합니다.")
                            };
                    }
                    break;

                case 2: // 네트워크관리자 (기술자 톤)
                    switch (spy)
                    {
                        case 1:
                            return new[] {
                                new DialogueChoice("비밀번호가 평문으로 남았나요?",
                                    "네, 그게 더 문제예요. 누군가 빠르게 일을 처리한 흔적이죠."),
                                new DialogueChoice("다른 시간대에도 비슷한 접근이 있었어요?",
                                    "지난주에도 한 번 있었어요. 점점 대담해지는 패턴이에요."),
                                new DialogueChoice("분석 결과 정리해 주세요.",
                                    "보고서 작성해서 보안팀에 올리겠습니다.")
                            };
                        case 3:
                            return new[] {
                                new DialogueChoice("외부 IP가 어디였나요?",
                                    "추적했더니 해외 프록시 거쳐서 들어왔어요. 일반 사용자는 못 만지는 루트예요."),
                                new DialogueChoice("그분이 평소 그런 기술 다루시던가요?",
                                    "원래 안 다루시는데, 최근 IT 매뉴얼 자주 보시는 거 봤어요."),
                                new DialogueChoice("조사 잘 부탁드립니다.",
                                    "다음에 또 잡히면 바로 알려드릴게요.")
                            };
                    }
                    break;

                case 3: // 시설관리자 (현장 톤)
                    switch (spy)
                    {
                        case 1:
                            return new[] {
                                new DialogueChoice("정확한 시간 다시 알려주세요.",
                                    "01시 58분 입장, 03시 14분 퇴장. 1시간 16분이에요."),
                                new DialogueChoice("그분이 자주 야근하시나요?",
                                    "전엔 거의 안 하셨어요. 최근 한 달 사이에 늘었어요."),
                                new DialogueChoice("감사합니다.",
                                    "도움 됐으면 합니다.")
                            };
                        case 2:
                            return new[] {
                                new DialogueChoice("안에서 뭐 하셨는지 추측되시나요?",
                                    "USB 슬롯에 뭔가 꽂거나, 데이터 옮기거나... 추측일 뿐이지만요."),
                                new DialogueChoice("다른 직원들도 그분 의심하나요?",
                                    "직접 말은 안 하시지만, 다들 좀 거리 두시는 분위기예요."),
                                new DialogueChoice("더 봐주세요.",
                                    "추가로 뭐 발견하면 바로 알려드릴게요.")
                            };
                    }
                    break;
            }

            // 폴백 — 일반 선택지
            return new[] {
                new DialogueChoice("더 자세히 알려주세요.", "딱히 더 드릴 정보는 없네요."),
                new DialogueChoice("감사합니다.", "도움이 됐길 바랍니다.")
            };
        }

        // 반복 대화는 짧고 단순
        private string BuildRepeatLine(int my, int spy)
        {
            // 기존 ResolveLine repeat 케이스 유지 (간략화)
            if (Actor != null && Actor.isSpy)
            {
                switch (my)
                {
                    case 1: return "정말 별 일 없어요. 평소처럼 일하고 있을 뿐이에요.";
                    case 2: return "이미 다 말씀드렸어요. 더 드릴 정보가 없습니다.";
                    case 3: return "이미 다 말씀드린 것 같은데요. 정말 별 일 없습니다.";
                }
            }
            switch (spy)
            {
                case 1: switch (my) {
                        case 2: return "접근 로그를 다시 봤더니 평문 비밀번호까지 남아있더라구요. 위험합니다.";
                        case 3: return "연구원이 새벽 3시까지 연구실에서 안 나왔어요. 야근치고는 너무 길죠.";
                    } break;
                case 2: switch (my) {
                        case 1: return "사실 어제밤 제 USB 슬롯에 뭔가 꽂혀있던 것 같아요.";
                        case 3: return "네트워크관리자 단말에서 외부 서버 IP로 트래픽이 나간 흔적을 봤어요.";
                    } break;
                case 3: switch (my) {
                        case 1: return "카메라 끄는 동안 시설관리자가 어디 있었는지 아무도 못 봤어요.";
                        case 2: return "시설관리자 카드키로 새벽에 서버실 출입한 로그가 있어요. 권한 밖인데도요.";
                    } break;
            }
            return "더 이상 드릴 정보는 없습니다.";
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
    }
}
