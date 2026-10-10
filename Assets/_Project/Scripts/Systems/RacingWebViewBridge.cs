using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForTheCompany.Systems
{
    /// <summary>
    /// UnityWebBrowser Canvas의 표시/숨김을 CanvasGroup.alpha로 제어한다.
    /// Canvas는 항상 활성 상태(CEF 프로세스 유지) — show=alpha 1, hide=alpha 0.
    /// 이로써 플레이어가 콘솔에 도착할 때쯤이면 CEF가 이미 로드되어 PRESS START가 즉시 반응.
    /// 씬에 "RacingWebViewCanvas"가 없으면 외부 브라우저(Application.OpenURL)로 폴백.
    /// 데스크톱 엔진(CEF)이 시작에 실패하거나 Show 후 일정 시간 안에 연결되지 않으면
    /// WebView를 숨기고 외부 브라우저 + 등수 입력 모달로 전환한다 (RacingMission 단계가 막히지 않게).
    /// </summary>
    public class RacingWebViewBridge : MonoBehaviour
    {
        public static RacingWebViewBridge Instance { get; private set; }

        [Tooltip("RawImage + WebBrowserUIBasic 컴포넌트가 붙은 Canvas root. 비워두면 자동으로 'RacingWebViewCanvas' 이름의 GameObject를 찾는다.")]
        public GameObject canvasRoot;

        private CanvasGroup canvasGroup;
        private object cachedClient; // WebBrowserClient (reflection)

        // Show 후 이 시간 동안 엔진이 준비(ReadySignalReceived + IsConnected)되지 않으면 대체 흐름으로 전환
        private const float EmbedReadyTimeoutSec = 15f;
        private Coroutine embedWatchdog;
        private bool startupTimedOut;     // 감시 타임아웃 기록 — 이후 엔진이 준비되면 다시 사용 가능
        private bool warnedStateCheck;
        private ScriptableObject runtimeCommsLayer; // 빈 포트를 넣은 TCPCommunicationLayer 런타임 복사본

        private enum EngineState { Pending, Ready, Failed }

        private const System.Reflection.BindingFlags PublicInstance =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        private const System.Reflection.BindingFlags PrivateInstance =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: CEF(데스크톱 전용) 대신 DOM iframe 사용 — Assets/Plugins/WebGL/RacingIframe.jslib
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void RacingIframe_Show(string url);
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void RacingIframe_Hide();

        private System.Action<int> webglRaceCallback;

        public bool IsAvailable => true; // iframe은 항상 사용 가능
#else
        /// <summary>임베드 WebView를 띄울 수 있는가 — 엔진이 실패로 판정됐거나 감시 타임아웃 뒤 아직 준비 전이면 false</summary>
        public bool IsAvailable
        {
            get
            {
                if (canvasRoot == null) return false;
                var state = GetEngineState();
                if (state == EngineState.Failed) return false;
                return !startupTimedOut || state == EngineState.Ready;
            }
        }
#endif
        public bool IsShowing { get; private set; }

        private bool jsRegistered;

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
            // FacilityScene 이외 씬에서는 WebView Canvas가 없으므로 스킵
            if (SceneManager.GetActiveScene().name != "FacilityScene") return;
            if (FindFirstObjectByType<RacingWebViewBridge>() != null) return;
            var go = new GameObject("RacingWebViewBridge");
            go.AddComponent<RacingWebViewBridge>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: CEF를 쓰지 않으므로 캔버스/UWB 초기화 전부 스킵.
            // (씬의 RacingWebViewCanvas는 미사용 — missing script 경고는 무해)
            return;
#endif

            if (canvasRoot == null)
            {
                var allCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                foreach (var c in allCanvases)
                {
                    if (c != null && c.gameObject.name == "RacingWebViewCanvas")
                    {
                        canvasRoot = c.gameObject;
                        break;
                    }
                }
            }

            if (canvasRoot != null)
            {
                // CEF 콜드 스타트(첫 실행)는 4초 기본 타임아웃을 넘겨서 UWB가 연결을 포기 → 빈 화면.
                // SetActive(true)로 CEF Init이 트리거되기 전에 engineStartupTimeout을 크게 올린다.
                SetEngineStartupTimeout(60000);
#if !UNITY_WEBGL || UNITY_EDITOR
                // 포트·캐시 경로도 Init(WebBrowserUIBasic.Start) 전에만 바꿀 수 있다
                PrepareClientBeforeInit();
#endif

                // 항상 active 유지 (CEF pre-warm), CanvasGroup으로 시각적/입력 차단
                canvasRoot.SetActive(true);
                canvasGroup = canvasRoot.GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = canvasRoot.AddComponent<CanvasGroup>();
                SetCanvasVisible(false);
                IsShowing = false;
                Debug.Log("[RacingWebView] Canvas 발견 — CEF 백그라운드 로드 시작");
            }
            else
            {
                Debug.LogWarning("[RacingWebView] 'RacingWebViewCanvas' GameObject를 못 찾음 — 외부 브라우저 폴백");
            }
        }

        /// <summary>
        /// Editor 시점에 박힌 절대경로(C:/.../Assets/StreamingAssets/security-race.html)는
        /// 빌드본에서 작동 안 함. CEF 초기화 후 런타임 streamingAssetsPath로 다시 LoadUrl.
        /// </summary>
        private void Start()
        {
            if (canvasRoot == null) return;
            // 게임 시작 시 CEF pre-warm 로드 (ready 폴링)
            StartCoroutine(LoadWhenReady(retryOnce: true));
        }

        private static string SecurityRaceUrl()
        {
            return "file:///" + System.IO.Path.Combine(
                Application.streamingAssetsPath, "security-race.html").Replace("\\", "/");
        }

        /// <summary>
        /// CEF 엔진이 ready 신호를 보낼 때까지 폴링한 뒤 LoadUrl.
        /// cold start(첫 실행 시 CEF가 늦게 connect)로 빈 화면 뜨던 문제 해결.
        /// 엔진이 실패로 판정되면(Dispose·프로세스 종료·Init 예외) 즉시 중단.
        /// retryOnce: ready 직후 첫 LoadUrl이 가끔 실패하는 콜드 스타트(pre-warm)용 재시도.
        /// 창을 연 뒤(Show)에는 재시도하지 않는다 — 0.6초 뒤 다시 불러오면 이미 시작한 레이스가 초기화됨.
        /// </summary>
        private System.Collections.IEnumerator LoadWhenReady(bool retryOnce)
        {
            string url = SecurityRaceUrl();

            // 1) client 등장 대기 (최대 8초)
            object client = GetOrFindClient();
            float ct = 0f;
            while (client == null && ct < 8f)
            {
                ct += 0.3f;
                yield return new WaitForSeconds(0.3f);
                client = GetOrFindClient();
            }
            if (client == null) yield break;

            // 2) ready 폴링 (최대 60초) — CEF cold start가 첫 실행 시 10초 이상 걸림.
            //    ready 확인되면 LoadUrl. ready 전에는 LoadUrl 호출 안 함 (Exception 방지).
            float t = 0f;
            while (t < 60f)
            {
                var state = GetEngineState();
                if (state == EngineState.Failed)
                {
                    Debug.LogWarning("[RacingWebView] CEF 엔진 시작 실패로 판정 — LoadUrl 중단 (레이싱은 외부 브라우저로 대체)");
                    yield break;
                }
                if (state == EngineState.Ready)
                {
                    LoadUrlOnClient(url);
                    Debug.Log("[RacingWebView] CEF ready 확인 → LoadUrl");
                    if (retryOnce)
                    {
                        // ready 직후 첫 LoadUrl이 가끔 실패하므로 짧게 한 번 더 시도
                        yield return new WaitForSeconds(0.6f);
                        // 그 사이 플레이어가 레이싱을 열었으면 진행 중인 화면을 덮어쓰지 않음
                        if (!IsShowing) LoadUrlOnClient(url);
                    }
                    yield break;
                }
                t += 0.3f;
                yield return new WaitForSeconds(0.3f);
            }
            // 3) 60초 폴링에도 ready 안 됨 — Exception 위험 있지만 마지막 시도
            Debug.LogWarning("[RacingWebView] CEF ready 60초 타임아웃 — 마지막 LoadUrl 시도");
            LoadUrlOnClient(url);
        }

        /// <summary>
        /// WebBrowserClient(UWB 패키지 소스 기준 멤버명) 상태를 reflection으로 판정.
        /// Ready  : ReadySignalReceived && IsConnected — LoadUrl/ExecuteJs가 예외 없이 동작하는 조건
        /// Failed : HasDisposed(시작 타임아웃·오류로 정리됨),
        ///          엔진 프로세스가 이미 종료됐거나 시작조차 못 함(포트 충돌·실행 차단, 준비 후 크래시·강제 종료 포함),
        ///          private hasInitialized만 true이고 HasInitialized는 false(Init 도중 예외 — 엔진 exe 없음·격리)
        /// Pending: 그 외 (아직 시작 중) — Show 후에는 감시 타임아웃이 대체 흐름을 보장
        /// </summary>
        private EngineState GetEngineState()
        {
            if (canvasRoot == null) return EngineState.Failed;
            var client = GetOrFindClient();
            if (client == null) return EngineState.Failed;
            try
            {
                bool? disposed = ReadBool(client, "HasDisposed", PublicInstance);
                bool? ready = ReadBool(client, "ReadySignalReceived", PublicInstance);
                bool? connected = ReadBool(client, "IsConnected", PublicInstance);
                if (disposed == null || ready == null || connected == null)
                {
                    // 멤버를 못 찾으면 준비됐다고 가정하지 않는다 — Show 감시 타임아웃이 대체 흐름으로 넘김
                    if (!warnedStateCheck)
                    {
                        warnedStateCheck = true;
                        Debug.LogWarning("[RacingWebView] WebBrowserClient 상태 멤버(HasDisposed/ReadySignalReceived/IsConnected)를 못 찾음");
                    }
                    return EngineState.Pending;
                }
                if (disposed.Value) return EngineState.Failed;
                // Ready보다 먼저 확인 — 준비 후 엔진이 죽어도 UWB는 Dispose하지 않고 IsConnected도 true로 남는다
                if (HasEngineProcessDied(client)) return EngineState.Failed;
                if (ready.Value && connected.Value) return EngineState.Ready;

                bool? initEntered = ReadBool(client, "hasInitialized", PrivateInstance);
                bool? initDone = ReadBool(client, "HasInitialized", PublicInstance);
                if (initEntered == true && initDone == false) return EngineState.Failed;
            }
            catch (System.Exception e)
            {
                // 0.25~0.3초마다 불리므로 한 번만 남김
                if (!warnedStateCheck)
                {
                    warnedStateCheck = true;
                    Debug.LogWarning($"[RacingWebView] 엔진 상태 확인 실패: {(e.InnerException ?? e).Message}");
                }
            }
            return EngineState.Pending;
        }

        /// <summary>WebBrowserClient.engineProcess(private, internal EngineProcess).HasExited 확인</summary>
        private static bool HasEngineProcessDied(object client)
        {
            var field = client.GetType().GetField("engineProcess", PrivateInstance);
            var process = field?.GetValue(client);
            if (process == null) return false; // 아직 시작 전 — 정리된 경우는 HasDisposed로 판정됨
            var hasExited = process.GetType().GetProperty("HasExited", PublicInstance);
            if (hasExited == null || hasExited.PropertyType != typeof(bool)) return false;
            try
            {
                return (bool)hasExited.GetValue(process);
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                // InvalidOperationException: Process.Start가 막혀(백신·AppLocker) 프로세스가 연결되지 않은 상태.
                // 그 밖의 예외로는 판정하지 않는다 — Ready 판정보다 먼저 불리므로 정상 엔진을 막지 않게
                return e.InnerException is System.InvalidOperationException;
            }
        }

        private static bool? ReadBool(object target, string name, System.Reflection.BindingFlags flags)
        {
            var type = target.GetType();
            var p = type.GetProperty(name, flags);
            if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(target);
            var f = type.GetField(name, flags);
            if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(target);
            return null;
        }

        private void LoadUrlOnClient(string url)
        {
            var client = GetOrFindClient();
            if (client == null)
            {
                Debug.LogWarning("[RacingWebView] WebBrowserClient를 찾을 수 없음 — LoadUrl 스킵");
                return;
            }
            try
            {
                var loadUrl = client.GetType().GetMethod("LoadUrl",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new System.Type[] { typeof(string) }, null);
                if (loadUrl != null)
                {
                    loadUrl.Invoke(client, new object[] { url });
                    Debug.Log($"[RacingWebView] 런타임 LoadUrl 호출: {url}");
                }
                else
                {
                    Debug.LogWarning("[RacingWebView] LoadUrl 메서드를 찾을 수 없음");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RacingWebView] LoadUrl 실패: {e.Message}");
            }
        }

        public void Show()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: StreamingAssets의 레이싱 HTML을 iframe으로 표시.
            // iframe에 포커스가 가면 키 입력은 iframe 문서로만 가므로 Unity 키 캡처를 끌 필요가 없다.
            // (WebGLInput은 Unity 6 웹 빌드 컴파일 참조에 없어 쓰면 빌드가 실패한다)
            RacingIframe_Show(Application.streamingAssetsPath + "/security-race.html");
            IsShowing = true;
#else
            if (canvasRoot == null)
            {
                Debug.LogWarning("[RacingWebView] RacingWebViewCanvas를 찾을 수 없음 — 외부 브라우저로 폴백");
                FallbackOpenExternal();
                return;
            }
            SetCanvasVisible(true);
            IsShowing = true;

            // Show 시점에도 ready 폴링 후 LoadUrl — Start() pre-warm이 cold start로 실패했어도 복구.
            // 플레이어가 콘솔 도착 = 게임 시작 후라 CEF가 거의 준비됨 → 빈 화면 방지.
            StartCoroutine(LoadWhenReady(retryOnce: false));

            if (embedWatchdog != null) StopCoroutine(embedWatchdog);
            embedWatchdog = StartCoroutine(WatchEmbeddedView());
#endif
        }

        public void Hide()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RacingIframe_Hide(); // 캔버스로 포커스를 돌려 키 입력을 Unity로 복귀
            IsShowing = false;
