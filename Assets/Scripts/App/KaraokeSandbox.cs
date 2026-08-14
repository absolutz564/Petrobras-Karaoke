using System.Collections;
using System.Collections.Generic;
using Karaoke.Audio;
using Karaoke.Core;
using Karaoke.Data;
using Karaoke.Scoring;
using Karaoke.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Karaoke.App
{
    /// <summary>
    /// Modo de teste: menu, jogo e resultado montados por codigo, sem depender
    /// de nenhuma arte na cena.
    ///
    /// Existe para experimentar as musicas rapidamente — a pauta rolante mostra
    /// a nota esperada e o que voce esta cantando lado a lado, o que a tela de
    /// producao (so letra) nao mostra. A cena de producao continua com o
    /// KaraokeApp e a sua arte; os dois nunca rodam juntos.
    ///
    /// Basta dar Play numa cena vazia (SampleScene): ele se instala sozinho.
    /// </summary>
    [DisallowMultipleComponent]
    public class KaraokeSandbox : MonoBehaviour
    {
        public enum Screen { Menu, Game, Result }

        [Tooltip("Todos os parametros de deteccao e pontuacao.")]
        public KaraokeSettings settings = new KaraokeSettings();

        [Header("Arte do resultado (opcional)")]
        public Sprite umaEstrela;
        public Sprite duasEstrelas;
        public Sprite tresEstrelas;
        public Sprite quatroEstrelas;
        public Sprite cincoEstrelas;

        /// <summary>Cria o modo de teste sozinho em cenas que nao tem a versao de producao.</summary>
        public static bool AutoBoot = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootstrapIfMissing()
        {
            if (!AutoBoot) return;
            if (FindObjectOfType<KaraokeSandbox>() != null) return;

            // se a cena tem a versao de producao, ela manda
            if (FindObjectOfType<KaraokeApp>() != null) return;

            var go = new GameObject("Karaoke (modo de teste)");
            go.AddComponent<KaraokeSandbox>();
        }

        public Screen Current { get; private set; }

        MicrophoneCapture mic;
        PitchTracker tracker;
        AudioSource audioSource;
        List<SongChart> songs;

        SandboxMenuScreen menu;
        SandboxGameplayScreen gameplay;
        SandboxResultScreen result;
        PitchLabScreen pitchLab;

        SongChart lastSong;
        Transform canvasRoot;

        void Awake()
        {
            Application.runInBackground = true;

            EnsureCamera();
            Canvas canvas = EnsureCanvas();
            EnsureEventSystem();
            canvasRoot = canvas.transform;

            audioSource = UIBuilder.Ensure<AudioSource>(gameObject);
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;

            settings.ApplyDifficulty();
            mic = new MicrophoneCapture();
            tracker = new PitchTracker(mic, settings);
            // o modo de teste ve tambem o catalogo antigo em Resources/SongsTest
            songs = SongLibrary.LoadAllIncludingTests();

            menu = new SandboxMenuScreen(canvasRoot, songs, tracker);
            gameplay = new SandboxGameplayScreen(canvasRoot, settings, tracker, audioSource);
            result = new SandboxResultScreen(canvasRoot, StarArt);

            menu.SongChosen += StartSong;
            menu.PitchLabRequested += TogglePitchLab;
            gameplay.Finished += OnFinished;
            gameplay.Aborted += () => Show(Screen.Menu);
            result.RetryRequested += () => { if (lastSong != null) StartSong(lastSong); };
            result.MenuRequested += () => Show(Screen.Menu);

            Show(Screen.Menu);
        }

        IEnumerator Start()
        {
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);

            if (MicrophoneCapture.HasDevice)
            {
                if (mic.Start()) Debug.Log("[Karaoke] Microfone: " + mic.Device + " @ " + mic.SampleRate + " Hz");
                else Debug.LogWarning("[Karaoke] " + mic.LastError);
            }
            else Debug.LogWarning("[Karaoke] Nenhum microfone: da para jogar, mas sem pontuacao.");
        }

        void OnDestroy()
        {
            if (mic != null) mic.Dispose();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            tracker.Tick();

            switch (Current)
            {
                case Screen.Menu: menu.Tick(dt); break;
                case Screen.Game: gameplay.Tick(dt); break;
                case Screen.Result: result.Tick(dt); break;
            }

            if (Input.GetKeyDown(KeyCode.F1)) TogglePitchLab();
            if (pitchLab != null && pitchLab.Root.gameObject.activeSelf) pitchLab.Tick(dt);
        }

        void StartSong(SongChart chart)
        {
            if (chart == null) return;
            lastSong = chart;
            Show(Screen.Game);
            gameplay.Begin(chart);
        }

        void OnFinished(SongChart chart, ScoreEngine engine)
        {
            gameplay.Stop();
            result.Show(chart, engine);
            Show(Screen.Result);
        }

        void Show(Screen screen)
        {
            if (screen != Screen.Game) gameplay.Stop();

            Current = screen;
            menu.SetVisible(screen == Screen.Menu);
            gameplay.SetVisible(screen == Screen.Game);
            result.SetVisible(screen == Screen.Result);
        }

        public Sprite StarArt(int stars)
        {
            switch (stars)
            {
                case 5: if (cincoEstrelas != null) return cincoEstrelas; break;
                case 4: if (quatroEstrelas != null) return quatroEstrelas; break;
                case 3: if (tresEstrelas != null) return tresEstrelas; break;
                case 2: if (duasEstrelas != null) return duasEstrelas; break;
                default: if (umaEstrela != null) return umaEstrela; break;
            }
            return Resources.Load<Sprite>("UI/estrelas" + Mathf.Clamp(stars, 1, 5));
        }

        void TogglePitchLab()
        {
            if (pitchLab == null)
            {
                pitchLab = new PitchLabScreen(canvasRoot, tracker, audioSource);
                pitchLab.BackRequested += () => pitchLab.SetVisible(false);
            }
            pitchLab.SetVisible(!pitchLab.Root.gameObject.activeSelf);
        }

        // ------------------------------------------------------------ infra

        void EnsureCamera()
        {
            if (Camera.main != null) return;

            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Background;
            cam.orthographic = true;
            go.transform.position = new Vector3(0f, 0f, -10f);
            if (FindObjectOfType<AudioListener>() == null) go.AddComponent<AudioListener>();
        }

        Canvas EnsureCanvas()
        {
            Transform existing = transform.Find("SandboxCanvas");
            GameObject go = existing != null ? existing.gameObject : new GameObject("SandboxCanvas");
            if (existing == null) go.transform.SetParent(transform, false);

            Canvas canvas = UIBuilder.Ensure<Canvas>(go);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = UIBuilder.Ensure<CanvasScaler>(go);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            UIBuilder.Ensure<GraphicRaycaster>(go);
            return canvas;
        }

        void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }
    }
}
