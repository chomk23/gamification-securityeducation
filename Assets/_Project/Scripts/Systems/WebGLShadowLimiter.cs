using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForTheCompany.Systems
{
    /// <summary>
    /// 웹 플레이어 전용 — 씬이 로드될 때마다 방향광이 아닌 모든 Light의 실시간 그림자를 끈다.
    /// 웹은 Mobile_RPAsset을 쓰는데, 그 에셋의 보조 조명 그림자 '지원'은 켜 둬야 한다
    /// (꺼 두면 셰이더가 연결되지 않은 그림자 지도를 읽어 3D가 검게 나온다 — 커밋 9db2934).
    /// 대신 그림자를 드리우는 보조 조명(천장 포인트 라이트 4개)의 그림자를 꺼서 URP가 1×1 빈 그림자 지도만 연결하게 하고,
    /// 매 프레임 포인트 라이트 큐브 6면 × 4 = 24장의 그림자 지도 렌더링을 없앤다.
    /// 데스크톱 빌드와 에디터에서는 아무것도 하지 않는다.
    /// </summary>
    public static class WebGLShadowLimiter
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            DisableAdditionalLightShadows(); // 첫 씬은 구독 전에 이미 로드됨
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => DisableAdditionalLightShadows();

        private static void DisableAdditionalLightShadows()
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var light in lights)
            {
                if (light.type == LightType.Directional) continue;
                light.shadows = LightShadows.None;
            }
        }
#endif
    }
}
