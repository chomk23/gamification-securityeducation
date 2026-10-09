using UnityEngine;
using UnityEngine.SceneManagement;
using ForTheCompany.Core;

namespace ForTheCompany.Systems
{
    /// <summary>
    /// MainMenuScene 컨트롤러 — SecureSense 디자인 (Mission Dossier 톤).
    /// 다크 + 네온 그린, 모노스페이스 터미널 미학.
    /// 흐름: Menu → 인트로 3장 → FacilityScene
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        public string facilitySceneName = "FacilityScene";

        private enum Phase { Menu, Intro }
        private Phase phase = Phase.Menu;
        private int introIndex;

        private static readonly (string title, string body)[] introSlides =
        {
            (
                "CLASSIFIED // BRIEFING 01",
                "대기업 연구시설 'For The Company' — 차세대 보안 칩 설계도가 외부로 유출되었습니다.\n\n" +
                "내부 감사 결과: 시설 내부 인원 중 한 명이 정보를 빼돌리고 있습니다."
            ),
            (
                "SUSPECT PROFILE // 03 TARGETS",
                "용의자 명단:\n\n" +
                "  ▶ 연구원 — 칩 설계 핵심 멤버\n" +
                "  ▶ 네트워크관리자 — 외부 통신 권한 보유\n" +
                "  ▶ 시설관리자 — 출입 통제 및 카드키 관리\n\n" +
                "셋 중 한 명이 진짜 산업스파이."
            ),
            (
                "OBJECTIVE // PRIMARY",
                "시설을 자유롭게 돌아다니며 단서 수집.\n" +
                "보안교육 미니게임으로 추가 정보 확보.\n\n" +
                "보안통제실 동료 수사관에게 가서 단 한 번 — 진짜 스파이를 지목하라.\n" +
                "오답 시 정보는 영원히 외부로 유출."
            )
        };

        private void Awake()
        {
            if (GameSession.Instance == null)
            {
                var go = new GameObject("GameSession");
                go.AddComponent<GameSession>();
            }

            // 빌드본에서 OnGUI 매 프레임 갱신 보장 (펄스 dot, hover jitter 등 애니메이션)
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            // 커서는 NeonCursorController가 항상 false로 유지 (네온 커서 사용)
            Cursor.lockState = CursorLockMode.None;
        }

        private void OnGUI()
        {
            UITheme.ApplyWebFont();
            // 풀스크린 다크 배경 + 그리드 패턴
            UITheme.DrawGridBg(new Rect(0, 0, Screen.width, Screen.height));
            UITheme.DrawScanlines(new Rect(0, 0, Screen.width, Screen.height), 0.05f);

            if (phase == Phase.Menu) DrawMenu();
            else DrawIntro();

            HandleKeys();
        }

        // ═══════════════════ MAIN MENU ═══════════════════

