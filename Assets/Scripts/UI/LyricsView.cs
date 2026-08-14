using System.Collections.Generic;
using System.Text;
using Karaoke.Data;
using TMPro;
using UnityEngine;

namespace Karaoke.UI
{
    /// <summary>
    /// Letra em janela deslizante de 4 linhas, com a parte ja cantada em
    /// amarelo e o resto em branco.
    ///
    /// O amarelo anda pelo TEMPO da musica usando a lista de silabas do chart,
    /// que tem o instante do ataque de cada uma. Antes ele andava pelo indice
    /// da nota que estava pontuando — mas a nota comeca onde a voz estabiliza,
    /// depois da consoante, e isso atrasava a letra silaba por silaba.
    ///
    /// A janela acompanha a linha atual (duas acima, a atual, uma abaixo) e
    /// rola de uma em uma; blocos fixos de 4 faziam o amarelo saltar do rodape
    /// para o topo ao virar o bloco.
    /// </summary>
    public class LyricsView
    {
        public const int WindowLines = 4;
        const int LinesAbove = 2;

        readonly TMP_Text[] slots;
        readonly StringBuilder builder = new StringBuilder(256);

        string sungColor = "#FFC72C";
        string pendingColor = "#FFFFFF";

        SongChart chart;
        List<int>[] byLine;      // indices de silaba por linha
        int lineCount;
        int renderedTop = int.MinValue;
        int renderedSyllable = int.MinValue;

        public LyricsView(TMP_Text[] slots)
        {
            this.slots = slots;
        }

        public void SetColors(Color sung, Color pending)
        {
            sungColor = "#" + ColorUtility.ToHtmlStringRGB(sung);
            pendingColor = "#" + ColorUtility.ToHtmlStringRGB(pending);
        }

        public bool HasSyllables => chart != null && chart.Syllables.Count > 0;

        public void SetChart(SongChart chart)
        {
            this.chart = chart;
            renderedTop = int.MinValue;
            renderedSyllable = int.MinValue;

            lineCount = Mathf.Max(1, chart != null ? chart.LineCount : 1);
            if (chart != null && chart.LyricLines.Count > lineCount) lineCount = chart.LyricLines.Count;

            byLine = new List<int>[lineCount];
            for (int i = 0; i < lineCount; i++) byLine[i] = new List<int>();

            if (chart != null)
            {
                for (int i = 0; i < chart.Syllables.Count; i++)
                {
                    int line = Mathf.Clamp(chart.Syllables[i].Line, 0, lineCount - 1);
                    byLine[line].Add(i);
                }
            }
            Clear();
        }

        public void Clear()
        {
            foreach (TMP_Text slot in slots)
                if (slot != null) slot.text = "";
        }

        /// <summary>
        /// Liga e desliga as quatro linhas de uma vez.
        ///
        /// Serve para a contagem regressiva: o numero fica no centro da tela,
        /// em cima de onde a letra aparece. Desligamos o objeto inteiro, e nao
        /// so o texto, porque a linha pode ter arte propria por tras.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (slots == null) return;
            foreach (TMP_Text slot in slots)
                if (slot != null && slot.gameObject.activeSelf != visible)
                    slot.gameObject.SetActive(visible);
        }

        /// <summary>Atualiza a letra para o instante da musica.</summary>
        public void Tick(float songTime)
        {
            if (chart == null || slots == null || byLine == null) return;

            int current = LastSyllableAt(songTime);
            int line = current >= 0 ? Mathf.Clamp(chart.Syllables[current].Line, 0, lineCount - 1) : 0;
            int top = Mathf.Clamp(line - LinesAbove, 0, Mathf.Max(0, lineCount - WindowLines));

            if (top == renderedTop && current == renderedSyllable) return;
            renderedTop = top;
            renderedSyllable = current;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                int index = top + i;
                slots[i].text = index < lineCount ? Render(index, current) : "";
            }
        }

        /// <summary>Ultima silaba cujo ataque ja passou. Busca linear a partir do ultimo resultado.</summary>
        int LastSyllableAt(float time)
        {
            List<LyricSyllable> syllables = chart.Syllables;
            int found = -1;

            int start = renderedSyllable >= 0 && renderedSyllable < syllables.Count &&
                        syllables[renderedSyllable].StartTime <= time ? renderedSyllable : 0;

            for (int i = start; i < syllables.Count; i++)
            {
                if (syllables[i].StartTime > time) break;
                found = i;
            }
            return found;
        }

        string Render(int line, int current)
        {
            string full = FullText(line);
            if (string.IsNullOrEmpty(full)) return "";

            int sung = Mathf.Clamp(SungLength(line, current), 0, full.Length);

            builder.Length = 0;
            if (sung > 0)
            {
                builder.Append("<color=").Append(sungColor).Append('>');
                builder.Append(full, 0, sung);
                builder.Append("</color>");
            }
            if (sung < full.Length)
            {
                builder.Append("<color=").Append(pendingColor).Append('>');
                builder.Append(full, sung, full.Length - sung);
                builder.Append("</color>");
            }
            return builder.ToString();
        }

        /// <summary>Linha completa da letra; sem ela, soma as silabas da linha.</summary>
        string FullText(int line)
        {
            if (chart.LyricLines != null && line < chart.LyricLines.Count)
            {
                string text = chart.LyricLines[line];
                if (!string.IsNullOrEmpty(text)) return text;
            }

            var sb = new StringBuilder();
            foreach (int index in byLine[line]) sb.Append(chart.Syllables[index].Text);
            return sb.ToString().TrimEnd();
        }

        int SungLength(int line, int current)
        {
            int length = 0;
            foreach (int index in byLine[line])
            {
                if (index > current) break;
                length += chart.Syllables[index].Text.Length;
            }
            return length;
        }
    }
}
