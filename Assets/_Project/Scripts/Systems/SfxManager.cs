using UnityEngine;

namespace ForTheCompany.Systems
{
    /// <summary>
    /// Procedural 8-bit/chiptune 효과음 — 외부 파일 없이 sin/square wave로 직접 생성.
    /// DontDestroyOnLoad 싱글톤, 모든 씬에서 SfxManager.PlayClick() 같은 정적 메서드로 호출.
    /// 클립은 처음 생성 시 캐시되어 매번 새로 만들지 않음.
    /// </summary>
    public class SfxManager : MonoBehaviour
    {
        public static SfxManager Instance { get; private set; }

        private AudioSource source;

        // 캐시 — 한 번 생성하면 재사용
        private AudioClip clipHover;
        private AudioClip clipClick;
        private AudioClip clipDialogue;
        private AudioClip clipStageAdvance;
        private AudioClip clipCorrect;
        private AudioClip clipWrong;
        private AudioClip clipModalOpen;
        private AudioClip[] clipsTypewriter; // 음정 다른 4종 — 매 글자마다 랜덤

        // 매 글자마다 호출되지만 최소 간격으로 제한 (너무 잦으면 시끄러움)
        private float typewriterCooldownUntil = -1f;
        private const float TypewriterMinInterval = 0.045f; // 45ms 이상 간격

        private const int SampleRate = 22050;
        private enum WaveType { Square, Sine, Triangle }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("SfxManager");
            DontDestroyOnLoad(go);
            go.AddComponent<SfxManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            // 시작 마스터 볼륨 — 일시정지 메뉴에서 저장한 값 (없으면 기본 30%)
            // (메인 메뉴 hover/click부터 적용)
            AudioListener.volume = PlayerPrefs.GetFloat(PauseMenu.VolumePrefKey, PauseMenu.DefaultVolume);

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;       // 2D 사운드
            source.ignoreListenerPause = true; // PauseMenu timeScale=0에서도 들림
            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;

            // 모든 클립 미리 생성 (한 번만 — 첫 프레임 비용)
            clipHover        = MakeBeep(1200f, 0.025f, 0.15f, WaveType.Square);
            clipClick        = MakeBeep(700f,  0.06f,  0.30f, WaveType.Square);
            clipDialogue     = MakeBeep(520f,  0.04f,  0.18f, WaveType.Triangle);
            clipModalOpen    = MakeBeep(450f,  0.10f,  0.25f, WaveType.Sine);

            // 단계 진행 — 상승 두 음 (C5 → G5)
            clipStageAdvance = MakeSequence(
                new[] { 523f, 784f },
                new[] { 0.08f, 0.14f },
                0.28f, WaveType.Square);

            // 정답 — C5 → E5 → G5 arpeggio (밝은 상승)
            clipCorrect = MakeSequence(
                new[] { 523f, 659f, 784f, 1047f },
                new[] { 0.05f, 0.05f, 0.05f, 0.12f },
                0.30f, WaveType.Square);

            // 오답 — G3 → E3 하강 부저
            clipWrong = MakeSequence(
                new[] { 196f, 165f },
                new[] { 0.10f, 0.18f },
                0.28f, WaveType.Square);

            // Typewriter — Undertale 스타일 "뾱뾱뾱" 4종 (음정 미세 차이)
            // 매우 짧고(15ms) 작아서(0.07) 빠르게 연속 재생해도 거슬리지 않음
            clipsTypewriter = new[]
            {
                MakeBeep(620f, 0.015f, 0.10f, WaveType.Square),
                MakeBeep(680f, 0.015f, 0.10f, WaveType.Square),
                MakeBeep(740f, 0.015f, 0.10f, WaveType.Square),
                MakeBeep(800f, 0.015f, 0.10f, WaveType.Square),
            };
        }

        // ═══════════════════ PUBLIC API ═══════════════════

