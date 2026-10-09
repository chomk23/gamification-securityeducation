using UnityEngine;
using ForTheCompany.Core;

namespace ForTheCompany.Player
{
    public class RacingConsole : MonoBehaviour, IInteractable
    {
        public float interactRadius = 2.5f;

        public Vector3 InteractPosition => transform.position;
        public float InteractRadius => interactRadius;

        public bool CanInteract
        {
            get
            {
                var rmc = ForTheCompany.Systems.RacingMissionController.Instance;
                if (rmc == null) return false;
                if (rmc.HasCompleted) return false;
                if (rmc.IsOpen) return false;
                var s = GameSession.Instance;
                if (s != null && s.Outcome != RunOutcome.Ongoing) return false;
                // 레이싱 단계 전에 1등 하면 단계는 그대로인데 콘솔만 '클리어'로 잠겨 퀘스트가 멈춘다
                var quest = ForTheCompany.Systems.QuestManager.Instance;
                if (quest != null && quest.CurrentStage != ForTheCompany.Systems.QuestManager.Stage.RacingMission)
                    return false;
                return true;
            }
        }

        public string PromptText
        {
            get
            {
                var rmc = ForTheCompany.Systems.RacingMissionController.Instance;
                if (rmc != null && rmc.HasCompleted) return "이미 클리어 (보안 레이싱)";
                return "Space: 보안 레이싱 시작 (60초)";
            }
        }

        public void Interact()
        {
            ForTheCompany.Systems.RacingMissionController.Instance?.Launch();
        }
    }
}
