using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using ForTheCompany.Core;
using ForTheCompany.Managers;
using ForTheCompany.Systems;

namespace ForTheCompany.Player
{
    /// <summary>
    /// 경비원 NPC — 중앙복도에 자동 스폰. 스파이 후보가 아님 (NPCRoster 등록 X).
    /// 스토리 모드의 안내자 역할 — QuestManager 단계에 따라 다른 대사 출력하고 Briefing 단계를 다음으로 진행시킴.
    /// </summary>
    public class GuardNPC : MonoBehaviour, IInteractable
    {
        public static GuardNPC Instance { get; private set; }

        [Header("Identity")]
        public string displayName = "경비원";

        [Header("Interaction")]
        public float interactRadius = 2.5f;

        public string LastResult { get; private set; }

        // IInteractable
        public Vector3 InteractPosition => transform.position;
        public float InteractRadius => interactRadius;
        public bool CanInteract => ForTheCompany.Core.GameSession.Instance == null
            || ForTheCompany.Core.GameSession.Instance.Outcome == ForTheCompany.Core.RunOutcome.Ongoing;
        public string PromptText => $"Space: {displayName}과 대화";

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
            if (SceneManager.GetActiveScene().name != "FacilityScene") return;
            if (FindFirstObjectByType<GuardNPC>() != null) return;

            // 중앙복도 시작 지점 근처에 경비원 배치, player 방향(남쪽) 향함
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "GuardNPC";
            go.transform.position = new Vector3(0f, 1.1f, 4f);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // 평소 player 방향 향함
            go.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", new Color(0.3f, 0.3f, 0.85f)); // 진한 파랑 (보안)
                mr.SetPropertyBlock(mpb);
            }