        private void DrawMenu()
        {
            Vector2 mp = UITheme.GetMousePos();

            float w = Mathf.Min(880, Screen.width - 80);
            float h = 620;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            // 메인 패널 — 가짜 OS 윈도우
            UITheme.DrawWinBar(new Rect(x, y, w, 36), "secure-sense.exe");
            UITheme.DrawRect(new Rect(x, y + 36, w, h - 36), UITheme.Bg1);
            UITheme.DrawBorder(new Rect(x, y, w, h), UITheme.LineStrong);

            // ── 상단 태그 + dot (hover 시 펄스 강화) ──
            float topY = y + 60;
            var tagRect = new Rect(x + 32, topY, 110, 20);
            bool hoverTag = tagRect.Contains(mp);
            UITheme.DrawTag(tagRect, "// SYSTEM ONLINE", UITheme.NeonGreen);
            UITheme.DrawPulseDot(new Vector2(x + 156, topY + 10), UITheme.NeonGreen,
                hoverTag ? 6f : 4f);

            // ── ASCII 코드 라인 (hover 시 형광 그린으로 강조) ──
            var codeArea = new Rect(x + 32, topY + 28, w - 64, 32);
            bool hoverCode = codeArea.Contains(mp);
            var codeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = hoverCode ? UITheme.NeonGreen : UITheme.InkFaint }
            };
            GUI.Label(new Rect(x + 32, topY + 28, w - 64, 16),
                "> initializing secure-sense v0.1...", codeStyle);
            GUI.Label(new Rect(x + 32, topY + 44, w - 64, 16),
                "> classified briefing loaded. clearance level: omega", codeStyle);

            // ── 타이틀 (네온사인 깜빡임 + hover 시 글리치 강화) ──
            var titleRect = new Rect(x, y + 130, w, 70);
            bool hoverTitle = titleRect.Contains(mp);

            // 네온 깜빡임: 기본은 부드러운 호흡 + 가끔 빠른 dropout (실제 형광등 시뮬)
            float t = Time.unscaledTime;
            float baseBreath = 0.82f + 0.18f * Mathf.Sin(t * 1.6f);      // 0.82~1.0 부드러운 호흡
            float noiseSeed = Mathf.PerlinNoise(t * 6f, 3.7f);
            float quickFlicker = noiseSeed < 0.18f ? 0.45f : 1f;          // 18% 확률 짧은 dropout
            float fastSpark = Mathf.Sin(t * 38f) > 0.96f ? 0.6f : 1f;     // 가끔 빠른 깜빡
            float neonBright = Mathf.Clamp01(baseBreath * quickFlicker * fastSpark);

            // hover 시 떨림 + 더 큰 글리치
            float jitter = hoverTitle ? Mathf.Sin(t * 22f) * 1.2f : 0f;
            float glitchOffset = hoverTitle ? 5f : 2f;
            // 글리치(시안/마젠타) — 깜빡임 따라가지만 살짝 다른 위상으로 더 어수선한 느낌
            float glitchPulse = 0.5f + 0.5f * Mathf.Sin(t * 2.3f + 1.1f);
            float glitchAlpha = hoverTitle
                ? (0.7f + 0.3f * glitchPulse)
                : (0.35f + 0.25f * glitchPulse);

            // 메인 텍스트 — 깜빡임이 적용된 흰색
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 56,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(UITheme.Ink.r, UITheme.Ink.g, UITheme.Ink.b, neonBright) }
            };
            var titleGlitchC = new GUIStyle(titleStyle)
            {
                normal = { textColor = new Color(UITheme.NeonCyan.r, UITheme.NeonCyan.g,
                    UITheme.NeonCyan.b, glitchAlpha * neonBright) }
            };
            var titleGlitchM = new GUIStyle(titleStyle)
            {
                normal = { textColor = new Color(UITheme.NeonMagenta.r, UITheme.NeonMagenta.g,
                    UITheme.NeonMagenta.b, glitchAlpha * neonBright) }
            };

            // 외곽 글로우 — 깜빡임이 강할수록 강한 글로우
            if (neonBright > 0.6f)
            {
                float glowAlpha = (neonBright - 0.6f) * 0.4f;
                UITheme.DrawRect(new Rect(titleRect.x, titleRect.y + 18, titleRect.width, 34),
                    new Color(UITheme.NeonCyan.r, UITheme.NeonCyan.g, UITheme.NeonCyan.b, glowAlpha * 0.12f));
            }

            GUI.Label(new Rect(titleRect.x - glitchOffset + jitter, titleRect.y,
                titleRect.width, titleRect.height), "FOR THE COMPANY", titleGlitchC);
            GUI.Label(new Rect(titleRect.x + glitchOffset - jitter, titleRect.y,
                titleRect.width, titleRect.height), "FOR THE COMPANY", titleGlitchM);
            GUI.Label(titleRect, "FOR THE COMPANY", titleStyle);

            // ── 서브타이틀 (hover 시 더 굵어지고 흰색으로) ──
            var subRect = new Rect(x, y + 205, w, 24);
            bool hoverSub = subRect.Contains(mp);
            var subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hoverSub ? 15 : 14,
                fontStyle = hoverSub ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = hoverSub ? Color.white : UITheme.NeonGreen }
            };
            GUI.Label(subRect, "// SECURITY INVESTIGATION // CLASSIFIED", subStyle);

            // ── 인트로 카드 (hover 시 보더 강조 + 본문 흰색) ──
            float cardY = y + 250;
            float cardH = 150;
            var cardRect = new Rect(x + 60, cardY, w - 120, cardH);
            bool hoverCard = cardRect.Contains(mp);
            UITheme.DrawCard(cardRect, hoverCard
                ? UITheme.NeonGreen
                : UITheme.NeonGreen * new Color(1, 1, 1, 0.3f));
            if (hoverCard)
            {
                // 외곽 글로우 시뮬레이션
                UITheme.DrawBorder(new Rect(cardRect.x - 1, cardRect.y - 1,
                    cardRect.width + 2, cardRect.height + 2), UITheme.NeonGreen, 2f);
            }

            // 카드 헤더
            UITheme.DrawRect(new Rect(x + 60, cardY, w - 120, 28),
                hoverCard ? UITheme.Bg4 : UITheme.Bg3);
            var cardHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hoverCard ? 12 : 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(16, 0, 0, 0),
                normal = { textColor = UITheme.NeonGreen }
            };
            GUI.Label(new Rect(x + 60, cardY, w - 120, 28),
                "▶ MISSION BRIEF // INDUSTRIAL ESPIONAGE", cardHeaderStyle);

            // 카드 본문
            var cardBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(20, 20, 14, 14),
                normal = { textColor = hoverCard ? Color.white : UITheme.Ink }
            };
            GUI.Label(new Rect(x + 60, cardY + 28, w - 120, cardH - 28),
                "대기업 연구시설에 산업스파이가 잠입했다.\n" +
                "당신은 보안조사관 — 단서를 모아 진짜 스파이를 정확히 지목하라.",
                cardBodyStyle);

            // 버튼 영역
            float btnY = y + 430;
            float btnW = 340, btnH = 52;
            float btnX = x + (w - btnW) * 0.5f;

            if (UITheme.NeonButton(new Rect(btnX, btnY, btnW, btnH),
                "▶ 수사 시작  [엔터]", UITheme.NeonGreen))
                BeginIntro();

            if (UITheme.GhostButton(new Rect(btnX, btnY + 68, btnW, btnH - 8),
                "▣ 나가기  [ESC]"))
                QuitGame();

            // ── 하단 푸터 — 좌우 hover 시 시안 강조 ──
            float footerY = y + h - 36;
            UITheme.DrawRect(new Rect(x, footerY, w, 1), UITheme.Line);

            var footerLRect = new Rect(x, footerY, w / 2, 36);
            var footerRRect = new Rect(x + w / 2, footerY, w / 2, 36);
            bool hoverFL = footerLRect.Contains(mp);
            bool hoverFR = footerRRect.Contains(mp);

            var footerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hoverFL ? 11 : 10,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(20, 0, 0, 0),
                normal = { textColor = hoverFL ? UITheme.NeonCyan : UITheme.InkFaint }
            };
            GUI.Label(footerLRect,
                "// v0.1 MVP · for the company · build " + Application.version, footerStyle);

            var footerRStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hoverFR ? 11 : 10,
                fontStyle = hoverFR ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 20, 0, 0),
                normal = { textColor = hoverFR ? UITheme.NeonGreen : UITheme.InkFaint }
            };
            GUI.Label(footerRRect,
                "STATUS: STANDBY ●", footerRStyle);
        }

        // ═══════════════════ INTRO SLIDES ═══════════════════

        private void DrawIntro()
        {
            if (introIndex < 0 || introIndex >= introSlides.Length) return;
            var slide = introSlides[introIndex];

            Vector2 mp = UITheme.GetMousePos();

            float w = Mathf.Min(920, Screen.width - 80);
            float h = 600;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            // 메인 패널
            UITheme.DrawWinBar(new Rect(x, y, w, 36),
                $"briefing-{introIndex + 1:D2}.dossier");
            UITheme.DrawRect(new Rect(x, y + 36, w, h - 36), UITheme.Bg1);
            UITheme.DrawBorder(new Rect(x, y, w, h), UITheme.LineStrong);

            // ── 상단 챕터 태그 + dot (hover 시 펄스 강화) ──
            float topY = y + 60;
            var chapterTagRect = new Rect(x + 32, topY, 90, 20);
            bool hoverChapter = chapterTagRect.Contains(mp);
            UITheme.DrawTag(chapterTagRect,
                $"{introIndex + 1:D2} / {introSlides.Length:D2}", UITheme.NeonCyan);
            UITheme.DrawPulseDot(new Vector2(x + 136, topY + 10), UITheme.NeonCyan,
                hoverChapter ? 6f : 4f);

            // ── 헤더 (hover 시 크기 + 흰색으로 강조 + 미세 떨림) ──
            var headerRect = new Rect(x, y + 100, w, 50);
            bool hoverHeader = headerRect.Contains(mp);
            // 진폭 1.5→0.4, 속도 18→9 (눈에 거슬리지 않는 미묘한 떨림)
            float headerJitter = hoverHeader ? Mathf.Sin(Time.unscaledTime * 9f) * 0.4f : 0f;
            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hoverHeader ? 30 : 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(32, 32, 0, 0),
                normal = { textColor = hoverHeader ? Color.white : UITheme.NeonMagenta }
            };
            GUI.Label(new Rect(x + headerJitter, y + 100, w, 50), slide.title, headerStyle);

            // 헤더 아래 구분선
            UITheme.DrawRect(new Rect(x + 32, y + 158, w - 64,
                hoverHeader ? 2 : 1),
                hoverHeader ? UITheme.NeonMagenta : UITheme.Line);

            // ── 본문 카드 (hover 시 보더 시안 + 본문 흰색) ──
            float bodyY = y + 180;
            float bodyH = h - 320;
            var bodyCardRect = new Rect(x + 32, bodyY, w - 64, bodyH);
            bool hoverBody = bodyCardRect.Contains(mp);
            UITheme.DrawCard(bodyCardRect, hoverBody ? UITheme.NeonCyan : null);
            if (hoverBody)
            {
                UITheme.DrawBorder(new Rect(bodyCardRect.x - 1, bodyCardRect.y - 1,
                    bodyCardRect.width + 2, bodyCardRect.height + 2), UITheme.NeonCyan, 2f);
            }

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hoverBody ? 18 : 17,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(28, 28, 24, 24),
                normal = { textColor = hoverBody ? Color.white : UITheme.Ink }
            };
            GUI.Label(new Rect(x + 32, bodyY, w - 64, bodyH), slide.body, bodyStyle);

            // 진행 표시 (작은 dot들 — 챕터 인디케이터)
            float dotsY = y + h - 100;
            float dotSize = 8f, dotGap = 14f;
            float dotsW = introSlides.Length * dotSize + (introSlides.Length - 1) * (dotGap - dotSize);
            float dotsX = x + (w - dotsW) * 0.5f;
            for (int i = 0; i < introSlides.Length; i++)
            {
                Color c = i <= introIndex ? UITheme.NeonGreen : UITheme.Bg4;
                UITheme.DrawRect(new Rect(dotsX + i * dotGap, dotsY, dotSize, dotSize), c);
            }

            // 버튼 (NEXT/DEPLOY 단일 — 중앙 배치, SKIP 제거)
            float btnY = y + h - 68;
            float btnW = 220, btnH = 44;
            float btnX = x + (w - btnW) * 0.5f;

            bool isLast = introIndex == introSlides.Length - 1;
            if (UITheme.NeonButton(new Rect(btnX, btnY, btnW, btnH),
                isLast ? "▶ DEPLOY  [ENTER]" : "▶ NEXT  [ENTER]", UITheme.NeonGreen))
                NextIntro();

            // 하단 힌트
            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UITheme.InkFaint }
            };
            GUI.Label(new Rect(0, Screen.height - 22, Screen.width, 16),
                "// ENTER: 다음  ·  ESC: 뒤로", hintStyle);
        }

        // ═══════════════════ INPUT ═══════════════════

        private void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (phase == Phase.Menu)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter ||
                    e.keyCode == KeyCode.Space)
                    BeginIntro();
                else if (e.keyCode == KeyCode.Escape)
                    QuitGame();
            }
            else
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter ||
                    e.keyCode == KeyCode.Space)
                    NextIntro();
                else if (e.keyCode == KeyCode.Escape)
                {
                    phase = Phase.Menu;
                    introIndex = 0;
                }
            }
        }

        public void BeginIntro() { phase = Phase.Intro; introIndex = 0; }
        public void NextIntro()
        {
            if (introIndex >= introSlides.Length - 1) LoadGameScene();
            else introIndex++;
        }
        public void LoadGameScene()
        {
            var s = GameSession.Instance;
            if (s != null) s.StartNewRun();
            SceneManager.LoadScene(facilitySceneName);
        }
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
