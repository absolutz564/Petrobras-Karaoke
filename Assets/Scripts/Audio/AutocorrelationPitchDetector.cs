using UnityEngine;

namespace Karaoke.Audio
{
    /// <summary>
    /// Autocorrelacao normalizada (NSDF) com escolha de pico no estilo
    /// McLeod Pitch Method.
    ///
    /// nsdf(lag) = 2 * sum(x[i]*x[i+lag]) / sum(x[i]^2 + x[i+lag]^2)
    ///
    /// A normalizacao mantem o resultado em [-1, 1] independente do volume,
    /// o que da uma medida de confianca util de graca. Em vez de pegar o maior
    /// pico (que costuma cair na oitava abaixo), pegamos o PRIMEIRO pico local
    /// que atinge 90% do maior — o classico truque contra erro de oitava.
    /// </summary>
    public class AutocorrelationPitchDetector : PitchDetectorBase
    {
        public override string Name => "Autocorrelacao (NSDF)";

        /// <summary>Fracao do maior pico que um pico anterior precisa atingir para ser preferido.</summary>
        public float PeakPreference = 0.9f;

        float[] nsdf;

        protected override PitchResult Analyze()
        {
            if (nsdf == null || nsdf.Length < maxLag + 2) nsdf = new float[maxLag + 2];

            // NSDF apenas na faixa de interesse (+1 de folga para interpolar).
            int hi = Mathf.Min(maxLag + 1, length - 1);
            for (int lag = Mathf.Max(1, minLag - 1); lag <= hi; lag++)
            {
                double corr = 0.0, energy = 0.0;
                int count = length - lag;
                for (int i = 0; i < count; i++)
                {
                    float a = buffer[i];
                    float b = buffer[i + lag];
                    corr += (double)a * b;
                    energy += (double)a * a + (double)b * b;
                }
                nsdf[lag] = energy > 1e-12 ? (float)(2.0 * corr / energy) : 0f;
            }

            int lo = Mathf.Max(1, minLag);

            // 1a passada: maior pico local da faixa.
            float best = 0f;
            for (int lag = lo + 1; lag < hi; lag++)
            {
                if (nsdf[lag] > nsdf[lag - 1] && nsdf[lag] >= nsdf[lag + 1] && nsdf[lag] > best)
                    best = nsdf[lag];
            }
            if (best <= 0f) return PitchResult.Unvoiced(0f);

            // 2a passada: primeiro pico local que chega perto do melhor.
            float threshold = best * PeakPreference;
            int chosen = -1;
            for (int lag = lo + 1; lag < hi; lag++)
            {
                if (nsdf[lag] > nsdf[lag - 1] && nsdf[lag] >= nsdf[lag + 1] && nsdf[lag] >= threshold)
                {
                    chosen = lag;
                    break;
                }
            }
            if (chosen < 0) return PitchResult.Unvoiced(0f);

            float offset = ParabolicOffset(nsdf[chosen - 1], nsdf[chosen], nsdf[chosen + 1]);
            float period = chosen + offset;
            if (period <= 0f) return PitchResult.Unvoiced(0f);

            return new PitchResult
            {
                frequency = rate / period,
                clarity = Mathf.Clamp01(nsdf[chosen])
            };
        }
    }
}
