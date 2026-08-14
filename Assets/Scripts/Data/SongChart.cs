using System;
using System.Collections.Generic;
using UnityEngine;

namespace Karaoke.Data
{
    // ---------- DTOs (formato de arquivo, lido com JsonUtility) ----------

    [Serializable]
    public class SongNoteDto
    {
        /// <summary>Inicio em batidas (1 batida = 1 semínima).</summary>
        public float beat;
        /// <summary>Duracao em batidas.</summary>
        public float length = 1f;
        /// <summary>Numero MIDI (60 = Do central).</summary>
        public int midi = 60;
        /// <summary>Silaba exibida.</summary>
        public string text = "";
        /// <summary>Indice da linha da letra (para quebrar as frases na tela).</summary>
        public int line = 0;
    }

    /// <summary>
    /// Uma silaba da letra com seu proprio instante.
    ///
    /// Separada das notas de proposito: a NOTA precisa comecar onde a voz esta
    /// estavel (senao a pontuacao despenca), enquanto a SILABA precisa comecar
    /// no ataque, que cai na consoante. Tentar servir aos dois com o mesmo
    /// objeto estragava um ou outro.
    /// </summary>
    [Serializable]
    public class SyllableDto
    {
        public float time;
        public string text = "";
        public int line = 0;
    }

    [Serializable]
    public class SongChartDto
    {
        public string id = "";
        public string title = "";
        public string artist = "";
        /// <summary>Forró, Piseiro ou Sertanejo — o backend so aceita esses tres.</summary>
        public string estilo = "";
        /// <summary>Nome exato da musica no backend ("Asa Branca - Luiz Gonzaga").</summary>
        public string rotulo = "";
        public string difficulty = "";
        public string credits = "";
        /// <summary>Caminho opcional de um AudioClip dentro de Resources/ (sem extensao).</summary>
        public string audioResource = "";
        public int order = 100;
        public float bpm = 100f;
        /// <summary>Segundos de espera antes da batida 0.</summary>
        public float gap = 0f;
        /// <summary>Texto completo de cada linha, indexado por line.</summary>
        public string[] lyricLines;
        /// <summary>Letra silaba a silaba, com o instante do ataque de cada uma.</summary>
        public SyllableDto[] syllables;
        public SongNoteDto[] notes;
    }

    // ---------- Modelo em runtime (tudo em segundos) ----------

    public class LyricSyllable
    {
        public float StartTime;
        public string Text;
        public int Line;
    }

    public class SongNote
    {
        public float StartTime;
        public float Duration;
        public float Midi;
        public string Text;
        public int Line;

        public float EndTime => StartTime + Duration;
        public bool Contains(float t) => t >= StartTime && t <= EndTime;
    }

    public class SongChart
    {
        public string Id;
        public string Title;
        public string Artist;
        /// <summary>Estilo usado no ranking: Forró, Piseiro ou Sertanejo.</summary>
        public string Estilo;
        /// <summary>Rotulo exato esperado pelo backend no campo "musica".</summary>
        public string Rotulo;
        public string Difficulty;
        public string Credits;
        public string AudioResource;
        public int Order;
        public float Bpm;
        public float Gap;
        public List<SongNote> Notes = new List<SongNote>();

        /// <summary>
        /// Texto completo de cada linha da letra. Existe porque o trecho de
        /// audio pode acabar no meio de uma frase: as notas so cobrem o que foi
        /// cantado, mas a tela mostra a linha inteira e pinta so o que passou.
        /// </summary>
        public List<string> LyricLines = new List<string>();

        /// <summary>Letra silaba a silaba, ordenada por tempo. Vazia = letra pelas notas.</summary>
        public List<LyricSyllable> Syllables = new List<LyricSyllable>();

        public float EndTime { get; private set; }
        public float MinMidi { get; private set; }
        public float MaxMidi { get; private set; }
        /// <summary>Soma da duracao de todas as notas — base para distribuir os pontos.</summary>
        public float SungDuration { get; private set; }
        public int LineCount { get; private set; }

        public float SecondsPerBeat => 60f / Mathf.Max(1f, Bpm);

        /// <summary>
        /// Aceita JSON (formato deste projeto) ou UltraStar .txt — decidido pelo
        /// primeiro caractere. Assim basta jogar o arquivo em Resources/Songs.
        /// </summary>
        public static SongChart Parse(string content, string sourceName = "")
        {
            if (string.IsNullOrEmpty(content)) return null;
            string trimmed = content.TrimStart();
            if (trimmed.StartsWith("{")) return FromJson(content, sourceName);
            return UltraStarImporter.Parse(content, sourceName);
        }

