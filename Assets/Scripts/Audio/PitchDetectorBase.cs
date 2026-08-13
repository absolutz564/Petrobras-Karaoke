using Karaoke.Core;
using UnityEngine;

namespace Karaoke.Audio
{
    /// <summary>
    /// Trabalho comum aos detectores: gate de silencio, decimacao e remocao de DC.
    /// A decimacao (media de N amostras) reduz a taxa efetiva: com fator 2 a
    /// janela cai de 2048@44.1k para 1024@22.05k, deixando o custo O(N*lags)
    /// 4x menor. Nyquist de 11 kHz continua muito acima da fundamental da voz.
    /// </summary>
    public abstract class PitchDetectorBase : IPitchDetector
    {
        public abstract string Name { get; }

        public float MinHz { get; set; } = 70f;
        public float MaxHz { get; set; } = 1100f;
        public float RmsThreshold { get; set; } = 0.012f;
        public float ClarityThreshold { get; set; } = 0.6f;
        public int Decimation { get; set; } = 2;

        protected float[] buffer;   // sinal decimado e centrado
        protected int length;       // amostras validas em buffer
        protected int rate;         // taxa efetiva apos decimacao
        protected int minLag;
        protected int maxLag;

        public PitchResult Detect(float[] samples, int sampleRate)
        {
            float rms = PitchUtils.Rms(samples);
            if (samples == null || samples.Length < 64 || rms < RmsThreshold)
                return PitchResult.Unvoiced(rms);

            Prepare(samples, sampleRate);
            if (maxLag <= minLag + 2)
                return PitchResult.Unvoiced(rms);

            PitchResult r = Analyze();
            r.rms = rms;
            if (r.frequency < MinHz || r.frequency > MaxHz || r.clarity < ClarityThreshold)
            {
                r.voiced = false;
                if (r.frequency < MinHz || r.frequency > MaxHz) r.frequency = 0f;
            }
            else
            {
                r.voiced = true;
            }
            return r;
        }

        /// <summary>Roda o algoritmo sobre buffer/length/rate e devolve frequencia + clareza.</summary>
        protected abstract PitchResult Analyze();

        void Prepare(float[] samples, int sampleRate)
        {
            int dec = Mathf.Clamp(Decimation, 1, 4);
            int n = samples.Length / dec;
            if (buffer == null || buffer.Length < n) buffer = new float[n];
            length = n;
            rate = Mathf.Max(1, sampleRate / dec);

            float mean = 0f;
            for (int i = 0; i < n; i++)
            {
                float acc = 0f;
                int baseIdx = i * dec;
                for (int k = 0; k < dec; k++) acc += samples[baseIdx + k];
                acc /= dec;
                buffer[i] = acc;
                mean += acc;
            }
            mean /= n;
            for (int i = 0; i < n; i++) buffer[i] -= mean; // remove offset DC

            minLag = Mathf.Max(2, Mathf.FloorToInt(rate / MaxHz));
            maxLag = Mathf.Min(n - 2, Mathf.CeilToInt(rate / MinHz));
        }

        /// <summary>Interpolacao parabolica no pico/vale para resolucao sub-amostra.</summary>
        protected static float ParabolicOffset(float yPrev, float y, float yNext)
        {
            float denom = yPrev - 2f * y + yNext;
            if (Mathf.Abs(denom) < 1e-9f) return 0f;
            return Mathf.Clamp(0.5f * (yPrev - yNext) / denom, -1f, 1f);
        }
    }
}
