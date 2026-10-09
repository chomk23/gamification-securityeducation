using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForTheCompany.Systems
{
    /// <summary>
    /// UnityWebBrowser Canvas의 표시/숨김을 CanvasGroup.alpha로 제어한다.
    /// Canvas는 항상 활성 상태(CEF 프로세스 유지) — show=alpha 1, hide=alpha 0.
    /// 이로써 플레이어가 콘솔에 도착할 때쯤이면 CEF가 이미 로드되어 PRESS START가 즉시 반응.
    /// 씬에 "RacingWebViewCanvas"가 없으면 외부 브라우저(Application.OpenURL)로 폴백.
    /// </summary>
    public class RacingWebViewBridge : MonoBehaviour
    {
        public static RacingWebViewBridge Instance { get; private set; }

        [Tooltip("RawImage + WebBrowserUIBasic 컴포넌트가 붙은 Canvas root. 비워두면 자동으로 'RacingWebViewCanvas' 이름의 GameObject를 찾는다.")]
        public GameObject canvasRoot;

        private CanvasGroup canvasGroup;
        private object cachedClient; // WebBrowserClient (reflection)

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: CEF(데스크톱 전용) 대신 DOM iframe 사용 — Assets/Plugins/WebGL/RacingIframe.jslib
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void RacingIframe_Show(string url);
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void RacingIframe_Hide();

        private System.Action<int> webglRaceCallback;

        public bool IsAvailable => true; // iframe은 항상 사용 가능
#else
        public bool IsAvailable => canvasRoot != null;
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

                // 항상 active 유지 (CEF pre-warm), CanvasGroup으로 시각적/입력 차단
                canvasRoot.SetActive(true);
                canvasGroup = canvasRoot.GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = canvasRoot.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
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
        /// ready 속성을 못 찾으면 일정 시간 후 그냥 시도.
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
                if (IsClientReady(client))
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

        /// <summary>WebBrowserClient의 ready 상태를 reflection으로 확인 (여러 이름 후보 시도)</summary>
        private static bool IsClientReady(object client)
        {
            if (client == null) return false;
            var type = client.GetType();
            string[] names = { "ReadySignalReceived", "IsConnected", "HasInitialized", "IsInitialized" };
            const System.Reflection.BindingFlags BF =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            foreach (var name in names)
            {
                var p = type.GetProperty(name, BF);
                if (p != null && p.PropertyType == typeof(bool))
                    return (bool)p.GetValue(client);
                var f = type.GetField(name, BF);
                if (f != null && f.FieldType == typeof(bool))
                    return (bool)f.GetValue(client);
            }
            // ready 속성을 못 찾으면 true로 간주 (그냥 시도)
            return true;
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
            // Unity가 키 입력을 독점하면 iframe 조작 불가 → 잠시 해제.
            WebGLInput.captureAllKeyboardInput = false;
            RacingIframe_Show(Application.streamingAssetsPath + "/security-race.html");
            IsShowing = true;
#else
            if (canvasRoot == null)
            {
                Debug.LogWarning("[RacingWebView] RacingWebViewCanvas를 찾을 수 없음 — 외부 브라우저로 폴백");
                FallbackOpenExternal();
                return;
            }
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
            IsShowing = true;

            // Show 시점에도 ready 폴링 후 LoadUrl — Start() pre-warm이 cold start로 실패했어도 복구.
            // 플레이어가 콘솔 도착 = 게임 시작 후라 CEF가 거의 준비됨 → 빈 화면 방지.
            StartCoroutine(LoadWhenReady(retryOnce: false));
#endif
        }

        public void Hide()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RacingIframe_Hide();
            WebGLInput.captureAllKeyboardInput = true; // 키 입력을 Unity로 복귀
            IsShowing = false;
#else
            if (canvasRoot == null) return;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
            IsShowing = false;
            // 다음 진입 시 인트로 화면으로 시작하도록 페이지 리로드
            ReloadPage();
#endif
        }

        /// <summary>
        /// WebBrowserClient.ExecuteJs("location.reload()")로 HTML 페이지 초기화.
        /// reflection으로 ExecuteJs 메서드 찾음.
        /// </summary>
        private void ReloadPage()
        {
            var client = GetOrFindClient();
            if (client == null) return;
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
#if UNITY_WEBGL && !UNITY_EDITOR
            // iframe은 씬과 무관한 DOM이라 씬이 바뀌어도 남는다 — 떠 있으면 닫고 키 입력을 Unity로 복귀
            if (IsShowing)
            {
                RacingIframe_Hide();
                WebGLInput.captureAllKeyboardInput = true;
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
