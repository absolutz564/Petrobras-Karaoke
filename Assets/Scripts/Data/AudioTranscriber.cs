using System.Collections.Generic;
using Karaoke.Audio;
using Karaoke.Core;
using UnityEngine;

namespace Karaoke.Data
{
    public class TranscriptionOptions
    {
        [Tooltip("Algoritmo usado na analise. YIN erra menos oitava em material real.")]
        public DetectorKind detector = DetectorKind.Yin;

        public int windowSize = 2048;
        [Tooltip("Passo entre analises, em segundos. 0.011 = ~90 medicoes por segundo.")]
        public float hopSeconds = 0.011f;
        public int decimation = 2;

        public float minHz = 80f;
        public float maxHz = 1000f;
        [Tooltip("Gate de silencio. Suba para ignorar trechos baixos.")]
        public float rmsThreshold = 0.02f;
        [Tooltip("Confianca minima. Em mixagem cheia (com instrumentos) vale subir para 0.8.")]
        public float clarityThreshold = 0.75f;

        [Tooltip("Notas mais curtas que isso sao descartadas (ruido de deteccao).")]
        public float minNoteSeconds = 0.12f;
        [Tooltip("Variacao de pitch tolerada dentro da mesma nota, em semitons.")]
        public float maxJumpSemitones = 0.8f;
        [Tooltip("Silencio que encerra a nota atual.")]
        public float silenceGapSeconds = 0.08f;
        [Tooltip("Intervalo sem nota que comeca uma nova linha de letra.")]
        public float lineGapSeconds = 0.9f;
        [Tooltip("Teto de notas por linha. Sem isso, canto continuo vira uma linha unica que nao cabe na tela.")]
        public int maxNotesPerLine = 10;
        /// <summary>
        /// Dobra para a oitava da vizinhanca as notas que destoam mais de 7
        /// semitons. DESLIGADO por padrao: medido em 6 musicas reais (forro,
        /// sertanejo, piseiro), ligar isso SEMPRE piorou a fidelidade quando
        /// comparada exigindo a oitava certa - de 95,6% para 84,4% no pior caso.
        /// Cantor de verdade mistura peito e falsete na mesma frase, entao
        /// âmbito largo costuma ser real, nao erro do detector.
        /// Só ligue em material comprovadamente monotimbrico e ruidoso.
        /// </summary>
        [Tooltip("Dobra notas que destoam da oitava das vizinhas. 0 = desligado (recomendado).")]
        public int octaveFixWindow = 0;
        [Tooltip("Descarta notas curtas isoladas que destoam das vizinhas (tipico erro de oitava no ataque). 0 desliga.")]
        public float outlierSemitones = 5f;
        [Tooltip("Duracao ate a qual uma nota destoante e considerada artefato.")]
        public float outlierMaxSeconds = 0.25f;
        [Tooltip("Texto colocado em cada nota gerada.")]
        public string syllable = "lá ";
    }

    public class TranscriptionReport
    {
        public int Frames;
        public int VoicedFrames;
        public int RawSegments;
        public int Notes;
        public float AverageClarity;
        public float CoverageSeconds;
        public float TotalSeconds;

        public float VoicedPercent => Frames > 0 ? 100f * VoicedFrames / Frames : 0f;
        public float CoveragePercent => TotalSeconds > 0f ? 100f * CoverageSeconds / TotalSeconds : 0f;

        public override string ToString()
        {
            return string.Format(
                "{0} notas em {1:0.0}s | {2:0}% dos frames com pitch confiavel | {3:0}% do tempo virou nota | clareza media {4:0.00}",
                Notes, TotalSeconds, VoicedPercent, CoveragePercent, AverageClarity);
        }
    }

