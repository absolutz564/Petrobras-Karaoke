namespace Karaoke.Audio
{
    public struct PitchResult
    {
        /// <summary>Frequencia fundamental estimada em Hz (0 se nao detectada).</summary>
        public float frequency;
        /// <summary>Confianca do detector, 0..1.</summary>
        public float clarity;
        /// <summary>Volume RMS do bloco analisado.</summary>
        public float rms;
        /// <summary>True quando ha voz com pitch confiavel (nao e silencio nem ruido).</summary>
        public bool voiced;

        public static PitchResult Unvoiced(float rms)
        {
            return new PitchResult { frequency = 0f, clarity = 0f, rms = rms, voiced = false };
        }
    }

    /// <summary>
    /// Contrato do detector de pitch. Permite trocar autocorrelacao por YIN
    /// (ou qualquer outro algoritmo) sem tocar no resto do jogo.
    /// </summary>
    public interface IPitchDetector
    {
        string Name { get; }
        float MinHz { get; set; }
        float MaxHz { get; set; }
        float RmsThreshold { get; set; }
        float ClarityThreshold { get; set; }
        int Decimation { get; set; }

        PitchResult Detect(float[] samples, int sampleRate);
    }
}
