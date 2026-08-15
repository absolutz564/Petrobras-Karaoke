using System;
using Karaoke.Audio;
using Karaoke.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Karaoke.UI
{
    /// <summary>
    /// Pitch Lab: teste do microfone + detector isolado do resto do jogo.
    ///
    /// Mostra frequencia, nota, desvio em cents, volume, confianca e um
    /// historico rolante do pitch. Permite trocar o algoritmo (autocorrelacao /
    /// YIN) e o tamanho da janela em runtime, e tocar tons de referencia para
    /// validar a deteccao ("toca La 440, o detector deve mostrar La4 / ~0 cents").
    /// </summary>
    public class PitchLabScreen
    {
        const int HistorySize = 140;
        const float HistoryLowMidi = 40f;   // ~82 Hz
        const float HistoryHighMidi = 86f;  // ~1568 Hz

        public RectTransform Root { get; private set; }
        public event Action BackRequested;

        readonly PitchTracker tracker;
        readonly AudioSource audioSource;

        Text noteText, hzText, centsText, infoText, detectorLabel, windowLabel;
        Image levelFill, clarityFill, centsMarker;
        RectTransform graphRoot;
        Image[] historyBars;
        float[] historyMidi;
        bool[] historyVoiced;
        int historyHead;
        AudioClip toneClip;

        public PitchLabScreen(Transform parent, PitchTracker tracker, AudioSource audioSource)
        {
            this.tracker = tracker;
            this.audioSource = audioSource;
            Build(parent);
        }

        public void SetVisible(bool visible)
        {
            Root.gameObject.SetActive(visible);
            if (!visible && audioSource != null) audioSource.Stop();
        }

        void Build(Transform parent)
        {
            Root = UIBuilder.NewRect(parent, "PitchLabScreen");
            UIBuilder.Stretch(Root);

            Image bg = UIBuilder.NewImage(Root, "Background", Palette.Background);
            UIBuilder.Stretch(bg.rectTransform);

            Text header = UIBuilder.NewText(Root, "Header", "PITCH LAB", 52, Palette.Accent2, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIBuilder.SetAnchors(header.rectTransform, new Vector2(0f, 0.90f), new Vector2(1f, 0.99f), Vector2.zero, Vector2.zero);

            Text sub = UIBuilder.NewText(Root, "Sub", "Cante ou toque um tom de referencia. Nada aqui depende da musica.", 24, Palette.TextDim, TextAnchor.MiddleCenter);
            UIBuilder.SetAnchors(sub.rectTransform, new Vector2(0f, 0.86f), new Vector2(1f, 0.90f), Vector2.zero, Vector2.zero);

            // ---- leitura principal ----
            noteText = UIBuilder.NewText(Root, "Note", "--", 150, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIBuilder.SetAnchors(noteText.rectTransform, new Vector2(0.05f, 0.62f), new Vector2(0.55f, 0.86f), Vector2.zero, Vector2.zero);

            hzText = UIBuilder.NewText(Root, "Hz", "0.0 Hz", 40, Palette.Accent2, TextAnchor.MiddleCenter);
            UIBuilder.SetAnchors(hzText.rectTransform, new Vector2(0.05f, 0.56f), new Vector2(0.55f, 0.62f), Vector2.zero, Vector2.zero);

            // ---- afinador (cents) ----
            RectTransform centsBg = UIBuilder.NewImage(Root, "CentsBg", new Color(0f, 0f, 0f, 0.45f)).rectTransform;
            UIBuilder.SetAnchors(centsBg, new Vector2(0.05f, 0.48f), new Vector2(0.55f, 0.545f), Vector2.zero, Vector2.zero);

            RectTransform centerLine = UIBuilder.NewImage(centsBg, "Center", new Color(1f, 1f, 1f, 0.35f)).rectTransform;
            UIBuilder.SetFixed(centerLine, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 0f));
            centerLine.anchorMin = new Vector2(0.5f, 0f);
            centerLine.anchorMax = new Vector2(0.5f, 1f);
            centerLine.sizeDelta = new Vector2(3f, 0f);

            centsMarker = UIBuilder.NewImage(centsBg, "Marker", Palette.Good);
            UIBuilder.SetFixed(centsMarker.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 0f));
            centsMarker.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            centsMarker.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            centsMarker.rectTransform.sizeDelta = new Vector2(14f, -8f);

            centsText = UIBuilder.NewText(Root, "Cents", "0 cents", 26, Palette.TextDim, TextAnchor.MiddleCenter);
            UIBuilder.SetAnchors(centsText.rectTransform, new Vector2(0.05f, 0.43f), new Vector2(0.55f, 0.48f), Vector2.zero, Vector2.zero);

            // ---- medidores ----
            levelFill = MakeMeter("Volume (RMS)", 0.60f);
            clarityFill = MakeMeter("Confianca", 0.52f);

            // ---- historico ----
            graphRoot = UIBuilder.NewImage(Root, "Graph", Palette.Lane).rectTransform;
            UIBuilder.SetAnchors(graphRoot, new Vector2(0.05f, 0.20f), new Vector2(0.95f, 0.40f), Vector2.zero, Vector2.zero);
            UIBuilder.Ensure<RectMask2D>(graphRoot.gameObject);

            infoText = UIBuilder.NewText(Root, "Info", "", 22, Palette.TextDim, TextAnchor.UpperLeft);
            UIBuilder.SetAnchors(infoText.rectTransform, new Vector2(0.6f, 0.43f), new Vector2(0.95f, 0.86f), Vector2.zero, Vector2.zero);

            // ---- controles ----
            detectorLabel = UIBuilder.NewText(Root, "DetectorLabel", "", 22, Palette.TextDim, TextAnchor.MiddleCenter);
            UIBuilder.SetAnchors(detectorLabel.rectTransform, new Vector2(0.05f, 0.145f), new Vector2(0.32f, 0.19f), Vector2.zero, Vector2.zero);

            windowLabel = UIBuilder.NewText(Root, "WindowLabel", "", 22, Palette.TextDim, TextAnchor.MiddleCenter);
            UIBuilder.SetAnchors(windowLabel.rectTransform, new Vector2(0.34f, 0.145f), new Vector2(0.61f, 0.19f), Vector2.zero, Vector2.zero);

            // Trocar de microfone sem recompilar: numa ativacao, a entrada certa
            // pode nao ser a que o sistema escolheu, e nao da para depurar la.
            UIBuilder.NewButton(Root, "SwitchMic", "Trocar microfone", Palette.Accent, 26, CycleMicrophone)
                .GetComponent<RectTransform>().Apply(new Vector2(0.63f, 0.145f), new Vector2(0.95f, 0.19f));

            UIBuilder.NewButton(Root, "SwitchDetector", "Trocar algoritmo", Palette.PanelSoft, 26, CycleDetector)
                .GetComponent<RectTransform>().Apply(new Vector2(0.05f, 0.07f), new Vector2(0.32f, 0.14f));

            UIBuilder.NewButton(Root, "SwitchWindow", "Trocar janela", Palette.PanelSoft, 26, CycleWindow)
                .GetComponent<RectTransform>().Apply(new Vector2(0.34f, 0.07f), new Vector2(0.61f, 0.14f));

            UIBuilder.NewButton(Root, "ToneA4", "Tocar La 440", Palette.Accent2, 26, () => PlayTone(440f))
                .GetComponent<RectTransform>().Apply(new Vector2(0.63f, 0.07f), new Vector2(0.78f, 0.14f));

            UIBuilder.NewButton(Root, "ToneC4", "Tocar Do 261", Palette.Accent2, 26, () => PlayTone(261.63f))
                .GetComponent<RectTransform>().Apply(new Vector2(0.795f, 0.07f), new Vector2(0.95f, 0.14f));

            UIBuilder.NewButton(Root, "Back", "Voltar ao menu (ESC)", Palette.Accent, 26,
                () => { if (BackRequested != null) BackRequested(); })
                .GetComponent<RectTransform>().Apply(new Vector2(0.35f, 0.01f), new Vector2(0.65f, 0.06f));
        }

        Image MakeMeter(string label, float y)
        {
            Text caption = UIBuilder.NewText(Root, label + "Caption", label, 22, Palette.TextDim, TextAnchor.LowerLeft);
            UIBuilder.SetAnchors(caption.rectTransform, new Vector2(0.6f, y + 0.035f), new Vector2(0.95f, y + 0.07f), Vector2.zero, Vector2.zero);

            RectTransform bg = UIBuilder.NewImage(Root, label + "Bg", new Color(0f, 0f, 0f, 0.45f)).rectTransform;
            UIBuilder.SetAnchors(bg, new Vector2(0.6f, y), new Vector2(0.95f, y + 0.035f), Vector2.zero, Vector2.zero);

            Image fill = UIBuilder.NewImage(bg, label + "Fill", Palette.Good);
            UIBuilder.SetAnchors(fill.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            return fill;
        }

        /// <summary>Passa para o proximo microfone da lista do sistema e reinicia a captura.</summary>
        void CycleMicrophone()
        {
            string[] devices = MicrophoneCapture.Devices;
            if (tracker.Mic == null || devices == null || devices.Length == 0)
            {
                Debug.LogWarning("[Karaoke] Nenhum microfone para trocar." + MicrophoneCapture.DescribeDevices());
                return;
            }

            int next = (System.Array.IndexOf(devices, tracker.Mic.Device) + 1) % devices.Length;
            if (tracker.Mic.Start(devices[next]))
            {
                tracker.Reset();
                Debug.Log("[Karaoke] Microfone agora: [" + next + "] " + devices[next] +
                          " @ " + tracker.Mic.SampleRate + " Hz");
            }
            else Debug.LogWarning("[Karaoke] Nao consegui abrir '" + devices[next] + "': " + tracker.Mic.LastError);
        }

        void CycleDetector()
        {
            DetectorKind next = tracker.Settings.detector == DetectorKind.Autocorrelation
                ? DetectorKind.Yin
                : DetectorKind.Autocorrelation;
            tracker.SetDetector(next);
        }

        void CycleWindow()
        {
            int size = tracker.WindowSize * 2;
            if (size > 4096) size = 1024;
            tracker.SetWindowSize(size);
        }

        void PlayTone(float hz)
        {
            if (toneClip != null) UnityEngine.Object.Destroy(toneClip);
            toneClip = GuideToneSynth.Tone(hz, 2.5f);
            audioSource.Stop();
            audioSource.clip = toneClip;
            audioSource.Play();
        }

        public void Tick(float dt)
        {
            if (Input.GetKeyDown(KeyCode.Escape) && BackRequested != null)
            {
                BackRequested();
                return;
            }

            PitchResult r = tracker.Latest;
            bool voiced = tracker.Voiced;

            if (voiced)
            {
                float midi = tracker.SmoothedMidi;
                noteText.text = PitchUtils.NoteName(midi);
                noteText.color = Palette.Text;
                hzText.text = r.frequency.ToString("0.0") + " Hz";
                float cents = PitchUtils.CentsFromNearestNote(midi);
                centsText.text = (cents >= 0f ? "+" : "") + Mathf.RoundToInt(cents) + " cents";
                centsMarker.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(cents / 50f, -1f, 1f) * 240f, 0f);
                centsMarker.color = Mathf.Abs(cents) < 15f ? Palette.Good : Mathf.Abs(cents) < 35f ? Palette.Warn : Palette.Bad;
            }
            else
            {
                noteText.text = "--";
                noteText.color = Palette.TextDim;
                hzText.text = r.rms > 0.001f ? "sem pitch estavel" : "silencio";
                centsText.text = "";
            }

            levelFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(r.rms * 8f), 1f);
            levelFill.color = r.rms >= tracker.Gate ? Palette.Good : Palette.Bad;
            clarityFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(r.clarity), 1f);
            clarityFill.color = r.clarity >= tracker.Settings.clarityThreshold ? Palette.Good : Palette.Warn;

            PushHistory(voiced ? tracker.SmoothedMidi : 0f, voiced);

            detectorLabel.text = "algoritmo: " + tracker.Detector.Name;
            windowLabel.text = "janela: " + tracker.WindowSize + " amostras";

            int rate = tracker.SampleRate;
            float latencyMs = rate > 0 ? tracker.WindowSize / (float)rate * 1000f : 0f;
            string[] devices = MicrophoneCapture.Devices;
            int deviceIndex = tracker.Mic != null ? System.Array.IndexOf(devices, tracker.Mic.Device) : -1;

            infoText.text =
                "microfone: " + (tracker.Mic != null && tracker.Mic.IsRecording ? tracker.Mic.Device : "INATIVO") +
                (deviceIndex >= 0 ? "  [" + deviceIndex + " de " + devices.Length + "]" : "") + "\n" +
                "taxa: " + rate + " Hz\n" +
                "janela: " + tracker.WindowSize + " amostras (" + latencyMs.ToString("0") + " ms)\n" +
                "decimacao: " + tracker.Settings.decimation + "x  ->  " + (rate / Mathf.Max(1, tracker.Settings.decimation)) + " Hz efetivos\n" +
                "faixa: " + tracker.Settings.minHz.ToString("0") + " - " + tracker.Settings.maxHz.ToString("0") + " Hz\n" +
                "ganho: " + tracker.Settings.ganhoDoMicrofone.ToString("0.0") + "x\n" +
                "volume agora (RMS): " + r.rms.ToString("0.0000") + "\n" +
                "ruido de fundo: " + tracker.NoiseFloor.ToString("0.0000") + "\n" +
                "gate em vigor: " + tracker.Gate.ToString("0.0000") +
                (tracker.Settings.gateAutomatico ? "  (automatico, teto " + tracker.Settings.rmsThreshold.ToString("0.000") + ")" : "  (fixo)") + "\n" +
                "confianca minima: " + tracker.Settings.clarityThreshold.ToString("0.00") + "\n\n" +
                "Nao pega voz? Se a barra de volume nem se mexe ao falar, o\n" +
                "microfone escolhido esta errado: use \"Trocar microfone\".";
        }

        /// <summary>140 barrinhas do grafico: so em runtime, para nao encher a cena salva.</summary>
        void BuildHistory()
        {
            historyBars = new Image[HistorySize];
            historyMidi = new float[HistorySize];
            historyVoiced = new bool[HistorySize];

            for (int i = 0; i < HistorySize; i++)
            {
                Image bar = UIBuilder.NewImage(graphRoot, "Bar" + i, Palette.Accent2);
                float x0 = i / (float)HistorySize;
                float x1 = (i + 1) / (float)HistorySize;
                UIBuilder.SetAnchors(bar.rectTransform, new Vector2(x0, 0.5f), new Vector2(x1, 0.5f),
                                     new Vector2(1f, -4f), new Vector2(-1f, 4f));
                bar.gameObject.SetActive(false);
                historyBars[i] = bar;
            }
        }

        /// <summary>
        /// Ring buffer + redesenho: a amostra mais nova entra em historyHead e as
        /// barras leem o buffer em ordem, dando o efeito de rolagem sem precisar
        /// copiar transforms.
        /// </summary>
        void PushHistory(float midi, bool voiced)
        {
            if (historyBars == null) BuildHistory();

            historyMidi[historyHead] = midi;
            historyVoiced[historyHead] = voiced;
            historyHead = (historyHead + 1) % HistorySize;

            for (int i = 0; i < HistorySize; i++)
            {
                int src = (historyHead + i) % HistorySize;
                Image bar = historyBars[i];
                bool on = historyVoiced[src] && historyMidi[src] > 0f;
                if (bar.gameObject.activeSelf != on) bar.gameObject.SetActive(on);
                if (!on) continue;

                float t = Mathf.Clamp01(Mathf.InverseLerp(HistoryLowMidi, HistoryHighMidi, historyMidi[src]));
                RectTransform rt = bar.rectTransform;
                rt.anchorMin = new Vector2(rt.anchorMin.x, t);
                rt.anchorMax = new Vector2(rt.anchorMax.x, t);
                bar.color = i == HistorySize - 1 ? Palette.Good : Palette.Accent2;
            }
        }
    }
}
