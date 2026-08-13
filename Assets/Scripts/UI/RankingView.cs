using System.Collections.Generic;
using Karaoke.App;
using Karaoke.Net;
using TMPro;
using UnityEngine;

namespace Karaoke.UI
{
    /// <summary>
    /// Preenche as tres colunas do ranking com o Top N vindo do backend.
    ///
    /// Cada coluna tem linhas chamadas RankRow0..RankRow9, e dentro de cada uma
    /// PosText, NomeText e PontosText. Linha sem dados fica desligada, entao a
    /// coluna encolhe sozinha quando o ranking ainda tem poucos registros.
    /// </summary>
    public class RankingView
    {
        class Row
        {
            public GameObject go;
            public TMP_Text pos;
            public TMP_Text nome;
            public TMP_Text pontos;
        }

        readonly List<Row> forro = new List<Row>();
        readonly List<Row> piseiro = new List<Row>();
        readonly List<Row> sertanejo = new List<Row>();

        public int RowsPerColumn { get; private set; }

        float rowSpacing;

        public RankingView(Transform forroList, Transform piseiroList, Transform sertanejoList,
                           int rowsPerColumn, float rowSpacing)
        {
            RowsPerColumn = rowsPerColumn;
            this.rowSpacing = rowSpacing;
            Collect(forroList, forro);
            Collect(piseiroList, piseiro);
            Collect(sertanejoList, sertanejo);
        }

        void Collect(Transform list, List<Row> into)
        {
            if (list == null) return;

            Transform template = SceneBinder.Find(list, SceneNames.RankRowPrefix + "0");

            for (int i = 0; i < RowsPerColumn; i++)
            {
                Transform rowTransform = SceneBinder.Find(list, SceneNames.RankRowPrefix + i);

                // Voce monta so a RankRow0; as outras nove saem dela por clone.
                // Assim a arte da linha fica no seu controle e ninguem precisa
                // duplicar 120 objetos na mao.
                if (rowTransform == null && template != null)
                {
                    GameObject clone = Object.Instantiate(template.gameObject, template.parent);
                    clone.name = SceneNames.RankRowPrefix + i;
                    clone.transform.SetSiblingIndex(template.GetSiblingIndex() + i);
                    rowTransform = clone.transform;

                    // Sem um Layout Group na lista, o clone nasce em cima do
                    // original: descemos cada um pelo espacamento configurado.
                    var rect = rowTransform as RectTransform;
                    var templateRect = template as RectTransform;
                    bool managedByLayout = list.GetComponent<UnityEngine.UI.LayoutGroup>() != null;

                    if (rect != null && templateRect != null && !managedByLayout)
                        rect.anchoredPosition = templateRect.anchoredPosition + new Vector2(0f, -rowSpacing * i);
                }

                if (rowTransform == null) continue;

                var binder = new SceneBinder(rowTransform);
                into.Add(new Row
                {
                    go = rowTransform.gameObject,
                    pos = binder.Optional<TMP_Text>(SceneNames.RowPosText),
                    nome = binder.Optional<TMP_Text>(SceneNames.RowNameText),
                    pontos = binder.Optional<TMP_Text>(SceneNames.RowPointsText)
                });
            }
        }

        public bool IsEmpty => forro.Count == 0 && piseiro.Count == 0 && sertanejo.Count == 0;

        public void Show(RankingBoard board)
        {
            Fill(forro, board != null ? board.Forro : null);
            Fill(piseiro, board != null ? board.Piseiro : null);
            Fill(sertanejo, board != null ? board.Sertanejo : null);
        }

        static void Fill(List<Row> rows, List<RankingEntry> entries)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                bool hasEntry = entries != null && i < entries.Count;

                if (row.go != null && row.go.activeSelf != hasEntry) row.go.SetActive(hasEntry);
                if (!hasEntry) continue;

                RankingEntry entry = entries[i];
                if (row.pos != null) row.pos.text = (i + 1) + "º";
                if (row.nome != null) row.nome.text = entry.nome;
                if (row.pontos != null) row.pontos.text = entry.pontos.ToString();
            }
        }

        /// <summary>Mostra todas as linhas com traco — usado quando a rede falha.</summary>
        public void ShowUnavailable()
        {
            foreach (List<Row> column in new[] { forro, piseiro, sertanejo })
            {
                for (int i = 0; i < column.Count; i++)
                {
                    Row row = column[i];
                    if (row.go != null) row.go.SetActive(i == 0);
                    if (i != 0) continue;
                    if (row.pos != null) row.pos.text = "";
                    if (row.nome != null) row.nome.text = "ranking indisponivel";
                    if (row.pontos != null) row.pontos.text = "";
                }
            }
        }
    }
}