    /// <summary>
    /// Transcreve um sinal de audio para um rascunho de melodia (SongChartDto),
    /// rodando o mesmo detector de pitch do jogo quadro a quadro e agrupando
    /// medicoes estaveis em notas.
    ///
    /// O resultado sai com bpm = 60 de proposito: assim 1 batida = 1 segundo e o
    /// JSON gerado pode ser lido/editado direto em segundos.
    ///
    /// Aviso honesto: isto e um RASCUNHO. Em uma gravacao com voz solo o
    /// resultado costuma ser bom; em mixagem cheia (bateria, sanfona, guitarra)
    /// o detector segue o que estiver mais forte, entao espere revisar as notas
    /// na mao. O relatorio devolvido diz o quanto confiar.
    /// </summary>
    public static class AudioTranscriber
    {
        public static SongChartDto Transcribe(float[] mono, int sampleRate, TranscriptionOptions options,
                                              out TranscriptionReport report)
        {
            if (options == null) options = new TranscriptionOptions();
            report = new TranscriptionReport();

            var dto = new SongChartDto { bpm = 60f, gap = 0f, notes = new SongNoteDto[0] };
            if (mono == null || mono.Length < options.windowSize || sampleRate <= 0) return dto;

            IPitchDetector detector = options.detector == DetectorKind.Autocorrelation
                ? (IPitchDetector)new AutocorrelationPitchDetector()
                : new YinPitchDetector();
            detector.MinHz = options.minHz;
            detector.MaxHz = options.maxHz;
            detector.RmsThreshold = options.rmsThreshold;
            detector.ClarityThreshold = options.clarityThreshold;
            detector.Decimation = options.decimation;

            int hop = Mathf.Max(64, Mathf.RoundToInt(options.hopSeconds * sampleRate));
            int window = Mathf.Max(256, options.windowSize);
            var buffer = new float[window];

            var midis = new List<float>();   // NaN = frame sem pitch
            var times = new List<float>();
            float claritySum = 0f;

            for (int start = 0; start + window <= mono.Length; start += hop)
            {
                System.Array.Copy(mono, start, buffer, 0, window);
                PitchResult r = detector.Detect(buffer, sampleRate);

                report.Frames++;
                times.Add(start / (float)sampleRate);

                if (r.voiced && r.frequency > 0f)
                {
                    report.VoicedFrames++;
                    claritySum += r.clarity;
                    midis.Add(PitchUtils.HzToMidi(r.frequency));
                }
                else
                {
                    midis.Add(float.NaN);
                }
            }

            report.TotalSeconds = mono.Length / (float)sampleRate;
            report.AverageClarity = report.VoicedFrames > 0 ? claritySum / report.VoicedFrames : 0f;

            SmoothMedian(midis, 5);
            List<SongNoteDto> notes = Segment(midis, times, hop / (float)sampleRate, options, report);

            FixOctaveJumps(notes, options.octaveFixWindow);
            AssignLines(notes, options.lineGapSeconds, options.maxNotesPerLine);
            dto.notes = notes.ToArray();
            report.Notes = notes.Count;
            foreach (SongNoteDto n in notes) report.CoverageSeconds += n.length;

            return dto;
        }

        /// <summary>Mediana movel ignorando frames sem pitch — remove saltos de oitava isolados.</summary>
        static void SmoothMedian(List<float> midis, int size)
        {
            if (size < 3) return;
            var source = new List<float>(midis);
            var scratch = new List<float>(size);
            int half = size / 2;

            for (int i = 0; i < midis.Count; i++)
            {
                if (float.IsNaN(source[i])) continue;
                scratch.Clear();
                for (int k = i - half; k <= i + half; k++)
                {
                    if (k < 0 || k >= source.Count) continue;
                    if (!float.IsNaN(source[k])) scratch.Add(source[k]);
                }
                if (scratch.Count == 0) continue;
                scratch.Sort();
                midis[i] = scratch[scratch.Count / 2];
            }
        }

        static List<SongNoteDto> Segment(List<float> midis, List<float> times, float frameSeconds,
                                         TranscriptionOptions options, TranscriptionReport report)
        {
            var notes = new List<SongNoteDto>();
            var current = new List<float>();
            float noteStart = 0f;
            float lastVoiced = 0f;
            float silence = 0f;

            for (int i = 0; i < midis.Count; i++)
            {
                float midi = midis[i];
                float t = times[i];

                if (float.IsNaN(midi))
                {
                    if (current.Count > 0)
                    {
                        silence += frameSeconds;
                        if (silence >= options.silenceGapSeconds)
                        {
                            Emit(notes, current, noteStart, lastVoiced + frameSeconds, options, report);
                            current.Clear();
                            silence = 0f;
                        }
                    }
                    continue;
                }

                silence = 0f;

                if (current.Count == 0)
                {
                    noteStart = t;
                    current.Add(midi);
                    lastVoiced = t;
                    continue;
                }

                float reference = Median(current);
                if (Mathf.Abs(midi - reference) > options.maxJumpSemitones)
                {
                    Emit(notes, current, noteStart, lastVoiced + frameSeconds, options, report);
                    current.Clear();
                    noteStart = t;
                }

                current.Add(midi);
                lastVoiced = t;
            }

            if (current.Count > 0)
                Emit(notes, current, noteStart, lastVoiced + frameSeconds, options, report);

            MergeAdjacent(notes, options);
            RemoveOutliers(notes, options);
            MergeAdjacent(notes, options);
            return notes;
        }

