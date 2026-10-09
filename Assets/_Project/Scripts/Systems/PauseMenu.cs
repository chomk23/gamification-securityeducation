using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ForTheCompany.Core;

namespace ForTheCompany.Systems
{
    /// <summary>
    /// FacilityScene에서 ESC 누르면 뜨는 일시정지 메뉴 — 계속하기/게임 설명/설정/메인 메뉴/종료.
    /// 설정에는 소리 조절 슬라이더 포함 (AudioListener.volume).
    /// ESC로 닫기 + 게임 복귀, Time.timeScale 일시정지.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public static PauseMenu Instance { get; private set; }

        public enum Page { Main, Help, Settings, License }
        public Page CurrentPage { get; private set; } = Page.Main;
        public bool IsOpen { get; private set; }

        // 마지막 timeScale 백업 (복귀 시 복원)
        private float pausedTimeScale;

        // 소리 볼륨 (0~1) — 기본 30% (효과음이 너무 크지 않게). PlayerPrefs에 저장해 재실행 후에도 유지.
        public const string VolumePrefKey = "ftc.masterVolume";
        public const float DefaultVolume = 0.3f;
        private static float volume = -1f; // -1 = 아직 저장값을 읽지 않음

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
            if (FindFirstObjectByType<PauseMenu>() != null) return;
            var go = new GameObject("PauseMenu");
            go.AddComponent<PauseMenu>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (volume < 0f) volume = PlayerPrefs.GetFloat(VolumePrefKey, DefaultVolume);
            AudioListener.volume = volume;
        }

        private static void SetVolume(float v)
        {
            volume = Mathf.Clamp01(v);
            AudioListener.volume = volume;
            PlayerPrefs.SetFloat(VolumePrefKey, volume); // 디스크 기록은 메뉴를 닫거나 나갈 때(PlayerPrefs.Save)
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // 다른 모달이 떠있으면 ESC는 그쪽이 처리하므로 PauseMenu는 가만히
            if (!IsOpen && IsAnyOtherModalOpen()) return;

            // 인벤토리·지목창·레이싱 창을 방금(같은 프레임) ESC로 닫았으면 그 ESC로 메뉴를 열지 않음
            // (FacilityHUD가 먼저 실행돼 창을 닫아버린 경우의 실행 순서 race 방지)
            if (!IsOpen && FacilityHUD.EscConsumedFrame == Time.frameCount) return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (IsOpen)
                {
                    if (CurrentPage == Page.Main) Close();
                    else CurrentPage = Page.Main; // 서브 페이지면 메인으로
                }
                else
                {
                    Open();
                }
            }
        }

        private bool IsAnyOtherModalOpen()
        {
            // 오프닝 컷씬 중에는 ESC 가로채지 않음
            if (IntroMonologue.IsCutsceneActive) return true;

            var ds = DialogueSystem.Instance;
            if (ds != null && ds.IsActive) return true;
            var sqc = SecurityQuizController.Instance;
            if (sqc != null && sqc.IsOpen) return true;
            var rmc = RacingMissionController.Instance;
            if (rmc != null && rmc.IsOpen) return true;
            if (FacilityHUD.IsInventoryOpen) return true;
            // 액세시언 콘솔 메뉴
            var partner = ForTheCompany.Player.AccusationPartner.Instance;
            if (partner != null && partner.IsMenuOpen) return true;
            // 게임 종료 화면
            var s = GameSession.Instance;
            if (s != null && s.Outcome != RunOutcome.Ongoing) return true;
            return false;
        }

        public void Open()
        {
            IsOpen = true;
            CurrentPage = Page.Main;
            pausedTimeScale = Time.timeScale;
            Time.timeScale = 0f; // 게임 일시정지
            // 커서는 NeonCursorController가 항상 false로 유지 (네온 커서 사용)
            Cursor.lockState = CursorLockMode.None;
        }

        public void Close()
        {
            IsOpen = false;
            Time.timeScale = pausedTimeScale > 0f ? pausedTimeScale : 1f;
            PlayerPrefs.Save(); // 볼륨 설정 저장
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            UITheme.ApplyWebFont();

            // 어둠 오버레이 + 스캔라인
            UITheme.DrawRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(UITheme.Bg0.r, UITheme.Bg0.g, UITheme.Bg0.b, 0.9f));
            UITheme.DrawScanlines(new Rect(0, 0, Screen.width, Screen.height), 0.05f);

            switch (CurrentPage)
            {
                case Page.Main:     DrawMainPage(); break;
                case Page.Help:     DrawHelpPage(); break;
                case Page.Settings: DrawSettingsPage(); break;
                case Page.License:  DrawLicensePage(); break;
            }
        }

        // ═══════════════════ MAIN PAGE ═══════════════════

        private void DrawMainPage()
        {
            float w = 480, h = 560; // 라이센스 버튼 추가로 확장 (480 → 560)
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            var rect = new Rect(x, y, w, h);
            UITheme.DrawRect(rect, UITheme.Bg1);
            UITheme.DrawBorder(rect, UITheme.NeonGreen, 1f);

            // 윈도우 헤더
            UITheme.DrawWinBar(new Rect(x, y, w, 32), "pause-menu.dossier");

            // 헤더
            float headerY = y + 50;
            UITheme.DrawPulseDot(new Vector2(x + 28, headerY + 14), UITheme.NeonGreen, 4f);

            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = UITheme.NeonGreen }
            };
            GUI.Label(new Rect(x + 44, headerY, w - 80, 22),
                "▶ 시스템 일시정지 // SYSTEM PAUSED", tagSt);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(x + 44, headerY + 18, w - 80, 32),
                "일시정지 메뉴", titleSt);

            // 구분선
            UITheme.DrawRect(new Rect(x + 24, headerY + 60, w - 48, 1), UITheme.Line);

            // 버튼들
            float btnY = headerY + 90;
            float btnH = 50, gap = 10;
            float btnW = w - 80;
            float btnX = x + 40;

            if (UITheme.NeonButton(new Rect(btnX, btnY, btnW, btnH),
                "▶ 게임 계속하기  [ESC]", UITheme.NeonGreen))
                Close();

            if (UITheme.GhostButton(new Rect(btnX, btnY + (btnH + gap), btnW, btnH),
                "▶ 게임 설명"))
                CurrentPage = Page.Help;

            if (UITheme.GhostButton(new Rect(btnX, btnY + (btnH + gap) * 2, btnW, btnH),
                "▶ 볼륨"))
                CurrentPage = Page.Settings;

            if (UITheme.GhostButton(new Rect(btnX, btnY + (btnH + gap) * 3, btnW, btnH),
                "▶ 라이센스"))
                CurrentPage = Page.License;

            if (UITheme.GhostButton(new Rect(btnX, btnY + (btnH + gap) * 4, btnW, btnH),
                "▶ 메인 메뉴로 돌아가기"))
                ReturnToMainMenu();

            if (UITheme.NeonButton(new Rect(btnX, btnY + (btnH + gap) * 5, btnW, btnH),
                "▣ 게임 종료", UITheme.Danger))
                QuitGame();
        }

        // ═══════════════════ HELP PAGE ═══════════════════

        private void DrawHelpPage()
        {
            float w = 700, h = 580;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            var rect = new Rect(x, y, w, h);
            UITheme.DrawRect(rect, UITheme.Bg1);
            UITheme.DrawBorder(rect, UITheme.NeonCyan, 1f);

            UITheme.DrawWinBar(new Rect(x, y, w, 32), "help.dossier");

            float headerY = y + 50;
            UITheme.DrawPulseDot(new Vector2(x + 28, headerY + 14), UITheme.NeonCyan, 4f);

            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = UITheme.NeonCyan }
            };
            GUI.Label(new Rect(x + 44, headerY, w - 80, 22),
                "▶ 게임 설명 // BRIEFING", tagSt);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(x + 44, headerY + 18, w - 80, 32),
                "FOR THE COMPANY", titleSt);

            UITheme.DrawRect(new Rect(x + 24, headerY + 60, w - 48, 1), UITheme.Line);

            // 본문
            float bodyY = headerY + 80;
            var sectionSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonGreen }
            };
            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, wordWrap = true,
                normal = { textColor = Color.white }
            };

            GUI.Label(new Rect(x + 32, bodyY, w - 64, 22),
                "▶ 목표", sectionSt);
            int limitMin = GameSession.Instance != null
                ? Mathf.RoundToInt(GameSession.Instance.totalTime / 60f) : 15;
            GUI.Label(new Rect(x + 32, bodyY + 22, w - 64, 50),
                "당신은 보안조사관. 시설 내 3명의 용의자 중 산업스파이를 찾아내라.\n" +
                $"{limitMin}분 안에 단서를 모아 보안통제실 동료 수사관에게 가서 정확히 한 번만 지목할 수 있다.", bodySt);

            GUI.Label(new Rect(x + 32, bodyY + 90, w - 64, 22),
                "▶ 조작", sectionSt);
            GUI.Label(new Rect(x + 32, bodyY + 112, w - 64, 100),
                "WASD : 이동   |   SHIFT : 달리기\n" +
                "마우스 휠 : 줌인/아웃\n" +
                "SPACE : NPC 대화 / 단서 조사 / 지목\n" +
                "I : 인벤토리 (수집한 단서 목록)\n" +
                "ESC : 일시정지 메뉴 (지금 이 화면)", bodySt);

            GUI.Label(new Rect(x + 32, bodyY + 220, w - 64, 22),
                "▶ 진행 흐름", sectionSt);
            GUI.Label(new Rect(x + 32, bodyY + 242, w - 64, 130),
                "1. 경비원과 브리핑 → 연구원과 대화 → 보안 교육 모듈 풀기\n" +
                "2. 휴게실에서 보안 레이싱 게임 1등 하기\n" +
                "3. 네트워크관리자와 대화 → 카드키 획득\n" +
                "4. 시설관리자와 대화 → 단서 마무리\n" +
                "5. 보안통제실 동료 수사관에게 가서 산업스파이 지목 (단 한 번!)", bodySt);

            // 닫기 버튼
            if (UITheme.NeonButton(new Rect(x + (w - 240) * 0.5f, y + h - 70, 240, 46),
                "◀ 메뉴로 돌아가기", UITheme.NeonCyan))
                CurrentPage = Page.Main;
        }

        // ═══════════════════ SETTINGS PAGE ═══════════════════

        private void DrawSettingsPage()
        {
            float w = 580, h = 460;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            var rect = new Rect(x, y, w, h);
            UITheme.DrawRect(rect, UITheme.Bg1);
            UITheme.DrawBorder(rect, UITheme.NeonViolet, 1f);

            UITheme.DrawWinBar(new Rect(x, y, w, 32), "settings.dossier");

            float headerY = y + 50;
            UITheme.DrawPulseDot(new Vector2(x + 28, headerY + 14), UITheme.NeonViolet, 4f);

            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = UITheme.NeonViolet }
            };
            GUI.Label(new Rect(x + 44, headerY, w - 80, 22),
                "▶ 볼륨 // VOLUME", tagSt);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(x + 44, headerY + 18, w - 80, 32),
                "볼륨", titleSt);

            UITheme.DrawRect(new Rect(x + 24, headerY + 60, w - 48, 1), UITheme.Line);

            // ───── 소리 조절 ─────
            float optY = headerY + 90;

            var labelSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonGreen }
            };
            GUI.Label(new Rect(x + 32, optY, 200, 22),
                "▶ 마스터 볼륨", labelSt);

            // 현재 값 표시 (우측)
            var valSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(x + w - 100, optY, 70, 22),
                $"{Mathf.RoundToInt(volume * 100)}%", valSt);

            // 슬라이더 트랙 배경
            float sliderY = optY + 32;
            float sliderX = x + 32;
            float sliderW = w - 64;
            UITheme.DrawRect(new Rect(sliderX, sliderY + 6, sliderW, 4), UITheme.Bg3);
            // 채워진 부분
            UITheme.DrawRect(new Rect(sliderX, sliderY + 6, sliderW * volume, 4), UITheme.NeonGreen);

            // 슬라이더 (실제 인터랙션) — 투명하게 위에 덮기
            float newVol = GUI.HorizontalSlider(new Rect(sliderX, sliderY, sliderW, 18),
                volume, 0f, 1f);
            if (!Mathf.Approximately(newVol, volume))
                SetVolume(newVol);

            // 음소거/최대 빠른 버튼
            float btnY = sliderY + 30;
            float btnW = 100, btnH = 32, btnGap = 10;
            if (UITheme.GhostButton(new Rect(x + 32, btnY, btnW, btnH),
                "▣ 음소거"))
                SetVolume(0f);
            if (UITheme.GhostButton(new Rect(x + 32 + btnW + btnGap, btnY, btnW, btnH),
                "▣ 50%"))
                SetVolume(0.5f);
            if (UITheme.GhostButton(new Rect(x + 32 + (btnW + btnGap) * 2, btnY, btnW, btnH),
                "▣ 최대"))
                SetVolume(1f);

            // 닫기 버튼
            if (UITheme.NeonButton(new Rect(x + (w - 240) * 0.5f, y + h - 70, 240, 46),
                "◀ 메뉴로 돌아가기", UITheme.NeonViolet))
                CurrentPage = Page.Main;
        }

        // ═══════════════════ LICENSE PAGE ═══════════════════

        private void DrawLicensePage()
        {
            float w = 700, h = 560;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            var rect = new Rect(x, y, w, h);
            UITheme.DrawRect(rect, UITheme.Bg1);
            UITheme.DrawBorder(rect, UITheme.NeonYellow, 1f);

            UITheme.DrawWinBar(new Rect(x, y, w, 32), "licenses.dossier");

            float headerY = y + 50;
            UITheme.DrawPulseDot(new Vector2(x + 28, headerY + 14), UITheme.NeonYellow, 4f);

            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = UITheme.NeonYellow }
            };
            GUI.Label(new Rect(x + 44, headerY, w - 80, 22),
                "▶ 라이센스 // THIRD-PARTY ASSETS", tagSt);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(x + 44, headerY + 18, w - 80, 32),
                "사용한 에셋 및 라이센스", titleSt);

            UITheme.DrawRect(new Rect(x + 24, headerY + 60, w - 48, 1), UITheme.Line);

            // 에셋 목록 (이름 — 라이센스)
            var nameSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            var licSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 8, 0, 0),
                normal = { textColor = UITheme.NeonCyan }
            };

            var entries = new (string name, string lic)[]
            {
                ("Kenney Blocky Characters (캐릭터 모델)", "CC0 — kenney.nl"),
                ("ScifiOfficeLite by Terresquall (가구·벽·바닥)", "무료 라이센스"),
                ("Synty POLYGON 시리즈 (환경 에셋)", "Synty Store EULA"),
                ("BGM 3트랙 (메뉴·게임플레이·엔딩)", "Pixabay Content License"),
                ("UnityWebBrowser by Voltstro-Studios", "MIT License"),
                ("CEF (Chromium Embedded Framework)", "BSD 3-Clause"),
                ("Noto Sans KR (웹 버전 한글 폰트)", "SIL Open Font License 1.1"),
                ("Unity 6 + Universal Render Pipeline", "Unity 엔진"),
            };

            float rowY = headerY + 80;
            float rowH = 36; // 8행이 하단 안내·버튼과 겹치지 않게 (40 → 36)
            for (int i = 0; i < entries.Length; i++)
            {
                var rowRect = new Rect(x + 32, rowY + rowH * i, w - 64, rowH - 6);
                UITheme.DrawRect(rowRect, UITheme.Bg2);
                UITheme.DrawRect(new Rect(rowRect.x, rowRect.y, 3f, rowRect.height), UITheme.NeonYellow);
                GUI.Label(new Rect(rowRect.x + 14, rowRect.y, rowRect.width * 0.62f, rowRect.height),
                    entries[i].name, nameSt);
                GUI.Label(new Rect(rowRect.x + rowRect.width * 0.55f, rowRect.y,
                    rowRect.width * 0.45f - 8, rowRect.height), entries[i].lic, licSt);
            }

            // 하단 안내
            var footSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UITheme.InkDim }
            };
            GUI.Label(new Rect(x + 32, rowY + rowH * entries.Length + 6, w - 64, 34),
                "본 게임은 개인 학습용 프로젝트이며, 각 에셋은 해당 라이센스 조건을 따릅니다.", footSt);

            // 닫기 버튼
            if (UITheme.NeonButton(new Rect(x + (w - 240) * 0.5f, y + h - 70, 240, 46),
                "◀ 메뉴로 돌아가기", UITheme.NeonYellow))
                CurrentPage = Page.Main;
        }

        // ═══════════════════ ACTIONS ═══════════════════

        private void ReturnToMainMenu()
        {
            Time.timeScale = 1f;
            IsOpen = false;
            PlayerPrefs.Save(); // 볼륨 설정 저장
            const string menuScene = "MainMenuScene";
            if (Application.CanStreamedLevelBeLoaded(menuScene))
                SceneManager.LoadScene(menuScene);
        }

        private void QuitGame()
        {
            Time.timeScale = 1f;
            PlayerPrefs.Save(); // 볼륨 설정 저장
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
