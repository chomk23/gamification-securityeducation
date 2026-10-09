using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using ForTheCompany.Core;
using ForTheCompany.Player;

namespace ForTheCompany.Systems
{
    /// <summary>
    /// Security Race 미니게임은 외부 HTML(StreamingAssets/security-race.html)을 브라우저로 띄우고,
    /// 사용자가 게임 완료 후 Unity로 돌아와 등수를 입력하면 단서 보상을 지급한다.
    /// </summary>
    public class RacingMissionController : MonoBehaviour
    {
        public static RacingMissionController Instance { get; private set; }

        public enum Phase { Inactive, AwaitingRank, Finished }
        public Phase CurrentPhase { get; private set; } = Phase.Inactive;
        public bool IsOpen => CurrentPhase != Phase.Inactive;
        public bool HasCompleted { get; private set; }

        // Result fields
        public int FinalRank { get; private set; }
        public int ClueReward { get; private set; }
        public string ResultMessage { get; private set; }
        public float ResultShownAt { get; private set; }

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
            // FacilityScene 이외에는 RacingConsole 큐브가 불필요
            if (SceneManager.GetActiveScene().name != "FacilityScene") return;
            if (FindFirstObjectByType<RacingMissionController>() != null) return;
            var go = new GameObject("RacingMissionController");
            var ctrl = go.AddComponent<RacingMissionController>();
            ctrl.SpawnConsole();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        /// <summary>
        /// 휴게실 안에 RacingConsole 인터랙션 마커 자동 배치.
        /// 시각적 큐브 없음 — 휴게실에 미리 배치된 모니터/컴퓨터 prefab 옆 빈 GameObject로만 작동.
        /// PlayerInteractor가 거리 기반이라 Collider/MeshRenderer 없어도 SPACE 인터랙션 정상.
        /// </summary>
        private void SpawnConsole()
        {
            if (FindFirstObjectByType<RacingConsole>() != null) return;

            var go = new GameObject("RacingConsole");
            go.transform.position = new Vector3(-13f, 0.75f, 2f);
            // MeshRenderer / Collider 없음 → 보이지도 막히지도 않음
            go.AddComponent<RacingConsole>();
        }

        /// <summary>WebView Canvas 또는 외부 브라우저로 HTML 띄우고 결과 대기</summary>
        public void Launch()
        {
            if (HasCompleted || CurrentPhase != Phase.Inactive) return;
            if (!IsRunOngoing()) return;
            var quest = QuestManager.Instance;
            if (quest != null && quest.CurrentStage != QuestManager.Stage.RacingMission) return;

            // 1순위: Unity 임베드 WebView (RacingWebViewCanvas가 씬에 있으면)
            var bridge = RacingWebViewBridge.Instance;
            if (bridge != null && bridge.IsAvailable)
            {
                bridge.Show();
                bridge.RegisterRaceFinishedCallback(OnHtmlRaceFinished);
                Debug.Log("[Race] 임베드 WebView 활성화 + JS 브릿지 등록");
            }
            else
            {
                // 폴백: 외부 브라우저
                RacingWebViewBridge.FallbackOpenExternal();
                Debug.Log("[Race] 외부 브라우저로 폴백 (WebView Canvas 미구성)");
            }

            CurrentPhase = Phase.AwaitingRank;
        }

        /// <summary>HTML에서 uwb.ExecuteJsMethod("OnRaceFinished", rank) 호출 시 실행됨</summary>
        public void OnHtmlRaceFinished(int rank)
        {
            if (CurrentPhase != Phase.AwaitingRank) return;
            // 1등만 클리어 인정
            if (rank == 1)
            {
                ReportRank(1);
                Debug.Log("[Race] HTML에서 1등 신호 — 자동 클리어");
            }
            else
            {
                // 1등 못 했으면 모달 닫고 콘솔 재사용 가능하게 — 결과는 토스트로 안내
                Cancel();
                string result = rank >= 2 ? $"{rank}등으로 들어왔습니다" : "시간 안에 완주하지 못했습니다";
                PlayerInteractor.Instance?.ShowToast(
                    $"보안 레이싱 — {result}.\n1등만 클리어됩니다. 휴게실 컴퓨터에서 다시 도전하세요.");
                Debug.Log($"[Race] HTML에서 {rank}등 신호 — 재시도 가능");
            }
        }

        private static bool IsRunOngoing()
        {
            var s = GameSession.Instance;
            return s == null || s.Outcome == RunOutcome.Ongoing;
        }

        /// <summary>플레이어가 게임 후 Unity로 돌아와 등수 보고 (1/2/3)</summary>
        public void ReportRank(int rank)
        {
            if (CurrentPhase != Phase.AwaitingRank) return;
            if (rank < 1 || rank > 3) return;
            // 시간 초과 등으로 게임이 이미 끝났으면 늦게 도착한 결과는 반영하지 않음
            if (!IsRunOngoing()) { Cancel(); return; }

            FinalRank = rank;
            ClueReward = rank == 1 ? 5 : rank == 2 ? 3 : 1;
            ResultMessage = rank switch
            {
                1 => $"🥇 1등! 모든 차를 제치고 우승. +{ClueReward} 단서 획득",
                2 => $"🥈 2등! 결승선 통과. +{ClueReward} 단서 획득",
                3 => $"🥉 3등! 완주했습니다. +{ClueReward} 단서 획득",
                _ => ""
            };

            if (GameSession.Instance != null)
            {
                GameSession.Instance.totalClues += ClueReward;
                GameSession.Instance.LastEncounterRewardClues = ClueReward;
                // 보안 레이싱 우승은 인벤토리 단서 목록에 넣지 않음 (추리용 단서가 아니라 통과 보상일 뿐)
            }

            HasCompleted = true;
            CurrentPhase = Phase.Finished;
            ResultShownAt = Time.time;
            RacingWebViewBridge.Instance?.Hide();

            // 스토리 모드: 1등이면 RacingMission 단계 진행
            if (rank == 1)
                QuestManager.Instance?.TryAdvance(QuestManager.Stage.RacingMission);

            Debug.Log($"[Race] 등수 {rank}, 보상 +{ClueReward} 단서");
        }

        /// <summary>등수 입력 취소 (보상 없음, 콘솔은 다시 사용 가능)</summary>
        public void Cancel()
        {
            if (CurrentPhase != Phase.AwaitingRank) return;
            CurrentPhase = Phase.Inactive;
            RacingWebViewBridge.Instance?.Hide();
        }

        public void Acknowledge()
        {
            if (CurrentPhase == Phase.Finished)
                CurrentPhase = Phase.Inactive;
        }
    }
}
