using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ForTheCompany.Player
{
    public class PlayerInteractor : MonoBehaviour
    {
        public static PlayerInteractor Instance { get; private set; }

        [Header("Detection")]
        public float searchRadius = 4f;

        public IInteractable Nearest { get; private set; }
        public string LastInteractionResult { get; private set; }
        public float LastInteractionTime { get; private set; }

        /// <summary>외부 시스템(NPC, Quest 등)에서 짧은 토스트 메시지를 띄울 때 사용</summary>
        public void ShowToast(string message)
        {
            LastInteractionResult = message;
            LastInteractionTime = Time.time;
        }

        private readonly List<IInteractable> cache = new List<IInteractable>();
        private float refreshTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            RefreshCache();
        }

        private void RefreshCache()
        {
            cache.Clear();
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var b in behaviours)
            {
                if (b is IInteractable i) cache.Add(i);
            }
        }

        private void Update()
        {
            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = 1.5f;
                RefreshCache();
            }

            FindNearest();
            HandleInput();
        }

        private void FindNearest()
        {
            IInteractable best = null;
            float bestSqr = float.MaxValue;
            Vector3 myPos = transform.position;

            foreach (var n in cache)
            {
                if (n == null) continue;
                if (!n.CanInteract) continue;
                float sqr = (n.InteractPosition - myPos).sqrMagnitude;
                if (sqr > searchRadius * searchRadius) continue;
                if (sqr > n.InteractRadius * n.InteractRadius) continue;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = n;
                }
            }
            Nearest = best;
        }

        private void HandleInput()
        {
            // Block input when any modal is open
            var rmc = ForTheCompany.Systems.RacingMissionController.Instance;
            if (rmc != null && rmc.IsOpen) return;
            var sqc = ForTheCompany.Systems.SecurityQuizController.Instance;
            if (sqc != null && sqc.IsOpen) return;
            // 대화창 활성 중이면 DialogueSystem이 E/Space를 직접 처리 — Interactor는 무시
            // (대화를 닫은 바로 그 프레임의 Space도 무시 — 닫자마자 같은 NPC 대화가 다시 열리는 것 방지)
            var ds = ForTheCompany.Systems.DialogueSystem.Instance;
            if (ds != null && (ds.IsActive || ds.ClosedFrame == Time.frameCount)) return;
            // 인벤토리 열림 중에도 상호작용 차단
            if (ForTheCompany.Systems.FacilityHUD.IsInventoryOpen) return;
            // 일시정지 메뉴 / 오프닝 컷씬 중에도 상호작용 차단 (SPACE 먹통)
            var pm = ForTheCompany.Systems.PauseMenu.Instance;
            if (pm != null && pm.IsOpen) return;
            if (ForTheCompany.Systems.IntroMonologue.IsCutsceneActive) return;
            // 지목 모달 (AI로봇 한세)
            var partner = AccusationPartner.Instance;
            if (partner != null && partner.IsMenuOpen) return;

            if (Nearest == null) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (!kb.spaceKey.wasPressedThisFrame) return;

            Nearest.Interact();

            if (Nearest is NPCInteractable npc && !string.IsNullOrEmpty(npc.LastResult))
            {
                LastInteractionResult = npc.LastResult;
                LastInteractionTime = Time.time;
            }
            else if (Nearest is GuardNPC guard && !string.IsNullOrEmpty(guard.LastResult))
            {
                LastInteractionResult = guard.LastResult;
                LastInteractionTime = Time.time;
            }
        }
    }
}
