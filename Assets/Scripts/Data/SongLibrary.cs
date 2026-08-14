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

        /// <summary>Catalogo extra, so para o modo de teste — nao aparece no jogo de producao.</summary>
        public const string TestFolder = "SongsTest";

        public static List<SongChart> LoadAll() => LoadAll(ResourceFolder);

        /// <summary>Carrega o catalogo de producao mais o de teste, sem repetir id.</summary>
        public static List<SongChart> LoadAllIncludingTests()
        {
            List<SongChart> charts = LoadAll(ResourceFolder);
            var ids = new HashSet<string>();
            foreach (SongChart c in charts) ids.Add(c.Id);

            foreach (SongChart c in LoadAll(TestFolder))
                if (ids.Add(c.Id)) charts.Add(c);

            return charts;
        }

        public static List<SongChart> LoadAll(string folder)
        {
            var charts = new List<SongChart>();
            TextAsset[] assets = Resources.LoadAll<TextAsset>(folder);

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
                Debug.LogWarning("[Karaoke] Nenhuma musica encontrada em Resources/" + folder);

            return charts;
        }
    }
}