#else
            if (canvasRoot == null) return;
            StopWatchdog();
            SetCanvasVisible(false);
            IsShowing = false;
            // 다음 진입 시 인트로 화면으로 시작하도록 페이지 리로드
            ReloadPage();
#endif
        }

        private void SetCanvasVisible(bool visible)
        {
            if (canvasGroup == null) return;
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        private void StopWatchdog()
        {
            if (embedWatchdog == null) return;
            StopCoroutine(embedWatchdog);
            embedWatchdog = null;
        }

        /// <summary>
        /// Show 이후 엔진 상태 감시. 실패(Dispose·프로세스 종료·Init 예외)면 즉시,
        /// 준비 안 된 상태가 EmbedReadyTimeoutSec 이상 이어지면(시작 지연·레이스 중 연결 끊김) 대체 흐름으로 전환.
        /// 흰 RawImage만 보이고 PRESS START를 누를 수 없는 채로 RacingMission 단계가 막히는 것을 막는다.
        /// </summary>
        private System.Collections.IEnumerator WatchEmbeddedView()
        {
            float notReadySince = Time.realtimeSinceStartup;
            while (IsShowing)
            {
                // 첫 판정도 한 박자 뒤 — Launch가 Show 다음 줄에서 phase를 AwaitingRank로 바꾼 뒤여야 한다
                yield return new WaitForSecondsRealtime(0.25f);
                if (!IsShowing) break;

                var state = GetEngineState();
                if (state == EngineState.Ready)
                {
                    notReadySince = Time.realtimeSinceStartup;
                    continue;
                }
                if (state == EngineState.Failed)
                {
                    FailOverToExternal("엔진 시작 실패 또는 종료");
                    yield break;
                }
                if (Time.realtimeSinceStartup - notReadySince >= EmbedReadyTimeoutSec)
                {
                    startupTimedOut = true;
                    FailOverToExternal($"{EmbedReadyTimeoutSec:0}초 안에 엔진이 준비되지 않음");
                    yield break;
                }
            }
            embedWatchdog = null;
        }

        /// <summary>WebView를 숨기고 RacingMissionController에 대체 흐름(외부 브라우저 + 등수 입력 모달)을 맡긴다</summary>
        private void FailOverToExternal(string reason)
        {
            embedWatchdog = null;
            Debug.LogWarning($"[RacingWebView] 임베드 레이싱 사용 불가({reason}) — 외부 브라우저 대체 흐름으로 전환");
            // Hide()와 달리 페이지 리로드는 하지 않음 — 준비 안 된 클라이언트에 ExecuteJs하면 예외
            SetCanvasVisible(false);
            IsShowing = false;
            RacingMissionController.Instance?.OnEmbeddedViewFailed();
        }

        /// <summary>
        /// WebBrowserClient.ExecuteJs("location.reload()")로 HTML 페이지 초기화.
        /// reflection으로 ExecuteJs 메서드 찾음.
        /// </summary>
        private void ReloadPage()
        {
            var client = GetOrFindClient();
            if (client == null) return;
            // 준비 전·실패한 클라이언트는 ExecuteJs/LoadUrl이 예외를 던진다 — 다음 Show의 LoadUrl이 새로 불러옴
            if (GetEngineState() != EngineState.Ready) return;
            try
            {
                var executeJs = client.GetType().GetMethod("ExecuteJs",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new System.Type[] { typeof(string) }, null);
                if (executeJs != null)
                {
                    executeJs.Invoke(client, new object[] { "location.reload();" });
                    Debug.Log("[RacingWebView] 페이지 리로드 (location.reload)");
                    return;
                }
                // fallback: LoadUrl
                var loadUrl = client.GetType().GetMethod("LoadUrl",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new System.Type[] { typeof(string) }, null);
                if (loadUrl != null)
                {
                    string url = "file:///" +
                        System.IO.Path.Combine(Application.streamingAssetsPath, "security-race.html")
                        .Replace("\\", "/");
                    loadUrl.Invoke(client, new object[] { url });
                    Debug.Log("[RacingWebView] 페이지 리로드 (LoadUrl)");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RacingWebView] 페이지 리로드 실패: {e.Message}");
            }
        }

        /// <summary>
        /// CEF 엔진 시작 타임아웃(ms)을 reflection으로 변경.
        /// 기본 4000ms는 콜드 스타트 시 너무 짧아 UWB가 연결을 포기 → 빈 화면 발생.
        /// SetActive(true)로 CEF Init이 시작되기 전에 호출해야 효과 있음.
        /// </summary>
        private void SetEngineStartupTimeout(int ms)
        {
            var client = GetOrFindClient();
            if (client == null)
            {
                Debug.LogWarning("[RacingWebView] WebBrowserClient를 못 찾음 — engineStartupTimeout 설정 스킵");
                return;
            }
            try
            {
                var field = client.GetType().GetField("engineStartupTimeout",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(int))
                {
                    field.SetValue(client, ms);
                    Debug.Log($"[RacingWebView] engineStartupTimeout {ms}ms로 설정 (콜드 스타트 대응)");
                }
                else
                {
                    Debug.LogWarning("[RacingWebView] engineStartupTimeout 필드를 못 찾음");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RacingWebView] engineStartupTimeout 설정 실패: {e.Message}");
            }
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        /// <summary>
        /// CEF Init 전에 통신 포트와 캐시 경로를 이 프로세스 전용으로 바꾼다.
        /// 고정 5555/5556을 다른 프로그램(안드로이드 에뮬레이터 adb 등)이나 같이 띄운 게임이 쓰고 있으면
        /// 엔진이 연결되지 못하고, 같은 캐시 폴더를 두 엔진이 쓰면 두 번째 엔진이 죽으며 첫 게임 위에 Chromium 창을 띄운다.
        /// </summary>
        private void PrepareClientBeforeInit()
        {
            var client = GetOrFindClient();
            if (client == null) return;
            try
            {
                if (ReadBool(client, "HasInitialized", PublicInstance) == true)
                {
                    Debug.LogWarning("[RacingWebView] WebBrowserClient가 이미 Init됨 — 포트/캐시 경로 변경 스킵");
                    return;
                }
                AssignFreeTcpPorts(client);
#if !UNITY_EDITOR
                AssignInstanceCachePath(client);
#endif
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RacingWebView] 포트/캐시 경로 설정 실패: {(e.InnerException ?? e).Message}");
            }
        }

        /// <summary>
        /// 공유 에셋(TCP Communication Layer.asset)은 건드리지 않고 런타임 복사본에 빈 루프백 포트를 넣어
        /// browserClient.communicationLayer에 끼운다. TCPCommunicationLayer가 아니면(파이프 등) 그대로 둔다.
        /// </summary>
        private void AssignFreeTcpPorts(object client)
        {
            var layerField = client.GetType().GetField("communicationLayer", PublicInstance);
            var shared = layerField?.GetValue(client) as ScriptableObject;
            if (shared == null || shared.GetType().Name != "TCPCommunicationLayer") return;

            var layerType = shared.GetType();
            var inField = layerType.GetField("inPort", PublicInstance);
            var outField = layerType.GetField("outPort", PublicInstance);
            if (inField == null || outField == null
                || inField.FieldType != typeof(int) || outField.FieldType != typeof(int))
            {
                Debug.LogWarning("[RacingWebView] TCPCommunicationLayer.inPort/outPort를 못 찾음 — 고정 포트 사용");
                return;
            }
            if (!TryGetFreeLoopbackPorts(out int inPort, out int outPort))
            {
                Debug.LogWarning("[RacingWebView] 빈 루프백 포트를 못 구함 — 고정 포트 사용");
                return;
            }

            var copy = Instantiate(shared);
            copy.name = shared.name + " (Runtime)";
            inField.SetValue(copy, inPort);
            outField.SetValue(copy, outPort);
            layerField.SetValue(client, copy);
            runtimeCommsLayer = copy;
            Debug.Log($"[RacingWebView] CEF 통신 포트 in {inPort} / out {outPort} 사용");
        }

        /// <summary>
        /// OS가 고른 빈 루프백 포트 2개. 두 리스너를 함께 열어 서로 다른 포트를 받은 뒤 닫는다.
        /// TCPCommunicationLayer의 [Range(1024, 65353)] 밖 포트는 건너뛴다.
        /// </summary>
        private static bool TryGetFreeLoopbackPorts(out int first, out int second)
        {
            first = second = 0;
            var listeners = new System.Collections.Generic.List<System.Net.Sockets.TcpListener>();
            try
            {
                for (int i = 0; i < 8 && second == 0; i++)
                {
                    var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                    l.Start();
                    listeners.Add(l);
                    int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
                    if (port < 1024 || port > 65353) continue;
                    if (first == 0) first = port;
                    else if (port != first) second = port;
                }
                return first != 0 && second != 0;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RacingWebView] 빈 포트 탐색 실패: {e.Message}");
                return false;
            }
            finally
            {
                foreach (var l in listeners) l.Stop();
            }
        }

