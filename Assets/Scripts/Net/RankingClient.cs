using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Karaoke.Net
{
    [Serializable]
    public class BackendSettings
    {
        [Tooltip("Raiz da API, sem barra no fim.")]
        public string baseUrl = "https://karaoke-petrobras.dilis.com.br/api";

        [Tooltip("Header x-api-key da Unity.")]
        public string apiKey = "troque-esta-chave-da-unity";

        [Tooltip("Tempo maximo de espera por requisicao, em segundos.")]
        public int timeoutSeconds = 10;

        [Tooltip("Quantas posicoes buscar por estilo no ranking.")]
        public int rankingLimit = 10;
    }

    /// <summary>Uma linha do ranking, como o backend devolve.</summary>
    [Serializable]
    public class RankingEntry
    {
        public int pos;
        public int id;
        public string nome;
        public string musica;
        public int pontos;
        public string origem;
        public string created_at;
    }

    /// <summary>Ranking completo, ja separado por estilo.</summary>
    public class RankingBoard
    {
        public readonly List<RankingEntry> Forro = new List<RankingEntry>();
        public readonly List<RankingEntry> Piseiro = new List<RankingEntry>();
        public readonly List<RankingEntry> Sertanejo = new List<RankingEntry>();

        public List<RankingEntry> ByEstilo(string estilo)
        {
            if (string.IsNullOrEmpty(estilo)) return Forro;
            if (estilo.StartsWith("Pis", StringComparison.OrdinalIgnoreCase)) return Piseiro;
            if (estilo.StartsWith("Ser", StringComparison.OrdinalIgnoreCase)) return Sertanejo;
            return Forro;
        }

        public int Total => Forro.Count + Piseiro.Count + Sertanejo.Count;
    }

    /// <summary>
    /// Conversa com o backend do ranking.
    ///
    ///   POST /scores   (header x-api-key)  -> registra a pontuacao do jogador
    ///   GET  /ranking?limit=N              -> Top N por estilo
    ///
    /// Tudo por corrotina: o jogo nunca trava esperando a rede, e toda chamada
    /// termina no callback com sucesso/erro. Se a rede cair, o jogo continua —
    /// numa ativacao presencial, travar a tela seria pior que perder um registro.
    /// </summary>
    public static class RankingClient
    {
        [Serializable]
        class ScorePayload
        {
            public string nome;
            public string estilo;
            public string musica;
            public int pontos;
        }

        [Serializable]
        class EntryList
        {
            // preenchido por JsonUtility via reflexao
#pragma warning disable 0649
            public RankingEntry[] items;
#pragma warning restore 0649
        }

        /// <summary>Registra uma pontuacao. onDone(sucesso, mensagemDeErro).</summary>
        public static IEnumerator SubmitScore(BackendSettings settings, string nome, string estilo,
                                              string musica, int pontos, Action<bool, string> onDone)
        {
            var payload = new ScorePayload
            {
                nome = Sanitize(nome, 40),
                estilo = estilo,
                musica = musica,
                pontos = Mathf.Clamp(pontos, 0, 100)
            };

            string json = JsonUtility.ToJson(payload);
            byte[] body = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(settings.baseUrl + "/scores", UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-api-key", settings.apiKey);
                request.timeout = Mathf.Max(1, settings.timeoutSeconds);

                yield return request.SendWebRequest();

                bool ok = request.result == UnityWebRequest.Result.Success &&
                          request.responseCode >= 200 && request.responseCode < 300;

                if (!ok)
                {
                    string detail = request.downloadHandler != null ? request.downloadHandler.text : "";
                    string message = "HTTP " + request.responseCode + " " + request.error +
                                     (string.IsNullOrEmpty(detail) ? "" : " | " + detail);
                    Debug.LogWarning("[Karaoke] Falha ao registrar pontuacao: " + message);
                    if (onDone != null) onDone(false, message);
                }
                else
                {
                    Debug.Log("[Karaoke] Pontuacao registrada: " + json);
                    if (onDone != null) onDone(true, null);
                }
            }
        }

        /// <summary>Busca o Top N de cada estilo. onDone(board, mensagemDeErro).</summary>
        public static IEnumerator FetchRanking(BackendSettings settings, Action<RankingBoard, string> onDone)
        {
            string url = settings.baseUrl + "/ranking?limit=" + Mathf.Clamp(settings.rankingLimit, 1, 100);

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("x-api-key", settings.apiKey);
                request.timeout = Mathf.Max(1, settings.timeoutSeconds);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string message = "HTTP " + request.responseCode + " " + request.error;
                    Debug.LogWarning("[Karaoke] Falha ao buscar ranking: " + message);
                    if (onDone != null) onDone(null, message);
                    yield break;
                }

                RankingBoard board;
                try
                {
                    board = Parse(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Karaoke] Ranking veio em formato inesperado: " + e.Message);
                    if (onDone != null) onDone(null, e.Message);
                    yield break;
                }

                if (onDone != null) onDone(board, null);
            }
        }

        /// <summary>
        /// As chaves do JSON sao "Forró", "Piseiro" e "Sertanejo" — com acento,
        /// o que JsonUtility nao consegue mapear para campo de classe. Entao
        /// recortamos o array de cada estilo e deixamos o JsonUtility cuidar so
        /// dos itens, que tem nomes simples.
        /// </summary>
        public static RankingBoard Parse(string json)
        {
            var board = new RankingBoard();
            board.Forro.AddRange(ParseArray(json, "Forró"));
            board.Piseiro.AddRange(ParseArray(json, "Piseiro"));
            board.Sertanejo.AddRange(ParseArray(json, "Sertanejo"));
            return board;
        }

        static RankingEntry[] ParseArray(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return new RankingEntry[0];

            int at = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (at < 0) return new RankingEntry[0];

            int open = json.IndexOf('[', at);
            if (open < 0) return new RankingEntry[0];

            int depth = 0;
            int close = -1;
            for (int i = open; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']')
                {
                    depth--;
                    if (depth == 0) { close = i; break; }
                }
            }
            if (close < 0) return new RankingEntry[0];

            string array = json.Substring(open, close - open + 1);
            EntryList parsed = JsonUtility.FromJson<EntryList>("{\"items\":" + array + "}");
            return parsed != null && parsed.items != null ? parsed.items : new RankingEntry[0];
        }

        static string Sanitize(string value, int maxLength)
        {
            string text = (value ?? "").Trim();
            if (text.Length > maxLength) text = text.Substring(0, maxLength);
            return text;
        }
    }
}
