using System.Text;
using Karaoke.Audio;
using Karaoke.Core;
using UnityEditor;
using UnityEngine;

namespace Karaoke.EditorTools
{
    /// <summary>
    /// Testa os detectores com sinais sinteticos, sem microfone e sem entrar em
    /// Play. Gera ondas com harmonicos (mais parecidas com voz que uma senoide
    /// pura) em varias frequencias e compara o pitch detectado com o real.
    ///
    /// Menu: Karaoke > Testar detectores (sinais sinteticos)
    /// Criterio: erro abaixo de 20 cents (1/5 de semitom) em toda a faixa vocal.
    /// </summary>
    public static class PitchDetectorSelfTest
    {
        const int SampleRate = 44100;

        static readonly float[] TestFrequencies =
        {
            82.41f,  // E2  - baixo
            110.00f, // A2
            146.83f, // D3
            196.00f, // G3
            261.63f, // C4
            329.63f, // E4
            440.00f, // A4
            587.33f, // D5
            880.00f  // A5  - soprano
        };

        [MenuItem("Karaoke/Testar detectores (sinais sinteticos)", false, 30)]
        public static void Run()
        {
            var settings = new KaraokeSettings();
            var sb = new StringBuilder();
            sb.AppendLine("[Karaoke] Auto-teste dos detectores de pitch (janela " + settings.windowSize +
                          " @ " + SampleRate + " Hz, decimacao " + settings.decimation + "x)");
            sb.AppendLine(string.Format("{0,-12} {1,-14} {2,-14} {3,-10} {4,-10}", "esperado", "autocorrelacao", "YIN", "erro AC", "erro YIN"));

            var autocorrelation = new AutocorrelationPitchDetector();
            var yin = new YinPitchDetector();
            Configure(autocorrelation, settings);
            Configure(yin, settings);

            int failures = 0;
            var buffer = new float[settings.windowSize];

            foreach (float freq in TestFrequencies)
            {
                FillHarmonicWave(buffer, freq, SampleRate);

                PitchResult ac = autocorrelation.Detect(buffer, SampleRate);
                PitchResult yn = yin.Detect(buffer, SampleRate);

                float acCents = Cents(ac.frequency, freq);
                float ynCents = Cents(yn.frequency, freq);

                if (!ac.voiced || Mathf.Abs(acCents) > 20f) failures++;
                if (!yn.voiced || Mathf.Abs(ynCents) > 20f) failures++;

                sb.AppendLine(string.Format("{0,-12:0.00} {1,-14:0.00} {2,-14:0.00} {3,-10:+0.0;-0.0} {4,-10:+0.0;-0.0}",
                    freq, ac.frequency, yn.frequency, acCents, ynCents));
            }

            // Ruido puro nao deve ser confundido com voz.
            FillNoise(buffer, 0.05f);
            PitchResult noiseAc = autocorrelation.Detect(buffer, SampleRate);
            PitchResult noiseYin = yin.Detect(buffer, SampleRate);
            sb.AppendLine("ruido branco  -> autocorrelacao voiced=" + noiseAc.voiced + " (clareza " + noiseAc.clarity.ToString("0.00") +
                          "), YIN voiced=" + noiseYin.voiced + " (clareza " + noiseYin.clarity.ToString("0.00") + ")");

            // Silencio deve ser rejeitado pelo gate de RMS.
            for (int i = 0; i < buffer.Length; i++) buffer[i] = 0f;
            sb.AppendLine("silencio      -> autocorrelacao voiced=" + autocorrelation.Detect(buffer, SampleRate).voiced +
                          ", YIN voiced=" + yin.Detect(buffer, SampleRate).voiced);

            sb.AppendLine(failures == 0
                ? "RESULTADO: OK - todos dentro de 20 cents."
                : "RESULTADO: " + failures + " medicao(oes) fora do criterio (>20 cents ou nao detectada).");

            if (failures == 0) Debug.Log(sb.ToString());
            else Debug.LogWarning(sb.ToString());
        }

        static void Configure(IPitchDetector d, KaraokeSettings s)
        {
            d.MinHz = s.minHz;
            d.MaxHz = s.maxHz;
            d.RmsThreshold = s.rmsThreshold;
            d.ClarityThreshold = s.clarityThreshold;
            d.Decimation = s.decimation;
        }

        /// <summary>Fundamental + harmonicos decrescentes: aproxima o timbre de uma voz.</summary>
        static void FillHarmonicWave(float[] buffer, float freq, int sampleRate)
        {
            double step = 2.0 * Mathf.PI * freq / sampleRate;
            for (int i = 0; i < buffer.Length; i++)
            {
                double ph = step * i;
                float v = Mathf.Sin((float)ph)
                          + 0.5f * Mathf.Sin((float)(ph * 2.0))
                          + 0.25f * Mathf.Sin((float)(ph * 3.0))
                          + 0.12f * Mathf.Sin((float)(ph * 4.0));
                buffer[i] = v * 0.25f;
            }
        }

        static void FillNoise(float[] buffer, float amplitude)
        {
            var rng = new System.Random(1234);
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * amplitude;
        }

        static float Cents(float measured, float expected)
        {
            if (measured <= 0f) return 9999f;
            return (PitchUtils.HzToMidi(measured) - PitchUtils.HzToMidi(expected)) * 100f;
        }
    }
}
