using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ForTheCompany.Core;
using ForTheCompany.Player;
using ForTheCompany.Managers;

namespace ForTheCompany.Systems
{
    public class FacilityHUD : MonoBehaviour
    {
        private GUIStyle bigStyle;
        private GUIStyle midStyle;
        private GUIStyle smallStyle;
        private GUIStyle toastStyle;
        private GUIStyle promptStyle;
        private GUIStyle titleStyle;
        private GUIStyle endStyle;
        private GUIStyle btnStyle;
        private GUIStyle bodyStyle;
        private GUIStyle namePlateStyle;
        private GUIStyle quizQuestionStyle;
        private GUIStyle quizResultStyle;
        private GUIStyle quizButtonStyle;

        public static bool IsInventoryOpen { get; private set; }
        // 이 HUD가 ESC로 창(인벤토리·지목창·레이싱)을 닫은 프레임 — 같은 프레임에 PauseMenu가 그 ESC로 열리는 것 방지
        // (FacilityHUD/PauseMenu 실행 순서와 무관하게 ESC 1회=닫기만 되도록)
        public static int EscConsumedFrame { get; private set; } = -1;
        private Vector2 inventoryScroll;
        private Vector2 endWrongScroll; // 결과창 오답 노트 스크롤
        private bool runEndCleanupDone;  // 결과창 진입 시 남은 창 정리 1회

        private void Awake()
        {
            // static이라 재시작·씬 이동 후에도 남는다 — 새 판은 항상 닫힌 상태로 시작
            IsInventoryOpen = false;
            EscConsumedFrame = -1;
        }

        // ───── 인벤토리/모달 라이트(흰색) 테마 색 팔레트 ─────
        // 보안 교육 모달과 동일한 톤 — 흰 패널 + 검은 글자, 의미 색은 흰 배경 대비 진하게.
        private static readonly Color LtPanel   = new Color(0.97f, 0.98f, 0.99f); // 패널 흰색
        private static readonly Color LtHeader  = new Color(0.91f, 0.93f, 0.96f); // 헤더바
        private static readonly Color LtCard    = new Color(1f, 1f, 1f);          // 카드 흰색
        private static readonly Color LtCardDim = new Color(0.93f, 0.94f, 0.96f); // 흐린 카드(비선택)
        private static readonly Color LtCardHov = new Color(0.85f, 0.92f, 0.97f); // hover 카드(연하늘)
        private static readonly Color LtInk     = new Color(0.08f, 0.09f, 0.11f); // 본문 검정
        private static readonly Color LtInkSub  = new Color(0.32f, 0.35f, 0.40f); // 보조 회색
        private static readonly Color LtInkFaint= new Color(0.55f, 0.58f, 0.63f); // 흐린 회색
        private static readonly Color LtLine    = new Color(0f, 0f, 0f, 0.12f);   // 옅은 구분선
        private static readonly Color LtLineStrong = new Color(0f, 0f, 0f, 0.22f);
        // 의미 색 (흰 배경용 진한 버전 — WCAG AA 통과)
        private static readonly Color LtViolet  = new Color(0.46f, 0.30f, 0.78f); // 보라(알리바이·강조)
        private static readonly Color LtCyan    = new Color(0.06f, 0.42f, 0.47f); // 시안(증언·헤더)
        private static readonly Color LtGreen   = new Color(0.08f, 0.45f, 0.24f); // 초록(단서·선택)
        private static readonly Color LtYellow  = new Color(0.62f, 0.45f, 0.02f); // 노랑(보안교육) — 진한 황금
        private static readonly Color LtMagenta = new Color(0.78f, 0.10f, 0.40f); // 마젠타(용의자·미니게임)
        private static readonly Color LtDanger  = new Color(0.78f, 0.10f, 0.18f); // 빨강(경고·모순단서)

        /// <summary>단서 출처색 → 흰 배경용 진한 버전</summary>
        private static Color GetSourceColorLight(ClueSource src) => src switch
        {
            ClueSource.Environment => LtYellow,
            ClueSource.NPC => LtCyan,
            ClueSource.Minigame => LtGreen,
            _ => LtViolet
        };

        /// <summary>흰 배경용 태그 칩 — 색 채움 + 흰 글자 (UITheme.DrawTag는 다크 전용이라 흐릿함)</summary>
        private static void DrawTagLight(Rect rect, string text, Color accent)
        {
            UITheme.DrawRect(rect, accent); // 진한 색 채움
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(rect, text.ToUpper(), style);
        }