#if !UNITY_EDITOR
        // 같은 설치 폴더의 기본 캐시(…_Data/UWBCache)를 이 프로세스가 쓰고 있다는 표시. 프로세스 종료 시 OS가 해제.
        // 씬을 다시 불러도 프로세스 단위로 유지해야 하므로 static.
        private static System.Threading.Mutex defaultCacheOwner;
        private static bool cacheOwnerChecked;
        private static bool cacheOwnerCheckFailed;

        /// <summary>
        /// 같은 폴더의 게임이 이미 기본 캐시 경로를 쓰고 있으면 이 프로세스 전용 임시 캐시 경로로 바꾼다.
        /// 혼자 실행 중이면 기존 기본 경로를 그대로 쓴다.
        /// </summary>
        private static void AssignInstanceCachePath(object client)
        {
            if (OwnsDefaultCache()) return;
            var prop = client.GetType().GetProperty("CachePath", PublicInstance);
            if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(System.IO.FileInfo)) return;
            string dir = System.IO.Path.Combine(Application.temporaryCachePath,
                "UWBCache_" + System.Diagnostics.Process.GetCurrentProcess().Id);
            prop.SetValue(client, new System.IO.FileInfo(dir));
            Debug.Log($"[RacingWebView] 다른 게임 인스턴스가 기본 CEF 캐시 사용 중 — 전용 캐시 경로 사용: {dir}");
        }

        private static bool OwnsDefaultCache()
        {
            if (!cacheOwnerChecked)
            {
                cacheOwnerChecked = true;
                try
                {
                    // 뮤텍스 이름에 '\'를 쓸 수 없으므로 dataPath를 해시로 바꾼다
                    string name = "ForTheCompany.UWBCache." + StableHash(Application.dataPath);
                    var mutex = new System.Threading.Mutex(true, name, out bool createdNew);
                    if (createdNew) defaultCacheOwner = mutex;
                    else mutex.Dispose();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[RacingWebView] 캐시 경로 점유 확인 실패: {e.Message}");
                    cacheOwnerCheckFailed = true;
                }
            }
            // 판정 불가면 기존 동작(기본 경로) 유지
            return defaultCacheOwner != null || cacheOwnerCheckFailed;
        }

        private static string StableHash(string s)
        {
            // FNV-1a 64 — string.GetHashCode는 프로세스마다 같다는 보장이 없음
            ulong h = 14695981039346656037UL;
            foreach (char c in s.ToLowerInvariant())
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            return h.ToString("x16");
        }
