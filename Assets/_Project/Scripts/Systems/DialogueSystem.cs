using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ForTheCompany.Systems
{
    /// <summary>플레이어 선택지 — 마지막 라인 끝에 우측 박스로 표시</summary>
    public class DialogueChoice
    {
        public string Label;        // 선택지 텍스트 (플레이어가 클릭할 답)
        public string Response;     // NPC의 응답 (선택 시 추가 라인으로 표시)
        public Action OnSelect;     // 선택 시 추가 콜백 (단계 진행 등)

        public DialogueChoice(string label, string response = null, Action onSelect = null)
        {
            Label = label;
            Response = response;
            OnSelect = onSelect;
        }
    }
    /// <summary>
    /// 화면 하단 RPG 스타일 대화 시스템.
    /// StartDialogue(speaker, lines)로 시작 — 라인이 typewriter 효과로 한 글자씩 표시.
    /// Space/Enter/E로 다음 라인. 타이핑 중이면 즉시 완성, 완성 상태면 다음 라인.
    /// 마지막 라인 후 OnEnded 콜백 호출.
    /// </summary>
    public class DialogueSystem : MonoBehaviour
    {
        public static DialogueSystem Instance { get; private set; }

        [Header("Typewriter")]
        public float charsPerSecond = 45f;

        // 현재 대화 상태
        public bool IsActive { get; private set; }
        public string CurrentSpeaker { get; private set; }
        public string CurrentFullLine { get; private set; }
        public string CurrentVisibleLine { get; private set; }
        public bool LineComplete { get; private set; }
        public bool IsLastLine => lines != null && lineIndex >= lines.Count - 1;

        public float OpenTime { get; private set; } // 슬라이드 인 애니메이션용
        public Transform CurrentNPCTransform { get; private set; } // 카메라 줌인 대상
        // 대화가 끝난 프레임 — 대화를 닫은 Space가 같은 프레임에 상호작용을 다시 여는 것 방지
        public int ClosedFrame { get; private set; } = -1;

        // 선택지 (마지막 라인에서만 표시, 선택 전엔 LineComplete여도 다음 라인 진행 차단)
        public IReadOnlyList<DialogueChoice> CurrentChoices => choices;
        public bool HasChoices => choices != null && choices.Count > 0;
        public bool AwaitingChoice => IsActive && IsLastLine && LineComplete && HasChoices;

        private List<string> lines;
        private List<string> lineSpeakers;   // 각 라인별 화자 — NPC 라인 vs 플레이어 응답 라인 구분
        private string npcSpeaker;            // 원래 NPC speaker (선택지 응답 복귀용)
        private List<DialogueChoice> choices;
        private int lineIndex;
        private float charProgress;
        private Action onEnded;

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
            if (FindFirstObjectByType<DialogueSystem>() != null) return;
            var go = new GameObject("DialogueSystem");
            go.AddComponent<DialogueSystem>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        /// <summary>여러 라인 대화 시작. 종료 시 onEnded 콜백 호출.</summary>
        public void StartDialogue(string speaker, IEnumerable<string> textLines,
            Transform npcTransform = null, Action onEnded = null,
            IEnumerable<DialogueChoice> playerChoices = null)
        {
            lines = new List<string>();
            lineSpeakers = new List<string>();
            npcSpeaker = speaker ?? "";
            foreach (var t in textLines)
            {
                if (string.IsNullOrEmpty(t)) continue;
                lines.Add(t);
                lineSpeakers.Add(npcSpeaker);  // 모든 시작 라인은 NPC speaker
            }
            if (lines.Count == 0) return;

            choices = null;
            if (playerChoices != null)
            {
                choices = new List<DialogueChoice>();
                foreach (var c in playerChoices)
                    if (c != null) choices.Add(c);
                if (choices.Count == 0) choices = null;
            }

            CurrentSpeaker = npcSpeaker;
            CurrentNPCTransform = npcTransform;
            lineIndex = 0;
            this.onEnded = onEnded;
            IsActive = true;
            OpenTime = Time.time;
            StartLine();
        }

        /// <summary>한 줄짜리 간편 호출 — \n\n으로 분할</summary>
        public void StartDialogue(string speaker, string text,
            Transform npcTransform = null, Action onEnded = null,
            IEnumerable<DialogueChoice> playerChoices = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            var split = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            StartDialogue(speaker, split, npcTransform, onEnded, playerChoices);
        }

        /// <summary>플레이어가 선택지 선택 — index는 0-based</summary>
        public void SelectChoice(int index)
        {
            if (!AwaitingChoice) return;
            if (index < 0 || index >= choices.Count) return;
            var choice = choices[index];
            choices = null; // 선택지 닫음

            // 응답 라인을 다음 라인으로 추가하고 진행
            // 플레이어 발화는 speaker="나", NPC 응답은 원래 npcSpeaker로 복귀
            if (!string.IsNullOrEmpty(choice.Response))
            {
                lines.Add(choice.Label);
                lineSpeakers.Add("나");
                lines.Add(choice.Response);
                lineSpeakers.Add(npcSpeaker);
                lineIndex = lines.Count - 2;
                StartLine();
            }
            else
            {
                // 응답 없으면 그냥 종료
                lineIndex = lines.Count;
            }

            choice.OnSelect?.Invoke();
        }

        private void StartLine()
        {
            CurrentFullLine = lines[lineIndex];
            // 화자 라벨 갱신 — 플레이어 응답이면 "나", NPC 응답이면 NPC 이름
            if (lineSpeakers != null && lineIndex < lineSpeakers.Count)
                CurrentSpeaker = lineSpeakers[lineIndex];
            CurrentVisibleLine = "";
            charProgress = 0f;
            LineComplete = false;
        }

        private void Update()
        {
            if (!IsActive) return;

            // 타이프라이터 진행
            if (!LineComplete)
            {
                int prevVisible = CurrentVisibleLine != null ? CurrentVisibleLine.Length : 0;
                charProgress += Time.deltaTime * charsPerSecond;
                int visible = Mathf.Min(CurrentFullLine.Length, Mathf.FloorToInt(charProgress));
                CurrentVisibleLine = CurrentFullLine.Substring(0, visible);

                // 글자가 새로 추가됐고, 공백/줄바꿈이 아니면 타이핑 비프 재생
                if (visible > prevVisible && visible > 0)
                {
                    char newChar = CurrentFullLine[visible - 1];
                    if (!char.IsWhiteSpace(newChar))
                        SfxManager.PlayTypewriter();
                }

                if (visible >= CurrentFullLine.Length)
                {
                    LineComplete = true;
                }
            }

            // 시작 직후 0.2초는 입력 무시 — E키로 대화를 막 시작한 같은 프레임에
            // wasPressedThisFrame이 또 감지되어 즉시 완성/스킵되는 것 방지
            if (Time.time - OpenTime < 0.2f) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            // 선택지 대기 중이면 advance 무시, 숫자키(1~4)로만 선택
            if (AwaitingChoice)
            {
                for (int i = 0; i < Mathf.Min(choices.Count, 4); i++)
                {
                    Key num = (Key)((int)Key.Digit1 + i);
                    if (kb[num].wasPressedThisFrame)
                    {
                        SelectChoice(i);
                        return;
                    }
                }
                return; // 선택 전엔 Space/Enter/E 무시
            }

            // 진행 입력 (Space / Enter — 상호작용 키와 동일)
            bool advance = kb.spaceKey.wasPressedThisFrame
                || kb.enterKey.wasPressedThisFrame;
            if (!advance) return;

            if (!LineComplete)
            {
                // 타이핑 중 → 즉시 완성
                CurrentVisibleLine = CurrentFullLine;
                charProgress = CurrentFullLine.Length;
                LineComplete = true;
            }
            else
            {
                // 다음 라인 또는 종료
                lineIndex++;
                if (lineIndex >= lines.Count)
                {
                    End();
                }
                else
                {
                    StartLine();
                    SfxManager.PlayDialogue();
                }
            }
        }

        private void End()
        {
            IsActive = false;
            ClosedFrame = Time.frameCount;
            lines = null;
            lineSpeakers = null;
            choices = null;
            CurrentSpeaker = "";
            npcSpeaker = "";
            CurrentFullLine = "";
            CurrentVisibleLine = "";
            CurrentNPCTransform = null;
            var cb = onEnded;
            onEnded = null;
            cb?.Invoke();
        }

        /// <summary>외부에서 강제 종료 (콜백 호출 안 함)</summary>
        public void ForceClose()
        {
            IsActive = false;
            ClosedFrame = Time.frameCount;
            lines = null;
            lineSpeakers = null;
            choices = null;
            onEnded = null;
            CurrentSpeaker = "";
            npcSpeaker = "";
            CurrentFullLine = "";
            CurrentVisibleLine = "";
            CurrentNPCTransform = null;
        }
    }
}