        /// <summary>
        /// Tira notas curtas cuja altura destoa das duas vizinhas. Sao quase
        /// sempre erro de oitava no ataque da nota (o detector se agarra a um
        /// harmonico antes da voz estabilizar), e nao um salto real da melodia —
        /// um salto real dura o suficiente para ser cantado.
        /// </summary>
        static void RemoveOutliers(List<SongNoteDto> notes, TranscriptionOptions options)
        {
            if (options.outlierSemitones <= 0f) return;

            for (int i = notes.Count - 1; i >= 0; i--)
            {
                if (notes[i].length > options.outlierMaxSeconds) continue;

                SongNoteDto prev = i > 0 ? notes[i - 1] : null;
                SongNoteDto next = i < notes.Count - 1 ? notes[i + 1] : null;
                if (prev == null && next == null) continue;

                bool farFromPrev = prev == null || Mathf.Abs(notes[i].midi - prev.midi) > options.outlierSemitones;
                bool farFromNext = next == null || Mathf.Abs(notes[i].midi - next.midi) > options.outlierSemitones;

                if (farFromPrev && farFromNext) notes.RemoveAt(i);
            }
        }

        static void Emit(List<SongNoteDto> notes, List<float> midis, float start, float end,
                         TranscriptionOptions options, TranscriptionReport report)
        {
            report.RawSegments++;
            float duration = end - start;
            if (duration < options.minNoteSeconds || midis.Count == 0) return;

            notes.Add(new SongNoteDto
            {
                beat = start,                       // bpm = 60 => batida == segundo
                length = duration,
                midi = Mathf.RoundToInt(Median(midis)),
                text = options.syllable,
                line = 0
            });
        }

        /// <summary>Junta notas vizinhas de mesma altura separadas por um vao minusculo.</summary>
        static void MergeAdjacent(List<SongNoteDto> notes, TranscriptionOptions options)
        {
            for (int i = notes.Count - 1; i > 0; i--)
            {
                SongNoteDto prev = notes[i - 1];
                SongNoteDto cur = notes[i];
                float gap = cur.beat - (prev.beat + prev.length);
                if (cur.midi == prev.midi && gap <= options.silenceGapSeconds)
                {
                    prev.length = cur.beat + cur.length - prev.beat;
                    notes.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Corrige erro de oitava comparando cada nota com a mediana das
        /// vizinhas: numa mesma passagem cantada, um desvio maior que 7 semitons
        /// da vizinhanca e quase sempre o detector tendo travado num harmonico
        /// (ou na subharmonica), nao um salto real de melodia.
        /// </summary>
        static void FixOctaveJumps(List<SongNoteDto> notes, int window)
        {
            if (window <= 0 || notes.Count < 3) return;
            var neighbourhood = new List<int>(window * 2 + 1);

            for (int i = 0; i < notes.Count; i++)
            {
                neighbourhood.Clear();
                for (int k = i - window; k <= i + window; k++)
                {
                    if (k < 0 || k >= notes.Count || k == i) continue;
                    neighbourhood.Add(notes[k].midi);
                }
                if (neighbourhood.Count == 0) continue;

                neighbourhood.Sort();
                int median = neighbourhood[neighbourhood.Count / 2];

                int midi = notes[i].midi;
                while (midi - median > 7) midi -= 12;
                while (median - midi > 7) midi += 12;
                notes[i].midi = midi;
            }
        }

        static void AssignLines(List<SongNoteDto> notes, float lineGapSeconds, int maxNotesPerLine)
        {
            int line = 0;
            int inLine = 0;

            for (int i = 0; i < notes.Count; i++)
            {
                if (i > 0)
                {
                    float gap = notes[i].beat - (notes[i - 1].beat + notes[i - 1].length);
                    bool breakOnPause = gap > lineGapSeconds;
                    // quebra tambem por tamanho, senao canto continuo vira uma
                    // linha unica que estoura a largura da tela
                    bool breakOnLength = maxNotesPerLine > 0 && inLine >= maxNotesPerLine;
                    if (breakOnPause || breakOnLength) { line++; inLine = 0; }
                }
                notes[i].line = line;
                inLine++;
            }
        }

        static float Median(List<float> values)
        {
            if (values.Count == 0) return 0f;
            if (values.Count == 1) return values[0];
            var copy = new List<float>(values);
            copy.Sort();
            return copy[copy.Count / 2];
        }

        /// <summary>Mistura canais intercalados em um vetor mono.</summary>
        public static float[] ToMono(float[] interleaved, int channels)
        {
            if (channels <= 1) return interleaved;
            int frames = interleaved.Length / channels;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
                mono[i] = sum / channels;
            }
            return mono;
        }
    }
}