#endif
#endif

        private object GetOrFindClient()
        {
            if (cachedClient != null) return cachedClient;
            if (canvasRoot == null) return null;
            var allBehaviours = canvasRoot.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var b in allBehaviours)
            {
                if (b == null) continue;
                if (b.GetType().Name != "WebBrowserUIBasic") continue;
                var clientField = b.GetType().GetField("browserClient",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var client = clientField?.GetValue(b);
                if (client != null)
                {
                    cachedClient = client;
                    return client;
                }
            }
            return null;
        }

        /// <summary>
        /// HTML에서 uwb.ExecuteJsMethod("OnRaceFinished", rank) 호출 시 실행될 콜백 등록
        /// </summary>
        public void RegisterRaceFinishedCallback(System.Action<int> callback)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: 콜백을 저장해두고 iframe postMessage(→ jslib → SendMessage)로 호출
            if (callback != null) webglRaceCallback = callback;
#else
            if (canvasRoot == null || callback == null) return;
            if (jsRegistered) return; // 이미 등록됨 — ArgumentException 방지
            try
            {
                var client = GetOrFindClient();
                if (client == null)
                {
                    Debug.LogWarning("[RacingWebView] WebBrowserClient를 찾을 수 없음");
                    return;
                }
                var methods = client.GetType().GetMethods();
                foreach (var m in methods)
                {
                    if (m.Name != "RegisterJsMethod") continue;
                    if (!m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 2) continue;
                    var generic = m.MakeGenericMethod(typeof(int));
                    generic.Invoke(client, new object[] { "OnRaceFinished", callback });
                    jsRegistered = true;
                    Debug.Log("[RacingWebView] JS 메서드 'OnRaceFinished' 등록 완료");
                    return;
                }
                Debug.LogWarning("[RacingWebView] RegisterJsMethod<T> 메서드를 찾을 수 없음");
            }
            catch (System.Exception e)
            {
                var inner = e.InnerException;
                if (inner != null)
                    Debug.LogWarning($"[RacingWebView] JS 메서드 등록 실패: {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
                else
                    Debug.LogWarning($"[RacingWebView] JS 메서드 등록 실패: {e.Message}");
            }
#endif
        }

        /// <summary>
        /// WebGL 전용 — RacingIframe.jslib가 SendMessage("RacingWebViewBridge", "OnRaceFinishedFromJs", rank)로 호출.
        /// iframe 안 레이싱 HTML이 postMessage로 보낸 등수를 게임 로직(콜백)에 전달.
        /// </summary>
        public void OnRaceFinishedFromJs(int rank)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log($"[RacingWebView] WebGL iframe 등수 수신: {rank}");
            webglRaceCallback?.Invoke(rank);
