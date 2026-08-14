using System;
using System.Collections.Generic;
using Karaoke.Core;
using Karaoke.Data;
using UnityEngine;

namespace Karaoke.Scoring
{
    public enum NoteRating
    {
        Miss = 0,
        Ok = 1,
        Good = 2,
        Perfect = 3
    }

    public class NoteScore
    {
        public int Index;
        public SongNote Note;
        /// <summary>0..1 — fracao da duracao da nota cantada dentro da tolerancia.</summary>
        public float Accuracy;
        public float Points;
        public NoteRating Rating;
    }

    /// <summary>
    /// Comparacao pitch cantado x pitch esperado, com pontuacao ponderada por tempo.
    ///
    /// Regras:
    ///  - a acuracia e acumulada em SEGUNDOS (nao em frames), entao o placar nao
    ///    muda se o jogo roda a 30 ou 144 fps;
    ///  - frames de silencio/pitch nao confiavel simplesmente nao somam nada —
    ///    nao ha punicao extra, ja que a nota valera menos por si so;
    ///  - erro <= perfectSemitones vale 1.0 e decai linearmente ate 0 em
    ///    maxSemitones;
    ///  - notas mais longas valem mais pontos (peso = duracao / duracao total).
    ///
    /// Pontuacao final: 9000 de acuracia + ate 1000 de bonus de sequencia.
    /// </summary>
    public class ScoreEngine
    {
        /// <summary>
        /// Escala final de 0 a 50 pontos, dividida em 5 faixas de estrelas.
        /// A afinacao responde por 45 pontos e a sequencia por 5 (mesma
        /// proporcao 90/10 de antes, agora na escala que a tela final usa).
        /// </summary>
        public const float AccuracyPoints = 45f;
        public const float ComboPoints = 5f;
        public const int MaxPoints = 50;

        public SongChart Chart { get; private set; }
        public List<NoteScore> Results { get; private set; }

