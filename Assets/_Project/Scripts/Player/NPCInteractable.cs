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

            string[] lines = BuildFirstTalkLines(my);
            // 알리바이 진술 한 줄 추가 — 인벤토리 보드 추리의 기준이 된다.
            // 무고한 NPC는 진실, 스파이는 거짓(장소 기록과 시간대가 어긋남). 말투는 같게 — 말투로 범인이 드러나지 않게.
            string alibi = GameSession.GetAlibi(my);
            if (!string.IsNullOrEmpty(alibi))
            {
                var withAlibi = new string[lines.Length + 1];
                System.Array.Copy(lines, withAlibi, lines.Length);
                withAlibi[lines.Length] = $"아, 어제 제 행적은요 — {alibi}";
                lines = withAlibi;
            }
            DialogueChoice[] choices = BuildFirstTalkChoices(my);
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
            string msg = BuildRepeatLine(my);

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

            // 다른 창이 떠 있으면 닫힐 때까지 대기 — 여기서 중단하면 이 방의 보안교육(장소 기록 포함)을 다시 열 방법이 없다
            yield return WaitUntilNoBlockingModal();
            if (IsRunOver()) { quizFlowPending = false; yield break; }

            string[] transitionLines = GetTransitionLines();
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
        /// 스파이 여부와 무관하게 같은 말투 (말투로 범인이 드러나지 않게).</summary>
        private string[] GetTransitionLines()
        {
            if (Actor == null || Actor.data == null)
                return new[] { "잠시만요, 같이 봐주실 게 있어요." };

            int role = (int)Actor.data.role;
            string transition, nextHint;

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
        //
        // 진술은 누가 스파이든 똑같다 — 대화로 범인이 드러나지 않게 하고(학습자가 직접 판단),
        // 판단은 알리바이와 장소 기록(GameSession.GetRoomRecord)의 시각을 대조해서 내리게 한다.
        // 모든 진술은 세 사람의 알리바이·기록 어느 것과도 모순되지 않아야 한다 (누구든 무고할 수 있으므로).
        // 세 사람이 서로를 한 번씩 가볍게 의심한다: 연구원→시설관리자, 네트워크관리자→연구원, 시설관리자→네트워크관리자.

        private static string[] BuildFirstTalkLines(int my)
        {
            switch (my)
            {
                case 1: // 연구원
                    return new[] {
                        "네... 보안 사건 얘기는 들었어요. 설계도가 밖으로 나갔다니 아직도 믿기지 않네요.",
                        "어제 저녁엔 다들 평소처럼 일했어요. 밤 9시쯤 네트워크관리자분이 서버 점검한다며 복도를 지나가셨고요.",
                        "그러고 보니 시설관리자분이 저녁에 CCTV 점검을 꽤 오래 하시던 게 좀 기억에 남네요.",
                        "참, 연구실에서 주인 모를 USB가 하나 발견됐어요. 확인해 보시면 조사에 도움이 될 거예요."
                    };
                case 2: // 네트워크관리자
                    return new[] {
                        "트래픽 로그 보러 오셨죠? 어젯밤 외부로 나간 데이터 흔적이 있긴 해요.",
                        "다만 어느 단말에서 나갔는지는 아직 특정이 안 됐어요. 접속 기록을 더 맞춰 봐야 해요.",
                        "연구원분은 요즘 마감 때문에 늦게까지 남아 계셨다고 들었어요. 야근이 잦으시더라고요.",
                        "서버실 모니터에 어제 접속 로그를 띄워 뒀어요. 직접 보시는 게 빠를 거예요."
                    };
                case 3: // 시설관리자
                    return new[] {
                        "카드키 출입 기록이요? 어제 밤에도 보안 구역 출입이 몇 건 있긴 했어요.",
                        "카드마다 주인이 등록돼 있어서, 누가 언제 드나들었는지는 발급 단말에 그대로 찍혀요.",
                        "네트워크관리자분은 요즘 서버실에 자주 드나드시더라고요. 업무상 그럴 수 있긴 하지만요.",
                        "전력실 옆 카드키 발급 단말을 확인해 보세요. 출입 이력이 다 남아 있어요."
                    };
            }
            return new[] { "별다른 단서는 없습니다." };
        }

        private static DialogueChoice[] BuildFirstTalkChoices(int my)
        {
            switch (my)
            {
                case 1: // 연구원 (학자 톤)
                    return new[] {
                        new DialogueChoice("어제 몇 시쯤 퇴근하셨어요?",
                            "자정 전에 나왔어요. 마감 자료 정리하느라 좀 늦었죠."),
                        new DialogueChoice("발견된 USB가 누구 건지 아세요?",
                            "아뇨, 처음 보는 거예요. 연구실 공용 USB는 전부 파란색이거든요."),
                        new DialogueChoice("시설관리자분 점검이 평소보다 길었나요?",
                            "길어 보이긴 했는데... 저도 지나가다 본 거라 정확하진 않아요.")
                    };
                case 2: // 네트워크관리자 (기술자 톤)
                    return new[] {
                        new DialogueChoice("외부로 나간 데이터 양은 어느 정도였나요?",
                            "꽤 컸어요. 그 시간에 누가 접속해 있었는지는 접속 기록으로 보시는 게 정확해요."),
                        new DialogueChoice("접속 기록은 조작될 수 있나요?",
                            "기록은 별도 서버에 이중으로 남아서, 지우거나 고치기는 어려워요."),
                        new DialogueChoice("연구원분 야근은 언제부터 늘었나요?",
                            "제가 직접 본 건 아니고, 출입 기록에서 늦은 퇴실이 몇 번 보였어요.")
                    };
                case 3: // 시설관리자 (현장 톤)
                    return new[] {
                        new DialogueChoice("보안 구역 출입은 몇 시쯤이었나요?",
                            "밤늦은 시간대였어요. 누가 언제 나갔는지는 발급 단말 기록에 남아 있어요."),
                        new DialogueChoice("카드키 기록은 믿을 만한가요?",
                            "네, 출입할 때마다 카드 주인과 시각이 찍혀요. 찍힌 기록은 나중에 고칠 수도 없고요."),
                        new DialogueChoice("네트워크관리자분 서버실 출입은 정상인가요?",
                            "출입 권한은 있어요. 다만 횟수가 평소보다 많긴 했어요.")
                    };
            }

            // 폴백 — 일반 선택지
            return new[] {
                new DialogueChoice("더 자세히 알려주세요.", "딱히 더 드릴 정보는 없네요."),
                new DialogueChoice("감사합니다.", "도움이 됐길 바랍니다.")
            };
        }

        // 반복 대화는 짧고 단순 (이 역시 스파이 여부와 무관)
        private static string BuildRepeatLine(int my)
        {
            switch (my)
            {
                case 1: return "더 기억나는 게 있으면 말씀드릴게요. 수사 보드에 모인 기록부터 확인해 보세요.";
                case 2: return "로그는 이미 다 넘겨드렸어요. 시각을 하나씩 맞춰 보시면 보일 거예요.";
                case 3: return "출입 기록은 발급 단말에 다 남아 있어요. 알리바이랑 시간을 맞춰 보세요.";
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
