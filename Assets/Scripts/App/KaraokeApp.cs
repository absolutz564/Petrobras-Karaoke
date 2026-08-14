using System.Collections;
using System.Collections.Generic;
using Karaoke.Audio;
using Karaoke.Core;
using Karaoke.Data;
using Karaoke.Net;
using Karaoke.Scoring;
using Karaoke.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Karaoke.App
{
    public enum GameScreen
    {
        Menu,
        MusicSelect,
        Game,
        EndGame,
        Obrigado,
        /// <summary>Fora do fluxo por ora; ver o bloco de ranking comentado abaixo.</summary>
        Ranking
    }

    /// <summary>
    /// Controlador do fluxo das cinco telas.
    ///
    ///   Menu -> Selecao de musica -> Jogo -> Resultado (+ modal) -> Obrigado
    ///
    /// O ranking na tela saiu do fluxo: depois de cadastrar o nome vai para a
    /// tela "Obrigado". O placar continua sendo enviado ao servidor; so a
    /// exibicao do Top 10 e que foi desligada, e o codigo dela esta comentado
    /// logo abaixo de SubmitRoutine, pronto para voltar.
    ///
    /// Nao cria interface: liga-se aos objetos que ja existem no Canvas pelo
    /// NOME (busca recursiva), entao mover ou reagrupar objetos no editor nao
    /// quebra nada. Se algum nome faltar, o Console lista exatamente quais.
    /// </summary>
    [DisallowMultipleComponent]
    public class KaraokeApp : MonoBehaviour
    {
        [Header("Ajustes de deteccao e pontuacao")]
        public KaraokeSettings settings = new KaraokeSettings();

        [Header("Backend do ranking")]
        public BackendSettings backend = new BackendSettings();

        [Header("Arte do resultado (por faixa de estrelas)")]
        public Sprite umaEstrela;
        public Sprite duasEstrelas;
        public Sprite tresEstrelas;
        public Sprite quatroEstrelas;
        public Sprite cincoEstrelas;

        [Header("Controle")]
        [Tooltip("Alem do clique, aceita teclado: 1-6 escolhem musica, Enter confirma, 1 cadastra, 5 pula.")]
        public bool aceitarTeclado = true;

        [Tooltip("Abre o Pitch Lab (teste de microfone) com F1.")]
        public bool pitchLabComF1 = true;

        [Tooltip("F2 imprime no Console o relogio da musica x o relogio do audio x a nota ativa. Use para medir atraso.")]
        public bool diagnosticoComF2 = true;

        [Tooltip("Desloca a letra no tempo, em segundos. Positivo = letra adianta. So mexa se F2 mostrar diferenca real.")]
        [Range(-1f, 1f)] public float ajusteDaLetra = 0f;

        [Header("Ranking")]
        [Tooltip("So volta a valer se a tela de ranking voltar ao fluxo. Distancia vertical entre as linhas clonadas.")]
        public float espacamentoLinhaRanking = 120f;

        public GameScreen Current { get; private set; }

        // ------------------------------------------------------------ cena
        Transform canvasRoot;
        GameObject menuScreen, musicSelectScreen, gameScreen, endGameScreen, thankYouScreen;

        Button menuAdvanceButton, confirmButton, endRankingButton, endSkipButton, registerButton;
        GameObject registerModal;
        TMP_InputField nameInput;
        Image scoreImage;
        TMP_Text endPointsText, pointsText, multiplierText, counterText;
        GameObject counterImage;

        readonly List<SongButtonBinding> songButtons = new List<SongButtonBinding>();
        LyricsView lyrics;
        // RankingView ranking;      // ranking fora do fluxo
        bool sceneReady;

        // ----------------------------------------------------------- estado
        MicrophoneCapture mic;
        PitchTracker tracker;
        AudioSource audioSource;
        List<SongChart> songs;

        SongChart selectedSong;
        ScoreEngine engine;
        float songTime, countdown;
        bool playing, counting;
        int lastNoteIndex = -1;
        PitchLabScreen pitchLab;
        bool diagnostico;
        float diagnosticoTimer;
        readonly LatencySweep sweep = new LatencySweep();

        // =================================================================

        void Awake()
        {
            Application.runInBackground = true;

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;

            settings.ApplyDifficulty();
            mic = new MicrophoneCapture();
            tracker = new PitchTracker(mic, settings);
            songs = SongLibrary.LoadAll();

            sceneReady = BindScene();
            if (sceneReady) WireButtons();

            Show(GameScreen.Menu);
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
            else
            {
                Debug.LogWarning("[Karaoke] Nenhum microfone detectado: o jogo roda, mas sem pontuacao.");
            }
        }

        void OnDestroy()
        {
            if (mic != null) mic.Dispose();
        }

        // ------------------------------------------------------------ bind

        bool BindScene()
        {
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Karaoke] Nao achei um Canvas na cena.");
                return false;
            }
            canvasRoot = canvas.transform;

            var root = new SceneBinder(canvasRoot);
            Transform menu = root.Require(SceneNames.Menu);
            Transform select = root.Require(SceneNames.MusicSelect);
            Transform game = root.Require(SceneNames.Game);
            Transform end = root.Require(SceneNames.EndGame);
            Transform obrigado = root.Optional(SceneNames.ThankYou);
            if (!root.Report("Telas principais")) return false;

            menuScreen = menu.gameObject;
            musicSelectScreen = select.gameObject;
            gameScreen = game.gameObject;
            endGameScreen = end.gameObject;
            thankYouScreen = obrigado != null ? obrigado.gameObject : null;

            if (thankYouScreen == null)
                Debug.LogWarning("[Karaoke] Nao achei a tela \"" + SceneNames.ThankYou + "\" no Canvas. " +
                                 "Sem ela, depois de cadastrar o nome o jogo volta direto para o inicio.");

            // A tela de ranking continua na hierarquia, mas ninguem mais a
            // liga nem desliga: se ficar ativa no editor, cobriria o jogo
            // inteiro. Garantimos que ela nasce apagada.
            Transform leftoverRanking = root.Optional(SceneNames.Ranking);
            if (leftoverRanking != null) leftoverRanking.gameObject.SetActive(false);

            // cada tela tem seu proprio binder: assim dois objetos com o mesmo
            // nome em telas diferentes (ButtonSkip) nao se confundem
            var menuBinder = new SceneBinder(menu);
            menuAdvanceButton = menuBinder.Require<Button>(SceneNames.MenuAdvanceButton);
            menuBinder.Report("Tela 1 (Menu)");

            var selectBinder = new SceneBinder(select);
            confirmButton = selectBinder.Require<Button>(SceneNames.ConfirmButton);
            selectBinder.Report("Tela 2 (MusicSelect)");
            select.GetComponentsInChildren(true, songButtons);

            var gameBinder = new SceneBinder(game);
            pointsText = gameBinder.Require<TMP_Text>(SceneNames.PointsText);
            multiplierText = gameBinder.Require<TMP_Text>(SceneNames.MultiplierText);
            counterText = gameBinder.Require<TMP_Text>(SceneNames.CounterText);
            Transform counter = gameBinder.Require(SceneNames.CounterImage);
            counterImage = counter != null ? counter.gameObject : null;

            var lyricSlots = new TMP_Text[LyricsView.WindowLines];
            for (int i = 0; i < lyricSlots.Length; i++)
                lyricSlots[i] = gameBinder.Require<TMP_Text>(SceneNames.LyricLinePrefix + i);
            gameBinder.Report("Tela 3 (Game)");
            lyrics = new LyricsView(lyricSlots);

            var endBinder = new SceneBinder(end);
            scoreImage = endBinder.Require<Image>(SceneNames.ScoreImage);
            endPointsText = endBinder.Require<TMP_Text>(SceneNames.EndPointsText);
            endRankingButton = endBinder.Require<Button>(SceneNames.RankingButton);
            endSkipButton = endBinder.Require<Button>(SceneNames.SkipButton);
            Transform modal = endBinder.Require(SceneNames.RegisterModal);
            registerModal = modal != null ? modal.gameObject : null;
            nameInput = endBinder.Require<TMP_InputField>(SceneNames.NameInput);
            if (nameInput != null) nameInput.characterLimit = 40;   // limite do backend
            registerButton = endBinder.Require<Button>(SceneNames.RegisterButton);
            endBinder.Report("Tela 4 (EndGame)");

            // --- tela 5 (Ranking): fora do fluxo. Para voltar, descomente este
            // bloco, declare de novo os campos rankingScreen/rankingSkipButton/
            // ranking, e troque ShowThanks() por ShowRanking() em SubmitRoutine.
            //
            // Transform rank = root.Require(SceneNames.Ranking);
            // rankingScreen = rank.gameObject;
            // var rankBinder = new SceneBinder(rank);
            // rankingSkipButton = rankBinder.Optional<Button>(SceneNames.SkipButton);
            // ranking = new RankingView(rankBinder.Optional(SceneNames.RankingForroList),
            //                           rankBinder.Optional(SceneNames.RankingPiseiroList),
            //                           rankBinder.Optional(SceneNames.RankingSertanejoList),
            //                           backend.rankingLimit,
            //                           espacamentoLinhaRanking);
            // if (ranking.IsEmpty)
            //     Debug.LogWarning("[Karaoke] Tela 5 (Ranking) sem linhas: crie RankRow0..RankRow" +
            //                      (backend.rankingLimit - 1) + " dentro de cada lista, cada uma com PosText, NomeText e PontosText.");

            return true;
        }

        void WireButtons()
        {
            Click(menuAdvanceButton, () => Show(GameScreen.MusicSelect));
            Click(confirmButton, StartSelectedSong);
            Click(endRankingButton, OpenRegisterModal);
            Click(endSkipButton, BackToStart);
            Click(registerButton, SubmitScore);
            // Click(rankingSkipButton, BackToStart);   // ranking fora do fluxo

            foreach (SongButtonBinding song in songButtons)
            {
                SongButtonBinding captured = song;
                Button button = song.GetComponent<Button>();
                Click(button, () => SelectSong(captured));
            }
        }

        static void Click(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        // ----------------------------------------------------------- fluxo

        void Show(GameScreen screen)
        {
            Current = screen;

            if (menuScreen != null) menuScreen.SetActive(screen == GameScreen.Menu);
            if (musicSelectScreen != null) musicSelectScreen.SetActive(screen == GameScreen.MusicSelect);
            if (gameScreen != null) gameScreen.SetActive(screen == GameScreen.Game);
            if (endGameScreen != null) endGameScreen.SetActive(screen == GameScreen.EndGame);
            if (thankYouScreen != null) thankYouScreen.SetActive(screen == GameScreen.Obrigado);
            // if (rankingScreen != null) rankingScreen.SetActive(screen == GameScreen.Ranking);

            if (screen != GameScreen.Game)
            {
                playing = false;
                counting = false;
                audioSource.Stop();
            }

            if (screen == GameScreen.MusicSelect) RefreshSelection();
            if (screen == GameScreen.EndGame && registerModal != null) registerModal.SetActive(false);
        }

        void BackToStart()
        {
            // recarrega a cena: zera microfone, audio e qualquer estado pendente
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // --------------------------------------------------- tela 2: musica

        void SelectSong(SongButtonBinding button)
        {
            selectedSong = FindSong(button.SongId);
            if (selectedSong == null)
                Debug.LogWarning("[Karaoke] Nenhuma musica casa com o botao '" + button.name +
                                 "'. Preencha songId no componente SongButtonBinding.");
            RefreshSelection();
        }

        SongChart FindSong(string buttonId)
        {
            string wanted = SongButtonBinding.IdFromName(buttonId);
            foreach (SongChart song in songs)
                if (SongButtonBinding.IdFromName(song.Id) == wanted) return song;
            return null;
        }

        void RefreshSelection()
        {
            foreach (SongButtonBinding button in songButtons)
            {
                bool isSelected = selectedSong != null &&
                                  SongButtonBinding.IdFromName(button.SongId) == SongButtonBinding.IdFromName(selectedSong.Id);
                button.SetSelected(isSelected);
            }

            if (confirmButton != null) confirmButton.interactable = selectedSong != null;
        }

        // ------------------------------------------------------ tela 3: jogo

        void StartSelectedSong()
        {
            if (selectedSong == null) return;

            Show(GameScreen.Game);

            engine = new ScoreEngine(selectedSong, settings);
            lyrics.SetChart(selectedSong);
            lastNoteIndex = -1;
            songTime = 0f;

            AudioClip clip = string.IsNullOrEmpty(selectedSong.AudioResource)
                ? null
                : Resources.Load<AudioClip>(selectedSong.AudioResource);

            if (clip == null && !string.IsNullOrEmpty(selectedSong.AudioResource))
                Debug.LogWarning("[Karaoke] Audio nao encontrado em Resources: " + selectedSong.AudioResource);
            if (clip == null && settings.guideTones)
                clip = GuideToneSynth.Build(selectedSong, 0f, 44100, settings.guideVolume);

            audioSource.Stop();
            audioSource.clip = clip;

            tracker.Reset();
            sweep.Clear();
            countdown = Mathf.Max(1, settings.readyCountdown);
            counting = true;
            playing = false;

            if (counterImage != null) counterImage.SetActive(true);
            UpdateHud();

            // A letra so entra quando a contagem sai: o numero fica em cima
            // dela. Ela e preenchida no primeiro Tick, logo depois.
            lyrics.SetVisible(false);

            if (!lyrics.HasSyllables)
                Debug.LogWarning("[Karaoke] '" + selectedSong.Title + "' nao tem a lista de silabas: a letra nao vai acompanhar.");
        }

        void TickGame(float dt)
        {
            if (counting)
            {
                countdown -= dt;
                if (counterText != null) counterText.text = Mathf.Max(1, Mathf.CeilToInt(countdown)).ToString();

                if (countdown <= 0f)
                {
                    counting = false;
                    playing = true;
                    if (counterImage != null) counterImage.SetActive(false);
                    lyrics.SetVisible(true);
                    lyrics.Tick(0f);
                    if (audioSource.clip != null) audioSource.Play();
                }
                return;
            }

            if (!playing) return;

            songTime += dt;
            if (audioSource.clip != null && audioSource.isPlaying)
            {
                float fromAudio = audioSource.time;
                if (Mathf.Abs(fromAudio - songTime) > 0.04f) songTime = fromAudio;
            }

            engine.Feed(songTime - settings.micLatencySeconds, dt, tracker.SmoothedMidi, tracker.Voiced);
            sweep.Record(songTime, dt, tracker.SmoothedMidi, tracker.Voiced);
            if (engine.ActiveIndex >= 0) lastNoteIndex = engine.ActiveIndex;

            // A letra anda pelo tempo da musica, nao pela nota que esta
            // pontuando: a nota comeca depois da consoante, onde a voz
            // estabiliza, e isso atrasaria o amarelo silaba por silaba.
            lyrics.Tick(songTime + ajusteDaLetra);
            UpdateHud();
            if (diagnostico) LogDiagnostico();

            if (songTime > selectedSong.EndTime + 1.2f)
            {
                playing = false;
                engine.Finish();
                audioSource.Stop();
                Debug.Log(sweep.Report(selectedSong, settings));
                ShowResult();
            }
        }

        /// <summary>Indice da nota que cobre este instante, para a letra andar sem depender do motor.</summary>
        int NoteAt(float time)
        {
            List<SongNote> notes = selectedSong.Notes;
            for (int i = Mathf.Max(0, lastNoteIndex); i < notes.Count; i++)
            {
                if (notes[i].EndTime < time) continue;
                return notes[i].StartTime <= time ? i : -1;
            }
            return -1;
        }

        /// <summary>
        /// Compara os tres relogios que precisam concordar: o do jogo, o do
        /// AudioSource e o da nota que deveria estar sendo cantada. Se os dois
        /// primeiros baterem e a letra ainda parecer fora, o desencontro esta
        /// no mapa da musica, nao no motor.
        /// </summary>
        void LogDiagnostico()
        {
            diagnosticoTimer -= Time.deltaTime;
            if (diagnosticoTimer > 0f) return;
            diagnosticoTimer = 0.5f;

            float audio = audioSource.clip != null ? audioSource.time : -1f;
            int index = engine.ActiveIndex >= 0 ? engine.ActiveIndex : lastNoteIndex;
            string nota = index >= 0 && index < selectedSong.Notes.Count
                ? string.Format("nota {0} '{1}' comeca em {2:0.00}s (linha {3})",
                    index, selectedSong.Notes[index].Text.Trim(), selectedSong.Notes[index].StartTime,
                    selectedSong.Notes[index].Line)
                : "nenhuma nota ativa";

            Debug.Log(string.Format("[Karaoke F2] jogo {0:0.00}s | audio {1:0.00}s | diferenca {2:+0.000;-0.000}s | {3}",
                songTime, audio, audio >= 0f ? audio - songTime : 0f, nota));
        }

        void UpdateHud()
        {
            if (pointsText != null) pointsText.text = Mathf.RoundToInt(engine != null ? engine.LiveScore : 0f).ToString();
            if (multiplierText != null) multiplierText.text = "x" + (engine != null ? engine.Multiplier : 1);
        }

        // -------------------------------------------------- tela 4: resultado

        void ShowResult()
        {
            Show(GameScreen.EndGame);

            int stars = engine.Stars;
            if (scoreImage != null)
            {
                Sprite art = StarArt(stars);
                scoreImage.sprite = art;
                scoreImage.enabled = art != null;
                if (art == null)
                    Debug.LogWarning("[Karaoke] Sem sprite para " + stars + " estrela(s): preencha os slots no Inspector.");
            }

            if (endPointsText != null) endPointsText.text = engine.FinalPoints.ToString();
            if (nameInput != null) nameInput.text = "";
            if (registerModal != null) registerModal.SetActive(false);
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

        void OpenRegisterModal()
        {
            if (registerModal == null) return;
            registerModal.SetActive(true);
            if (nameInput != null)
            {
                nameInput.Select();
                nameInput.ActivateInputField();
            }
        }

        void SubmitScore()
        {
            string nome = nameInput != null ? nameInput.text.Trim() : "";
            if (string.IsNullOrEmpty(nome))
            {
                Debug.Log("[Karaoke] Nome vazio: nao envio ao ranking.");
                if (nameInput != null) nameInput.ActivateInputField();
                return;
            }

            if (registerButton != null) registerButton.interactable = false;
            StartCoroutine(SubmitRoutine(nome));
        }

        IEnumerator SubmitRoutine(string nome)
        {
            yield return RankingClient.SubmitScore(backend, nome, selectedSong.Estilo, selectedSong.Rotulo,
                                                   engine.FinalPoints, null);

            if (registerButton != null) registerButton.interactable = true;
            ShowThanks();
        }

        // ------------------------------------------------- tela 6: obrigado

        /// <summary>
        /// Fim do fluxo: uma imagem de agradecimento. Qualquer tecla recomeca,
        /// igual a tela de ranking fazia.
        /// </summary>
        void ShowThanks()
        {
            if (thankYouScreen == null) { BackToStart(); return; }
            Show(GameScreen.Obrigado);
        }

        // --------------------------------------------------- tela 5: ranking
        //
        // Fora do fluxo. Para voltar: descomente, restaure o bloco de binding
        // em BindScene e chame ShowRanking() no lugar de ShowThanks().
        //
        // void ShowRanking()
        // {
        //     Show(GameScreen.Ranking);
        //     if (ranking != null) StartCoroutine(LoadRankingRoutine());
        // }
        //
        // IEnumerator LoadRankingRoutine()
        // {
        //     RankingBoard board = null;
        //     yield return RankingClient.FetchRanking(backend, (result, error) => board = result);
        //
        //     if (board != null) ranking.Show(board);
        //     else ranking.ShowUnavailable();
        // }

        // ------------------------------------------------------------ loop

        void Update()
        {
            float dt = Time.deltaTime;
            tracker.Tick();

            if (Current == GameScreen.Game) TickGame(dt);
            if (aceitarTeclado) HandleKeyboard();

            if (pitchLabComF1 && Input.GetKeyDown(KeyCode.F1)) TogglePitchLab();
            if (diagnosticoComF2 && Input.GetKeyDown(KeyCode.F2))
            {
                diagnostico = !diagnostico;
                Debug.Log("[Karaoke] Diagnostico de tempo " + (diagnostico ? "ligado" : "desligado"));
            }
            if (pitchLab != null && pitchLab.Root.gameObject.activeSelf) pitchLab.Tick(dt);
        }

        void HandleKeyboard()
        {
            switch (Current)
            {
                case GameScreen.Menu:
                    if (AnyKey()) Show(GameScreen.MusicSelect);
                    break;

                case GameScreen.MusicSelect:
                    for (int i = 0; i < songButtons.Count && i < 9; i++)
                        if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SelectSong(songButtons[i]);
                    // Espaco confirma tambem: e a tecla que a mao acha primeiro
                    // e nao ha campo de texto nesta tela para atrapalhar.
                    if (selectedSong != null && (Input.GetKeyDown(KeyCode.Return) ||
                                                 Input.GetKeyDown(KeyCode.KeypadEnter) ||
                                                 Input.GetKeyDown(KeyCode.Space)))
                        StartSelectedSong();
                    break;

                case GameScreen.EndGame:
                    bool modalOpen = registerModal != null && registerModal.activeSelf;
                    if (modalOpen)
                    {
                        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) SubmitScore();
                    }
                    else
                    {
                        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) OpenRegisterModal();
                        if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) BackToStart();
                    }
                    break;

                case GameScreen.Obrigado:
                    if (AnyKey()) BackToStart();
                    break;
            }
        }

        /// <summary>Qualquer tecla, menos F1 (que abre o Pitch Lab).</summary>
        static bool AnyKey()
        {
            return Input.anyKeyDown && !Input.GetKeyDown(KeyCode.F1) && !Input.GetMouseButtonDown(0);
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
    }
}