            go.AddComponent<GuardNPC>();
            Debug.Log("[GuardNPC] 중앙복도에 경비원 배치 (0, 1.1, 4)");
        }

        [Header("Patrol — 중앙복도 좁은 범위 어슬렁")]
        public float patrolRadius = 3f;       // 너무 멀리 안 가게 작게
        public float patrolSpeed = 0.9f;
        public float waitTimeAtPoint = 2.5f;

        private Vector3 patrolCenter;
        private Vector3 currentTarget;
        private float waitUntil;
        private bool waiting;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            patrolCenter = transform.position;   // 시작 위치 = 어슬렁 중심
            ChoosePatrolTarget();
        }

        private void Update()
        {
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
            if (toTarget.magnitude < 0.15f)
            {
                waiting = true;
                waitUntil = Time.time + waitTimeAtPoint;
                return;
            }

            Vector3 dir = toTarget.normalized;
            transform.position += dir * patrolSpeed * Time.deltaTime;
            // 진행 방향 회전 (CharacterBobbing이 걷기 애니메이션 표현)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), 8f * Time.deltaTime);
        }

        private void ChoosePatrolTarget()
        {
            Vector2 r = Random.insideUnitCircle * patrolRadius;
            currentTarget = patrolCenter + new Vector3(r.x, 0f, r.y);
            currentTarget.y = transform.position.y;
        }

        private bool IsBlockedForPatrol()
        {
            var ds = DialogueSystem.Instance;
            if (ds != null && ds.IsActive) return true;
            var pm = PauseMenu.Instance;
            if (pm != null && pm.IsOpen) return true;
            if (IntroMonologue.IsCutsceneActive) return true;
            var sqc = SecurityQuizController.Instance;
            if (sqc != null && sqc.IsOpen) return true;
            var rmc = RacingMissionController.Instance;
            if (rmc != null && rmc.IsOpen) return true;
            var bridge = RacingWebViewBridge.Instance;
            if (bridge != null && bridge.IsShowing) return true;
            var s = ForTheCompany.Core.GameSession.Instance;
            if (s != null && s.Outcome != ForTheCompany.Core.RunOutcome.Ongoing) return true;
            return false;
        }

        public void Interact()
        {
            FacePlayer();

            var quest = QuestManager.Instance;
            var ds = DialogueSystem.Instance;

            // GuardCheckIn 단계 — 연구원 정보 기반 중간 브리핑 + 보안 레이싱 안내
            if (ds != null && quest != null && quest.CurrentStage == QuestManager.Stage.GuardCheckIn)
            {
                LastResult = "";
                StartGuardCheckInDialogue(ds, quest);
                return;
            }

            // Briefing 단계 — 다중 라인 + 선택지 표시
            if (ds != null && quest != null && quest.CurrentStage == QuestManager.Stage.Briefing)
            {
                LastResult = "";
                var lines = new[]
                {
                    "보안조사관님, 와주셔서 감사합니다.",
                    "어제 차세대 보안 칩 설계도가 외부로 유출됐습니다. " +
                    "내부 감사 결과 — 시설 안의 누군가가 정보를 빼돌리고 있습니다.",
                    "용의자는 셋 — 연구원, 네트워크관리자, 시설관리자. " +
                    "셋 중 한 명이 산업스파이로 의심되고 있습니다.",
                    "조사를 어떻게 시작하시겠습니까?"
                };
                var choices = new List<DialogueChoice>
                {
                    new DialogueChoice(
                        "연구원부터 먼저 만나보겠습니다.",
                        "현명한 선택입니다. 연구실은 서북쪽에 있습니다. 대화 마치시면 다시 저에게 보고해주세요. 행운을 빕니다."),
                    new DialogueChoice(
                        "조사 시 주의할 점이 있나요?",
                        "용의자 모두 상냥하게 대하세요. 직접 추궁하면 입을 닫습니다. 단서로 압박하는 게 정석입니다. 먼저 연구실의 연구원부터 만나보시죠."),
                    new DialogueChoice(
                        "용의자 셋의 인상부터 알려주시겠습니까?",
                        "연구원은 칩 설계 핵심 인력, 네트워크관리자는 외부 통신 권한 보유, 시설관리자는 카드키 발급 담당입니다. 모두 동기가 있죠. 먼저 연구실의 연구원부터 만나보세요.")
                };

                ds.StartDialogue(displayName, lines, transform,
                    () =>
                    {
                        quest.TryAdvance(QuestManager.Stage.Briefing);
                        // 인벤토리에 브리핑 내용 기록
                        GameSession.Instance?.AddClue(
                            "경비원 브리핑",
                            "차세대 보안 칩 설계도가 외부 유출. 용의자 셋: 연구원, 네트워크관리자, 시설관리자. " +
                            "각자의 직무 권한과 동기가 있으나 진짜 스파이는 한 명.",
                            ClueSource.NPC, -1, "BRIEF");
                    },
                    choices);
                Debug.Log("[Guard] Briefing 시작 (분기 선택지 포함)");
                return;
            }

            // 반복 대화 — 현재 단계 안내 메시지만 (단일 라인)
            string msg = ResolveLine();
            Debug.Log($"[Guard] {msg}");
            if (ds != null)
            {
                LastResult = "";
                ds.StartDialogue(displayName, msg, transform, null);
            }
            else
            {
                LastResult = $"{displayName}: {msg}";
            }
        }

        /// <summary>연구원 대화·보안 미션 후 경비원 중간 브리핑 — 추리 방법 안내 + 레이싱 안내.
        /// 누가 스파이인지는 암시하지 않는다 (판단은 플레이어가 알리바이와 기록을 대조해서).</summary>
        private void StartGuardCheckInDialogue(DialogueSystem ds, QuestManager quest)
        {
            string hintLine =
                "용의자들은 저마다 어젯밤 행적을 말할 겁니다. 서로를 의심하는 말도 나오겠지만, 증언만으론 판단하기 어렵습니다. " +
                "각자의 알리바이를 수사 보드에 모아 두고, 장소마다 남은 기록의 시각과 하나씩 맞춰 보세요.";

            var lines = new[]
            {
                "조사관님, 돌아오셨군요. 연구원과 보안 교육 모듈은 잘 마치셨습니까?",
                "수집하신 단서는 인벤토리(I 키)에 다 기록돼 있을 겁니다. 지금 한번 정리해두시는 게 좋습니다.",
                hintLine,
                "그리고 다음 단계 — 휴게실에 보안 의식 평가용 컴퓨터가 한 대 있습니다. " +
                "직원들 보안 의식을 평가하는 시뮬레이션인데, 1등으로 통과하면 추가 단서를 얻을 수 있습니다.",
                "휴게실 안 모니터 앞에 가서 상호작용하시면 평가가 시작됩니다. 결승선까지 다른 차들을 제치고 들어가세요."
            };

            var choices = new List<DialogueChoice>
            {
                new DialogueChoice(
                    "지금 바로 휴게실로 가보겠습니다.",
                    "좋습니다. 휴게실은 남쪽에 있습니다. 안쪽 컴퓨터 앞에서 SPACE로 시작하시면 됩니다. 행운을 빕니다."),
                new DialogueChoice(
                    "보안 레이싱은 어떤 게임인가요?",
                    "8-bit 스타일 종스크롤 레이싱입니다. 60초 안에 보안 문제를 풀면서 다른 차를 제치세요. " +
                    "1등은 +5 단서, 그 외엔 보상 없이 재도전 가능합니다."),
                new DialogueChoice(
                    "다른 NPC들도 먼저 만나봐도 됩니까?",
                    "조사는 정해진 순서대로 진행됩니다. 보안 레이싱을 먼저 끝내셔야 " +
                    "다음 용의자인 네트워크관리자를 만나실 수 있습니다. 그 사이 수사 보드(I)로 단서를 정리해 두세요.")
            };

            ds.StartDialogue(displayName, lines, transform,
                () =>
                {
                    quest.TryAdvance(QuestManager.Stage.GuardCheckIn);
                    GameSession.Instance?.AddClue(
                        "경비원 중간 브리핑",
                        "연구원 정보 정리 + 휴게실 컴퓨터에서 보안 레이싱 평가 안내. " + hintLine,
                        ClueSource.NPC, -1, "BRIEF");
                },
                choices);
            Debug.Log("[Guard] GuardCheckIn 중간 브리핑 시작");
        }

        private void FacePlayer()
        {
            var p = PlayerInteractor.Instance;
            if (p == null) return;
            Vector3 toPlayer = p.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f) return;

            // 둘이 서로 마주보게 회전 (다른 NPC와 동일)
            transform.rotation = Quaternion.LookRotation(toPlayer.normalized);
            p.transform.rotation = Quaternion.LookRotation(-toPlayer.normalized);
        }

        /// <summary>현재 단계에 맞춰 다른 안내 대사</summary>
        private string ResolveLine()
        {
            var quest = QuestManager.Instance;
            if (quest == null) return "보안조사관님, 환영합니다.";

            switch (quest.CurrentStage)
            {
                case QuestManager.Stage.Briefing:
                    return
                        "보안조사관님, 어제 차세대 보안 칩 설계도가 외부로 유출됐습니다. " +
                        "용의자는 셋 — 연구원, 네트워크관리자, 시설관리자.\n" +
                        "먼저 연구실로 가서 연구원과 이야기해보세요. " +
                        "단서는 용의자 대화와 보안 교육, 보안 레이싱으로 모을 수 있습니다.";
                case QuestManager.Stage.MeetResearcher:
                    return "연구실의 연구원에게 먼저 가보세요. 서북쪽에 있습니다.";
                case QuestManager.Stage.GuardCheckIn:
                    return "연구원 만나셨군요. 잠시 보고 받고 싶으니 말씀해주세요. (※ 다시 [Space])";
                case QuestManager.Stage.RacingMission:
                    return "휴게실의 보안 레이싱 게임에서 1등을 노리세요. 남쪽에 있습니다.";
                case QuestManager.Stage.MeetNetworkAdmin:
                    return "다음은 서버실의 네트워크관리자와 대화하세요. 북쪽에 있습니다.";
                case QuestManager.Stage.MeetFacilityManager:
                    return "마지막 용의자, 시설관리자에게 가보세요. 동쪽에 있습니다.";
                case QuestManager.Stage.Accusation:
                    return "이제 보안통제실의 동료 수사관에게 가서 스파이를 지목할 시간입니다.";
                case QuestManager.Stage.Done:
                    return "수고하셨습니다.";
            }
            return "";
        }
    }
}
