using UnityEngine;

namespace Karaoke.Core
{
    /// <summary>
    /// Conversoes entre frequencia (Hz), numero MIDI e nomes de nota.
    /// MIDI 69 = La 440 Hz (A4). Cada semitom = 1 unidade MIDI.
    /// </summary>
    public static class PitchUtils
    {
        public const float A4Hz = 440f;
        public const int A4Midi = 69;

        static readonly string[] Sharp = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        static readonly string[] Solfege = { "Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si" };

        public static float HzToMidi(float hz)
        {
            if (hz <= 0f) return 0f;
            return A4Midi + 12f * Mathf.Log(hz / A4Hz, 2f);
        }

        public static float MidiToHz(float midi)
        {
            return A4Hz * Mathf.Pow(2f, (midi - A4Midi) / 12f);
        }

        /// <summary>Nome da nota + oitava, ex: "La4" / "A4".</summary>
        public static string NoteName(float midi, bool solfege = true)
        {
            int rounded = Mathf.RoundToInt(midi);
            int pitchClass = ((rounded % 12) + 12) % 12;
            int octave = rounded / 12 - 1;
            string name = solfege ? Solfege[pitchClass] : Sharp[pitchClass];
            return name + octave;
        }

        /// <summary>Desvio em cents entre o pitch cantado e a nota temperada mais proxima.</summary>
        public static float CentsFromNearestNote(float midi)
        {
            return (midi - Mathf.Round(midi)) * 100f;
        }

        /// <summary>
        /// Distancia em semitons entre o pitch detectado e o alvo.
        /// Com octaveAgnostic, o resultado e dobrado para [-6, +6] — ou seja,
        /// cantar a melodia uma oitava acima/abaixo continua valendo ponto
        /// (comportamento padrao de jogos de karaoke, essencial para vozes
        /// masculinas cantando referencias femininas e vice-versa).
        /// </summary>
        public static float SemitoneDelta(float detectedMidi, float targetMidi, bool octaveAgnostic)
        {
            float d = detectedMidi - targetMidi;
            if (!octaveAgnostic) return d;
            return Mathf.Repeat(d + 6f, 12f) - 6f;
        }

        /// <summary>RMS (volume medio) de um bloco de amostras, 0..1.</summary>
        public static float Rms(float[] samples, int count = -1)
        {
            if (samples == null || samples.Length == 0) return 0f;
            if (count < 0 || count > samples.Length) count = samples.Length;
            double sum = 0.0;
            for (int i = 0; i < count; i++) sum += (double)samples[i] * samples[i];
            return Mathf.Sqrt((float)(sum / count));
        }
    }
}
