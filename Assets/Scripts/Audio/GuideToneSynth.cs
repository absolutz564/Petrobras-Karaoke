using Karaoke.Core;
using Karaoke.Data;
using UnityEngine;

namespace Karaoke.Audio
{
    /// <summary>
    /// Gera, em tempo de execucao, um AudioClip com a melodia da musica em
    /// senoides (+ 2 harmonicos) e cliques de contagem inicial.
    ///
    /// Motivo: o projeto funciona e e testavel sem nenhum arquivo de audio.
    /// Quando a musica tiver "audioResource" apontando para um clip em
    /// Resources/, esse clip e usado no lugar do tom guia.
    /// </summary>
    public static class GuideToneSynth
    {
        public static AudioClip Build(SongChart chart, float countInSeconds, int sampleRate = 44100, float volume = 0.22f)
        {
            if (chart == null || chart.Notes.Count == 0) return null;

            float totalSeconds = countInSeconds + chart.EndTime + 1.5f;
            int totalSamples = Mathf.CeilToInt(totalSeconds * sampleRate);
            var data = new float[totalSamples];

            AddCountIn(data, sampleRate, countInSeconds, chart.SecondsPerBeat);

            foreach (SongNote note in chart.Notes)
            {
                float freq = PitchUtils.MidiToHz(note.Midi);
                int start = Mathf.RoundToInt((countInSeconds + note.StartTime) * sampleRate);
                int count = Mathf.RoundToInt(note.Duration * sampleRate);
                if (start < 0 || count <= 0) continue;

                float attack = Mathf.Min(0.02f * sampleRate, count * 0.2f);
                float release = Mathf.Min(0.08f * sampleRate, count * 0.4f);
                double phaseStep = 2.0 * Mathf.PI * freq / sampleRate;

                for (int i = 0; i < count; i++)
                {
                    int idx = start + i;
                    if (idx >= totalSamples) break;

                    float env = 1f;
                    if (i < attack) env = i / attack;
                    else if (i > count - release) env = (count - i) / release;

                    double ph = phaseStep * i;
                    float s = (float)(Mathf.Sin((float)ph)
                                      + 0.30f * Mathf.Sin((float)(ph * 2.0))
                                      + 0.12f * Mathf.Sin((float)(ph * 3.0)));
                    data[idx] += s * env * volume * 0.7f;
                }
            }

            for (int i = 0; i < totalSamples; i++)
                data[i] = Mathf.Clamp(data[i], -1f, 1f);

            AudioClip clip = AudioClip.Create("guide_" + chart.Id, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void AddCountIn(float[] data, int sampleRate, float countInSeconds, float secondsPerBeat)
        {
            if (countInSeconds <= 0f || secondsPerBeat <= 0f) return;

            for (int k = 1; k <= 4; k++)
            {
                float t = countInSeconds - k * secondsPerBeat;
                if (t < 0f) break;
                int start = Mathf.RoundToInt(t * sampleRate);
                int count = Mathf.RoundToInt(0.06f * sampleRate);
                float freq = k == 4 ? 1600f : 1100f; // primeiro clique mais agudo
                double phaseStep = 2.0 * Mathf.PI * freq / sampleRate;

                for (int i = 0; i < count; i++)
                {
                    int idx = start + i;
                    if (idx < 0 || idx >= data.Length) continue;
                    float env = 1f - (float)i / count;
                    data[idx] += Mathf.Sin((float)(phaseStep * i)) * env * env * 0.25f;
                }
            }
        }

        /// <summary>Tom puro para calibrar o detector (usado no Pitch Lab).</summary>
        public static AudioClip Tone(float frequency, float seconds = 2f, int sampleRate = 44100, float volume = 0.3f)
        {
            int total = Mathf.CeilToInt(seconds * sampleRate);
            var data = new float[total];
            double phaseStep = 2.0 * Mathf.PI * frequency / sampleRate;
            int fade = Mathf.RoundToInt(0.02f * sampleRate);

            for (int i = 0; i < total; i++)
            {
                float env = 1f;
                if (i < fade) env = i / (float)fade;
                else if (i > total - fade) env = (total - i) / (float)fade;
                data[i] = Mathf.Sin((float)(phaseStep * i)) * env * volume;
            }

            AudioClip clip = AudioClip.Create("tone_" + Mathf.RoundToInt(frequency), total, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