        public static void PlayHover()        => Instance?.Play(Instance.clipHover);
        public static void PlayClick()        => Instance?.Play(Instance.clipClick);
        public static void PlayDialogue()     => Instance?.Play(Instance.clipDialogue);
        public static void PlayStageAdvance() => Instance?.Play(Instance.clipStageAdvance);
        public static void PlayCorrect()      => Instance?.Play(Instance.clipCorrect);
        public static void PlayWrong()        => Instance?.Play(Instance.clipWrong);
        public static void PlayModalOpen()    => Instance?.Play(Instance.clipModalOpen);

        /// <summary>Undertale 스타일 타이핑 비프 — DialogueSystem이 글자 한 칸씩 늘릴 때 호출.
        /// 음정 4종 랜덤 + 최소 간격(45ms) 쿨다운으로 자연스러운 변주.</summary>
        public static void PlayTypewriter()
        {
            if (Instance == null || Instance.clipsTypewriter == null) return;
            if (Instance.source == null) return;
            // 쿨다운 — 너무 빠른 연속 재생 방지
            if (Time.unscaledTime < Instance.typewriterCooldownUntil) return;
            Instance.typewriterCooldownUntil = Time.unscaledTime + TypewriterMinInterval;

            int i = Random.Range(0, Instance.clipsTypewriter.Length);
            Instance.source.PlayOneShot(Instance.clipsTypewriter[i]);
        }

        private void Play(AudioClip clip)
        {
            if (clip == null || source == null) return;
            source.PlayOneShot(clip);
        }

        // ═══════════════════ PROCEDURAL WAVE GENERATION ═══════════════════

        private static AudioClip MakeBeep(float frequency, float duration, float volume, WaveType type)
        {
            int samples = (int)(SampleRate * duration);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float wave = SampleWave(type, frequency, t);
                float env = ASREnvelope(t, duration);
                data[i] = wave * volume * env;
            }

            var clip = AudioClip.Create($"sfx_{frequency}_{duration}", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>여러 음표 시퀀스 (arpeggio, chime 등) — 음마다 frequency/duration 지정</summary>
        private static AudioClip MakeSequence(float[] freqs, float[] durations, float volume, WaveType type)
        {
            int totalSamples = 0;
            for (int n = 0; n < durations.Length; n++)
                totalSamples += (int)(SampleRate * durations[n]);

            float[] data = new float[totalSamples];
            int offset = 0;

            for (int n = 0; n < freqs.Length; n++)
            {
                float freq = freqs[n];
                float dur = durations[n];
                int samples = (int)(SampleRate * dur);

                for (int i = 0; i < samples; i++)
                {
                    float t = (float)i / SampleRate;
                    float wave = SampleWave(type, freq, t);
                    float env = ASREnvelope(t, dur);
                    data[offset + i] = wave * volume * env;
                }
                offset += samples;
            }

            var clip = AudioClip.Create($"sfx_seq_{freqs.Length}", totalSamples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float SampleWave(WaveType type, float frequency, float t)
        {
            float phase = 2f * Mathf.PI * frequency * t;
            switch (type)
            {
                case WaveType.Sine:     return Mathf.Sin(phase);
                case WaveType.Square:   return Mathf.Sin(phase) > 0f ? 1f : -1f;
                case WaveType.Triangle: return Mathf.Asin(Mathf.Sin(phase)) * (2f / Mathf.PI);
                default:                return 0f;
            }
        }

        /// <summary>Attack-Sustain-Release 엔벨로프 — 짧은 fade in/out으로 클릭/팝 노이즈 방지</summary>
        private static float ASREnvelope(float t, float duration)
        {
            float attack = Mathf.Min(0.005f, duration * 0.15f);
            float release = Mathf.Min(0.025f, duration * 0.40f);
            if (t < attack) return t / attack;
            if (t > duration - release) return Mathf.Max(0f, (duration - t) / release);
            return 1f;
        }
    }
}
