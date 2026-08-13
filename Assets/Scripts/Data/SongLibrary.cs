using System;
using System.Collections.Generic;
using UnityEngine;

namespace Karaoke.Data
{
    /// <summary>
    /// Carrega todas as musicas de Assets/Resources/Songs.
    /// Aceita .json (formato do projeto) e .txt (UltraStar).
    /// Para adicionar musica: solte o arquivo na pasta. Nao precisa mexer em codigo.
    /// </summary>
    public static class SongLibrary
    {
        public const string ResourceFolder = "Songs";

        public static List<SongChart> LoadAll()
        {
            var charts = new List<SongChart>();
            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourceFolder);

            foreach (var asset in assets)
            {
                if (asset == null) continue;
                try
                {
                    SongChart chart = SongChart.Parse(asset.text, asset.name);
                    if (chart != null) charts.Add(chart);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Karaoke] Falha ao ler a musica '" + asset.name + "': " + e.Message);
                }
            }

            charts.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });

            if (charts.Count == 0)
                Debug.LogWarning("[Karaoke] Nenhuma musica encontrada em Resources/" + ResourceFolder);

            return charts;
        }
    }
}
