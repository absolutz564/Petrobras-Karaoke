using System.Collections.Generic;
using System.Text;
using Karaoke.Core;
using Karaoke.Data;
using Karaoke.Scoring;
using UnityEditor;
using UnityEngine;

namespace Karaoke.EditorTools
{
    /// <summary>
    /// Simula cantores "de laboratorio" (perfeito, uma oitava abaixo, meio tom
    /// fora, tres semitons fora, mudo) contra todas as musicas, a 60 fps.
    ///
    /// Serve para conferir o efeito de mudancas em perfectSemitones /
    /// maxSemitones / octaveAgnostic sem precisar cantar.
    /// Menu: Karaoke > Simular pontuacao (sem microfone)
    /// </summary>
    public static class ScoringSimulation
    {
        [MenuItem("Karaoke/Simular pontuacao (sem microfone)", false, 40)]
        public static void Run()
        {
            List<SongChart> charts = SongLibrary.LoadAll();
            if (charts.Count == 0)
            {
                Debug.LogWarning("[Karaoke] Nenhuma musica para simular.");
                return;
            }

            var settings = new KaraokeSettings();
            var sb = new StringBuilder();
            sb.AppendLine("[Karaoke] Simulacao de pontuacao (tolerancia " + settings.perfectSemitones +
                          " semitom perfeito / " + settings.maxSemitones + " zero, oitavas " +
                          (settings.octaveAgnostic ? "equivalentes" : "distintas") + ")");
            sb.AppendLine(string.Format("{0,-30} {1,10} {2,12} {3,10} {4,12} {5,8}",
                "musica", "perfeito", "-1 oitava", "+0.5 st", "+3 st", "mudo"));

            foreach (SongChart chart in charts)
            {
                float perfect = Simulate(chart, settings, 0f, true);
                sb.AppendLine(string.Format("{0,-30} {1,10:0} {2,12:0} {3,10:0} {4,12:0} {5,8:0}   ({6} estrelas: {7})",
                    chart.Title,
                    perfect,
                    Simulate(chart, settings, -12f, true),
                    Simulate(chart, settings, 0.5f, true),
                    Simulate(chart, settings, 3f, true),
                    Simulate(chart, settings, 0f, false),
                    ScoreEngine.StarsFor(Mathf.RoundToInt(perfect)),
                    ScoreEngine.LabelFor(ScoreEngine.StarsFor(Mathf.RoundToInt(perfect)))));
            }

            sb.AppendLine("Placar maximo = 50 (45 de afinacao + 5 de sequencia).");
            sb.AppendLine("Faixas: 45-50 = 5 estrelas | 38-44 = 4 | 28-37 = 3 | 18-27 = 2 | 0-17 = 1.");
            Debug.Log(sb.ToString());
        }

        static float Simulate(SongChart chart, KaraokeSettings settings, float semitoneOffset, bool sings)
        {
            var engine = new ScoreEngine(chart, settings);
            const float dt = 1f / 60f;
            int hint = 0;

            for (float t = 0f; t < chart.EndTime + 0.5f; t += dt)
            {
                int index = chart.NoteAt(t, hint);
                if (index >= 0) hint = index;

                bool voiced = sings && index >= 0;
                float midi = index >= 0 ? chart.Notes[index].Midi + semitoneOffset : 0f;
                engine.Feed(t, dt, midi, voiced);
            }

            engine.Finish();
            return engine.TotalScore;
        }
    }
}
