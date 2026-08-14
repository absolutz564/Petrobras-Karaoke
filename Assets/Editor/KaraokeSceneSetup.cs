using System.Collections.Generic;
using System.Text;
using Karaoke.App;
using Karaoke.Data;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Karaoke.EditorTools
{
    /// <summary>
    /// Utilitarios de editor no menu "Karaoke".
    ///
    /// O jogo se liga aos objetos da cena pelo nome, entao o mais util aqui e
    /// conferir se esta tudo no lugar ANTES de dar Play — o verificador aponta
    /// nome por nome o que falta, sem precisar entrar em Play e ler o Console.
    /// </summary>
    public static class KaraokeSceneSetup
    {
        [MenuItem("Karaoke/Conferir objetos da cena", false, 5)]
        public static void CheckScene()
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Karaoke] Nao ha Canvas na cena aberta.");
                return;
            }

            var missing = new List<string>();
            var notes = new List<string>();
            Transform root = canvas.transform;

            Transform menu = Screen(root, SceneNames.Menu, missing);
            Transform select = Screen(root, SceneNames.MusicSelect, missing);
            Transform game = Screen(root, SceneNames.Game, missing);
            Transform end = Screen(root, SceneNames.EndGame, missing);
            Transform obrigado = Screen(root, SceneNames.ThankYou, missing);

            Need<Button>(menu, SceneNames.MenuAdvanceButton, missing);
            Need<Button>(select, SceneNames.ConfirmButton, missing);

            if (select != null)
            {
                var buttons = new List<Karaoke.UI.SongButtonBinding>();
                select.GetComponentsInChildren(true, buttons);
                if (buttons.Count == 0)
                    notes.Add("Nenhum botao de musica tem o componente SongButtonBinding (cole nos 6 botoes).");
                else
                {
                    int semSprite = 0;
                    foreach (var b in buttons) if (b.selectedSprite == null) semSprite++;
                    notes.Add(buttons.Count + " botao(oes) de musica com SongButtonBinding" +
                              (semSprite > 0 ? " — " + semSprite + " sem o sprite de selecionado" : ""));
                }
            }

            Need<TMP_Text>(game, SceneNames.PointsText, missing);
            Need<TMP_Text>(game, SceneNames.MultiplierText, missing);
            Need<TMP_Text>(game, SceneNames.CounterText, missing);
            Need(game, SceneNames.CounterImage, missing);
            for (int i = 0; i < Karaoke.UI.LyricsView.WindowLines; i++)
                Need<TMP_Text>(game, SceneNames.LyricLinePrefix + i, missing);

            Need<Image>(end, SceneNames.ScoreImage, missing);
            Need<TMP_Text>(end, SceneNames.EndPointsText, missing);
            Need<Button>(end, SceneNames.RankingButton, missing);
            Need<Button>(end, SceneNames.SkipButton, missing);
            Need(end, SceneNames.RegisterModal, missing);
            Need<TMP_InputField>(end, SceneNames.NameInput, missing);
            Need<Button>(end, SceneNames.RegisterButton, missing);

            if (obrigado != null) notes.Add(SceneNames.ThankYou + ": qualquer tecla nesta tela reinicia o jogo.");

            // Ranking fora do fluxo — as tres colunas deixaram de ser exigidas.
            // Voltando a tela, e so descomentar aqui e em KaraokeApp.BindScene.
            //
            // Transform rank = Screen(root, SceneNames.Ranking, missing);
            // CheckRankingColumn(rank, SceneNames.RankingForroList, missing, notes);
            // CheckRankingColumn(rank, SceneNames.RankingPiseiroList, missing, notes);
            // CheckRankingColumn(rank, SceneNames.RankingSertanejoList, missing, notes);

            var sb = new StringBuilder();
            sb.AppendLine("[Karaoke] Conferencia da cena");
            foreach (string n in notes) sb.AppendLine("   . " + n);

            if (missing.Count == 0)
            {
                sb.AppendLine("   Tudo no lugar: pode dar Play.");
                Debug.Log(sb.ToString());
            }
            else
            {
                sb.AppendLine("   Faltam " + missing.Count + " objeto(s):");
                foreach (string m in missing) sb.AppendLine("   - " + m);
                Debug.LogError(sb.ToString());
            }
        }

        static void CheckRankingColumn(Transform rank, string listName, List<string> missing, List<string> notes)
        {
            if (rank == null) return;

            Transform list = SceneBinder.Find(rank, listName);
            if (list == null) { missing.Add(SceneNames.Ranking + " / " + listName); return; }

            int rows = 0, incomplete = 0;
            for (int i = 0; i < 10; i++)
            {
                Transform row = SceneBinder.Find(list, SceneNames.RankRowPrefix + i);
                if (row == null) continue;
                rows++;

                bool complete = SceneBinder.Find(row, SceneNames.RowPosText) != null &&
                                SceneBinder.Find(row, SceneNames.RowNameText) != null &&
                                SceneBinder.Find(row, SceneNames.RowPointsText) != null;
                if (!complete) incomplete++;
            }

            if (rows == 0)
                missing.Add(listName + " / " + SceneNames.RankRowPrefix + "0 (com " + SceneNames.RowPosText + ", " +
                            SceneNames.RowNameText + " e " + SceneNames.RowPointsText + ") — as outras 9 saem por clone");
            else if (incomplete > 0)
                missing.Add(listName + ": " + incomplete + " linha(s) sem " + SceneNames.RowPosText + " / " +
                            SceneNames.RowNameText + " / " + SceneNames.RowPointsText);
            else
                notes.Add(listName + ": " + rows + " linha(s) prontas" + (rows == 1 ? " (as outras 9 saem por clone no Play)" : ""));
        }

        static Transform Screen(Transform root, string name, List<string> missing)
        {
            Transform found = SceneBinder.Find(root, name);
            if (found == null) missing.Add("Canvas / " + name);
            return found;
        }

        static void Need(Transform screen, string name, List<string> missing)
        {
            if (screen == null) return;
            if (SceneBinder.Find(screen, name) == null) missing.Add(screen.name + " / " + name);
        }

        static void Need<T>(Transform screen, string name, List<string> missing) where T : Component
        {
            if (screen == null) return;
            Transform found = SceneBinder.Find(screen, name);
            if (found == null) { missing.Add(screen.name + " / " + name); return; }
            if (found.GetComponent<T>() == null)
                missing.Add(screen.name + " / " + name + "  (falta o componente " + typeof(T).Name + ")");
        }

        [MenuItem("Karaoke/Validar musicas de Resources_Songs", false, 20)]
        public static void ValidateSongs()
        {
            List<SongChart> charts = SongLibrary.LoadAll();
            if (charts.Count == 0)
            {
                Debug.LogWarning("[Karaoke] Nenhuma musica carregada. Confira Assets/Resources/Songs.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("[Karaoke] " + charts.Count + " musica(s) carregada(s):");
            foreach (SongChart c in charts)
            {
                sb.AppendLine(string.Format("  {0,-24} {1,-10} {2,3} notas  {3,5:0.0}s  ranking: \"{4}\"",
                    c.Title, c.Estilo, c.Notes.Count, c.EndTime, c.Rotulo));
                if (string.IsNullOrEmpty(c.Estilo))
                    sb.AppendLine("      SEM ESTILO: o envio ao ranking vai ser recusado pelo backend.");
            }
            Debug.Log(sb.ToString());
        }
    }
}
