using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ForTheCompany.Core;
using ForTheCompany.Player;

namespace ForTheCompany.Systems
{
    /// <summary>
    /// FacilityScene 첫 진입 시 오프닝 컷씬:
    /// 1) 검은 화면에서 페이드 인 (1.5초)
    /// 2) 플레이어가 시작 위치 뒤에서 시작 위치까지 자연스럽게 워크 (3.5초)
    /// 3) 동시에 보안조사관 1인칭 속마음 대화창 자동 시작
    /// 4) 컷씬 중 모든 인풋 차단 (RealtimePlayerController 비활성)
    /// 5) 대화 종료 시 인풋 정상화
    ///
    /// 한 런당 1회 (GameSession.hasShownIntroMonologue 플래그).
    /// </summary>
    public class IntroMonologue : MonoBehaviour
    {
        public static IntroMonologue Instance { get; private set; }

        // 컷씬 동안 다른 시스템이 활동 차단 여부 확인할 수 있게 정적 플래그
        public static bool IsCutsceneActive { get; private set; }

        private const float FadeDuration = 1.5f;
        private const float WalkDuration = 3.5f;
        private const float WalkBackDistance = 3f; // 뒤로 3m 이동 후 거기서 워크 시작
        private const float DialogueStartDelay = 0.4f; // 페이드 시작 후 대화 등장 시점

        private float fadeAlpha = 0f;
        private bool monologueEnded = false;

        // 인트로 대화 종료 후 표시되는 조작키 안내 창 (닫을 때까지 입력 차단 유지)
        private bool showControlsPopup = false;
        private float popupOpenTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            EnsureSpawned();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureSpawned();

        private static void EnsureSpawned()
        {
            if (SceneManager.GetActiveScene().name != "FacilityScene") return;
            if (FindFirstObjectByType<IntroMonologue>() != null) return;
            var go = new GameObject("IntroMonologue");
            go.AddComponent<IntroMonologue>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            var s = GameSession.Instance;
            if (s != null && s.hasShownIntroMonologue) return;
            StartCoroutine(RunCutscene());
        }

        private IEnumerator RunCutscene()
        {
            // PlayerInteractor가 spawn될 때까지 대기 (최대 1초)
            float waitT = 0f;
            while (PlayerInteractor.Instance == null && waitT < 1f)
            {
                waitT += Time.deltaTime;
                yield return null;
            }

            var pi = PlayerInteractor.Instance;
            if (pi == null) yield break;

            var playerT = pi.transform;
            var cc = pi.GetComponent<CharacterController>();
            var rtpc = pi.GetComponent<RealtimePlayerController>();
            if (cc == null || rtpc == null) yield break;

            // ── 1) 컷씬 시작: 인풋 차단, 페이드 검정으로 시작 ──
            IsCutsceneActive = true;
            fadeAlpha = 1f;
            rtpc.enabled = false;

            // 시작 위치 백업, 플레이어를 뒤(-Z)로 이동 + +Z 바라보게 회전
            Vector3 startPos = playerT.position;
            Vector3 backPos = startPos + new Vector3(0f, 0f, -WalkBackDistance);

            cc.enabled = false;
            playerT.position = backPos;
            playerT.rotation = Quaternion.LookRotation(Vector3.forward);
            cc.enabled = true;

            // GameSession 플래그 set (재진입 방지)
            var s = GameSession.Instance;
            if (s != null) s.hasShownIntroMonologue = true;
            // 컷씬·조작키 안내 동안엔 입력이 막혀 있으므로 플레이 시간(결과창 '사용 시간')을 재지 않는다
            // (원래 꺼져 있던 타이머는 다시 켜지 않음 — 에디터에서 FacilityScene을 바로 실행한 경우 등)
            bool timerWasActive = s != null && s.TimerActive;
            if (s != null) s.StopTimer();

            // ── 2) 페이드 인 시작 (동시에 진행) ──
            StartCoroutine(FadeInRoutine());

            // 살짝 대기 후 대화창 + 워크 동시 시작
            yield return new WaitForSeconds(DialogueStartDelay);

            // ── 3) 속마음 대화 시작 ──
            StartMonologueDialogue();

            // ── 4) 플레이어 워크 (뒤 → 시작 위치) ──
            float t = 0f;
            while (t < WalkDuration)
            {
                t += Time.deltaTime;
                float pct = Mathf.Clamp01(t / WalkDuration);
                // smoothstep — 천천히 가속/감속
                float eased = pct * pct * (3f - 2f * pct);
                Vector3 targetPos = Vector3.Lerp(backPos, startPos, eased);
                Vector3 move = targetPos - playerT.position;
                cc.Move(move);
                yield return null;
            }

            // 워크 완료 — 위치 정확히 보정
            cc.enabled = false;
            playerT.position = startPos;
            cc.enabled = true;

            // ── 5) 대화 종료 대기 ──
            while (!monologueEnded) yield return null;

            // ── 6) 조작키 안내 창 — 닫을 때까지 입력 차단 유지 ──
            showControlsPopup = true;
            popupOpenTime = Time.time;
            while (showControlsPopup) yield return null;

            // 컷씬 종료 — 인풋 정상화 + 플레이 시간 측정 시작
            IsCutsceneActive = false;
            rtpc.enabled = true;
            if (s != null && timerWasActive) s.ResumeTimer();
            Debug.Log("[IntroMonologue] 컷씬 종료 — 플레이어 조작 활성화");
        }

