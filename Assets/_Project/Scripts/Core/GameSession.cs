using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForTheCompany.Core
{
    public enum RunOutcome { Ongoing, Win, Lose }

    public enum ClueSource { Environment, NPC, Minigame }

    [System.Serializable]
    public class ClueEntry
    {
        public string title;
        public string text;
        public ClueSource source;
        public float acquiredTime;
        // 관련 용의자 역할 (1=연구원, 2=네트워크관리자, 3=시설관리자, -1=무관/전체)
        // 인벤토리에서 용의자 카드 클릭 시 점선 연결 대상 결정
        public int relatedRole = -1;
        // 단서 카테고리 태그 (BADGE/CCTV/NET/DATA/LOG/COMMS 등) — Board UI에서 표시
        public string tag = "";
        // 이 단서가 relatedRole 용의자의 알리바이를 깨는 '결정적 단서'인지.
        // true면 인벤토리 보드에서 ⚠ 모순 표시 — 진범 한 명에게만 부여된다.
        public bool contradictsAlibi = false;
    }

    public class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }

        [Header("Run Progress")]
        public int currentNodeId = -1;
        public List<int> clearedNodeIds = new List<int>();
        public int totalClues = 0;
        public int spyRoleIndex = -1;

        public RunOutcome Outcome { get; private set; } = RunOutcome.Ongoing;
        public string OutcomeMessage { get; private set; } = "";

        [Header("Time Limit")]
        [Tooltip("총 제한 시간(초). 스토리 모드 기본 7분(420초).")]
        public float totalTime = 900f;  // 15분
        public float TimeRemaining { get; private set; }
        public bool TimerActive { get; private set; }

        [Header("Player Stats")]
        public int playerHacking = 3;
        public int playerInvestigation = 3;
        public int playerSecurity = 3;

        public int LastEncounterRewardClues { get; set; }

        public List<ClueEntry> CollectedClues { get; } = new List<ClueEntry>();

        // ── 보안 교육 퀴즈 통계 (결과창 표시용) ──
        [System.Serializable]
        public class WrongQuizEntry
        {
            public string question;
            public string explanation;
        }
        public int quizFirstTryCorrect;   // 첫 시도에 맞춘 문제 수
        public int quizWrongCount;        // 한 번이라도 틀린 문제 수
        public List<WrongQuizEntry> WrongQuizzes { get; } = new List<WrongQuizEntry>();

        [Header("Cardkey")]
        public bool hasFacilityCardkey; // 단계 4(NetworkAdmin) 완료 시 발급 — 전력실 잠금 해제

        [Header("Intro")]
        public bool hasShownIntroMonologue; // FacilityScene 진입 시 속마음 인트로 표시 여부 (한 런당 1회)

        public static readonly string[] SpyRoleNames = { "연구원", "네트워크관리자", "시설관리자" };

        /// <summary>
        /// 용의자 역할(1=연구원,2=네트워크관리자,3=시설관리자)별 알리바이 진술.
        /// 무고한 2명은 진실, 진범은 거짓 — 거짓 알리바이는 결정적 단서와 시간대가 어긋난다.
        /// 대화·인벤토리 보드 양쪽에서 동일 텍스트를 참조한다.
        /// </summary>
        public static string GetAlibi(int role)
        {
            switch (role)
            {
                case 1: return "어제 새벽엔 퇴근하고 집에 있었어요. 연구실 근처에도 안 갔습니다.";
                case 2: return "전 어제 23시 정각에 퇴근했어요. 그 뒤로는 사내망 접속 기록이 없을 겁니다.";
                case 3: return "CCTV 점검은 저녁 7시에 끝냈고, 그 후엔 통제실에 들어가지 않았습니다.";
                default: return "";
            }
        }

        /// <summary>
        /// 진범의 알리바이를 깨는 결정적 단서 텍스트. 해당 역할이 실제 스파이일 때만,
        /// 그 사람 담당 장소의 보안 교육 모듈 완료 시 수집된다 (relatedRole=role, contradictsAlibi=true).
        /// </summary>
        public static string GetContradictionClue(int role)
        {
            switch (role)
            {
                case 1: return "새벽 2시 14분, 연구실 서버 백업이 본인 계정으로 외부 USB에 복사됨. '집에 있었다'는 진술과 시간·장소가 어긋난다.";
                case 2: return "어제 23시 47분, 본인 단말에서 외부 IP로 1.2GB가 전송됨. '23시 퇴근 후 미접속'이라는 진술과 어긋난다.";
                case 3: return "어제 22시 30분, 본인 카드키로 통제실 재출입 + CCTV 수동 정지 기록. '7시에 퇴근'이라는 진술과 어긋난다.";
                default: return "";
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void StartNewRun()
        {
            // 매 새 런마다 다른 Random 시퀀스 보장 (퀴즈 풀에서 매번 다른 문제 선택되도록)
            Random.InitState(System.Environment.TickCount);

            currentNodeId = -1;
            clearedNodeIds.Clear();
            totalClues = 0;
            spyRoleIndex = Random.Range(0, SpyRoleNames.Length);
            Outcome = RunOutcome.Ongoing;
            OutcomeMessage = "";
            LastEncounterRewardClues = 0;
            CollectedClues.Clear();
            quizFirstTryCorrect = 0;
            quizWrongCount = 0;
            WrongQuizzes.Clear();
            hasFacilityCardkey = false;
            hasShownIntroMonologue = false; // 새 런마다 속마음 인트로 다시 보임
            TimeRemaining = totalTime;
            TimerActive = true;
#if UNITY_EDITOR
            Debug.Log($"[Session] New run — spy role = {SpyRoleNames[spyRoleIndex]} (Editor 전용), time = {totalTime:F0}s");
#else
            Debug.Log($"[Session] New run — 스파이 무작위 배정됨, 시간 {totalTime:F0}초");
#endif
        }

        private void Update()
        {
            if (!TimerActive || Outcome != RunOutcome.Ongoing) return;
            // 씬을 넘어 살아남는 오브젝트라, 일시정지 메뉴로 메인 메뉴에 나가도 타이머가 계속 흐르던 문제 방지
            if (SceneManager.GetActiveScene().name != "FacilityScene") return;
            TimeRemaining -= Time.deltaTime;
            if (TimeRemaining <= 0f)
            {
                TimeRemaining = 0f;
                TimerActive = false;
                string actualSpy = spyRoleIndex >= 0 && spyRoleIndex < SpyRoleNames.Length
                    ? SpyRoleNames[spyRoleIndex] : "?";
                DeclareLose($"시간 초과 — 산업스파이를 잡지 못했습니다. 실제 스파이는 {actualSpy}.");
            }
        }

        public void StopTimer() { TimerActive = false; }
        public void ResumeTimer() { if (Outcome == RunOutcome.Ongoing) TimerActive = true; }

        /// <summary>인벤토리에 표시될 단서 항목 추가 (출처별 분류)</summary>
        public void AddClue(string title, string text, ClueSource source)
        {
            AddClue(title, text, source, -1, "");
        }

        /// <summary>인벤토리에 단서 + 관련 용의자 role + 카테고리 태그 추가 (Investigation Board용).
        /// contradictsAlibi=true면 해당 용의자의 알리바이를 깨는 결정적 단서로 표시된다.</summary>
        public void AddClue(string title, string text, ClueSource source, int relatedRole, string tag,
            bool contradictsAlibi = false)
        {
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(text)) return;
            CollectedClues.Add(new ClueEntry
            {
                title = title,
                text = text,
                source = source,
                acquiredTime = Time.time,
                relatedRole = relatedRole,
                tag = tag ?? "",
                contradictsAlibi = contradictsAlibi
            });
            Debug.Log($"[Clue] +1 [{source}] {title} (role={relatedRole}, tag={tag}, 모순={contradictsAlibi})");
        }

        public void DeclareWin(string message)
        {
            if (Outcome != RunOutcome.Ongoing) return;
            Outcome = RunOutcome.Win;
            OutcomeMessage = message;
        }

        public void DeclareLose(string message)
        {
            if (Outcome != RunOutcome.Ongoing) return;
            Outcome = RunOutcome.Lose;
            OutcomeMessage = message;
        }
    }
}
