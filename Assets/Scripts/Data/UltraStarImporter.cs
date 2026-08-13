using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Karaoke.Data
{
    /// <summary>
    /// Leitor de arquivos UltraStar (.txt) — o formato usado pela comunidade de
    /// karaoke, com milhares de musicas ja mapeadas.
    ///
    /// Cabecalho:  #TITLE:, #ARTIST:, #BPM:, #GAP: (ms), #MP3:
    /// Notas:      : startBeat length pitch texto      (nota normal)
    ///             * startBeat length pitch texto      (golden note)
    ///             F startBeat length pitch texto      (freestyle, sem pitch)
    ///             -  ...                              (quebra de linha)
    ///             E                                   (fim)
    ///
    /// Convencoes do formato: a batida do UltraStar equivale a 1/4 de batida
    /// musical (segundos = beat * 60 / (BPM * 4)) e o pitch e relativo, com
    /// 0 = Do central (MIDI 60).
    /// </summary>
    public static class UltraStarImporter
    {
        public const int PitchOffset = 60;

        public static SongChart Parse(string content, string sourceName = "")
        {
            if (string.IsNullOrEmpty(content)) return null;

            var dto = new SongChartDto { id = sourceName, title = sourceName, bpm = 100f };
            var notes = new List<SongNoteDto>();
            int line = 0;
            float ultraStarBpm = 0f;
            float gapMs = 0f;

            string[] rows = content.Split('\n');
            foreach (string raw in rows)
            {
                string row = raw.Trim('\r', ' ', '\t');
                if (row.Length == 0) continue;

                if (row[0] == '#')
                {
                    int sep = row.IndexOf(':');
                    if (sep <= 0) continue;
                    string key = row.Substring(1, sep - 1).Trim().ToUpperInvariant();
                    string value = row.Substring(sep + 1).Trim();

                    switch (key)
                    {
                        case "TITLE": dto.title = value; dto.id = value; break;
                        case "ARTIST": dto.artist = value; break;
                        case "CREATOR": dto.credits = value; break;
                        case "BPM": ultraStarBpm = ParseFloat(value); break;
                        case "GAP": gapMs = ParseFloat(value); break;
                    }
                    continue;
                }

                if (row[0] == 'E') break;

                if (row[0] == '-')
                {
                    line++;
                    continue;
                }

                if (row[0] != ':' && row[0] != '*' && row[0] != 'F') continue;

                // "<tipo> <beat> <length> <pitch> <texto...>"
                string body = row.Substring(1).TrimStart();
                string[] parts = body.Split(new[] { ' ' }, 4);
                if (parts.Length < 3) continue;

                float beat = ParseFloat(parts[0]);
                float length = ParseFloat(parts[1]);
                float pitch = ParseFloat(parts[2]);
                string text = parts.Length > 3 ? parts[3] : "";
                if (row[0] == 'F') continue; // freestyle nao tem pitch para pontuar

                notes.Add(new SongNoteDto
                {
                    beat = beat,
                    length = length,
                    midi = Mathf.RoundToInt(pitch) + PitchOffset,
                    text = text,
                    line = line
                });
            }

            if (notes.Count == 0)
            {
                Debug.LogWarning("[Karaoke] UltraStar sem notas: " + sourceName);
                return null;
            }

            if (ultraStarBpm <= 0f) ultraStarBpm = 100f;

            // Nosso DTO usa batidas = seminimas; UltraStar usa 1/4 disso.
            dto.bpm = ultraStarBpm * 4f;
            dto.gap = gapMs / 1000f;
            dto.notes = notes.ToArray();
            dto.difficulty = "Importada";
            return SongChart.FromDto(dto, sourceName);
        }

        static float ParseFloat(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            s = s.Replace(',', '.');
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }
    }
}
