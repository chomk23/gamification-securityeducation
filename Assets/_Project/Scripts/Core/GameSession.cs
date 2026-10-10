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
        // 이 단서가 relatedRole 용의자의 알리바이를 깨는 기록인지 (진범 한 명). 화면에는 드러내지 않는다.
        public bool contradictsAlibi = false;
        // 용의자 담당 장소의 시각 기록이 들어 있는 카드 — 세 용의자 모두 받으며 보드에서 같은 '▲ 알리바이 대조' 표시.
        // 이 중 어느 기록이 진술과 어긋나는지는 플레이어가 대조해서 판단한다.
        public bool alibiEvidence = false;
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

        // 제한 시간 없음 — 결과창 '사용 시간' 기록용으로 경과 시간만 잰다 (인트로 컷씬 동안은 멈춤)
        public float ElapsedTime { get; private set; }
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
        /// 무고한 2명은 진실, 진범은 거짓 — 거짓 알리바이만 담당 장소 기록(GetRoomRecord)과 시간대가 어긋난다.
        /// 대화·인벤토리 보드 양쪽에서 동일 텍스트를 참조한다.
        /// </summary>
        public static string GetAlibi(int role)
        {
            switch (role)
            {
                case 1: return "어젯밤 자정 전에 퇴근했고, 그 뒤로는 집에 있었어요. 연구실엔 다시 안 갔습니다.";
                case 2: return "전 어제 23시 정각에 퇴근했어요. 그 뒤로는 사내망에 접속한 적이 없습니다.";
                case 3: return "CCTV 점검은 저녁 7시 전에 끝냈고, 그 뒤로 통제실엔 들어가지 않았습니다.";
                default: return "";
            }
        }

        // 역할(1~3)별 기록 시각 범위 — '어제 0시'부터 센 분, 양 끝 포함 (24시 이후는 '오늘').
        // 무고 기록은 알리바이 기준 시각보다 5분 이상 이르고, 진범 기록은 20분 이상 늦다 (몇 분 차이로 헷갈리지 않게).
        // 알리바이(GetAlibi) 기준 시각: 연구원 자정 / 네트워크관리자 23시 / 시설관리자 19시.
        private static readonly int[,] RecordRanges =
        {
            // 무고 최소, 무고 최대, 진범 최소, 진범 최대
            { 0, 0, 0, 0 },
            { 22 * 60 + 30, 23 * 60 + 50, 24 * 60 + 40, 27 * 60 + 30 }, // 연구원 마지막 퇴실
            { 22 * 60 + 10, 22 * 60 + 55, 23 * 60 + 25, 24 * 60 + 50 }, // 네트워크관리자 마지막 로그아웃
            { 18 * 60 + 15, 18 * 60 + 55, 20 * 60 + 30, 23 * 60 + 30 }, // 시설관리자 마지막 퇴실
        };

        // 이번 판의 기록 시각 [역할, 0=무고 / 1=진범] — 처음 요청할 때 정하고 StartNewRun에서 비운다.
        // StartNewRun 없이 FacilityScene을 바로 실행해도 첫 요청 때 만들어진다.
        private static int[,] runRecordMinutes;

        private static void EnsureRunRecordTimes()
        {
            if (runRecordMinutes != null) return;
            runRecordMinutes = new int[4, 2];
            for (int role = 1; role <= 3; role++)
            {
                runRecordMinutes[role, 0] = Random.Range(RecordRanges[role, 0], RecordRanges[role, 1] + 1);
                runRecordMinutes[role, 1] = Random.Range(RecordRanges[role, 2], RecordRanges[role, 3] + 1);
            }
        }

        private static string FormatRecordTime(int minutes, bool dawn)
        {
            int h = minutes / 60, m = minutes % 60;
            if (h >= 24) return $"오늘 {(dawn ? "새벽 " : "")}{h - 24}시 {m:D2}분";
            return $"어제 {h}시 {m:D2}분";
        }

        /// <summary>
        /// 용의자 담당 장소의 '마지막 퇴실·로그아웃 시각' 한 줄 — 그 장소의 보안 교육을 마치면 세 용의자 모두 받는다.
        /// 세 줄 모두 같은 형식이고 범행 내용은 적지 않는다 (수상한 문구만 보고 고를 수 없게).
        /// 진범(contradicts=true)의 시각만 알리바이가 말한 시각 뒤로 넘어간다 — 대조는 플레이어 몫 (중간 난이도).
        /// 시각은 판마다 RecordRanges 안에서 새로 정하고 한 판 동안은 고정 — 다시 하기에서 지난 판 시각을 외워 맞힐 수 없게.
        /// 범위가 역할마다 달라 가장 늦은 시각이 늘 진범이 되지는 않는다 (예: 무고한 연구원 23시대 vs 진범 시설관리자 21시대).
        /// </summary>
        public static string GetRoomRecord(int role, bool contradicts)
        {
            if (role < 1 || role > 3) return "";
            EnsureRunRecordTimes();
            string time = FormatRecordTime(runRecordMinutes[role, contradicts ? 1 : 0], dawn: role == 1);
            switch (role)
            {
                case 1: return "[연구동 출입 기록] 연구원 카드 — 마지막 퇴실: " + time;
                case 2: return "[사내망 접속 기록] 네트워크관리자 계정 — 마지막 로그아웃: " + time;
                case 3: return "[통제실 출입 기록] 시설관리자 카드 — 마지막 퇴실: " + time;
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
            runRecordMinutes = null; // 장소 기록 시각도 새 판에서 다시 정한다
            Outcome = RunOutcome.Ongoing;
            OutcomeMessage = "";
            LastEncounterRewardClues = 0;
            CollectedClues.Clear();
            quizFirstTryCorrect = 0;
            quizWrongCount = 0;
            WrongQuizzes.Clear();
            hasFacilityCardkey = false;
            hasShownIntroMonologue = false; // 새 런마다 속마음 인트로 다시 보임
            ElapsedTime = 0f;
            TimerActive = true;
            // 실제 스파이는 FacilityScene의 NPCRoster가 정하고 spyRoleIndex를 맞춘다 (여기 값은 임시)
            Debug.Log("[Session] New run");
        }

        private void Update()
        {
            if (!TimerActive || Outcome != RunOutcome.Ongoing) return;
            // 씬을 넘어 살아남는 오브젝트라, 메인 메뉴에 나가 있는 동안은 재지 않는다
            if (SceneManager.GetActiveScene().name != "FacilityScene") return;
            ElapsedTime += Time.deltaTime;
        }

        public void StopTimer() { TimerActive = false; }
        public void ResumeTimer() { if (Outcome == RunOutcome.Ongoing) TimerActive = true; }

        /// <summary>인벤토리에 표시될 단서 항목 추가 (출처별 분류)</summary>
        public void AddClue(string title, string text, ClueSource source)
        {
            AddClue(title, text, source, -1, "");
        }

        /// <summary>인벤토리에 단서 + 관련 용의자 role + 카테고리 태그 추가 (Investigation Board용).
        /// alibiEvidence=true면 보드에서 '알리바이 대조' 카드로 표시된다 (진범 여부와 무관하게 같은 표시).</summary>
        public void AddClue(string title, string text, ClueSource source, int relatedRole, string tag,
            bool contradictsAlibi = false, bool alibiEvidence = false)
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
                contradictsAlibi = contradictsAlibi,
                alibiEvidence = alibiEvidence
            });
            // 모순 여부는 로그에 남기지 않는다 — 웹 빌드에선 브라우저 콘솔로 정답이 보인다
            Debug.Log($"[Clue] +1 [{source}] {title} (role={relatedRole}, tag={tag}, 대조={alibiEvidence})");
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