        public static SongChart FromJson(string json, string sourceName = "")
        {
            SongChartDto dto = JsonUtility.FromJson<SongChartDto>(json);
            return FromDto(dto, sourceName);
        }

        public static SongChart FromDto(SongChartDto dto, string sourceName = "")
        {
            if (dto == null || dto.notes == null || dto.notes.Length == 0)
            {
                Debug.LogWarning("[Karaoke] Chart sem notas: " + sourceName);
                return null;
            }

            var chart = new SongChart
            {
                Id = string.IsNullOrEmpty(dto.id) ? sourceName : dto.id,
                Title = string.IsNullOrEmpty(dto.title) ? sourceName : dto.title,
                Artist = dto.artist,
                Estilo = dto.estilo,
                // sem rotulo explicito, monta "Titulo - Artista" como o backend espera
                Rotulo = string.IsNullOrEmpty(dto.rotulo)
                    ? (dto.title + (string.IsNullOrEmpty(dto.artist) ? "" : " - " + dto.artist))
                    : dto.rotulo,
                Difficulty = dto.difficulty,
                Credits = dto.credits,
                AudioResource = dto.audioResource,
                Order = dto.order,
                Bpm = dto.bpm <= 0f ? 100f : dto.bpm,
                Gap = dto.gap
            };

            float spb = chart.SecondsPerBeat;
            foreach (var n in dto.notes)
            {
                if (n == null || n.length <= 0f) continue;
                chart.Notes.Add(new SongNote
                {
                    StartTime = chart.Gap + n.beat * spb,
                    Duration = n.length * spb,
                    Midi = n.midi,
                    Text = n.text ?? "",
                    Line = n.line
                });
            }

            if (dto.lyricLines != null) chart.LyricLines.AddRange(dto.lyricLines);

            if (dto.syllables != null)
            {
                foreach (SyllableDto s in dto.syllables)
                {
                    if (s == null) continue;
                    chart.Syllables.Add(new LyricSyllable { StartTime = s.time, Text = s.text ?? "", Line = s.line });
                }
                chart.Syllables.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            }

            if (chart.Notes.Count == 0) return null;
            chart.Notes.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            chart.Recalculate();
            chart.DeriveLyricsFromNotes();
            return chart;
        }

        /// <summary>
        /// Formatos antigos (e os arquivos UltraStar) trazem a letra escrita na
        /// propria nota, sem lista de silabas. Aqui a lista e montada a partir
        /// delas, para essas musicas tambem terem letra na tela.
        /// </summary>
        public void DeriveLyricsFromNotes()
        {
            if (Syllables.Count > 0) return;

            bool hasText = false;
            foreach (SongNote note in Notes)
                if (!string.IsNullOrEmpty(note.Text) && note.Text.Trim().Length > 0) { hasText = true; break; }
            if (!hasText) return;

            foreach (SongNote note in Notes)
            {
                if (string.IsNullOrEmpty(note.Text)) continue;
                Syllables.Add(new LyricSyllable { StartTime = note.StartTime, Text = note.Text, Line = note.Line });
            }

            if (LyricLines.Count > 0) return;
            for (int line = 0; line < LineCount; line++) LyricLines.Add(LineText(line).TrimEnd());
        }

        public void Recalculate()
        {
            EndTime = 0f;
            SungDuration = 0f;
            MinMidi = float.MaxValue;
            MaxMidi = float.MinValue;
            int maxLine = 0;
            foreach (var n in Notes)
            {
                EndTime = Mathf.Max(EndTime, n.EndTime);
                SungDuration += n.Duration;
                MinMidi = Mathf.Min(MinMidi, n.Midi);
                MaxMidi = Mathf.Max(MaxMidi, n.Midi);
                maxLine = Mathf.Max(maxLine, n.Line);
            }
            LineCount = maxLine + 1;
        }

        /// <summary>Indice da nota ativa em t, ou -1. Busca linear a partir de hint (uso por frame).</summary>
        public int NoteAt(float t, int hint = 0)
        {
            for (int i = Mathf.Max(0, hint); i < Notes.Count; i++)
            {
                if (Notes[i].EndTime < t) continue;
                return Notes[i].StartTime <= t ? i : -1;
            }
            return -1;
        }

        public string LineText(int line)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var n in Notes)
                if (n.Line == line) sb.Append(n.Text);
            return sb.ToString();
        }
    }
}
