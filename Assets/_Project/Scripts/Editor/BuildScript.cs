using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForTheCompany.EditorTools
{
    /// <summary>
    /// 학교 컴퓨터·교수님 시연용 Windows 빌드 자동화.
    /// 메뉴: For The Company → Build Windows (One Click)
    /// </summary>
    public static class BuildScript
    {
        private const string BuildFolder = "builds/ForTheCompany_Win";
        private const string ExeName = "ForTheCompany.exe";

        [MenuItem("For The Company/Build Windows (One Click)", priority = 1)]
        public static void BuildWindows()
        {
            // 빌드 씬 목록 (MainMenu → Facility 순서)
            var scenes = new[]
            {
                "Assets/_Project/Scenes/MainMenuScene.unity",
                "Assets/_Project/Scenes/FacilityScene.unity"
            };

            // 빌드 폴더 비우기
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), BuildFolder);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
            Directory.CreateDirectory(fullPath);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(BuildFolder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            Debug.Log("[Build] Windows 빌드 시작... 2~5분 소요");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                long sizeMb = (long)summary.totalSize / (1024 * 1024);
                Debug.Log($"[Build] ✓ 성공 — {BuildFolder}/{ExeName} ({sizeMb} MB, {summary.totalTime})");
                EditorUtility.RevealInFinder(Path.Combine(fullPath, ExeName));
                EditorUtility.DisplayDialog(
                    "빌드 완료",
                    $"경로: {BuildFolder}/{ExeName}\n크기: {sizeMb} MB\n\n" +
                    "이 폴더를 통째로 압축해서 USB·구글드라이브로 옮기세요.\n" +
                    "학교 컴퓨터에서 압축 풀고 .exe 더블클릭하면 끝.",
                    "확인");
            }
            else
            {
                Debug.LogError($"[Build] ✗ 실패: {summary.result} ({summary.totalErrors} 오류)");
                EditorUtility.DisplayDialog("빌드 실패",
                    $"Console 창에서 오류 메시지를 확인하세요.\n결과: {summary.result}",
                    "확인");
            }
        }

        private const string WebBuildFolder = "builds/ForTheCompany_Web";

        [MenuItem("For The Company/Build WebGL (Website)", priority = 2)]
        public static void BuildWebGL()
        {
            var scenes = new[]
            {
                "Assets/_Project/Scenes/MainMenuScene.unity",
                "Assets/_Project/Scenes/FacilityScene.unity"
            };

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), WebBuildFolder);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
            Directory.CreateDirectory(fullPath);

            // 어떤 정적 호스팅(서버 설정 불가 환경)에서도 돌아가도록 압축 해제 폴백 사용
            PlayerSettings.WebGL.decompressionFallback = true;

            // 기본 캔버스 960×600은 지목창(단서 전체 표시)·결과창이 들어가기에 낮다.
            // 높이 680 = 웹사이트 iframe(1280×720) 안에 기본 템플릿 하단 바(38px)까지 함께 들어가는 최대치.
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 680;

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = WebBuildFolder,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            Debug.Log("[Build] WebGL 빌드 시작... 첫 빌드는 10~30분 걸릴 수 있음");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                long sizeMb = (long)summary.totalSize / (1024 * 1024);
                Debug.Log($"[Build] ✓ 성공 — {WebBuildFolder} ({sizeMb} MB, {summary.totalTime})");
                EditorUtility.RevealInFinder(Path.Combine(fullPath, "index.html"));
                bool openNow = EditorUtility.DisplayDialog(
                    "WebGL 빌드 완료",
                    $"경로: {WebBuildFolder}\n\n" +
                    "이 폴더 전체(index.html, Build/, StreamingAssets/, TemplateData/)를\n" +
                    "웹사이트에 업로드하고 index.html로 링크하거나 iframe으로 넣으세요.\n\n" +
                    "※ index.html을 더블클릭하면 브라우저 보안 때문에 실행되지 않습니다.\n" +
                    "   PC에서 확인하려면 '브라우저에서 열기'를 누르세요.",
                    "브라우저에서 열기", "닫기");
                if (openNow) RunWebGLLocal();
            }
            else
            {
                Debug.LogError($"[Build] ✗ 실패: {summary.result} ({summary.totalErrors} 오류)");
                EditorUtility.DisplayDialog("빌드 실패",
                    $"Console 창에서 오류 메시지를 확인하세요.\n결과: {summary.result}",
                    "확인");
            }
        }

        private const int WebTestPort = 8090;

        /// <summary>
        /// 빌드된 웹 버전을 로컬 웹 서버로 띄워 브라우저에서 연다.
        /// index.html을 더블클릭(file://)하면 브라우저가 게임 파일 다운로드를 막아 실행되지 않는다.
        /// Unity에 포함된 테스트 서버(SimpleWebServer)를 Unity의 mono로 실행하며, Unity를 닫으면 서버도 종료된다.
        /// </summary>
        [MenuItem("For The Company/Run WebGL Build (Local Test)", priority = 3)]
        public static void RunWebGLLocal()
        {
            string buildPath = Path.GetFullPath(WebBuildFolder);
            if (!File.Exists(Path.Combine(buildPath, "index.html")))
            {
                EditorUtility.DisplayDialog("빌드 없음",
                    "아직 웹 빌드가 없습니다. 'Build WebGL (Website)'를 먼저 실행하세요.", "확인");
                return;
            }

            string url = $"http://localhost:{WebTestPort}/";
            if (!IsLocalPortOpen(WebTestPort))
            {
                string mono = Path.Combine(EditorApplication.applicationContentsPath, "MonoBleedingEdge", "bin",
                    Application.platform == RuntimePlatform.WindowsEditor ? "mono.exe" : "mono");
                string server = Path.Combine(
                    BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.WebGL, BuildOptions.None),
                    "BuildTools", "SimpleWebServer.exe");
                if (!File.Exists(mono) || !File.Exists(server))
                {
                    EditorUtility.DisplayDialog("테스트 서버 없음",
                        $"Unity 테스트 서버를 찾지 못했습니다.\n{server}", "확인");
                    return;
                }

                // 마지막 인자(에디터 PID): Unity가 종료되면 서버도 스스로 종료된다
                var psi = new System.Diagnostics.ProcessStartInfo(mono,
                    $"\"{server}\" \"{buildPath}\" {url} {System.Diagnostics.Process.GetCurrentProcess().Id}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi);

                for (int i = 0; i < 25 && !IsLocalPortOpen(WebTestPort); i++)
                    System.Threading.Thread.Sleep(200);
            }

            Debug.Log($"[Build] 웹 빌드 로컬 테스트: {url}");
            Application.OpenURL(url);
        }

        private static bool IsLocalPortOpen(int port)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var pending = client.BeginConnect("127.0.0.1", port, null, null);
                    bool connected = pending.AsyncWaitHandle.WaitOne(150) && client.Connected;
                    if (connected) client.EndConnect(pending);
                    return connected;
                }
            }
            catch
            {
                return false;
            }
        }

        [MenuItem("For The Company/Open Build Folder", priority = 4)]
        public static void OpenBuildFolder()
        {
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), BuildFolder);
            if (Directory.Exists(fullPath))
            {
                EditorUtility.RevealInFinder(fullPath);
            }
            else
            {
                EditorUtility.DisplayDialog("폴더 없음",
                    "아직 빌드한 적이 없습니다. 'Build Windows (One Click)' 먼저 실행하세요.",
                    "확인");
            }
        }
    }
}
