using System.Collections.Generic;
using Karaoke.Core;
using Karaoke.Data;
using UnityEngine;

namespace Karaoke.Scoring
{
    /// <summary>
    /// Descobre a latencia real do microfone usando a propria apresentacao.
    ///
    /// O pitch que o jogo le no instante T e o que foi cantado alguns
    /// milissegundos antes: o microfone tem buffer, a janela de analise tem
    /// 46 ms e o filtro de mediana atrasa mais alguns quadros. Se essa
    /// compensacao estiver errada, um cantor afinado perde metade da duracao
    /// de cada nota e o placar despenca sem motivo aparente.
    ///
    /// Em vez de chutar o valor, gravamos o que foi cantado e, no fim da
    /// musica, repontuamos com varios deslocamentos. O que der o melhor placar
    /// e a latencia real daquele equipamento.
    /// </summary>
    public class LatencySweep
    {
        struct Sample
        {
            public float Time;
            public float Dt;
            public float Midi;
            public bool Voiced;
        }

        readonly List<Sample> samples = new List<Sample>(4096);

        public int Count => samples.Count;

        public void Clear() => samples.Clear();

        public void Record(float songTime, float dt, float midi, bool voiced)
        {
            samples.Add(new Sample { Time = songTime, Dt = dt, Midi = midi, Voiced = voiced });
        }

        /// <summary>
        /// Repontua a apresentacao com deslocamentos de 0 a 400 ms e devolve o
        /// melhor. Retorna false quando nao ha material suficiente (ninguem
        /// cantou) para concluir alguma coisa.
        /// </summary>
        public bool Evaluate(SongChart chart, KaraokeSettings settings, out float bestLatency,
                             out float bestPercent, out float currentPercent)
        {
            bestLatency = settings.micLatencySeconds;
            bestPercent = 0f;
            currentPercent = 0f;

            int voiced = 0;
            foreach (Sample s in samples) if (s.Voiced) voiced++;
            if (chart == null || voiced < 30) return false;

            currentPercent = ScoreWith(chart, settings, settings.micLatencySeconds);
            bestPercent = currentPercent;

            for (float latency = 0f; latency <= 0.4f; latency += 0.02f)
            {
                float percent = ScoreWith(chart, settings, latency);
                if (percent > bestPercent)
                {
                    bestPercent = percent;
                    bestLatency = latency;
                }
            }
            return true;
        }

        float ScoreWith(SongChart chart, KaraokeSettings settings, float latency)
        {
            var engine = new ScoreEngine(chart, settings);
            foreach (Sample s in samples)
                engine.Feed(s.Time - latency, s.Dt, s.Midi, s.Voiced);
            engine.Finish();
            return engine.Percent;
        }

        /// <summary>Relatorio pronto para o Console, com a recomendacao.</summary>
        public string Report(SongChart chart, KaraokeSettings settings)
        {
            if (!Evaluate(chart, settings, out float best, out float bestPercent, out float current))
                return "[Karaoke] Pouca voz gravada para calibrar a latencia.";

            string verdict = Mathf.Abs(best - settings.micLatencySeconds) < 0.025f
                ? "A compensacao atual ja esta boa."
                : string.Format("Ajuste micLatencySeconds para {0:0.00} no Inspector: o mesmo canto valeria {1:0.0}% " +
                                "em vez de {2:0.0}%.", best, bestPercent, current);

            return string.Format("[Karaoke] Calibracao de latencia — atual {0:0.00}s da {1:0.0}% | " +
                                 "melhor {2:0.00}s da {3:0.0}%. {4}",
                                 settings.micLatencySeconds, current, best, bestPercent, verdict);
        }
    }
}
