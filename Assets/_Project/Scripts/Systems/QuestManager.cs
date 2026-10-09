using UnityEngine;
using UnityEngine.SceneManagement;
using ForTheCompany.Core;
using ForTheCompany.Data;

namespace ForTheCompany.Systems
{
    /// <summary>
    /// 스토리 모드의 단계별 진행을 관리한다.
    /// 6단계 선형 흐름: Briefing → MeetResearcher → RacingMission → MeetNetworkAdmin → MeetFacilityManager → Accusation
    /// 각 시스템(NPCInteractable, RacingMissionController, AccusationConsole)에서
    /// 자기 차례에 TryAdvance(stage)를 호출하여 다음 단계로 전환.
    /// </summary>
    public class QuestManager : MonoBehaviour
    {
        public static QuestManager Instance { get; private set; }

        public enum Stage
        {
            Briefing,            // 경비원 브리핑 받기
            MeetResearcher,      // 연구실의 연구원과 대화
            GuardCheckIn,        // 경비원에게 돌아가 중간 보고 + 다음 안내 받기
            RacingMission,       // 휴게실 보안 레이싱 클리어
            MeetNetworkAdmin,    // 서버실의 네트워크관리자와 대화
            MeetFacilityManager, // 전력실의 시설관리자와 대화
            Accusation,          // 보안통제실 동료 수사관에게 지목
            Done                 // 게임 종료
        }

        public Stage CurrentStage { get; private set; } = Stage.Briefing;

        public string LastAdvanceText { get; private set; }
        public float LastAdvanceTime { get; private set; }

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
            if (FindFirstObjectByType<QuestManager>() != null) return;
            var go = new GameObject("QuestManager");
            go.AddComponent<QuestManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            CurrentStage = Stage.Briefing;
            Debug.Log("[Quest] 시작 — Briefing 단계");
        }

        /// <summary>
        /// 주어진 단계가 현재 단계와 일치하면 다음으로 진행. 아니면 무시.
        /// 트리거 시스템들이 자기 stage를 매개변수로 호출.
        /// </summary>
        public bool TryAdvance(Stage stage)
        {
            if (stage != CurrentStage) return false;
            int next = (int)CurrentStage + 1;
            if (next >= System.Enum.GetValues(typeof(Stage)).Length)
                next = (int)Stage.Done;
            var prev = CurrentStage;
            CurrentStage = (Stage)next;

            // 단계별 부수 효과 — NetworkAdmin 끝나면 카드키 발급 (전력실 잠금 해제)
            ApplyStageSideEffects(prev);

            LastAdvanceText = $"✓ {ObjectiveOf(prev)}\n→ 다음: {ObjectiveOf(CurrentStage)}";
            LastAdvanceTime = Time.time;
            SfxManager.PlayStageAdvance();
            Debug.Log($"[Quest] {prev} → {CurrentStage}");
            return true;
        }

        private void ApplyStageSideEffects(Stage justCompleted)
        {
            if (justCompleted == Stage.MeetNetworkAdmin)
            {
                var s = GameSession.Instance;
                if (s != null && !s.hasFacilityCardkey)
                {
                    s.hasFacilityCardkey = true;
                    s.AddClue(
                        "보안 카드키 획득",
                        "네트워크관리자에게 받은 전력실 출입 카드키. 시설관리자 만나러 가는 길이 열림.",
                        ClueSource.NPC, -1, "BADGE"); // 용의자 연결 안 함 — 한 사람만 연결 단서가 많아 보이는 편향 방지
                    Debug.Log("[Quest] 보안 카드키 발급 — 전력실 잠금 해제");
                }
            }
        }

        /// <summary>NPC 역할에 따라 단계 진행 — NPCInteractable에서 호출.
        /// Security(경비원)는 Briefing/GuardCheckIn 둘 다 처리.</summary>
        public bool TryAdvanceByRole(RoleType role)
        {
            switch (role)
            {
                case RoleType.Security:
                    if (CurrentStage == Stage.Briefing)    return TryAdvance(Stage.Briefing);
                    if (CurrentStage == Stage.GuardCheckIn) return TryAdvance(Stage.GuardCheckIn);
                    return false;
                case RoleType.Researcher:      return TryAdvance(Stage.MeetResearcher);
                case RoleType.NetworkAdmin:    return TryAdvance(Stage.MeetNetworkAdmin);
                case RoleType.FacilityManager: return TryAdvance(Stage.MeetFacilityManager);
                default: return false;
            }
        }

        public string CurrentObjective => ObjectiveOf(CurrentStage);
        public string CurrentLocationHint => LocationHintOf(CurrentStage);

        public static string ObjectiveOf(Stage s)
        {
            switch (s)
            {
                case Stage.Briefing:            return "중앙복도의 경비원에게 브리핑 받기";
                case Stage.MeetResearcher:      return "연구실의 연구원과 대화하기";
                case Stage.GuardCheckIn:        return "중앙복도의 경비원에게 돌아가 중간 보고하기";
                case Stage.RacingMission:       return "휴게실 보안 레이싱 게임에서 1등 하기";
                case Stage.MeetNetworkAdmin:    return "서버실의 네트워크관리자와 대화하기";
                case Stage.MeetFacilityManager: return "전력실의 시설관리자와 대화하기";
                case Stage.Accusation:          return "보안통제실의 동료 수사관에게 가서 스파이 지목하기";
                case Stage.Done:                return "게임 종료";
            }
            return "";
        }

        public static string LocationHintOf(Stage s)
        {
            switch (s)
            {
                case Stage.Briefing:            return "장소: 중앙복도 (시작 지점)";
                case Stage.MeetResearcher:      return "장소: 연구실 (서북쪽)";
                case Stage.GuardCheckIn:        return "장소: 중앙복도 (경비원, 시작 지점 근처)";
                case Stage.RacingMission:       return "장소: 휴게실 (남쪽, 안쪽 컴퓨터/모니터 앞)";
                case Stage.MeetNetworkAdmin:    return "장소: 서버실 (북쪽)";
                case Stage.MeetFacilityManager: return "장소: 전력실 (동쪽)";
                case Stage.Accusation:          return "장소: 보안통제실 (동료 수사관)";
                case Stage.Done:                return "";
            }
            return "";
        }

        /// <summary>해당 NPC와 대화가 현재 차례인가? (HUD 안내용)</summary>
        public bool IsExpectedNPC(RoleType role)
        {
            switch (CurrentStage)
            {
                case Stage.Briefing:            return role == RoleType.Security;
                case Stage.MeetResearcher:      return role == RoleType.Researcher;
                case Stage.GuardCheckIn:        return role == RoleType.Security;
                case Stage.MeetNetworkAdmin:    return role == RoleType.NetworkAdmin;
                case Stage.MeetFacilityManager: return role == RoleType.FacilityManager;
                default: return false;
            }
        }
    }
}
