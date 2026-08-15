using UnityEngine;

namespace Karaoke.Audio
{
    /// <summary>
    /// Decide a partir de que volume um frame conta como voz.
    ///
    /// O gate fixo era o motivo de microfone fraco nao pontuar: um headset
    /// encostado na boca passa dos 0,012 de RMS com folga, mas o microfone
    /// embutido de um notebook, ou um microfone de mao com ganho baixo, fica
    /// abaixo disso e a musica inteira era descartada como silencio.
    ///
    /// Aqui o gate acompanha o ruido de fundo da sala e fica logo acima dele.
    /// O valor configurado vira o TETO, nunca o piso — entao isto so pode
    /// deixar a deteccao mais sensivel do que antes, nunca menos.
    /// </summary>
    public class SilenceGate
    {
        /// <summary>Quantas vezes acima do ruido de fundo a voz precisa estar.</summary>
        public const float Headroom = 4f;

        /// <summary>Piso absoluto: abaixo disto e ruido eletrico, nao voz.</summary>
        public const float MinimumGate = 0.0015f;

        public float NoiseFloor { get; private set; }
        public float Value { get; private set; }

        public SilenceGate(float configured)
        {
            Reset(configured);
        }

        public void Reset(float configured)
        {
            NoiseFloor = configured;
            Value = configured;
        }

        /// <summary>
        /// Alimenta o gate com o volume do frame e devolve o limiar em vigor.
        ///
        /// O piso desce depressa (achou um trecho silencioso, ja aproveita) e
        /// sobe bem devagar — senao uma frase cantada inteira levantaria o piso
        /// e a propria voz acabaria virando "ruido de fundo".
        /// </summary>
        public float Update(float rms, float configured, bool automatic)
        {
            configured = Mathf.Max(0.0005f, configured);

            if (!automatic)
            {
                NoiseFloor = configured;
                Value = configured;
                return Value;
            }

            NoiseFloor = rms < NoiseFloor
                ? Mathf.Lerp(NoiseFloor, rms, 0.5f)
                : Mathf.Lerp(NoiseFloor, rms, 0.001f);

            Value = Mathf.Clamp(NoiseFloor * Headroom, MinimumGate, configured);
            return Value;
        }

        public static float Rms(float[] samples, int count)
        {
            if (samples == null || count <= 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < count; i++) sum += samples[i] * samples[i];
            return Mathf.Sqrt(sum / count);
        }
    }
}
