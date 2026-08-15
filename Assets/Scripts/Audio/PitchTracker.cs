using Karaoke.Core;
using UnityEngine;

namespace Karaoke.Audio
{
    /// <summary>
    /// Cola microfone + detector + suavizacao. Chame Tick() uma vez por frame:
    /// le a janela mais recente do microfone, roda o detector e publica o pitch
    /// em Hz e em MIDI (ja filtrado por mediana, que mata saltos de oitava
    /// isolados sem introduzir o atraso de uma media movel).
    /// </summary>
    public class PitchTracker
    {
        public MicrophoneCapture Mic { get; private set; }
        public IPitchDetector Detector { get; private set; }
        public KaraokeSettings Settings { get; private set; }

        /// <summary>Resultado bruto do ultimo frame analisado.</summary>
        public PitchResult Latest;
        /// <summary>Pitch em MIDI apos filtro de mediana. Mantem o ultimo valor durante silencios (para o visual nao piscar).</summary>
        public float SmoothedMidi;
        /// <summary>Pitch em Hz correspondente ao SmoothedMidi.</summary>
        public float SmoothedHz => SmoothedMidi > 0f ? PitchUtils.MidiToHz(SmoothedMidi) : 0f;
        /// <summary>Ha voz com pitch confiavel neste momento.</summary>
        public bool Voiced;
        /// <summary>RMS suavizado, bom para barras de volume.</summary>
        public float Level;
        /// <summary>Gate de silencio em vigor agora — igual ao rmsThreshold quando o automatico esta desligado.</summary>
        public float Gate => gate.Value;
        /// <summary>Ruido de fundo medido da sala. Se estiver alto, o problema e a sala, nao o microfone.</summary>
        public float NoiseFloor => gate.NoiseFloor;
        public int WindowSize { get; private set; }
        public int SampleRate => Mic != null ? Mic.SampleRate : 0;

        SilenceGate gate;
        float[] window;
        readonly float[] median = new float[9];
        readonly float[] sortScratch = new float[9];
        int medianCount;
        float unvoicedTime;

        public PitchTracker(MicrophoneCapture mic, KaraokeSettings settings)
        {
            Mic = mic;
            Settings = settings;
            Rebuild();
        }

        /// <summary>Recria detector e buffers a partir das settings (chame apos mudar detector/janela).</summary>
        public void Rebuild()
        {
            Detector = Settings.detector == DetectorKind.Yin
                ? (IPitchDetector)new YinPitchDetector()
                : new AutocorrelationPitchDetector();
            ApplySettings();

            WindowSize = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Settings.windowSize), 256, 8192);
            window = new float[WindowSize];
            medianCount = 0;
            gate = new SilenceGate(Settings.rmsThreshold);
        }

        public void ApplySettings()
        {
            Detector.MinHz = Settings.minHz;
            Detector.MaxHz = Settings.maxHz;
            Detector.RmsThreshold = Settings.rmsThreshold;
            Detector.ClarityThreshold = Settings.clarityThreshold;
            Detector.Decimation = Settings.decimation;
        }

        public void SetDetector(DetectorKind kind)
        {
            Settings.detector = kind;
            Rebuild();
        }

        public void SetWindowSize(int size)
        {
            Settings.windowSize = size;
            WindowSize = Mathf.Clamp(Mathf.ClosestPowerOfTwo(size), 256, 8192);
            window = new float[WindowSize];
            medianCount = 0;
        }

        public void Tick()
        {
            if (Mic == null || !Mic.IsRecording)
            {
                Voiced = false;
                return;
            }

            if (!Mic.ReadLatest(window))
            {
                Voiced = false;
                return;
            }

            ApplyGain();
            Detector.RmsThreshold = gate.Update(SilenceGate.Rms(window, window.Length),
                                                Settings.rmsThreshold, Settings.gateAutomatico);

            Latest = Detector.Detect(window, Mic.SampleRate);
            Level = Mathf.Lerp(Level, Latest.rms, 0.35f);

            if (Latest.voiced && Latest.frequency > 0f)
            {
                PushMedian(PitchUtils.HzToMidi(Latest.frequency));
                SmoothedMidi = Median();
                Voiced = true;
                unvoicedTime = 0f;
            }
            else
            {
                Voiced = false;
                unvoicedTime += Time.deltaTime;
                // depois de um silencio real, esquece o historico para nao
                // "arrastar" a nota anterior para dentro da proxima frase
                if (unvoicedTime > 0.2f) medianCount = 0;
            }
        }

        void ApplyGain()
        {
            float gain = Mathf.Max(1f, Settings.ganhoDoMicrofone);
            if (gain <= 1f) return;
            for (int i = 0; i < window.Length; i++)
                window[i] = Mathf.Clamp(window[i] * gain, -1f, 1f);
        }

        void PushMedian(float midi)
        {
            int size = Mathf.Clamp(Settings.smoothingWindow, 1, median.Length);
            if (medianCount < size)
            {
                median[medianCount++] = midi;
                return;
            }
            for (int i = 0; i < size - 1; i++) median[i] = median[i + 1];
            median[size - 1] = midi;
            medianCount = size;
        }

        float Median()
        {
            if (medianCount == 0) return SmoothedMidi;
            for (int i = 0; i < medianCount; i++) sortScratch[i] = median[i];
            // insertion sort (no maximo 9 elementos, zero alocacao)
            for (int i = 1; i < medianCount; i++)
            {
                float v = sortScratch[i];
                int j = i - 1;
                while (j >= 0 && sortScratch[j] > v) { sortScratch[j + 1] = sortScratch[j]; j--; }
                sortScratch[j + 1] = v;
            }
            return sortScratch[medianCount / 2];
        }

        public void Reset()
        {
            medianCount = 0;
            Voiced = false;
            Level = 0f;
            gate.Reset(Settings.rmsThreshold);
            Latest = default(PitchResult);
        }
    }
}