#endif
        }

        /// <summary>
        /// WebGL 전용 — 레이싱 HTML의 닫기 버튼/ESC (jslib → SendMessage).
        /// iframe이 키 포커스를 가져가 Unity의 ESC 강제 종료가 동작하지 않으므로 이 경로로 닫는다.
        /// </summary>
        public void OnRaceCancelFromJs()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[RacingWebView] WebGL iframe 닫기 요청");
            RacingMissionController.Instance?.Cancel();
#endif
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // 같은 프레임의 WebBrowserClient.Dispose가 아직 참조할 수 있음 — Destroy는 프레임 끝에 처리되므로 안전
            if (runtimeCommsLayer != null) Destroy(runtimeCommsLayer);
#if UNITY_WEBGL && !UNITY_EDITOR
            // iframe은 씬과 무관한 DOM이라 씬이 바뀌어도 남는다 — 떠 있으면 닫고 포커스를 캔버스로 복귀
            if (IsShowing)
            {
                RacingIframe_Hide();
                IsShowing = false;
            }
#endif
        }

        public static void FallbackOpenExternal()
        {
            string filePath = System.IO.Path.Combine(Application.streamingAssetsPath, "security-race.html");
            string url = "file:///" + filePath.Replace("\\", "/");
            Application.OpenURL(url);
        }
    }
}
