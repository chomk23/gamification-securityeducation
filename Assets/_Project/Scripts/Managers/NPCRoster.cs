using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ForTheCompany.Player;

namespace ForTheCompany.Managers
{
    public class NPCRoster : MonoBehaviour
    {
        public static NPCRoster Instance { get; private set; }

        public List<NPCActor> npcs = new List<NPCActor>();

        [Header("Spy")]
        public bool assignSpyOnStart = true;
        [Tooltip("Editor에서만 콘솔에 스파이 정체를 노출. 빌드된 게임에서는 자동으로 익명 메시지만 출력.")]
        public bool revealSpyInLog = true;

        public NPCActor Spy { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 모든 NPC에 patrol 컴포넌트 자동 부착 (시설이 살아있는 느낌)
            foreach (var n in npcs)
            {
                if (n == null) continue;
                if (n.GetComponent<NPCPatrol>() == null)
                    n.gameObject.AddComponent<NPCPatrol>();
            }
        }

        private void Start()
        {
            if (assignSpyOnStart) AssignRandomSpy();
        }

        public void AssignRandomSpy()
        {
            if (npcs == null || npcs.Count == 0) return;

            foreach (var n in npcs)
                if (n != null) n.isSpy = false;

            int idx = Random.Range(0, npcs.Count);
            Spy = npcs[idx];
            Spy.isSpy = true;

            // GameSession.spyRoleIndex를 실제 스파이와 맞춘다
            // (따로 뽑으면 이 값을 쓰는 화면에 실제와 다른 스파이 이름이 뜬다)
            var session = ForTheCompany.Core.GameSession.Instance;
            if (session != null && Spy.data != null)
                session.spyRoleIndex = (int)Spy.data.role - 1;

#if UNITY_EDITOR
            if (revealSpyInLog)
                Debug.Log($"[Spy Assigned] {Spy.DisplayName} 가 스파이입니다 (Editor 전용 디버그).");
            else
                Debug.Log("[Spy Assigned] 스파이 1명이 무작위로 지정되었습니다.");
#else
            Debug.Log("[Spy Assigned] 스파이 1명이 무작위로 지정되었습니다.");
#endif
        }

        public IEnumerator RunAllNPCsTurn()
        {
            for (int i = 0; i < npcs.Count; i++)
            {
                var npc = npcs[i];
                if (npc == null) continue;

                var beh = npc.GetComponent<NPCBehavior>();
                if (beh != null)
                    yield return StartCoroutine(beh.TakeTurn());
            }
        }
    }
}