        private void Init()
        {
            if (bigStyle != null) return;
            bigStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            midStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = new Color(0.85f, 0.92f, 1f) }
            };
            smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.7f, 0.75f, 0.85f) }
            };
            toastStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 1f, 0.7f) }
            };
            promptStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.85f, 0.4f) }
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.4f, 0.4f) }
            };
            endStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 46, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 20, padding = new RectOffset(12, 12, 12, 12)
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18, wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            namePlateStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            quizQuestionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20, wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white }
            };
            quizResultStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17, wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 1f, 0.7f) }
            };
            quizButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16, wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 12, 8, 8)
            };
        }

        /// <summary>AccusationPartner NPC 참조 (지목 모달은 이제 NPC가 처리. 빨간 콘솔 삭제됨)</summary>
        private AccusationPartner FindPartner() => AccusationPartner.Instance;

        private void HandleInventoryToggle(Keyboard kb)
        {
            // 인벤토리가 이미 열렸으면 ESC/I로 닫기
            if (IsInventoryOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.iKey.wasPressedThisFrame)
                {
                    IsInventoryOpen = false;
                    // ESC로 닫은 경우, 같은 프레임에 PauseMenu가 그 ESC로 열리지 않도록 표시
                    if (kb.escapeKey.wasPressedThisFrame)
                        EscConsumedFrame = Time.frameCount;
                }
                return;
            }

            // 일시정지 메뉴 / 오프닝 컷씬 중엔 열지 않음 (메뉴 뒤에서 열린 채 남는 것 방지)
            var pm = PauseMenu.Instance;
            if (pm != null && pm.IsOpen) return;
            if (IntroMonologue.IsCutsceneActive) return;

            // 다른 모달 떠 있으면 토글 무시
            var partner = FindPartner();
            if (partner != null && partner.IsMenuOpen) return;
            var rmc = RacingMissionController.Instance;
            if (rmc != null && rmc.IsOpen) return;
            var ds = DialogueSystem.Instance;
            if (ds != null && ds.IsActive) return;
            var sqc = SecurityQuizController.Instance;
            if (sqc != null && sqc.IsOpen) return;
            var bridge = RacingWebViewBridge.Instance;
            if (bridge != null && bridge.IsShowing) return;

            // 결말 후엔 인벤토리 안 열림 (이미 결말 화면)
            var s = GameSession.Instance;
            if (s != null && s.Outcome != RunOutcome.Ongoing) return;

            if (kb.iKey.wasPressedThisFrame)
                IsInventoryOpen = true;
        }

        private void OnGUI()
        {
            UITheme.ApplyWebFont();
            Init();
            DrawNPCNameplates();
            DrawMiniMap();        // 좌상단 — 미니맵
            DrawInfoPanel();      // 우상단 — 통합 UI (단서/시간/목표)
            DrawQuestAdvanceToast();
            DrawInteractionPrompt();
            DrawToast();
            DrawQuizModal();
            DrawRacingModal();
            DrawAccusationModal();
            DrawDialogueBox();
            DrawInventoryPanel();
            DrawEndScreen();
            DrawHint();
        }

        /// <summary>SecureSense 터미널 톤 — 하단 대화창</summary>
        private void DrawDialogueBox()
        {
            var ds = DialogueSystem.Instance;
            if (ds == null || !ds.IsActive) return;

            // 슬라이드 인 애니메이션
            float t = Mathf.Clamp01((Time.time - ds.OpenTime) / 0.25f);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            float boxW = Mathf.Min(Screen.width - 80, 1120);
            float boxH = 130;  // 200 → 130 (본문 최대 2~3줄에 맞춤)
            float boxX = (Screen.width - boxW) * 0.5f;
            float targetY = Screen.height - boxH - 40;
            float startY = Screen.height + 20;
            float boxY = Mathf.Lerp(startY, targetY, eased);

            // 메인 박스 — Bg1 + 네온 그린 강조선
            UITheme.DrawRect(new Rect(boxX, boxY, boxW, boxH), UITheme.Bg1);
            UITheme.DrawBorder(new Rect(boxX, boxY, boxW, boxH), UITheme.Line);
            UITheme.DrawRect(new Rect(boxX, boxY, boxW, 2f), UITheme.NeonGreen);

            // 좌측 NPC 이름 태그 (위로 살짝 튀어나옴)
            if (!string.IsNullOrEmpty(ds.CurrentSpeaker))
            {
                float nameW = Mathf.Min(280f, ds.CurrentSpeaker.Length * 16f + 60f);
                float nameH = 32f;
                float nameX = boxX + 24f;
                float nameY = boxY - nameH * 0.5f;

                UITheme.DrawRect(new Rect(nameX, nameY, nameW, nameH), UITheme.Bg3);
                UITheme.DrawBorder(new Rect(nameX, nameY, nameW, nameH), UITheme.NeonGreen);

                var nameStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = UITheme.NeonGreen }
                };
                GUI.Label(new Rect(nameX, nameY, nameW, nameH),
                    "▶ " + ds.CurrentSpeaker.ToUpper(), nameStyle);

                // 우측 윈도우 chrome — "transmission.log"
                var winLabel = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleRight,
                    padding = new RectOffset(0, 24, 0, 0),
                    normal = { textColor = UITheme.InkFaint }
                };
                GUI.Label(new Rect(boxX, boxY + 8, boxW, 16),
                    $"// {ds.CurrentSpeaker.ToLower()}-transmission.log", winLabel);
            }

            // 본문 텍스트 — 타이프라이팅
            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18, wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = UITheme.Ink }
            };
            float padX = 36, padTop = 30, padBottom = 30;
            GUI.Label(new Rect(boxX + padX, boxY + padTop, boxW - padX * 2, boxH - padTop - padBottom),
                ds.CurrentVisibleLine, bodyStyle);

            // 진행/선택지
            if (ds.AwaitingChoice)
            {
                DrawDialogueChoices(ds);
            }
            else if (ds.LineComplete)
            {
                // 우하단 진행 안내 (블링킹)
                float blink = Mathf.Abs(Mathf.Sin(Time.time * 3f));
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.6f + 0.4f * blink);
                var promptSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                    padding = new RectOffset(0, 28, 0, 0),
                    normal = { textColor = UITheme.NeonGreen }
                };
                string hint = ds.IsLastLine ? "▶ [SPACE] END TRANSMISSION" : "▶ [SPACE] CONTINUE";
                GUI.Label(new Rect(boxX, boxY + boxH - 26, boxW, 18), hint, promptSt);
                GUI.color = prev;
            }
        }

        /// <summary>SecureSense 톤 — 우측 세로 선택지 박스 (마우스/숫자키)</summary>
        private void DrawDialogueChoices(DialogueSystem ds)
        {
            var choices = ds.CurrentChoices;
            if (choices == null || choices.Count == 0) return;

            float boxW = 420f;
            float boxX = Screen.width - boxW - 30f;
            float gap = 10f;

            // 라벨 스타일
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(48, 16, 10, 10)
            };
            var numStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UITheme.NeonGreen }
            };

            // 전체 높이 미리 계산 → 화면 세로 중앙에 정렬
            float totalH = 0f;
            var heights = new float[choices.Count];
            for (int i = 0; i < choices.Count; i++)
            {
                float labelH = labelStyle.CalcHeight(new GUIContent(choices[i].Label), boxW - 80);
                heights[i] = Mathf.Max(54f, labelH + 10f);
                totalH += heights[i];
            }
            totalH += gap * (choices.Count - 1);
            // 헤더(24px) 포함해서 중앙 정렬
            float headerH = 24f;
            float startY = (Screen.height - (totalH + headerH)) * 0.5f + headerH;

            // ── 헤더 ──
            var headerSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 0, 0, 0),
                normal = { textColor = UITheme.NeonCyan }
            };
            GUI.Label(new Rect(boxX, startY - headerH, boxW, 20),
                "▶ RESPONSE OPTIONS // SELECT ONE", headerSt);

            Vector2 mp = UITheme.GetMousePos();
            float currentY = startY;

            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                var content = new GUIContent(c.Label);
                float boxH = heights[i];
                var rect = new Rect(boxX, currentY, boxW, boxH);

                bool hover = rect.Contains(mp);

                // 배경 + 보더
                UITheme.DrawRect(rect, hover ? UITheme.Bg3 : UITheme.Bg2);
                UITheme.DrawBorder(rect, hover ? UITheme.NeonGreen : UITheme.Line);

                // 좌측 강조 띠
                UITheme.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height),
                    hover ? UITheme.NeonGreen : UITheme.NeonCyan);

                // 번호 (1, 2, 3 — 0 prefix 제거, 가운데 정렬)
                GUI.Label(new Rect(rect.x + 4, rect.y, 36, rect.height),
                    $"{i + 1}", numStyle);

                // 텍스트 — 항상 흰색 (가독성)
                labelStyle.normal.textColor = Color.white;
                GUI.Label(rect, content, labelStyle);

                // 투명 버튼 — 클릭 감지
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    ds.SelectChoice(i);
                }

                currentY += boxH + gap;
            }
            // 하단 힌트는 제거 (사용자 요청)
        }

        // ═══════════════════ 미니맵 (좌상단) ═══════════════════

        // 시설 좌표 범위 — 실제 방 경계(SciFiFloorsSetup 카펫 중심±폭)에 타이트하게 맞춤
        // X: 연구실 좌단 -22 / 카드키 우단 22 → 여유 두고 -25~25
        // Z: 아래 방 -14-4=-18 / 위 방 11+4=15 → -19~16
        private const float FacMinX = -25f, FacMaxX = 25f;
        private const float FacMinZ = -19f, FacMaxZ = 16f;

        // 방 정보: (이름, 중심 X, 중심 Z, 폭, 깊이, 색)
        private static readonly (string label, float cx, float cz, float w, float d, System.Func<Color> col)[] Rooms =
        {
            ("연구실",     -17f, 11f, 10f, 8f, () => UITheme.NeonCyan),
            ("서버실",      0f,  11f, 10f, 8f, () => UITheme.NeonMagenta),
            ("보안통제실", 13f,  11f, 10f, 8f, () => UITheme.NeonViolet),
            ("휴게실",    -13f,  0f,  10f, 8f, () => UITheme.NeonGreen),
            ("카드키",     18f,  2f,  8f,  8f, () => UITheme.Danger),
            ("창고",      -20f, -14f, 10f, 8f, () => UITheme.NeonYellow),
            ("데이터센터", 3f,  -14f, 16f, 8f, () => UITheme.NeonCyan),
            ("전력실",    18f,  -11f, 8f,  8f, () => UITheme.NeonYellow),
        };

        /// <summary>레이싱 WebView / 풀스크린 모달이 떠 있으면 HUD 패널 숨김</summary>
        private bool ShouldHideTopHud()
        {
            // 보안 레이싱 WebView (가장 큰 이유 — Unity OnGUI 위 또는 아래에 별도 렌더링)
            var bridge = RacingWebViewBridge.Instance;
            if (bridge != null && bridge.IsShowing) return true;

            // 풀스크린 모달들
            var sqc = SecurityQuizController.Instance;
            if (sqc != null && sqc.IsOpen) return true;

            var rmc = RacingMissionController.Instance;
            if (rmc != null && rmc.IsOpen) return true;

            var partner = FindPartner();
            if (partner != null && partner.IsMenuOpen) return true;

            if (IsInventoryOpen) return true;

            // 일시정지 메뉴 / 오프닝 컷씬 중에도 상단 HUD 숨김
            var pm = PauseMenu.Instance;
            if (pm != null && pm.IsOpen) return true;
            if (IntroMonologue.IsCutsceneActive) return true;

            var s = GameSession.Instance;
            if (s != null && s.Outcome != RunOutcome.Ongoing) return true;

            return false;
        }

        /// <summary>좌상단 미니맵 — 시설 평면도 + 실시간 플레이어 위치</summary>
        private void DrawMiniMap()
        {
            if (ShouldHideTopHud()) return;

            float panelW = 300f, panelH = 240f;
            float panelX = 16f, panelY = 16f;
            var panel = new Rect(panelX, panelY, panelW, panelH);

            // 패널 베이스
            UITheme.DrawRect(panel, UITheme.Bg1);
            UITheme.DrawBorder(panel, UITheme.LineStrong, 1f);

            // 윈도우 헤더
            var headerRect = new Rect(panelX, panelY, panelW, 28);
            UITheme.DrawRect(headerRect, UITheme.Bg3);
            UITheme.DrawRect(new Rect(panelX, panelY + 28, panelW, 1), UITheme.Line);

            var headerSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 0, 0, 0),
                normal = { textColor = UITheme.NeonGreen }
            };
            GUI.Label(headerRect, "▶ 시설 평면도 // 실시간", headerSt);
            UITheme.DrawPulseDot(new Vector2(panelX + panelW - 18, panelY + 14), UITheme.NeonGreen, 3f);

            // 맵 영역 (헤더 제외)
            float mapMargin = 12f;
            float mapX = panelX + mapMargin;
            float mapY = panelY + 28 + mapMargin;
            float mapW = panelW - mapMargin * 2;
            float mapH = panelH - 28 - mapMargin * 2;
            var mapRect = new Rect(mapX, mapY, mapW, mapH);

            // 맵 배경 (그리드)
            UITheme.DrawRect(mapRect, UITheme.Bg0);

            // 그리드 라인 (4×4 분할 — 흐릿하게)
            var gridCol = new Color(UITheme.NeonGreen.r, UITheme.NeonGreen.g, UITheme.NeonGreen.b, 0.06f);
            for (int i = 1; i < 4; i++)
            {
                float gx = mapX + (mapW / 4f) * i;
                float gy = mapY + (mapH / 4f) * i;
                UITheme.DrawRect(new Rect(gx, mapY, 1, mapH), gridCol);
                UITheme.DrawRect(new Rect(mapX, gy, mapW, 1), gridCol);
            }

            // 방 사각형 + 라벨
            var roomLabelSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UITheme.Ink }
            };
            foreach (var r in Rooms)
            {
                Vector2 minWorld = new Vector2(r.cx - r.w * 0.5f, r.cz - r.d * 0.5f);
                Vector2 maxWorld = new Vector2(r.cx + r.w * 0.5f, r.cz + r.d * 0.5f);
                Vector2 minPx = WorldToMap(minWorld, mapRect);
                Vector2 maxPx = WorldToMap(maxWorld, mapRect);
                // Z가 클수록 화면 위 → minPx.y가 maxPx보다 크므로 정렬
                float rx = Mathf.Min(minPx.x, maxPx.x);
                float ry = Mathf.Min(minPx.y, maxPx.y);
                float rw = Mathf.Abs(maxPx.x - minPx.x);
                float rh = Mathf.Abs(maxPx.y - minPx.y);
                var roomRect = new Rect(rx, ry, rw, rh);

                Color rc = r.col();
                UITheme.DrawRect(roomRect, new Color(rc.r, rc.g, rc.b, 0.18f));
                UITheme.DrawBorder(roomRect, new Color(rc.r, rc.g, rc.b, 0.7f), 1f);

                // 방 이름 (방이 충분히 크면)
                if (rw > 32 && rh > 16)
                    GUI.Label(roomRect, r.label, roomLabelSt);
            }

            // NPC 점 표시
            var roster = NPCRoster.Instance;
            if (roster != null)
            {
                foreach (var npc in roster.npcs)
                {
                    if (npc == null) continue;
                    Vector2 p = WorldToMap(
                        new Vector2(npc.transform.position.x, npc.transform.position.z), mapRect);
                    // 의심도에 따라 색
                    int sus = npc.suspicion;
                    Color npcCol = sus >= 7 ? UITheme.Danger
                        : sus >= 4 ? UITheme.NeonYellow
                        : UITheme.InkDim;
                    UITheme.DrawRect(new Rect(p.x - 3, p.y - 3, 6, 6), npcCol);
                }
            }

            // 플레이어 점 (펄스 — 가장 큰 강조)
            var player = PlayerInteractor.Instance;
            if (player != null)
            {
                Vector2 pp = WorldToMap(
                    new Vector2(player.transform.position.x, player.transform.position.z),
                    mapRect);

                // 외곽 펄스 링 (반투명, 큼)
                float pulse = 0.4f + 0.4f * Mathf.Sin(Time.unscaledTime * 4f);
                var ringCol = new Color(UITheme.NeonGreen.r, UITheme.NeonGreen.g,
                    UITheme.NeonGreen.b, pulse * 0.5f);
                float ringR = 8f;
                UITheme.DrawRect(new Rect(pp.x - ringR, pp.y - ringR, ringR * 2, ringR * 2), ringCol);

                // 중앙 솔리드 점
                UITheme.DrawRect(new Rect(pp.x - 4, pp.y - 4, 8, 8), UITheme.NeonGreen);
                UITheme.DrawBorder(new Rect(pp.x - 5, pp.y - 5, 10, 10), UITheme.Bg0, 1f);
            }

            // 맵 영역 보더
            UITheme.DrawBorder(mapRect, UITheme.Line, 1f);
        }

        /// <summary>월드 (x, z) → 미니맵 픽셀 좌표. Z↑ = 화면↑ (Y 뒤집기)</summary>
        private static Vector2 WorldToMap(Vector2 world, Rect mapRect)
        {
            float u = Mathf.InverseLerp(FacMinX, FacMaxX, world.x);
            float v = Mathf.InverseLerp(FacMinZ, FacMaxZ, world.y);
            float px = mapRect.x + u * mapRect.width;
            // Z가 클수록 화면 위쪽이므로 v 뒤집기
            float py = mapRect.y + (1f - v) * mapRect.height;
            return new Vector2(px, py);
        }

        // ═══════════════════ 통합 정보 패널 (우상단) ═══════════════════

        /// <summary>우상단 통합 패널 — 수사 진행 + 단서·시간 + 현재 목표</summary>
        private void DrawInfoPanel()
        {
            if (ShouldHideTopHud()) return;

            var s = GameSession.Instance;
            var quest = QuestManager.Instance;

            int clues = s != null ? s.totalClues : 0;

            float w = 340f;
            float x = Screen.width - w - 16f;
            float y = 16f;

            // 헤더 + 단서 섹션 높이 고정
            float headerH = 32f;
            float statsH = 70f;
            float objectiveH = 0f;
            bool hasQuest = quest != null && quest.CurrentStage != QuestManager.Stage.Done;

            // 본문 사이즈 계산용 스타일
            var objBodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = UITheme.Ink }
            };
            var locHintSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = UITheme.NeonCyan }
            };

            float contentW = w - 28;
            float bodyH = 0, hintH = 0;
            if (hasQuest)
            {
                bodyH = objBodySt.CalcHeight(new GUIContent(quest.CurrentObjective), contentW);
                hintH = locHintSt.CalcHeight(new GUIContent(quest.CurrentLocationHint), contentW);
                // 미니헤더(24) + 본문 + 진행dot(16) + 위치힌트 + 패딩
                objectiveH = 24 + 8 + bodyH + 10 + 16 + 6 + hintH + 16;
            }

            float h = headerH + statsH + objectiveH + 12;

            var panel = new Rect(x, y, w, h);
            UITheme.DrawRect(panel, UITheme.Bg1);
            UITheme.DrawBorder(panel, UITheme.LineStrong, 1f);

            // ── 헤더 ──
            var headerRect = new Rect(x, y, w, headerH);
            UITheme.DrawRect(headerRect, UITheme.Bg3);
            UITheme.DrawRect(new Rect(x, y + headerH, w, 1), UITheme.Line);

            var headerSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 0, 0, 0),
                normal = { textColor = UITheme.NeonGreen }
            };
            GUI.Label(headerRect, "▶ 수사 진행 중", headerSt);
            UITheme.DrawPulseDot(new Vector2(x + w - 18, y + 16), UITheme.NeonGreen, 4f);

            // ── 수집한 단서 (제한 시간 없음 — 남은 시간 칸 제거) ──
            float statsY = y + headerH + 6;

            var labelSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 0, 0, 0),
                normal = { textColor = UITheme.InkDim }
            };

            GUI.Label(new Rect(x, statsY, w, 16), "수집한 단서", labelSt);
            var clueValSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 0, 0, 0),
                normal = { textColor = UITheme.Ink }
            };
            GUI.Label(new Rect(x, statsY + 18, w, 36), clues.ToString("D2"), clueValSt);

            // ── 현재 목표 섹션 ──
            if (!hasQuest) return;

            int total = (int)QuestManager.Stage.Done;
            int step = (int)quest.CurrentStage + 1;

            float objY = y + headerH + statsH;
            UITheme.DrawRect(new Rect(x + 14, objY, w - 28, 1), UITheme.Line);

            // 미니 헤더 (마젠타)
            var miniHeaderSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 0, 0, 0),
                normal = { textColor = UITheme.NeonMagenta }
            };
            GUI.Label(new Rect(x, objY + 4, w - 60, 20),
                $"▶ 현재 목표 // 단계 {step:D2}", miniHeaderSt);

            var ratioSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 14, 0, 0),
                normal = { textColor = UITheme.InkDim }
            };
            GUI.Label(new Rect(x, objY + 4, w, 20), $"{step:D2} / {total:D2}", ratioSt);

            // 본문
            float cy = objY + 30;
            GUI.Label(new Rect(x + 14, cy, contentW, bodyH), quest.CurrentObjective, objBodySt);
            cy += bodyH + 10;

            // 진행도 dot들
            float dotSize = 6f, dotGap = 12f;
            float dotsX = x + 14;
            for (int i = 0; i < total; i++)
            {
                Color c = i < step ? UITheme.NeonGreen : UITheme.Bg4;
                UITheme.DrawRect(new Rect(dotsX + i * dotGap, cy + 5, dotSize, dotSize), c);
            }
            cy += 16;

            // 위치 힌트
            GUI.Label(new Rect(x + 14, cy + 4, contentW, hintH),
                "▶ " + quest.CurrentLocationHint, locHintSt);
        }

        // 단계 완료 토스트가 NPC 대화 토스트 종료 후 시작되도록 추적
        private string lastShownAdvanceText;
        private float advanceToastStartTime = -1f;
        private const float AdvanceToastDuration = 4f;

        /// <summary>NPC 대화 토스트가 끝난 뒤에 단계 완료 토스트 표시 (순차 흐름)</summary>
        private void DrawQuestAdvanceToast()
        {
            var quest = QuestManager.Instance;
            if (quest == null || string.IsNullOrEmpty(quest.LastAdvanceText)) return;

            // 새로운 단계 진행이 들어왔는지 감지
            if (quest.LastAdvanceText != lastShownAdvanceText)
            {
                lastShownAdvanceText = quest.LastAdvanceText;
                advanceToastStartTime = -1f; // NPC 토스트 종료 대기
            }

            // 하단 대화창 또는 NPC 대화 토스트가 활성이면 대기
            if (advanceToastStartTime < 0f)
            {
                var ds = DialogueSystem.Instance;
                if (ds != null && ds.IsActive) return; // 대화창 종료 대기
                var p = PlayerInteractor.Instance;
                bool talkActive = p != null
                    && !string.IsNullOrEmpty(p.LastInteractionResult)
                    && (Time.time - p.LastInteractionTime) <= 4f;
                if (talkActive) return; // 대화 토스트 종료 대기
                advanceToastStartTime = Time.time;
            }

            float elapsed = Time.time - advanceToastStartTime;
            if (elapsed > AdvanceToastDuration) return;

            float w = 640;
            float contentW = w - 60;
            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = UITheme.Ink }
            };
            float bodyH = bodySt.CalcHeight(new GUIContent(quest.LastAdvanceText), contentW);

            float headerH = 52;
            float pad = 18;
            float h = headerH + bodyH + pad * 2;

            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.24f;
            float alpha = Mathf.Clamp01(1f - (elapsed - 3f));
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            var toastRect = new Rect(x, y, w, h);

            // ── 패널 ──
            UITheme.DrawRect(toastRect, UITheme.Bg2);
            UITheme.DrawBorder(toastRect, UITheme.NeonGreen, 1f);

            // 좌측 강조선 (네온 그린)
            UITheme.DrawRect(new Rect(x, y, 3f, h), UITheme.NeonGreen);

            // 펄스 dot + 헤더
            UITheme.DrawPulseDot(new Vector2(x + 22, y + 22), UITheme.NeonGreen, 4f);

            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonGreen }
            };
            GUI.Label(new Rect(x + 36, y + 14, w - 60, 14),
                "▶ STAGE CLEARED // PROGRESS UPDATED", tagSt);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.Ink }
            };
            GUI.Label(new Rect(x + 36, y + 28, w - 60, 28),
                "단계 완료", titleSt);

            // 본문
            GUI.Label(new Rect(x + pad + 18, y + pad + headerH, contentW, bodyH),
                quest.LastAdvanceText, bodySt);

            GUI.color = prev;
        }

        private void DrawRacingModal()
        {
            var rmc = RacingMissionController.Instance;
            if (rmc == null || !rmc.IsOpen) return;

            // 임베드 WebView가 보이는 중엔 Unity 등수 입력 모달 숨김
            var bridge = RacingWebViewBridge.Instance;
            bool isEmbedded = bridge != null && bridge.IsShowing;
            if (rmc.CurrentPhase == RacingMissionController.Phase.AwaitingRank && isEmbedded)
            {
                DrawRacingEmbeddedClose(rmc);
                return;
            }

            // 어둠 오버레이
            UITheme.DrawRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(UITheme.Bg0.r, UITheme.Bg0.g, UITheme.Bg0.b, 0.86f));
            UITheme.DrawScanlines(new Rect(0, 0, Screen.width, Screen.height), 0.05f);

            float w = Mathf.Min(680, Screen.width - 80);
            float h = 500;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            var rect = new Rect(x, y, w, h);
            UITheme.DrawRect(rect, UITheme.Bg1);
            UITheme.DrawBorder(rect, UITheme.NeonCyan, 1f);

            // 윈도우 헤더
            UITheme.DrawWinBar(new Rect(x, y, w, 32), "security-race.module");

            // 헤더 섹션
            float headerY = y + 50;
            UITheme.DrawPulseDot(new Vector2(x + 28, headerY + 12), UITheme.NeonCyan, 4f);

            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonCyan }
            };
            GUI.Label(new Rect(x + 44, headerY + 2, w - 80, 14),
                "▶ HIGH-SPEED INFILTRATION // STAGE", tagSt);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.Ink }
            };
            GUI.Label(new Rect(x + 44, headerY + 16, w - 80, 32),
                "SECURITY RACE", titleSt);

            // 구분선
            UITheme.DrawRect(new Rect(x + 24, headerY + 58, w - 48, 1), UITheme.Line);

            if (rmc.CurrentPhase == RacingMissionController.Phase.AwaitingRank)
                DrawRankInputModal(rmc, x, y, w, h);
            else if (rmc.CurrentPhase == RacingMissionController.Phase.Finished)
                DrawRacingFinished(rmc, x, y, w, h);
        }

        private void DrawRacingEmbeddedClose(RacingMissionController rmc)
        {
            // 중앙 상단 — 우상단 InfoPanel과 겹치지 않도록
            float w = 360, h = 38;
            float x = (Screen.width - w) * 0.5f;
            float y = 12;

            var rect = new Rect(x, y, w, h);
            UITheme.DrawRect(rect, new Color(UITheme.Bg1.r, UITheme.Bg1.g, UITheme.Bg1.b, 0.92f));
            UITheme.DrawBorder(rect, UITheme.NeonCyan, 1f);

            UITheme.DrawPulseDot(new Vector2(x + 14, y + h * 0.5f), UITheme.NeonCyan, 3f);

            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = UITheme.NeonCyan }
            };
            GUI.Label(new Rect(x + 28, y, 180, h),
                "▶ 보안 레이싱 진행 중", tagSt);

            var hintSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = UITheme.InkDim }
            };
            GUI.Label(new Rect(x + 200, y, w - 216, h),
                "[ESC] 강제 종료", hintSt);
        }

        private void DrawRankInputModal(RacingMissionController rmc, float x, float y, float w, float h)
        {
            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = UITheme.InkDim }
            };
            GUI.Label(new Rect(x + 30, y + 120, w - 60, 80),
                "SECURITY RACE를 플레이하셨습니다.\n1등으로 결승선을 통과하면 클리어, 그 외엔 재시도하세요.",
                bodySt);

            float btnH = 60;
            float gap = 10;
            float startY = y + 220;

            DrawRankButton(new Rect(x + 30, startY, w - 60, btnH),
                "▶ 1등 했음 — 클리어 (+5 단서)",
                UITheme.NeonGreen, () => rmc.ReportRank(1));

            DrawRankButton(new Rect(x + 30, startY + (btnH + gap), w - 60, btnH),
                "▶ 1등 못 함 — 재시도 (보상 없음)",
                UITheme.InkDim, () => rmc.Cancel());
        }

        private void DrawRankButton(Rect r, string label, Color tint, System.Action onClick)
        {
            bool hover = r.Contains(UITheme.GetMousePos());
            UITheme.DrawRect(r, hover ? new Color(tint.r, tint.g, tint.b, 0.14f) : UITheme.Bg2);
            UITheme.DrawBorder(r, hover ? tint : UITheme.Line, hover ? 2f : 1f);

            var s = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = hover ? tint : UITheme.Ink },
                padding = new RectOffset(20, 0, 0, 0)
            };
            GUI.Label(r, label, s);

            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) onClick?.Invoke();
        }

        private void DrawRacingFinished(RacingMissionController rmc, float x, float y, float w, float h)
        {
            string rankLabel = rmc.FinalRank == 1 ? "1ST"
                             : rmc.FinalRank == 2 ? "2ND"
                             : "3RD";
            Color rankColor = rmc.FinalRank == 1 ? UITheme.NeonYellow : UITheme.NeonCyan;

            var rankS = new GUIStyle(GUI.skin.label)
            {
                fontSize = 56, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = rankColor }
            };
            GUI.Label(new Rect(x, y + 130, w, 80), rankLabel, rankS);

            var subSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UITheme.InkDim }
            };
            GUI.Label(new Rect(x, y + 200, w, 22),
                rmc.FinalRank == 1 ? "// SUCCESS"
                : rmc.FinalRank == 2 ? "// RUNNER-UP"
                : "// FINISHED", subSt);

            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, wordWrap = true,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = UITheme.Ink }
            };
            GUI.Label(new Rect(x + 30, y + 240, w - 60, 80),
                rmc.ResultMessage, bodySt);

            // 확인 버튼
            if (UITheme.NeonButton(new Rect(x + (w - 280) * 0.5f, y + h - 70, 280, 46),
                "▶ ACKNOWLEDGE  [SPACE / ESC / ENTER]", rankColor))
                rmc.Acknowledge();
        }

        private static Texture2D _solid;
        private static void DrawSolidRect(Rect r, Color c)
        {
            if (_solid == null)
            {
                _solid = new Texture2D(1, 1);
                _solid.SetPixel(0, 0, Color.white);
                _solid.Apply();
            }
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _solid);
            GUI.color = prev;
        }

        private void DrawNPCNameplates()
        {
            // WebView 레이싱 / 일시정지 / 컷씬 / 모달 중엔 이름표 숨김
            var bridge = RacingWebViewBridge.Instance;
            if (bridge != null && bridge.IsShowing) return;
            var pm = PauseMenu.Instance;
            if (pm != null && pm.IsOpen) return;
            if (IntroMonologue.IsCutsceneActive) return;
            var sqc = SecurityQuizController.Instance;
            if (sqc != null && sqc.IsOpen) return;
            var rmc = RacingMissionController.Instance;
            if (rmc != null && rmc.IsOpen) return;
            if (IsInventoryOpen) return;
            var partnerMenu = FindPartner();
            if (partnerMenu != null && partnerMenu.IsMenuOpen) return;

            var cam = Camera.main;
            if (cam == null) return;

            var nameSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UITheme.Ink }
            };

            // 용의자 NPC 3명 — 의심도 추적
            var roster = NPCRoster.Instance;
            if (roster != null)
            {
                foreach (var npc in roster.npcs)
                {
                    if (npc == null) continue;
                    DrawSingleNameplate(cam, nameSt, npc.transform, npc.DisplayName,
                        npc.suspicion, fixedBorder: null);
                }
            }

            // 경비원 — 의심도 없음, NeonCyan 보더로 구분 (보안 직군)
            var guard = GuardNPC.Instance;
            if (guard != null)
            {
                DrawSingleNameplate(cam, nameSt, guard.transform, guard.displayName,
                    suspicion: 0, fixedBorder: UITheme.NeonCyan);
            }

            // AI로봇 한세 — 보안통제실 동료 (NeonViolet 보더로 구분)
            var partner = AccusationPartner.Instance;
            if (partner != null)
            {
                DrawSingleNameplate(cam, nameSt, partner.transform, partner.displayName,
                    suspicion: 0, fixedBorder: UITheme.NeonViolet);
            }
        }

        /// <summary>이름표 한 개 그리기 — NPC 머리 가까이 (Y +1.7), 의심도 막대 옵션</summary>
        private static void DrawSingleNameplate(Camera cam, GUIStyle nameSt,
            Transform t, string name, int suspicion, Color? fixedBorder)
        {
            // 머리 위 1.7 단위 (기존 2.4 → 1.7, 캐릭터에 더 가깝게)
            Vector3 worldHead = t.position + Vector3.up * 1.7f;
            Vector3 screen = cam.WorldToScreenPoint(worldHead);
            if (screen.z < 0) return;

            float guiY = Screen.height - screen.y;
            float w = 160f, h = 24f;
            var rect = new Rect(screen.x - w * 0.5f, guiY - h * 0.5f, w, h);

            Color borderColor = fixedBorder
                ?? (suspicion >= 7 ? UITheme.Danger
                    : suspicion >= 4 ? UITheme.NeonYellow
                    : UITheme.Line);

            UITheme.DrawRect(rect, new Color(UITheme.Bg1.r, UITheme.Bg1.g, UITheme.Bg1.b, 0.92f));
            UITheme.DrawBorder(rect, borderColor, 1f);
            GUI.Label(rect, name, nameSt);

            // 의심도 막대 (이름표 바로 아래) — 의심도 0이면 안 그림
            if (suspicion > 0)
            {
                const float MaxSuspicion = 10f;
                float barW = 130f, barH = 4f;
                float barX = screen.x - barW * 0.5f;
                float barY = guiY + h * 0.5f + 3f;

                UITheme.DrawRect(new Rect(barX, barY, barW, barH), UITheme.Bg3);
                float pct = Mathf.Clamp01(suspicion / MaxSuspicion);
                Color susColor = suspicion >= 7 ? UITheme.Danger
                    : suspicion >= 4 ? UITheme.NeonYellow
                    : UITheme.InkDim;
                UITheme.DrawRect(new Rect(barX, barY, barW * pct, barH), susColor);
            }
        }

        private void DrawQuizModal()
        {
            var ctrl = SecurityQuizController.Instance;
            if (ctrl == null) return;
            var gsEnd = GameSession.Instance;
            if (gsEnd != null && gsEnd.Outcome != RunOutcome.Ongoing) return; // 결과창이 우선
            // Show result toast (briefly)
            if (!ctrl.IsOpen && !string.IsNullOrEmpty(ctrl.LastResultText)
                && Time.time - ctrl.LastResultTime < 3.5f && ctrl.LastWasCorrect)
            {
                DrawQuizResultToast(ctrl);
                return;
            }
            if (!ctrl.IsOpen) return;

            var data = ctrl.ActiveClue.data;

            // ── 흰색(라이트) 테마 색상 — 보안 교육 모달 전용 ──
            // 가독성 위해 패널은 흰색, 글자는 검은색 계열, 액센트만 보라/시안 유지
            Color panelBg     = new Color(0.97f, 0.98f, 0.99f);  // 거의 흰색
            Color headerBg    = new Color(0.91f, 0.93f, 0.96f);  // 헤더바 (살짝 회색)
            Color inkBlack    = new Color(0.08f, 0.09f, 0.11f);  // 본문 검정
            Color inkSub      = new Color(0.32f, 0.35f, 0.40f);  // 보조 회색
            Color lineLight   = new Color(0f, 0f, 0f, 0.12f);    // 옅은 구분선
            Color optBg       = new Color(1f, 1f, 1f, 1f);       // 선택지 기본(흰)
            Color optHoverBg  = new Color(0.85f, 0.80f, 0.98f);  // 선택지 hover (연보라)
            Color accentViolet= new Color(0.46f, 0.30f, 0.78f);  // 진한 보라 (흰 배경 대비)
            Color accentVioletDark = new Color(0.34f, 0.20f, 0.62f); // hover 배경용 더 진한 보라
            Color dangerDark  = new Color(0.78f, 0.10f, 0.18f);  // 흰 배경용 진한 빨강 (경고·오답)
            Color greenDark   = new Color(0.08f, 0.45f, 0.24f);  // 흰 배경용 진한 초록 (진행률)
            Color cyanDark    = new Color(0.06f, 0.42f, 0.47f);  // 흰 배경용 진한 시안 (위치 태그)

            // 어둠 오버레이 (배경은 그대로 어둡게 — 모달만 부각)
            UITheme.DrawRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(UITheme.Bg0.r, UITheme.Bg0.g, UITheme.Bg0.b, 0.90f));

            // ── 사이즈 확대: 780×600 → 900×680 (화면 여백 확보) ──
            float w = Mathf.Min(900, Screen.width - 60);
            float h = Mathf.Min(680, Screen.height - 60);
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            var rect = new Rect(x, y, w, h);

            // 흰색 패널 + 보라 외곽선 (살짝 두껍게)
            UITheme.DrawRect(rect, panelBg);
            UITheme.DrawBorder(rect, accentViolet, 2f);

            // ── 헤더바 (라이트 테마 전용 — DrawWinBar 대신 직접 그림) ──
            float barH = 38f;
            UITheme.DrawRect(new Rect(x, y, w, barH), headerBg);
            UITheme.DrawRect(new Rect(x, y + barH - 1, w, 1), lineLight);
            // mac 스타일 dot 3개
            float dotY = y + barH * 0.5f - 6f;
            UITheme.DrawRect(new Rect(x + 16, dotY, 12, 12), new Color(1f, 0.37f, 0.34f));
            UITheme.DrawRect(new Rect(x + 34, dotY, 12, 12), new Color(1f, 0.74f, 0.18f));
            UITheme.DrawRect(new Rect(x + 52, dotY, 12, 12), new Color(0.16f, 0.78f, 0.25f));
            var barTitleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = inkSub }
            };
            GUI.Label(new Rect(x, y, w, barH), "security-training.module", barTitleSt);
            // 우상단 안내 (필수 미션)
            var lockedSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 16, 0, 0),
                normal = { textColor = dangerDark }
            };
            GUI.Label(new Rect(x, y, w, barH), "▶ 필수 미션 // 정답 시 자동 종료", lockedSt);

            // ── 헤더 섹션 ──
            float headerY = y + barH + 16;
            UITheme.DrawPulseDot(new Vector2(x + 30, headerY + 13), accentViolet, 5f);

            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                normal = { textColor = accentViolet }
            };
            GUI.Label(new Rect(x + 48, headerY, w - 120, 16),
                "▶ SECURITY TRAINING // MODULE", tagSt);

            // 진행률 표시 (1/3, 2/3, 3/3) — 우측 상단
            if (ctrl.SessionTotal > 1)
            {
                var progressSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                    padding = new RectOffset(0, 20, 0, 0),
                    normal = { textColor = greenDark } // 진한 초록 (AA 통과)
                };
                GUI.Label(new Rect(x, headerY, w, 26),
                    $"▶ 문제 {ctrl.SessionCurrent} / {ctrl.SessionTotal}", progressSt);
            }

            // 큰 타이틀
            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30, fontStyle = FontStyle.Bold,
                normal = { textColor = inkBlack }
            };
            GUI.Label(new Rect(x + 48, headerY + 20, w - 120, 40),
                "보안 교육 미션", titleSt);

            // 위치 / 객체 태그
            var locTagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold,
                normal = { textColor = cyanDark } // 진한 시안 (AA 통과)
            };
            GUI.Label(new Rect(x + 30, headerY + 66, w - 60, 20),
                $"▶ {data.roomName.ToUpper()}  //  {data.objectLabel.ToUpper()}", locTagSt);

            // 구분선
            UITheme.DrawRect(new Rect(x + 28, headerY + 94, w - 56, 1), lineLight);

            // 큰 질문 텍스트
            var qSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 21, fontStyle = FontStyle.Bold, wordWrap = true,
                normal = { textColor = inkBlack }
            };
            float qY = headerY + 110;
            // 선택지·피드백이 모달 밖으로 밀려나지 않도록 질문 높이를 상한 클램프
            int n = data.quizOptions != null ? data.quizOptions.Length : 0;
            float btnH = 62;
            float gap = 12;
            float feedbackBand = 100f;                      // 하단 오답 피드백 + 힌트 버튼 영역
            float optionsBlockH = n * btnH + (n - 1) * gap; // 선택지 전체 높이
            // 선택지가 시작할 수 있는 최대 y (이보다 질문이 길면 질문을 잘라 표시)
            float maxStartY = y + h - feedbackBand - optionsBlockH;
            float rawQH = qSt.CalcHeight(new GUIContent(data.quizQuestion), w - 64);
            float maxQH = Mathf.Max(40f, (maxStartY - 20f) - qY);
            float qH = Mathf.Min(rawQH, maxQH);
            GUI.Label(new Rect(x + 32, qY, w - 64, qH), data.quizQuestion, qSt);

            // ── 선택지 시작 위치 (질문 아래, 단 모달 안에 들어오도록) ──
            float startY = Mathf.Min(Mathf.Max(qY + qH + 20, y + 230), maxStartY);

            if (ctrl.ShowingExplanation)
            {
                // ══════ 정답 해설 패널 — 선택지 대신 표시, [다음] 버튼으로만 진행 ══════
                float exBottom = y + h - 88;
                var exRect = new Rect(x + 32, startY, w - 64, exBottom - startY);
                UITheme.DrawRect(exRect, new Color(0.90f, 0.97f, 0.92f)); // 연초록 배경
                UITheme.DrawBorder(exRect, greenDark, 2f);
                UITheme.DrawRect(new Rect(exRect.x, exRect.y, 4f, exRect.height), greenDark);

                var exTagSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15, fontStyle = FontStyle.Bold,
                    normal = { textColor = greenDark }
                };
                GUI.Label(new Rect(exRect.x + 18, exRect.y + 10, exRect.width - 36, 22),
                    "✓ 정답입니다!  //  해설", exTagSt);

                var exBodySt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 17, wordWrap = true,
                    normal = { textColor = inkBlack }
                };
                GUI.Label(new Rect(exRect.x + 18, exRect.y + 38, exRect.width - 36, exRect.height - 48),
                    ctrl.CurrentExplanation, exBodySt);

                // [다음 문제 ▶] / [교육 완료] 버튼
                bool lastQuestion = ctrl.SessionCurrent >= ctrl.SessionTotal;
                string nbLabel = lastQuestion ? "▶ 교육 완료" : "다음 문제 ▶";
                var nbr = new Rect(x + (w - 260) * 0.5f, y + h - 74, 260, 54);
                bool nHover = nbr.Contains(UITheme.GetMousePos());
                UITheme.DrawRect(nbr, nHover ? new Color(0.74f, 0.92f, 0.80f) : new Color(0.88f, 0.96f, 0.90f));
                UITheme.DrawBorder(nbr, greenDark, nHover ? 3f : 2f);
                var nbSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 19, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = greenDark }
                };
                GUI.Label(nbr, nbLabel, nbSt);
                if (GUI.Button(nbr, GUIContent.none, GUIStyle.none))
                {
                    SfxManager.PlayClick();
                    ctrl.ProceedAfterExplanation();
                }
            }
            else
            {
                // ══════ 선택지 버튼들 ══════
                for (int i = 0; i < n; i++)
                {
                    var r = new Rect(x + 32, startY + (btnH + gap) * i, w - 64, btnH);
                    bool hover = r.Contains(UITheme.GetMousePos());

                    UITheme.DrawRect(r, hover ? optHoverBg : optBg);
                    UITheme.DrawBorder(r, hover ? accentViolet : lineLight, hover ? 2.5f : 1.5f);

                    // 번호 뱃지 (hover 시 연보라 배경 대비 위해 더 진한 보라)
                    var numSt = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 18, fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = hover ? accentVioletDark : accentViolet }
                    };
                    GUI.Label(new Rect(r.x + 14, r.y, 40, r.height), $"0{i + 1}", numSt);

                    // 선택지 본문 (검정, 큰 글씨)
                    var btnSt = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 18, fontStyle = hover ? FontStyle.Bold : FontStyle.Normal,
                        alignment = TextAnchor.MiddleLeft, wordWrap = true,
                        normal = { textColor = inkBlack }
                    };
                    GUI.Label(new Rect(r.x + 60, r.y, r.width - 76, r.height),
                        data.quizOptions[i], btnSt);

                    if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                        ctrl.Answer(i);
                }

                // ══════ 오답 상태 — 피드백 + 힌트 ══════
                if (ctrl.CurrentQuestionMissed)
                {
                    if (ctrl.HintRevealed)
                    {
                        // 힌트 박스 (주황 톤)
                        var hintRect = new Rect(x + 32, y + h - 100, w - 64, 84);
                        UITheme.DrawRect(hintRect, new Color(1f, 0.96f, 0.86f));
                        UITheme.DrawBorder(hintRect, new Color(0.72f, 0.50f, 0.04f), 2f);
                        UITheme.DrawRect(new Rect(hintRect.x, hintRect.y, 4f, hintRect.height),
                            new Color(0.72f, 0.50f, 0.04f));

                        var hintTagSt = new GUIStyle(GUI.skin.label)
                        {
                            fontSize = 13, fontStyle = FontStyle.Bold,
                            normal = { textColor = new Color(0.62f, 0.42f, 0.02f) }
                        };
                        GUI.Label(new Rect(hintRect.x + 16, hintRect.y + 8, hintRect.width - 32, 18),
                            "▶ 힌트", hintTagSt);

                        var hintBodySt = new GUIStyle(GUI.skin.label)
                        {
                            fontSize = 15, wordWrap = true,
                            normal = { textColor = inkBlack }
                        };
                        GUI.Label(new Rect(hintRect.x + 16, hintRect.y + 30, hintRect.width - 32, hintRect.height - 38),
                            ctrl.CurrentHint, hintBodySt);
                    }
                    else
                    {
                        // 오답 피드백 문구 (3.5초) + [힌트 보기] 버튼 (오답 후 상시)
                        if (!string.IsNullOrEmpty(ctrl.LastResultText)
                            && !ctrl.LastWasCorrect
                            && Time.time - ctrl.LastResultTime < 3.5f)
                        {
                            var feedSt = new GUIStyle(GUI.skin.label)
                            {
                                fontSize = 16, fontStyle = FontStyle.Bold,
                                alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = dangerDark }
                            };
                            GUI.Label(new Rect(x + 28, y + h - 98, w - 56, 24),
                                "▶ " + ctrl.LastResultText, feedSt);
                        }

                        var hbr = new Rect(x + (w - 200) * 0.5f, y + h - 66, 200, 46);
                        bool hHover = hbr.Contains(UITheme.GetMousePos());
                        UITheme.DrawRect(hbr, hHover ? new Color(1f, 0.93f, 0.76f) : new Color(1f, 0.97f, 0.88f));
                        UITheme.DrawBorder(hbr, new Color(0.72f, 0.50f, 0.04f), hHover ? 2.5f : 1.5f);
                        var hbSt = new GUIStyle(GUI.skin.label)
                        {
                            fontSize = 16, fontStyle = FontStyle.Bold,
                            alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = new Color(0.62f, 0.42f, 0.02f) }
                        };
                        GUI.Label(hbr, "▶ 힌트 보기", hbSt);
                        if (GUI.Button(hbr, GUIContent.none, GUIStyle.none))
                        {
                            SfxManager.PlayClick();
                            ctrl.RevealHint();
                        }
                    }
                }
            }
        }

        private void DrawQuizResultToast(SecurityQuizController ctrl)
        {
            float w = Mathf.Min(660, Screen.width - 80);

            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = UITheme.Ink }
            };
            float contentW = w - 80;
            float contentH = bodySt.CalcHeight(new GUIContent(ctrl.LastResultText), contentW);
            float h = Mathf.Max(96, contentH + 56);

            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.2f;

            var rect = new Rect(x, y, w, h);
            UITheme.DrawRect(rect, UITheme.Bg2);
            UITheme.DrawBorder(rect, UITheme.NeonGreen, 1f);
            UITheme.DrawRect(new Rect(x, y, 3f, h), UITheme.NeonGreen);

            UITheme.DrawPulseDot(new Vector2(x + 22, y + 22), UITheme.NeonGreen, 4f);
            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonGreen }
            };
            GUI.Label(new Rect(x + 36, y + 14, w - 60, 14),
                "▶ CORRECT // SECURITY MODULE PASSED", tagSt);

            GUI.Label(new Rect(x + 36, y + 36, contentW, contentH),
                ctrl.LastResultText, bodySt);
        }

        private void Update()
        {
            var s = GameSession.Instance;
            if (s != null && s.Outcome != RunOutcome.Ongoing && !runEndCleanupDone)
                CloseAllModalsForRunEnd();

            var kb = Keyboard.current;
            if (kb == null) return;
            bool esc = kb.escapeKey.wasPressedThisFrame;

            var partner = FindPartner();
            if (partner != null && partner.IsMenuOpen && esc)
            {
                partner.Close();
                EscConsumedFrame = Time.frameCount;
            }

            // 인벤토리 토글 (I) — 다른 모달 없을 때만
            HandleInventoryToggle(kb);

            var rmc = RacingMissionController.Instance;
            if (rmc != null)
            {
                if (rmc.CurrentPhase == RacingMissionController.Phase.AwaitingRank && esc)
                {
                    rmc.Cancel();
                    EscConsumedFrame = Time.frameCount;
                }
                else if (rmc.CurrentPhase == RacingMissionController.Phase.Finished
                    && (kb.spaceKey.wasPressedThisFrame || esc || kb.enterKey.wasPressedThisFrame))
                {
                    rmc.Acknowledge();
                    if (esc) EscConsumedFrame = Time.frameCount;
                }
            }

            if (s != null && s.Outcome != RunOutcome.Ongoing)
            {
                if (kb.rKey.wasPressedThisFrame) Restart();
                else if (kb.mKey.wasPressedThisFrame) ReturnToMenu();
            }
        }

        /// <summary>
        /// 결과창이 뜨는 순간 남아 있는 창(퀴즈·대화·수사 보드·지목창·레이싱)을 모두 닫는다.
        /// IMGUI는 먼저 그린 버튼이 클릭을 먼저 받기 때문에, 결과창 밑에 남은 버튼이
        /// RESTART/MAIN MENU 클릭을 가로채고 결과창 통계까지 바꿔버린다.
        /// </summary>
        private void CloseAllModalsForRunEnd()
        {
            runEndCleanupDone = true;
            IsInventoryOpen = false;
            SecurityQuizController.Instance?.ForceCloseForRunEnd();
            var ds = DialogueSystem.Instance;
            if (ds != null && ds.IsActive) ds.ForceClose();
            FindPartner()?.Close();
            var rmc = RacingMissionController.Instance;
            if (rmc != null)
            {
                rmc.Cancel();       // 레이스 진행 중이면 보상 없이 닫기 (WebView/iframe 포함)
                rmc.Acknowledge();  // 결과 확인 대기 중이면 닫기
            }
        }

        private void Restart()
        {
            var s = GameSession.Instance;
            if (s != null) s.StartNewRun();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void ReturnToMenu()
        {
            // 메인 메뉴 씬이 빌드 인덱스에 없으면 그냥 재시작
            const string menuScene = "MainMenuScene";
            if (Application.CanStreamedLevelBeLoaded(menuScene))
                SceneManager.LoadScene(menuScene);
            else
                Restart();
        }

        private void DrawInteractionPrompt()
        {
            // 대화창이 활성이면 상호작용 프롬프트 숨김
            var ds = DialogueSystem.Instance;
            if (ds != null && ds.IsActive) return;

            // 일시정지 / 오프닝 컷씬 중에도 숨김
            var pm = PauseMenu.Instance;
            if (pm != null && pm.IsOpen) return;
            if (IntroMonologue.IsCutsceneActive) return;

            var p = PlayerInteractor.Instance;
            if (p == null || p.Nearest == null) return;

            // Hide prompt while accusation modal open
            var partner = FindPartner();
            if (partner != null && partner.IsMenuOpen) return;

            string prompt = p.Nearest.PromptText;

            float w = 480;
            float h = 52;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height - 150;

            var rect = new Rect(x, y, w, h);
            UITheme.DrawRect(rect, UITheme.Bg2);
            UITheme.DrawBorder(rect, UITheme.NeonCyan, 1f);

            // 좌측 펄스 dot
            UITheme.DrawPulseDot(new Vector2(x + 18, y + h * 0.5f), UITheme.NeonCyan, 3f);

            // 좌측 '▶' 화살표 + 텍스트
            var promptSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UITheme.Ink }
            };
            GUI.Label(rect, "▶ " + prompt, promptSt);
        }

        private void DrawToast()
        {
            // 대화창이 활성이면 토스트 숨김
            var ds = DialogueSystem.Instance;
            if (ds != null && ds.IsActive) return;

            var p = PlayerInteractor.Instance;
            if (p == null || string.IsNullOrEmpty(p.LastInteractionResult)) return;
            float elapsed = Time.time - p.LastInteractionTime;
            if (elapsed > 4f) return;

            float w = Mathf.Min(700, Screen.width - 80);
            float contentW = w - 60;

            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, wordWrap = true,
                normal = { textColor = UITheme.Ink }
            };
            float contentH = bodySt.CalcHeight(new GUIContent(p.LastInteractionResult), contentW);

            float headerH = 28;
            float pad = 14;
            float h = headerH + contentH + pad * 2;

            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.24f;

            // 페이드아웃
            float alpha = Mathf.Clamp01(1f - (elapsed - 3f));
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            var rect = new Rect(x, y, w, h);
            UITheme.DrawRect(rect, UITheme.Bg2);
            UITheme.DrawBorder(rect, UITheme.NeonYellow, 1f);

            // 좌측 강조선
            UITheme.DrawRect(new Rect(x, y, 3f, h), UITheme.NeonYellow);

            // 헤더 라벨
            var headerSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonYellow }
            };
            GUI.Label(new Rect(x + 18, y + 10, w - 36, 16),
                "▶ INTEL // RELAY", headerSt);

            // 본문
            GUI.Label(new Rect(x + pad + 14, y + pad + headerH, contentW, contentH),
                p.LastInteractionResult, bodySt);

            GUI.color = prev;
        }

        private void DrawAccusationModal()
        {
            var partner = FindPartner();
            if (partner == null || !partner.IsMenuOpen) return;

            // 어둠 오버레이
            UITheme.DrawRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(UITheme.Bg0.r, UITheme.Bg0.g, UITheme.Bg0.b, 0.88f));
            UITheme.DrawScanlines(new Rect(0, 0, Screen.width, Screen.height), 0.05f);

            // 단서 전체를 스크롤 없이 다 보여주기 위해 크게 (거의 풀스크린)
            float w = Mathf.Min(1240, Screen.width - 40);
            float h = Mathf.Min(720, Screen.height - 40);
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            var rect = new Rect(x, y, w, h);

            // 패널 + 매젠타 보더 (위협적인 톤)
            UITheme.DrawRect(rect, UITheme.Bg1);
            UITheme.DrawBorder(rect, UITheme.NeonMagenta, 1f);

            // 윈도우 헤더
            UITheme.DrawWinBar(new Rect(x, y, w, 32), "verdict.terminal");

            // 헤더 섹션
            float headerY = y + 50;
            UITheme.DrawPulseDot(new Vector2(x + 26, headerY + 14), UITheme.NeonMagenta, 4f);

            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonMagenta }
            };
            GUI.Label(new Rect(x + 40, headerY + 4, w - 80, 14),
                "▶ FINAL ACCUSATION // IRREVERSIBLE", tagSt);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.Ink }
            };
            GUI.Label(new Rect(x + 40, headerY + 18, w - 80, 32),
                "산업스파이 지목", titleSt);

            // 본문 설명 (한 줄)
            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, wordWrap = true,
                normal = { textColor = UITheme.InkDim }
            };
            GUI.Label(new Rect(x + 40, headerY + 50, w - 80, 18),
                "수집한 단서를 대조해 산업스파이를 지목하라 — 정답이면 승리, 오답이면 패배. 되돌릴 수 없다.",
                bodySt);

            // 구분선
            UITheme.DrawRect(new Rect(x + 24, headerY + 74, w - 48, 1), UITheme.Line);

            // ── 2열 레이아웃: 좌(수집 단서 62%) / 우(용의자 지목 38%) ──
            float contentY = headerY + 86;
            float contentBottom = y + h - 20;
            float gapCol = 24f;
            float leftX = x + 28;
            float leftW = (w - 56 - gapCol) * 0.62f;
            float rightW = (w - 56 - gapCol) - leftW;

            DrawAccusationClueList(new Rect(leftX, contentY, leftW, contentBottom - contentY));
            DrawAccusationSuspectButtons(partner,
                new Rect(leftX + leftW + gapCol, contentY, rightW, contentBottom - contentY));
        }

        /// <summary>지목 모달 좌측 — 수집 단서 전체를 스크롤 없이 표시 (3열 동적 높이, 텍스트 잘림 없음).
        /// 장소 기록 카드(세 용의자 모두)는 같은 보라 '알리바이 대조' 표시 — 어느 것이 모순인지는 드러내지 않는다.</summary>
        private void DrawAccusationClueList(Rect area)
        {
            var s = GameSession.Instance;

            var hdrSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonCyan }
            };
            int total = s != null ? s.CollectedClues.Count : 0;
            GUI.Label(new Rect(area.x, area.y, area.width, 16),
                $"▶ 수집 단서 // EVIDENCE ({total})", hdrSt);

            var listArea = new Rect(area.x, area.y + 22, area.width, area.height - 22);
            UITheme.DrawRect(listArea, UITheme.Bg2);
            UITheme.DrawBorder(listArea, UITheme.Line, 1f);

            if (s == null || total == 0)
            {
                var emptySt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = UITheme.InkDim }
                };
                GUI.Label(listArea, "// 수집된 단서가 없습니다", emptySt);
                return;
            }

            // ── 3열 동적 높이 배치 — 전체 텍스트를 자르지 않고 모두 표시 ──
            float pad = 10f;
            float colGap = 8f;
            int cols = 3;
            float colW = (listArea.width - pad * 2 - colGap * (cols - 1)) / cols;
            float textW = colW - 22;

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold, wordWrap = true,
                normal = { textColor = Color.white }
            };
            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, wordWrap = true,
                normal = { textColor = UITheme.Ink }
            };

            // 각 열의 현재 y (가장 짧은 열에 다음 카드 배치)
            var colY = new float[cols];
            for (int c = 0; c < cols; c++) colY[c] = listArea.y + pad;

            GUI.BeginGroup(new Rect(listArea.x, listArea.y, listArea.width, listArea.height));

            for (int i = 0; i < total; i++)
            {
                var clue = s.CollectedClues[i];
                bool evidence = clue.alibiEvidence;
                Color accent = evidence ? UITheme.NeonViolet : GetSourceColor(clue.source);

                float titleH = titleSt.CalcHeight(new GUIContent(clue.title), textW);
                float bodyH = bodySt.CalcHeight(new GUIContent(clue.text), textW);
                float cardH = 6 + 12 + 3 + titleH + 2 + bodyH + 8;

                // 가장 짧은 열 선택
                int col = 0;
                for (int c = 1; c < cols; c++) if (colY[c] < colY[col]) col = c;

                // BeginGroup 내부 좌표 (listArea 기준 상대)
                float cx = pad + col * (colW + colGap);
                float cy = colY[col] - listArea.y;
                var cardR = new Rect(cx, cy, colW, cardH);
                colY[col] += cardH + colGap;

                UITheme.DrawRect(cardR, UITheme.Bg3);
                UITheme.DrawRect(new Rect(cardR.x, cardR.y, 3f, cardR.height), accent);
                if (evidence) UITheme.DrawBorder(cardR, UITheme.NeonViolet, 1f);

                // 태그/출처 라벨
                string tagText = evidence ? "▲ 알리바이 대조"
                    : (string.IsNullOrEmpty(clue.tag) ? SourceLabel(clue.source) : clue.tag);
                var tagChipSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9, fontStyle = FontStyle.Bold,
                    normal = { textColor = accent }
                };
                GUI.Label(new Rect(cardR.x + 11, cardR.y + 5, textW, 12), tagText, tagChipSt);

                // 제목 전체 (잘림 없음)
                GUI.Label(new Rect(cardR.x + 11, cardR.y + 20, textW, titleH), clue.title, titleSt);

                // 본문 전체 (잘림 없음)
                GUI.Label(new Rect(cardR.x + 11, cardR.y + 22 + titleH, textW, bodyH), clue.text, bodySt);
            }

            GUI.EndGroup();
        }

        /// <summary>지목 모달 우측 — 용의자 3버튼.</summary>
        private void DrawAccusationSuspectButtons(ForTheCompany.Player.AccusationPartner partner, Rect area)
        {
            var hdrSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonMagenta }
            };
            GUI.Label(new Rect(area.x, area.y, area.width, 16),
                "▶ 용의자 지목 // ACCUSE", hdrSt);

            var roster = NPCRoster.Instance;
            int n = roster != null ? roster.npcs.Count : 0;

            float listTop = area.y + 24;
            float btnH = 56, gap = 10;

            for (int i = 0; i < n; i++)
            {
                var r = new Rect(area.x, listTop + (btnH + gap) * i, area.width, btnH);
                string label = roster.npcs[i] != null ? roster.npcs[i].DisplayName : "?";

                // 이 용의자에게 연결된 단서 수 (모순 여부는 노출 안 함 — 중간 난이도)
                int linked = 0;
                var s = GameSession.Instance;
                if (s != null && roster.npcs[i] != null && roster.npcs[i].data != null)
                {
                    int role = (int)roster.npcs[i].data.role;
                    foreach (var c in s.CollectedClues)
                        if (c.relatedRole == role) linked++;
                }

                bool hover = r.Contains(UITheme.GetMousePos());
                UITheme.DrawRect(r, hover ? new Color(UITheme.NeonMagenta.r, UITheme.NeonMagenta.g,
                    UITheme.NeonMagenta.b, 0.14f) : UITheme.Bg2);
                UITheme.DrawBorder(r, hover ? UITheme.NeonMagenta : UITheme.Line, hover ? 2f : 1f);

                var btnSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = hover ? UITheme.NeonMagenta : UITheme.Ink }
                };
                GUI.Label(new Rect(r.x + 14, r.y + 6, r.width - 28, 22),
                    $"0{i + 1}   {label}", btnSt);

                // 연결 단서 수 (하단)
                var subSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10, alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = UITheme.InkDim }
                };
                GUI.Label(new Rect(r.x + 14, r.y + 30, r.width - 28, 18),
                    $"연결 단서 {linked}건", subSt);

                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    partner.Accuse(i);
            }
        }

        private void DrawEndScreen()
        {
            var s = GameSession.Instance;
            if (s == null || s.Outcome == RunOutcome.Ongoing) return;

            bool win = s.Outcome == RunOutcome.Win;
            Color accent = win ? UITheme.NeonGreen : UITheme.Danger;

            // ── 통계 계산 ──
            float elapsedSec = s.ElapsedTime;
            int em = Mathf.FloorToInt(elapsedSec / 60f);
            int es = Mathf.FloorToInt(elapsedSec % 60f);
            int total = s.totalClues;
            int envCnt = 0, npcCnt = 0;
            foreach (var c in s.CollectedClues)
            {
                switch (c.source)
                {
                    case ClueSource.Environment: envCnt++; break;
                    case ClueSource.NPC: npcCnt++; break;
                }
            }
            string spyName = "?";
            int spyRole = -1;
            if (NPCRoster.Instance != null && NPCRoster.Instance.Spy != null)
            {
                spyName = NPCRoster.Instance.Spy.DisplayName;
                if (NPCRoster.Instance.Spy.data != null)
                    spyRole = (int)NPCRoster.Instance.Spy.data.role;
            }
            string motiveText = GetSpyMotive(spyRole);

            // ── 풀스크린 어둠 + 스캔라인 ──
            UITheme.DrawRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(UITheme.Bg0.r, UITheme.Bg0.g, UITheme.Bg0.b, 0.92f));
            UITheme.DrawScanlines(new Rect(0, 0, Screen.width, Screen.height), 0.06f);

            float w = Mathf.Min(780, Screen.width - 80);
            float h = Mathf.Min(760, Screen.height - 40);  // 오답 노트 추가로 높이 확장 (700 → 760)
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;
            var rect = new Rect(x, y, w, h);

            UITheme.DrawRect(rect, UITheme.Bg1);
            UITheme.DrawBorder(rect, accent, 1f);
            UITheme.DrawWinBar(new Rect(x, y, w, 32), "mission-result.dossier");

            // 화면이 낮으면(웹 기본 600px 등) 위쪽 여백을 줄인다 — 고정 블록 합(614px)이
            // 패널보다 크면 오답 노트 높이가 음수가 되고 버튼이 다른 내용 위에 겹친다.
            bool compact = h < 740f;

            // ── 상태 태그 ──
            float headerY = y + 50;
            UITheme.DrawPulseDot(new Vector2(x + 28, headerY + 14), accent, 5f);

            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                normal = { textColor = accent }
            };
            GUI.Label(new Rect(x + 44, headerY + 4, w - 80, 16),
                win ? "▶ MISSION SUCCESS // SUSPECT IDENTIFIED"
                    : "▶ MISSION FAILED // INVESTIGATION COMPROMISED", tagSt);

            // ── 메인 결과 라벨 (좀 작게) ──
            var bigSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = compact ? 40 : 48, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = accent }
            };
            GUI.Label(new Rect(x, y + (compact ? 70 : 80), w, compact ? 52 : 64), win ? "VICTORY" : "DEFEAT", bigSt);

            // 부제 한국어
            var subKoSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UITheme.InkDim }
            };
            GUI.Label(new Rect(x, y + (compact ? 122 : 140), w, 22), win ? "사건 해결" : "조사 실패", subKoSt);

            // ── 통계 패널 ──
            float statsY = y + (compact ? 150 : 178);
            float statsH = 180;
            var statsRect = new Rect(x + 30, statsY, w - 60, statsH);
            UITheme.DrawRect(statsRect, UITheme.Bg2);
            UITheme.DrawBorder(statsRect, UITheme.Line, 1f);

            // 통계 헤더
            var statTagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonCyan }
            };
            GUI.Label(new Rect(statsRect.x + 14, statsRect.y + 8, statsRect.width - 28, 14),
                "▶ MISSION DEBRIEF // STATS", statTagSt);

            // 통계 행들 (좌측 라벨, 우측 값)
            DrawStatRow(statsRect, 0, "▶ 사용 시간", $"{em:D2}:{es:D2}", UITheme.NeonCyan);
            DrawStatRow(statsRect, 1, "▶ 단서 총 수집", $"{total}개", UITheme.NeonGreen);
            // 위 '단서 총 수집'은 보상 점수 합(상단 HUD와 동일)이고, 아래 두 줄은 수사 보드에 쌓인 카드 수
            DrawStatRow(statsRect, 2, "▶ 수사 보드 카드 · NPC 대화", $"{npcCnt}장",
                npcCnt > 0 ? UITheme.NeonCyan : UITheme.InkFaint);
            DrawStatRow(statsRect, 3, "▶ 수사 보드 카드 · 보안 교육", $"{envCnt}장",
                envCnt > 0 ? UITheme.NeonYellow : UITheme.InkFaint);
            DrawStatRow(statsRect, 4, "▶ 보안 문제 (첫 시도)",
                $"{s.quizFirstTryCorrect} 맞음 · {s.quizWrongCount} 틀림",
                s.quizWrongCount == 0 ? UITheme.NeonGreen : UITheme.NeonYellow);

            // ── 진짜 스파이 공개 ──
            float msgY = statsY + statsH + 10;
            var revealSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = accent }
            };
            GUI.Label(new Rect(x + 30, msgY, w - 60, 22),
                $"▶ 진짜 산업스파이: {spyName}", revealSt);

            // ── 모티브 박스 (서사적 동기) ──
            float motiveY = msgY + 28;
            float motiveH = compact ? 86f : 110f;
            const float minReviewH = 60f;
            // 그래도 공간이 모자라면 동기 박스를 생략 — 오답 노트와 버튼이 우선
            bool showMotive = (y + h - 70) - (motiveY + motiveH + 38) >= minReviewH;
            if (showMotive)
            {
                var motiveRect = new Rect(x + 30, motiveY, w - 60, motiveH);
                UITheme.DrawRect(motiveRect, UITheme.Bg2);
                UITheme.DrawBorder(motiveRect, accent, 1f);
                // 좌측 강조 띠
                UITheme.DrawRect(new Rect(motiveRect.x, motiveRect.y, 3f, motiveH), accent);

                var motiveTagSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = accent }
                };
                GUI.Label(new Rect(motiveRect.x + 14, motiveRect.y + 6, motiveRect.width - 28, 22),
                    "▶ MOTIVE // 유출 동기", motiveTagSt);

                var motiveBodySt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12, wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                    normal = { textColor = UITheme.Ink }
                };
                GUI.Label(new Rect(motiveRect.x + 16, motiveRect.y + 30, motiveRect.width - 32, motiveH - 36),
                    motiveText, motiveBodySt);
            }

            // ── 결과 메시지 (간단히) ──
            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, wordWrap = true,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = UITheme.InkDim }
            };
            float messageY = showMotive ? motiveY + motiveH + 6 : motiveY;
            GUI.Label(new Rect(x + 40, messageY, w - 80, 28),
                s.OutcomeMessage, bodySt);

            // ── 오답 노트 — 틀렸던 보안 문제 해설 ──
            float reviewY = messageY + 32;
            float reviewH = Mathf.Max(40f, (y + h - 70) - reviewY);
            var reviewRect = new Rect(x + 30, reviewY, w - 60, reviewH);
            UITheme.DrawRect(reviewRect, UITheme.Bg2);
            UITheme.DrawBorder(reviewRect, UITheme.Line, 1f);

            var rvTagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                normal = { textColor = UITheme.NeonYellow }
            };
            GUI.Label(new Rect(reviewRect.x + 14, reviewRect.y + 6, reviewRect.width - 28, 14),
                s.WrongQuizzes.Count > 0
                    ? $"▶ 오답 노트 // 틀렸던 문제 해설 ({s.WrongQuizzes.Count})"
                    : "▶ 오답 노트 // WRONG ANSWERS", rvTagSt);

            if (s.WrongQuizzes.Count == 0)
            {
                var perfectSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = UITheme.NeonGreen }
                };
                GUI.Label(new Rect(reviewRect.x, reviewRect.y + 20, reviewRect.width, reviewRect.height - 24),
                    "모든 보안 문제를 첫 시도에 맞혔습니다!", perfectSt);
            }
            else
            {
                var wqSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11, fontStyle = FontStyle.Bold, wordWrap = true,
                    normal = { textColor = Color.white }
                };
                var weSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11, wordWrap = true,
                    normal = { textColor = UITheme.Ink }
                };

                float pad = 12f;
                float innerW = reviewRect.width - pad * 2 - 14; // 스크롤바 여유
                // 전체 콘텐츠 높이 계산
                float contentH = 0f;
                foreach (var wq in s.WrongQuizzes)
                {
                    contentH += wqSt.CalcHeight(new GUIContent("Q. " + wq.question), innerW) + 2;
                    contentH += weSt.CalcHeight(new GUIContent("→ " + wq.explanation), innerW) + 10;
                }

                var viewRect = new Rect(reviewRect.x + pad, reviewRect.y + 24,
                    reviewRect.width - pad * 2, reviewRect.height - 30);
                var contentRect = new Rect(0, 0, innerW, contentH);
                endWrongScroll = GUI.BeginScrollView(viewRect, endWrongScroll, contentRect);

                float wy = 0f;
                foreach (var wq in s.WrongQuizzes)
                {
                    float qH = wqSt.CalcHeight(new GUIContent("Q. " + wq.question), innerW);
                    GUI.Label(new Rect(0, wy, innerW, qH), "Q. " + wq.question, wqSt);
                    wy += qH + 2;
                    float eH = weSt.CalcHeight(new GUIContent("→ " + wq.explanation), innerW);
                    GUI.Label(new Rect(0, wy, innerW, eH), "→ " + wq.explanation, weSt);
                    wy += eH + 10;
                }
                GUI.EndScrollView();
            }

            // ── 버튼 영역 ──
            float btnW = 230, btnH = 42, gap = 14;
            float twoW = btnW * 2 + gap;
            float bx = x + (w - twoW) * 0.5f;
            float by = y + h - 60;

            if (UITheme.NeonButton(new Rect(bx, by, btnW, btnH),
                "▶ RESTART  [R]", win ? UITheme.NeonGreen : UITheme.NeonCyan))
                Restart();

            if (UITheme.GhostButton(new Rect(bx + btnW + gap, by, btnW, btnH),
                "▶ MAIN MENU  [M]"))
                ReturnToMenu();
        }

        /// <summary>진짜 스파이의 직업(role)에 따른 정보 유출 동기 — 게임 마무리 서사</summary>
        private static string GetSpyMotive(int role)
        {
            // RoleType: 1=Researcher, 2=NetworkAdmin, 3=FacilityManager
            switch (role)
            {
                case 1: return
                    "3년간 핵심 연구를 주도했지만 회사는 그의 이름을 특허에서 제외했다.\n" +
                    "해외 경쟁사가 그의 가치를 알아봤고, 2억원의 이적 제안을 보내왔다.\n" +
                    "설계도를 빼돌리면 스타트업 창업과 새로운 인생이 가능했다.";
                case 2: return
                    "암호화폐 투자 손실로 빚이 눈덩이처럼 불어났다.\n" +
                    "다크웹의 누군가가 기업 보안 데이터에 거액을 제시했고,\n" +
                    "관리자 권한을 가진 그는 퇴근한 척한 뒤 밤늦게 외부 IP로 자료를 흘려보냈다.";
                case 3: return
                    "어머니의 긴급 수술비가 필요했지만 회사 대출은 거절됐다.\n" +
                    "외부 침입자는 단 한 번 — 카드키 사본과 30분간의 CCTV 정전을 요구했다.\n" +
                    "양심의 가책은 컸지만 다른 선택지가 없었다.";
                default:
                    return "동기는 끝까지 밝혀지지 않았다.";
            }
        }

        /// <summary>DrawEndScreen용 통계 한 줄 (좌측 라벨, 우측 값)</summary>
        private static void DrawStatRow(Rect container, int rowIndex, string label, string value, Color accent)
        {
            float rowH = 28;
            float startY = container.y + 30;
            float y = startY + rowIndex * rowH;

            var labelSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(20, 0, 0, 0),
                normal = { textColor = UITheme.InkDim }
            };
            var valueSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 20, 0, 0),
                normal = { textColor = accent }
            };
            GUI.Label(new Rect(container.x, y, container.width, rowH), label, labelSt);
            GUI.Label(new Rect(container.x, y, container.width, rowH), value, valueSt);
        }

        private void DrawHint()
        {
            // 레이싱 WebView 진행 중엔 하단 안내도 숨김 (WebView 위로 떠 보이지 않게)
            var bridge = RacingWebViewBridge.Instance;
            if (bridge != null && bridge.IsShowing) return;

            float h = 26;
            var bgRect = new Rect(0, Screen.height - h, Screen.width, h);
            UITheme.DrawRect(bgRect, new Color(UITheme.Bg0.r, UITheme.Bg0.g, UITheme.Bg0.b, 0.9f));
            UITheme.DrawRect(new Rect(0, Screen.height - h, Screen.width, 1), UITheme.LineStrong);

            var st = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(16, Screen.height - h, Screen.width - 32, h),
                "// [WASD] 이동  ·  [SHIFT] 달리기  ·  [휠] 줌  ·  [SPACE] 상호작용  ·  [I] 인벤토리  ·  [ESC] 메뉴",
                st);
        }

        /// <summary>I키로 토글되는 인벤토리 — SecureSense Dossier 스타일 단서 아카이브</summary>
        // ─── 인벤토리 상태 — 선택된 용의자 (좌측 하단 카드 클릭) ───
        // -1 = 미선택, 1=연구원, 2=네트워크관리자, 3=시설관리자
        private int selectedSuspectRole = -1;
        // 단서 보드 카드 캐싱된 위치 (카드 그리고 점선 연결할 때 사용)
        private readonly List<Rect> boardCardRects = new List<Rect>();
        // 현재 마우스 hover된 단서 인덱스 (-1 = 없음) → 인벤토리 마지막에 툴팁 표시
        private int hoveredClueIndex = -1;

        private void DrawInventoryPanel()
        {
            if (!IsInventoryOpen) return;
            var s = GameSession.Instance;
            if (s == null) return;
            if (s.Outcome != RunOutcome.Ongoing) return; // 결과창이 우선

            // ── 풀스크린 어둠 (배경은 어둡게, 패널만 흰색으로 부각) ──
            UITheme.DrawRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(UITheme.Bg0.r, UITheme.Bg0.g, UITheme.Bg0.b, 0.92f));

            // ── 메인 패널 (흰색 라이트 테마) ──
            float w = Mathf.Min(1280, Screen.width - 40);
            float h = Mathf.Min(760, Screen.height - 40);
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            var panelRect = new Rect(x, y, w, h);
            UITheme.DrawRect(panelRect, LtPanel);
            UITheme.DrawBorder(panelRect, LtViolet, 2f);

            // ── 라이트 헤더바 (DrawWinBar 대신 직접 그림) ──
            float barH = 32f;
            UITheme.DrawRect(new Rect(x, y, w, barH), LtHeader);
            UITheme.DrawRect(new Rect(x, y + barH - 1, w, 1), LtLine);
            float dotY = y + barH * 0.5f - 5.5f;
            UITheme.DrawRect(new Rect(x + 14, dotY, 11, 11), new Color(1f, 0.37f, 0.34f));
            UITheme.DrawRect(new Rect(x + 30, dotY, 11, 11), new Color(1f, 0.74f, 0.18f));
            UITheme.DrawRect(new Rect(x + 46, dotY, 11, 11), new Color(0.16f, 0.78f, 0.25f));
            var barTitleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = LtInkSub }
            };
            GUI.Label(new Rect(x, y, w, barH),
                "securesense · investigation board · case#IR-2026-0524", barTitleSt);

            // ── 상단 헤더 ──
            float headerY = y + 44;
            UITheme.DrawPulseDot(new Vector2(x + 24, headerY + 18), LtDanger, 4f);

            var headerTagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = LtDanger }
            };
            GUI.Label(new Rect(x + 40, headerY + 4, w - 80, 22),
                "▶ 진행 중인 사건 // ACTIVE INCIDENT", headerTagSt);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22, fontStyle = FontStyle.Bold,
                normal = { textColor = LtInk }
            };
            GUI.Label(new Rect(x + 40, headerY + 28, w - 80, 32),
                "차세대 보안 칩 설계도 유출 — 산업스파이 조사", titleSt);

            var hintSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 24, 0, 0),
                normal = { textColor = LtInkSub }
            };
            GUI.Label(new Rect(x, headerY + 6, w, 22),
                "// [I] 또는 [ESC] 닫기", hintSt);

            UITheme.DrawRect(new Rect(x + 24, headerY + 66, w - 48, 1), LtLine);

            // ── 본문 — 좌우 분할 (보드 + 용의자카드 / 우측 SUSPECT FILE) ──
            float bodyY = headerY + 78;
            float bodyH = h - (bodyY - y) - 16;

            float leftW = w * 0.65f;
            float rightW = w * 0.35f - 12;

            // 매 프레임 hover 체크 초기화
            hoveredClueIndex = -1;

            DrawInvestigationBoardSection(s, new Rect(x + 14, bodyY, leftW - 14, bodyH));
            DrawSuspectFileSection(s, new Rect(x + leftW + 4, bodyY, rightW, bodyH));

            // 단서 hover 툴팁 — 가장 위에 그려지도록 마지막 호출
            if (hoveredClueIndex >= 0 && hoveredClueIndex < s.CollectedClues.Count)
                DrawClueTooltip(s.CollectedClues[hoveredClueIndex]);
        }

        // ═════════════ 좌측: Investigation Board + 용의자 3 카드 ═════════════
        private void DrawInvestigationBoardSection(GameSession s, Rect area)
        {
            // 영역 분할 — 위쪽 EVIDENCE BOARD (75%) + 아래쪽 SUSPECTS 3 (25%)
            float boardH = area.height * 0.72f;
            float suspectsH = area.height - boardH - 10;
            var boardRect = new Rect(area.x, area.y, area.width, boardH);
            var suspectsRect = new Rect(area.x, area.y + boardH + 10, area.width, suspectsH);

            // ── 보드 패널 ──
            UITheme.DrawRect(boardRect, LtCard);
            UITheme.DrawBorder(boardRect, LtLine, 1f);

            // 보드 헤더
            var labelSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = LtGreen }
            };
            int total = s.CollectedClues.Count;
            GUI.Label(new Rect(boardRect.x + 14, boardRect.y + 8, 300, 22),
                $"▶ 증거 보드 // EVIDENCE BOARD · {total:D2}", labelSt);

            var subTag = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 14, 0, 0),
                normal = { textColor = LtInkSub }
            };
            GUI.Label(new Rect(boardRect.x, boardRect.y + 8, boardRect.width, 22),
                selectedSuspectRole < 0
                    ? "// 용의자 카드를 클릭하면 관련 단서가 점선으로 연결됩니다"
                    : $"// 선택됨: {GetRoleName(selectedSuspectRole)} — 관련 단서 강조",
                subTag);

            UITheme.DrawRect(new Rect(boardRect.x + 14, boardRect.y + 36, boardRect.width - 28, 1),
                LtLine);

            // 단서 카드들 자동 그리드 배치
            DrawEvidenceCards(s, new Rect(boardRect.x + 14, boardRect.y + 44,
                boardRect.width - 28, boardRect.height - 58));

            // ── 좌하단 용의자 3 카드 ──
            DrawSuspectsRow(suspectsRect);

            // ── 선택된 용의자 → 단서 점선 연결 ──
            if (selectedSuspectRole >= 0)
                DrawDottedConnections(s, suspectsRect);
        }

        private void DrawEvidenceCards(GameSession s, Rect area)
        {
            boardCardRects.Clear();
            int n = s.CollectedClues.Count;
            if (n == 0)
            {
                var emptySt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = LtInkFaint }
                };
                GUI.Label(area, "// 아직 수집된 단서가 없습니다", emptySt);
                return;
            }

            // 그리드 — 3열 자동 (단서 많아지면 행 추가)
            int cols = 3;
            int rows = Mathf.CeilToInt(n / (float)cols);
            float gap = 8f;
            float cardW = (area.width - gap * (cols - 1)) / cols;
            float cardH = 92f;
            float totalH = rows * cardH + (rows - 1) * gap;

            var viewRect = new Rect(area.x, area.y, area.width, area.height);
            var contentRect = new Rect(0, 0, area.width - 12, totalH);

            inventoryScroll = GUI.BeginScrollView(viewRect, inventoryScroll, contentRect);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = true,
                normal = { textColor = LtInk }
            };
            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, wordWrap = true,
                normal = { textColor = LtInkSub }
            };

            Vector2 mp = UITheme.GetMousePos();

            for (int i = 0; i < n; i++)
            {
                int r = i / cols, c = i % cols;
                float cx = c * (cardW + gap);
                float cy = r * (cardH + gap);
                var cardR = new Rect(cx, cy, cardW, cardH);

                var clue = s.CollectedClues[i];
                // 장소 기록 카드(세 용의자 모두)는 보라 강조 — 모순 여부는 표시하지 않는다
                Color accent = clue.alibiEvidence ? LtViolet : GetSourceColorLight(clue.source);
                bool isLinkedToSelected = selectedSuspectRole >= 0
                    && clue.relatedRole == selectedSuspectRole;
                bool dim = selectedSuspectRole >= 0 && !isLinkedToSelected;

                // 화면 좌표 변환 + hover 체크
                Rect screenRect = new Rect(
                    viewRect.x + cardR.x - inventoryScroll.x,
                    viewRect.y + cardR.y - inventoryScroll.y,
                    cardR.width, cardR.height);
                bool hover = screenRect.Contains(mp) && screenRect.yMin >= viewRect.y - 2
                          && screenRect.yMax <= viewRect.yMax + 2; // ScrollView 밖은 hover 무시
                if (hover) hoveredClueIndex = i;

                Color cardBg = hover ? LtCardHov : (dim ? LtCardDim : LtCard);
                UITheme.DrawRect(cardR, cardBg);
                UITheme.DrawBorder(cardR,
                    hover ? LtCyan : (isLinkedToSelected ? accent : LtLine),
                    (hover || isLinkedToSelected) ? 2f : 1f);

                // 좌측 띠 (출처 색)
                UITheme.DrawRect(new Rect(cardR.x, cardR.y, 4f, cardR.height),
                    dim ? new Color(accent.r, accent.g, accent.b, 0.35f) : accent);

                // 상단: 태그 + 번호 (태그 폭은 글자 길이에 맞춰 동적 — INTERVIEW 등 잘림 방지)
                if (!string.IsNullOrEmpty(clue.tag))
                {
                    var tagMeasure = new GUIStyle(GUI.skin.label)
                        { fontSize = 10, fontStyle = FontStyle.Bold };
                    string tagShown = clue.alibiEvidence ? "▲ " + clue.tag : clue.tag;
                    float tagW = tagMeasure.CalcSize(new GUIContent(tagShown.ToUpper())).x + 16f;
                    tagW = Mathf.Clamp(tagW, 44f, cardR.width - 48f); // 우측 #번호 공간 확보
                    DrawTagLight(new Rect(cardR.x + 8, cardR.y + 6, tagW, 15), tagShown, accent);
                }
                var numSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                    padding = new RectOffset(0, 8, 0, 0),
                    normal = { textColor = LtInkSub }
                };
                GUI.Label(new Rect(cardR.x, cardR.y + 5, cardR.width, 14),
                    $"#{i + 1:D2}", numSt);

                // 제목 (최대 1줄 — 잘림 처리)
                string title = TruncateText(clue.title, 22);
                GUI.Label(new Rect(cardR.x + 10, cardR.y + 24, cardR.width - 18, 24),
                    title, titleSt);

                // 본문 일부 (잘림 + "...")
                string preview = TruncateText(clue.text, 52);
                GUI.Label(new Rect(cardR.x + 10, cardR.y + 50, cardR.width - 18, cardR.height - 54),
                    preview, bodySt);

                boardCardRects.Add(screenRect);
            }

            GUI.EndScrollView();
        }

        /// <summary>긴 텍스트를 maxChars로 자르고 끝에 "..." 추가</summary>
        private static string TruncateText(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars).TrimEnd() + "...";
        }

        /// <summary>단서 hover 시 마우스 옆에 전체 내용 툴팁 표시</summary>
        private void DrawClueTooltip(ClueEntry clue)
        {
            Vector2 mp = UITheme.GetMousePos();

            float tipW = 380f;
            float pad = 14f;

            // 장소 기록 카드는 보라, 아니면 출처색 (흰 배경용 진한 버전)
            Color tipAccent = clue.alibiEvidence ? LtViolet : GetSourceColorLight(clue.source);

            var titleSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = LtInk }
            };
            var bodySt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, wordWrap = true,
                normal = { textColor = LtInk }
            };
            var tagSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                normal = { textColor = tipAccent }
            };

            float contentW = tipW - pad * 2;
            float titleH = titleSt.CalcHeight(new GUIContent(clue.title), contentW);
            float bodyH = bodySt.CalcHeight(new GUIContent(clue.text), contentW);
            float tipH = pad + 18 + 4 + titleH + 8 + bodyH + pad;

            // 마우스 우측 16px에 배치, 화면 밖이면 좌측으로
            float tx = mp.x + 16f;
            if (tx + tipW > Screen.width - 8) tx = mp.x - tipW - 16f;
            float ty = mp.y + 8f;
            if (ty + tipH > Screen.height - 8) ty = Screen.height - tipH - 8f;
            if (tx < 8) tx = 8;
            if (ty < 8) ty = 8;

            var tipR = new Rect(tx, ty, tipW, tipH);

            // GUI.depth 음수로 위에 그리도록
            int prevDepth = GUI.depth;
            GUI.depth = -500;

            UITheme.DrawRect(tipR, LtPanel);
            UITheme.DrawBorder(tipR, tipAccent, 1.5f);
            UITheme.DrawRect(new Rect(tipR.x, tipR.y, 4f, tipR.height), tipAccent);

            float cy = tipR.y + pad;

            // 태그 + 출처
            string tagLine = $"▶ {clue.tag ?? ""}";
            if (!string.IsNullOrEmpty(clue.tag)) tagLine += "  ·  ";
            tagLine += SourceLabel(clue.source);
            GUI.Label(new Rect(tipR.x + pad, cy, contentW, 14), tagLine, tagSt);
            cy += 18;

            // 구분선
            UITheme.DrawRect(new Rect(tipR.x + pad, cy, contentW, 1), LtLine);
            cy += 6;

            // 제목
            GUI.Label(new Rect(tipR.x + pad, cy, contentW, titleH), clue.title, titleSt);
            cy += titleH + 6;

            // 본문 전체
            GUI.Label(new Rect(tipR.x + pad, cy, contentW, bodyH), clue.text, bodySt);

            GUI.depth = prevDepth;
        }

        private static string SourceLabel(ClueSource src) => src switch
        {
            ClueSource.Environment => "보안교육",
            ClueSource.NPC => "증언",
            ClueSource.Minigame => "미니게임",
            _ => "기타"
        };

        private void DrawSuspectsRow(Rect area)
        {
            UITheme.DrawRect(area, LtCard);
            UITheme.DrawBorder(area, LtLine, 1f);

            var headerSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = LtMagenta }
            };
            GUI.Label(new Rect(area.x + 12, area.y + 6, area.width - 24, 22),
                "▶ 용의자 3 // SUSPECTS — 클릭으로 단서 연결", headerSt);

            // 3개 용의자 카드 가로 배치
            float cardY = area.y + 34;
            float cardH = area.height - 42;
            float gap = 8f;
            float cardW = (area.width - 24 - gap * 2) / 3;

            DrawSingleSuspectCard(new Rect(area.x + 12, cardY, cardW, cardH), 1, "연구원", "R&D");
            DrawSingleSuspectCard(new Rect(area.x + 12 + (cardW + gap), cardY, cardW, cardH),
                2, "네트워크관리자", "IT INFRA");
            DrawSingleSuspectCard(new Rect(area.x + 12 + (cardW + gap) * 2, cardY, cardW, cardH),
                3, "시설관리자", "OPS");
        }

        private void DrawSingleSuspectCard(Rect rect, int role, string name, string subTitle)
        {
            bool isSelected = selectedSuspectRole == role;
            bool hover = rect.Contains(UITheme.GetMousePos());

            // 이 용의자에게 연결된 단서 수만 집계 (모순 여부는 카드에 노출하지 않음 —
            // 중간 난이도: 플레이어가 클릭해서 알리바이↔증거를 직접 대조해야 함)
            int linkedCount = 0;
            var gsCard = GameSession.Instance;
            if (gsCard != null)
                foreach (var cl in gsCard.CollectedClues)
                    if (cl.relatedRole == role) linkedCount++;

            // 띠 색: 단서 있으면 시안, 없으면 흐림 (스파이를 한눈에 드러내지 않음)
            Color stripeColor = linkedCount > 0 ? LtCyan : LtInkFaint;
            Color borderC = isSelected ? LtGreen
                : hover ? stripeColor
                : LtLine;

            UITheme.DrawRect(rect, isSelected
                ? new Color(LtGreen.r, LtGreen.g, LtGreen.b, 0.12f)
                : (hover ? LtCardHov : LtCard));
            UITheme.DrawBorder(rect, borderC, isSelected ? 2.5f : 1f);

            // 좌측 띠 (단서/모순 상태 색)
            UITheme.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), stripeColor);

            // 이름
            var nameSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = isSelected ? LtGreen : LtInk }
            };
            GUI.Label(new Rect(rect.x + 12, rect.y + 8, rect.width - 24, 22), name, nameSt);

            // 부직군
            var subSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = LtInkSub }
            };
            GUI.Label(new Rect(rect.x + 12, rect.y + 30, rect.width - 24, 16), subTitle, subSt);

            // 수집 단서 수만 표시 (의심도 자동 카운터 폐기 — 알리바이·증거로 직접 추리)
            var evLabelSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = linkedCount > 0 ? LtGreen : LtInk }
            };
            GUI.Label(new Rect(rect.x + 12, rect.y + 54, rect.width - 24, 18),
                $"수집 단서 {linkedCount}건 · 클릭", evLabelSt);

            // 클릭
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                selectedSuspectRole = (selectedSuspectRole == role) ? -1 : role; // 토글
                SfxManager.PlayClick();
            }
        }

        private void DrawDottedConnections(GameSession s, Rect suspectsArea)
        {
            // 선택된 용의자 카드 위치 (좌하단 영역에서 role 인덱스 기반)
            int selIdx = selectedSuspectRole - 1; // 1→0, 2→1, 3→2
            if (selIdx < 0 || selIdx > 2) return;

            float gap = 8f;
            float cardW = (suspectsArea.width - 24 - gap * 2) / 3;
            float cardH = suspectsArea.height - 42;
            float cardX = suspectsArea.x + 12 + selIdx * (cardW + gap);
            float cardY = suspectsArea.y + 34;
            // 용의자 카드 상단 중앙 = 점선 시작점
            Vector2 suspectAnchor = new Vector2(cardX + cardW * 0.5f, cardY);

            // 단서 카드 중에서 연결된 것들 → 점선
            int cardIdx = 0;
            foreach (var clue in s.CollectedClues)
            {
                if (cardIdx >= boardCardRects.Count) break;
                Rect cardR = boardCardRects[cardIdx];
                cardIdx++;

                if (clue.relatedRole != selectedSuspectRole) continue;
                // 보드 밖으로 스크롤된 카드는 점선 안 그림 (대충 좌표 체크)
                if (cardR.yMax < suspectsArea.y - 200 || cardR.y > suspectsArea.y - 4) continue;

                Vector2 clueAnchor = new Vector2(cardR.center.x, cardR.yMax);
                UITheme.DrawDottedLine(suspectAnchor, clueAnchor, LtGreen, 3f, 4f);
            }
        }

        // ═════════════ 우측: SUSPECT FILE (선택된 용의자 상세) ═════════════
        private void DrawSuspectFileSection(GameSession s, Rect area)
        {
            UITheme.DrawRect(area, LtCard);
            UITheme.DrawBorder(area, LtLine, 1f);

            // 헤더
            var labelSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = LtCyan }
            };
            GUI.Label(new Rect(area.x + 14, area.y + 8, area.width - 28, 22),
                "▶ 용의자 파일 // SUSPECT FILE", labelSt);

            UITheme.DrawRect(new Rect(area.x + 14, area.y + 34, area.width - 28, 1), LtLine);

            if (selectedSuspectRole < 0)
            {
                var emptySt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14, wordWrap = true,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = LtInkSub }
                };
                GUI.Label(new Rect(area.x + 20, area.y + area.height * 0.4f, area.width - 40, 90),
                    "용의자를 선택해주세요.\n\n좌측 하단 3개 카드 중 하나를\n클릭하면 상세 정보가 표시됩니다.", emptySt);
                return;
            }

            // 선택된 NPC 찾기
            NPCActor selected = null;
            var roster = NPCRoster.Instance;
            if (roster != null)
            {
                foreach (var npc in roster.npcs)
                {
                    if (npc != null && npc.data != null && (int)npc.data.role == selectedSuspectRole)
                    {
                        selected = npc; break;
                    }
                }
            }
            if (selected == null) return;

            float cy = area.y + 48;

            // 이름 (큰)
            var nameSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24, fontStyle = FontStyle.Bold,
                normal = { textColor = LtInk }
            };
            GUI.Label(new Rect(area.x + 16, cy, area.width - 32, 34),
                selected.DisplayName, nameSt);
            cy += 38;

            var subSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = LtInkSub }
            };
            GUI.Label(new Rect(area.x + 16, cy, area.width - 32, 20),
                GetRoleSubtitle(selectedSuspectRole), subSt);
            cy += 28;

            // 알리바이 — 추리 기준 (▲ 장소 기록 카드의 시각과 대조해 거짓을 찾는다)
            UITheme.DrawRect(new Rect(area.x + 16, cy, area.width - 32, 1), LtLine);
            cy += 10;

            var sectionSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = LtViolet }
            };
            GUI.Label(new Rect(area.x + 16, cy, area.width - 32, 20),
                "▶ 알리바이 // ALIBI", sectionSt);
            cy += 24;
            var alibiSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Italic, wordWrap = true,
                normal = { textColor = LtInk }
            };
            string alibiStmt = GameSession.GetAlibi(selectedSuspectRole);
            string alibiShown = string.IsNullOrEmpty(alibiStmt) ? "(진술 없음)" : $"“{alibiStmt}”";
            float alibiH = alibiSt.CalcHeight(new GUIContent(alibiShown), area.width - 44);
            GUI.Label(new Rect(area.x + 16, cy, area.width - 32, alibiH), alibiShown, alibiSt);
            cy += alibiH + 8;

            // 연결된 단서 수
            int linked = 0;
            foreach (var c in s.CollectedClues)
                if (c.relatedRole == selectedSuspectRole) linked++;

            UITheme.DrawRect(new Rect(area.x + 16, cy, area.width - 32, 1), LtLine);
            cy += 10;

            var linkedSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = LtGreen }
            };
            GUI.Label(new Rect(area.x + 16, cy, area.width - 32, 20),
                "▶ 수집된 단서 // EVIDENCE", linkedSt);
            cy += 24;
            var linkedNumSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28, fontStyle = FontStyle.Bold,
                normal = { textColor = LtGreen }
            };
            GUI.Label(new Rect(area.x + 16, cy, area.width - 32, 38),
                $"{linked} 건", linkedNumSt);
            cy += 46;

            // 안내 — 추리 방법 (정답을 직접 알려주지 않음)
            var infoSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, wordWrap = true,
                normal = { textColor = LtInkSub }
            };
            GUI.Label(new Rect(area.x + 16, cy, area.width - 32, 100),
                "▶ 이 사람의 알리바이를 좌측 보드의 ▲ 기록 카드 시각과 대조하세요.\n진술과 시간대가 어긋나는 사람이 범인입니다.",
                infoSt);
        }

        // ─── 헬퍼 ───
        private static string GetRoleName(int role) => role switch
        {
            1 => "연구원",
            2 => "네트워크관리자",
            3 => "시설관리자",
            _ => "?"
        };
        private static string GetRoleSubtitle(int role) => role switch
        {
            1 => "선임 연구원 · R&D",
            2 => "네트워크 관리자 · IT INFRA",
            3 => "시설 관리자 · OPS",
            _ => ""
        };
        private static Color GetSourceColor(ClueSource src) => src switch
        {
            ClueSource.Environment => UITheme.NeonYellow,
            ClueSource.NPC => UITheme.NeonCyan,
            ClueSource.Minigame => UITheme.NeonGreen,
            _ => UITheme.NeonViolet
        };
    }
}