        /// <summary>Pontos de acuracia ja consolidados (notas encerradas).</summary>
        public float BasePoints { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int NotesCompleted { get; private set; }
        public int NotesHit { get; private set; }

        /// <summary>Nota sendo cantada agora, ou null.</summary>
        public SongNote ActiveNote { get; private set; }
        public int ActiveIndex { get; private set; }
        /// <summary>Acuracia parcial da nota ativa, 0..1.</summary>
        public float ActiveAccuracy { get; private set; }
        /// <summary>Ultimo erro em semitons (assinado) — usado pelo visual/afinador.</summary>
        public float LastDelta { get; private set; }
        public bool OnPitch { get; private set; }

        public event Action<NoteScore> NoteFinished;

        /// <summary>Multiplicador atual (1 ate settings.maxMultiplier).</summary>
        public int Multiplier { get; private set; } = 1;

        readonly KaraokeSettings settings;
        int cursor;
        float accumulatedAccuracySeconds;
        bool finished;

        int streak;                 // notas certas seguidas
        float rawPoints;            // soma de acuracia x peso x multiplicador
        readonly float maxRawPoints; // o mesmo somatorio para um canto perfeito

        public ScoreEngine(SongChart chart, KaraokeSettings settings)
        {
            this.settings = settings;
            Chart = chart;
            Results = new List<NoteScore>(chart != null ? chart.Notes.Count : 0);
            maxRawPoints = MaxRawFor(chart, settings);
        }

        /// <summary>
        /// Soma de peso x multiplicador para um canto perfeito.
        ///
        /// E o que mantem o teto em 50: como o multiplicador cresce ao longo da
        /// musica, o total bruto de um canto perfeito depende do tamanho da
        /// musica. Normalizando por ele, canto perfeito da exatamente 45 de
        /// afinacao (+5 de sequencia) em qualquer musica, e quebrar a sequencia
        /// custa mais do que so a nota errada.
        /// </summary>
        static float MaxRawFor(SongChart chart, KaraokeSettings settings)
        {
            if (chart == null || chart.Notes.Count == 0 || chart.SungDuration <= 0f) return 1f;

            float total = 0f;
            for (int i = 0; i < chart.Notes.Count; i++)
            {
                int multiplier = MultiplierFor(i, settings);   // sequencia perfeita: streak == i
                total += chart.Notes[i].Duration / chart.SungDuration * Weighted(multiplier, settings);
            }
            return Mathf.Max(0.0001f, total);
        }

        static int MultiplierFor(int streak, KaraokeSettings settings)
        {
            int step = Mathf.Max(1, settings.notesPerMultiplierStep);
            int max = Mathf.Max(1, settings.maxMultiplier);
            return Mathf.Clamp(1 + streak / step, 1, max);
        }

        /// <summary>
        /// Multiplicador como ele entra na conta dos pontos. O jogador ve x1..x5
        /// na tela; aqui o efeito e dosado por multiplierWeight, para uma nota
        /// errada nao arrasar o placar inteiro.
        /// </summary>
        static float Weighted(int multiplier, KaraokeSettings settings)
        {
            return 1f + (multiplier - 1f) * Mathf.Clamp01(settings.multiplierWeight);
        }

        public float TotalScore
        {
            get
            {
                float combo = Chart != null && Chart.Notes.Count > 0
                    ? (float)BestCombo / Chart.Notes.Count * ComboPoints
                    : 0f;
                return BasePoints + combo;
            }
        }

        /// <summary>Placar mostrado durante a musica (inclui a nota em andamento).</summary>
        public float LiveScore
        {
            get
            {
                float partial = 0f;
                if (ActiveNote != null && Chart != null && Chart.SungDuration > 0f)
                    partial = ActiveAccuracy * (ActiveNote.Duration / Chart.SungDuration)
                              * Weighted(Multiplier, settings) / maxRawPoints * AccuracyPoints;
                return BasePoints + partial;
            }
        }

        /// <summary>Percentual de acerto, 0..100.</summary>
        public float Percent => BasePoints / AccuracyPoints * 100f;

        /// <summary>
        /// Alimenta o motor com um frame de audio.
        /// </summary>
        /// <param name="time">tempo da musica em segundos (ja compensado pela latencia do microfone)</param>
        /// <param name="dt">duracao do frame em segundos</param>
        /// <param name="midi">pitch cantado em MIDI</param>
        /// <param name="voiced">false para silencio / pitch nao confiavel</param>
        public void Feed(float time, float dt, float midi, bool voiced)
        {
            if (Chart == null || finished) return;

            var notes = Chart.Notes;

            // encerra todas as notas que ja passaram
            while (cursor < notes.Count && time > notes[cursor].EndTime)
            {
                FinalizeNote(cursor);
                cursor++;
            }

            if (cursor >= notes.Count)
            {
                ActiveNote = null;
                ActiveIndex = -1;
                OnPitch = false;
                return;
            }

            SongNote note = notes[cursor];
            if (time < note.StartTime)
            {
                ActiveNote = null;
                ActiveIndex = -1;
                OnPitch = false;
                return;
            }

            ActiveNote = note;
            ActiveIndex = cursor;

            if (voiced && midi > 0f && dt > 0f)
            {
                float delta = PitchUtils.SemitoneDelta(midi, note.Midi, settings.octaveAgnostic);
                float accuracy = FrameAccuracy(Mathf.Abs(delta));
                accumulatedAccuracySeconds += accuracy * dt;
                LastDelta = delta;
                OnPitch = accuracy > 0f;
            }
            else
            {
                OnPitch = false;
            }

            ActiveAccuracy = AccuracyOfNote(note);
        }

        /// <summary>
        /// Acuracia da nota: quanto tempo afinado, dividido pela fracao da nota
        /// que consideramos suficiente. Cantar 75% da nota ja vale 100%.
        /// </summary>
        float AccuracyOfNote(SongNote note)
        {
            float required = Mathf.Max(0.05f, note.Duration * Mathf.Clamp(settings.fullCreditCoverage, 0.4f, 1f));
            return Mathf.Clamp01(accumulatedAccuracySeconds / required);
        }

        float FrameAccuracy(float absDelta)
        {
            float perfect = Mathf.Max(0.01f, settings.perfectSemitones);
            float max = Mathf.Max(perfect + 0.01f, settings.maxSemitones);
            if (absDelta <= perfect) return 1f;
            return Mathf.Clamp01(1f - (absDelta - perfect) / (max - perfect));
        }

        void FinalizeNote(int index)
        {
            SongNote note = Chart.Notes[index];
            float accuracy = AccuracyOfNote(note);
            float weight = Chart.SungDuration > 0f ? note.Duration / Chart.SungDuration : 0f;

            // o multiplicador que valia enquanto esta nota era cantada
            float multiplier = Weighted(Multiplier, settings);
            rawPoints += accuracy * weight * multiplier;
            float points = accuracy * weight * multiplier / maxRawPoints * AccuracyPoints;

            NoteRating rating;
            if (accuracy >= 0.9f) rating = NoteRating.Perfect;
            else if (accuracy >= 0.7f) rating = NoteRating.Good;
            else if (accuracy >= 0.4f) rating = NoteRating.Ok;
            else rating = NoteRating.Miss;

            if (rating == NoteRating.Miss)
            {
                Combo = 0;
                streak = 0;
            }
            else
            {
                Combo++;
                streak++;
                NotesHit++;
                if (Combo > BestCombo) BestCombo = Combo;
            }
            Multiplier = MultiplierFor(streak, settings);

            BasePoints += points;
            NotesCompleted++;

            var result = new NoteScore
            {
                Index = index,
                Note = note,
                Accuracy = accuracy,
                Points = points,
                Rating = rating
            };
            Results.Add(result);
            if (NoteFinished != null) NoteFinished(result);

            accumulatedAccuracySeconds = 0f;
            ActiveAccuracy = 0f;
        }

        /// <summary>Fecha as notas restantes (chamar quando a musica termina).</summary>
        public void Finish()
        {
            if (finished || Chart == null) return;
            while (cursor < Chart.Notes.Count)
            {
                FinalizeNote(cursor);
                cursor++;
            }
            ActiveNote = null;
            ActiveIndex = -1;
            finished = true;
        }

        public bool IsFinished => finished;

        /// <summary>Acuracia consolidada de uma nota ja encerrada, ou -1.</summary>
        public float AccuracyOf(int noteIndex)
        {
            foreach (var r in Results)
                if (r.Index == noteIndex) return r.Accuracy;
            return -1f;
        }

        /// <summary>Placar final arredondado, 0..50 — e o numero que o jogador ve.</summary>
        public int FinalPoints => Mathf.Clamp(Mathf.RoundToInt(TotalScore), 0, MaxPoints);

        /// <summary>Estrelas de 1 a 5, pelas faixas de pontos definidas no jogo.</summary>
        public int Stars => StarsFor(FinalPoints);

        /// <summary>Rotulo da faixa ("INCRIVEL!", "MANDOU BEM", ...).</summary>
        public string RatingLabel => LabelFor(Stars);

        /// <summary>
        /// Faixas de pontuacao:
        ///   45 a 50 -> 5 estrelas   INCRIVEL!
        ///   38 a 44 -> 4 estrelas   MANDOU BEM
        ///   28 a 37 -> 3 estrelas   FOI BEM
        ///   18 a 27 -> 2 estrelas   QUASE LA
        ///    0 a 17 -> 1 estrela    TENTE OUTRA VEZ
        /// </summary>
        public static int StarsFor(int points)
        {
            if (points >= 45) return 5;
            if (points >= 38) return 4;
            if (points >= 28) return 3;
            if (points >= 18) return 2;
            return 1;
        }

        public static string LabelFor(int stars)
        {
            switch (stars)
            {
                case 5: return "INCRÍVEL!";
                case 4: return "MANDOU BEM";
                case 3: return "FOI BEM";
                case 2: return "QUASE LÁ";
                default: return "TENTE OUTRA VEZ";
            }
        }
    }
}