        private void StartMonologueDialogue()
        {
            var ds = DialogueSystem.Instance;
            if (ds == null) { monologueEnded = true; return; }

            var lines = new[]
            {
                "...출근하자마자 시설로 직행이라니. 무슨 사건이길래.",
                "보안조사관 7년차에 이런 긴급 호출은 처음이야.",
                "차세대 보안 칩 설계도 유출이라고 했지... 외부 침입이 아니라는 뜻이군.",
                "내부에 누군가가 있다. 셋 중 한 명일 거야.",
                "일단 중앙복도의 경비원에게 가서 자세한 브리핑부터 받아봐야겠어."
            };
            ds.StartDialogue("나", lines, null, () => monologueEnded = true);
        }

        private IEnumerator FadeInRoutine()
        {
            float t = 0f;
            while (t < FadeDuration)
            {
                t += Time.unscaledDeltaTime;
                fadeAlpha = Mathf.Lerp(1f, 0f, t / FadeDuration);
                yield return null;
            }
            fadeAlpha = 0f;
        }

        private void Update()
        {
            // 조작키 안내 창 — Space/Enter/ESC로도 닫기 (열린 직후 0.3초는 무시)
            if (!showControlsPopup) return;
            if (Time.time - popupOpenTime < 0.3f) return;
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame
                || kb.enterKey.wasPressedThisFrame
                || kb.escapeKey.wasPressedThisFrame))
            {
                SfxManager.PlayClick();
                showControlsPopup = false;
            }
        }

        // 가장 위에 그려지도록 GUI.depth 매우 음수
        private void OnGUI()
        {
            UITheme.ApplyWebFont();
            if (fadeAlpha >= 0.01f)
            {
                GUI.depth = -10000;
                var c = new Color(0f, 0f, 0f, fadeAlpha);
                UITheme.DrawRect(new Rect(0, 0, Screen.width, Screen.height), c);
            }

            if (showControlsPopup)
            {
                GUI.depth = -9000;
                DrawControlsPopup();
            }
        }

        /// <summary>인트로 직후 조작키 안내 창 — 흰색 라이트 테마 (보안 교육 모달과 통일)</summary>
        private void DrawControlsPopup()
        {
            // 어둠 오버레이
            UITheme.DrawRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(0f, 0f, 0f, 0.75f));

            Color panelBg  = new Color(0.97f, 0.98f, 0.99f);
            Color inkBlack = new Color(0.08f, 0.09f, 0.11f);
            Color inkSub   = new Color(0.32f, 0.35f, 0.40f);
            Color violet   = new Color(0.46f, 0.30f, 0.78f);
            Color line     = new Color(0f, 0f, 0f, 0.12f);
            Color keyBg    = new Color(0.90f, 0.92f, 0.96f);

            float w = Mathf.Min(640, Screen.width - 60);
            float h = Mathf.Min(600, Screen.height - 60);
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            UITheme.DrawRect(new Rect(x, y, w, h), panelBg);
            UITheme.DrawBorder(new Rect(x, y, w, h), violet, 2f);

            // 헤더
            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                normal = { textColor = violet }
            };
            GUI.Label(new Rect(x + 30, y + 22, w - 60, 18), "▶ HOW TO PLAY // CONTROLS", tagSt);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28, fontStyle = FontStyle.Bold,
                normal = { textColor = inkBlack }
            };
            GUI.Label(new Rect(x + 30, y + 42, w - 60, 38), "조작 방법", titleSt);

            UITheme.DrawRect(new Rect(x + 26, y + 88, w - 52, 1), line);

            // 키 안내 행들
            var rows = new (string key, string desc)[]
            {
                ("W A S D",   "이동"),
                ("Shift",     "달리기"),
                ("마우스 휠", "카메라 줌인 / 줌아웃"),
                ("Space",     "대화 · 조사 · 상호작용"),
                ("1 ~ 3",     "대화 선택지 고르기"),
                ("I",         "수사 보드 (단서 확인)"),
                ("ESC",       "일시정지 메뉴"),
            };

            var keySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = violet }
            };
            var descSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = inkBlack }
            };

            float rowY = y + 104;
            float rowH = 50;
            for (int i = 0; i < rows.Length; i++)
            {
                float ry = rowY + rowH * i;
                // 키 뱃지
                var keyRect = new Rect(x + 40, ry + 6, 150, rowH - 14);
                UITheme.DrawRect(keyRect, keyBg);
                UITheme.DrawBorder(keyRect, line, 1.5f);
                GUI.Label(keyRect, rows[i].key, keySt);
                // 설명
                GUI.Label(new Rect(x + 210, ry, w - 250, rowH), rows[i].desc, descSt);
            }

            // 시작 버튼
            var br = new Rect(x + (w - 280) * 0.5f, y + h - 76, 280, 54);
            bool hover = br.Contains(UITheme.GetMousePos());
            UITheme.DrawRect(br, hover ? new Color(0.85f, 0.80f, 0.98f) : Color.white);
            UITheme.DrawBorder(br, violet, hover ? 3f : 2f);
            var bSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = violet }
            };
            GUI.Label(br, "▶ 수사 시작  [Space]", bSt);
            if (GUI.Button(br, GUIContent.none, GUIStyle.none))
            {
                SfxManager.PlayClick();
                showControlsPopup = false;
            }
        }
    }
}
