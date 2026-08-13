using UnityEngine;

namespace Karaoke.Audio
{
    /// <summary>
    /// YIN (de Cheveigne &amp; Kawahara, 2002). Usa a funcao diferenca
    ///
    ///   d(tau) = sum (x[i] - x[i+tau])^2
    ///
    /// normalizada cumulativamente (CMNDF) e escolhe o PRIMEIRO vale abaixo de
    /// um limiar absoluto — por isso erra menos oitava que autocorrelacao pura
    /// e e mais estavel em notas sustentadas. Custo comparavel.
    ///
    /// Troque em runtime pelo menu (Pitch Lab) ou em KaraokeSettings.detector.
    /// </summary>
    public class YinPitchDetector : PitchDetectorBase
    {
        public override string Name => "YIN (CMNDF)";

        /// <summary>Limiar absoluto do YIN. 0.10-0.20 e a faixa usual; menor = mais exigente.</summary>
        public float YinThreshold = 0.15f;

        float[] cmndf;

        protected override PitchResult Analyze()
        {
            int hi = Mathf.Min(maxLag + 1, length / 2);
            if (hi <= minLag + 2) return PitchResult.Unvoiced(0f);
            if (cmndf == null || cmndf.Length < hi + 1) cmndf = new float[hi + 1];

            int w = length - hi; // janela de comparacao constante para todos os taus
            cmndf[0] = 1f;
            double runningSum = 0.0;

            for (int tau = 1; tau <= hi; tau++)
            {
                double d = 0.0;
                for (int i = 0; i < w; i++)
                {
                    float diff = buffer[i] - buffer[i + tau];
                    d += (double)diff * diff;
                }
                runningSum += d;
                cmndf[tau] = runningSum > 1e-12 ? (float)(d * tau / runningSum) : 1f;
            }

            int chosen = -1;
            for (int tau = Mathf.Max(1, minLag); tau < hi; tau++)
            {
                if (cmndf[tau] < YinThreshold)
                {
                    // desce ate o fundo do vale
                    while (tau + 1 < hi && cmndf[tau + 1] < cmndf[tau]) tau++;
                    chosen = tau;
                    break;
                }
            }

            // Sem vale abaixo do limiar: usa o menor valor da faixa (pitch fraco).
            if (chosen < 0)
            {
                float min = float.MaxValue;
                for (int tau = Mathf.Max(1, minLag); tau < hi; tau++)
                {
                    if (cmndf[tau] < min) { min = cmndf[tau]; chosen = tau; }
                }
                if (chosen < 0) return PitchResult.Unvoiced(0f);
            }

            int prev = Mathf.Max(1, chosen - 1);
            int next = Mathf.Min(hi, chosen + 1);
            // vale invertido -> reutiliza a interpolacao de pico com sinal trocado
            float offset = ParabolicOffset(-cmndf[prev], -cmndf[chosen], -cmndf[next]);
            float period = chosen + offset;
            if (period <= 0f) return PitchResult.Unvoiced(0f);

            return new PitchResult
            {
                frequency = rate / period,
                clarity = Mathf.Clamp01(1f - cmndf[chosen])
            };
        }
    }
}
