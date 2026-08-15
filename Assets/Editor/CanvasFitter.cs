using System.Collections.Generic;
using System.Text;
using Karaoke.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Karaoke.EditorTools
{
    /// <summary>
    /// Adapta a interface inteira a qualquer resolucao sem tocar em elemento
    /// nenhum.
    ///
    /// A arte deste projeto foi montada num espaco unico e centrado: as telas
    /// sao caixas de tamanho fixo (3066,75 x 2044) e 63 dos 70 objetos estao
    /// ancorados em (0.5, 0.5). Isso e uma sorte enorme — significa que a
    /// posicao de cada elemento e um deslocamento a partir do centro, e nao
    /// uma coordenada presa a um canto.
    ///
    /// Entao basta contar esse espaco para o CanvasScaler:
    ///
    ///   - resolucao de referencia = o tamanho das telas (o espaco de desenho);
    ///   - modo Expand = a area de referencia SEMPRE cabe inteira na tela, com
    ///     a sobra indo para as laterais (ou para cima e baixo) conforme a
    ///     proporcao do monitor.
    ///
    /// A arte de fundo de cada tela e esticada para ocupar essa sobra, senao
    /// apareceria uma faixa vazia nas bordas do telao.
    ///
    /// Vale para o telao 16:9, para o notebook e para o Game view em qualquer
    /// proporcao: nada e cortado e nada muda de lugar.
    /// </summary>
    public static class CanvasFitter
    {
        static readonly string[] Screens =
        {
            SceneNames.Menu, SceneNames.MusicSelect, SceneNames.Game,
            SceneNames.EndGame, SceneNames.ThankYou, SceneNames.Ranking
        };

        [MenuItem("Karaoke/Adaptar telas para qualquer resolucao", false, 6)]
        public static void Fit()
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Karaoke] Nao ha Canvas na cena aberta.");
                return;
            }

            List<RectTransform> screens = FindScreens(canvas.transform);
            if (screens.Count == 0)
            {
                Debug.LogError("[Karaoke] Nenhuma tela encontrada no Canvas (Menu, MusicSelect, Game, EndGame, Obrigado, Ranking).");
                return;
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = Undo.AddComponent<CanvasScaler>(canvas.gameObject);

            Vector2 design = DesignSize(screens);

            // Rodar de novo depois de as telas ja estarem esticadas: elas nao
            // tem mais tamanho proprio para medir, entao mantemos a referencia
            // que ja esta configurada em vez de reclamar.
            if (design.x < 1f || design.y < 1f) design = scaler.referenceResolution;

            if (design.x < 1f || design.y < 1f)
            {
                Debug.LogError("[Karaoke] Nao consegui deduzir o espaco de desenho: as telas estao esticadas e o " +
                               "CanvasScaler nao tem resolucao de referencia. Preencha-a na mao com o tamanho da arte.");
                return;
            }

            Undo.RecordObject(scaler, "Adaptar Canvas");
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = design;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            EditorUtility.SetDirty(scaler);

            var report = new StringBuilder();
            report.AppendLine("[Karaoke] Telas adaptadas.");
            report.AppendLine("   Espaco de desenho detectado: " + design.x.ToString("0.##") + " x " + design.y.ToString("0.##") +
                              "  (proporcao " + (design.x / design.y).ToString("0.00") + ":1)");
            report.AppendLine("   CanvasScaler: referencia = esse espaco, modo Expand (a arte inteira sempre cabe).");

            foreach (RectTransform screen in screens)
            {
                Undo.RecordObject(screen, "Adaptar Canvas");
                screen.anchorMin = Vector2.zero;
                screen.anchorMax = Vector2.one;
                screen.offsetMin = Vector2.zero;
                screen.offsetMax = Vector2.zero;
                screen.localScale = Vector3.one;
                EditorUtility.SetDirty(screen);
                report.AppendLine("   " + screen.name + ": fundo esticado para preencher a sobra.");
            }

            report.AppendLine();
            report.AppendLine(Preview(design, 1920f, 1080f));
            Debug.Log(report.ToString());
        }

        [MenuItem("Karaoke/Conferir enquadramento das telas", false, 7)]
        public static void Check()
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null) { Debug.LogError("[Karaoke] Nao ha Canvas na cena aberta."); return; }

            List<RectTransform> screens = FindScreens(canvas.transform);
            var scaler = canvas.GetComponent<CanvasScaler>();
            var sb = new StringBuilder("[Karaoke] Enquadramento\n");

            if (scaler == null) { sb.AppendLine("   SEM CanvasScaler: a interface nao vai acompanhar a resolucao."); }
            else
            {
                sb.AppendLine("   modo: " + scaler.uiScaleMode + " / " + scaler.screenMatchMode);
                sb.AppendLine("   referencia: " + scaler.referenceResolution.x.ToString("0.##") + " x " +
                              scaler.referenceResolution.y.ToString("0.##"));
            }

            // O maior retangulo realmente ocupado pelos elementos, medido a
            // partir do centro: e o que precisa caber na tela.
            float extentX = 0f, extentY = 0f;
            foreach (RectTransform screen in screens)
            {
                foreach (RectTransform child in screen.GetComponentsInChildren<RectTransform>(true))
                {
                    if (child == screen) continue;
                    if (child.anchorMin != child.anchorMax) continue;   // esticado acompanha sozinho
                    Vector2 pos = RelativeToScreenCenter(child, screen);
                    Vector2 half = Vector2.Scale(child.rect.size, child.lossyScale / Mathf.Max(0.0001f, screen.lossyScale.x)) * 0.5f;
                    extentX = Mathf.Max(extentX, Mathf.Abs(pos.x) + Mathf.Abs(half.x));
                    extentY = Mathf.Max(extentY, Mathf.Abs(pos.y) + Mathf.Abs(half.y));
                }
            }

            sb.AppendLine("   area realmente usada: " + (extentX * 2f).ToString("0") + " x " + (extentY * 2f).ToString("0") +
                          "  (proporcao " + (extentX / Mathf.Max(0.001f, extentY)).ToString("0.00") + ":1)");
            if (scaler != null)
                sb.AppendLine(Preview(scaler.referenceResolution, 1920f, 1080f));

            Debug.Log(sb.ToString());
        }

        static Vector2 RelativeToScreenCenter(RectTransform child, RectTransform screen)
        {
            Vector2 sum = Vector2.zero;
            Transform t = child;
            while (t != null && t != screen)
            {
                var rt = t as RectTransform;
                if (rt != null) sum += rt.anchoredPosition;
                t = t.parent;
            }
            return sum;
        }

        /// <summary>Conta em numeros o que acontece na resolucao final.</summary>
        static string Preview(Vector2 design, float screenW, float screenH)
        {
            float scale = Mathf.Min(screenW / design.x, screenH / design.y);
            float canvasW = screenW / scale;
            float canvasH = screenH / scale;

            return "   Em " + screenW.ToString("0") + "x" + screenH.ToString("0") + ": escala " + scale.ToString("0.000") +
                   ", area do canvas " + canvasW.ToString("0") + " x " + canvasH.ToString("0") + ".\n" +
                   "   Sobra de " + ((canvasW - design.x) / 2f).ToString("0") + " unidades de cada lado e " +
                   ((canvasH - design.y) / 2f).ToString("0") + " em cima/embaixo — coberta pelo fundo esticado.";
        }

        static List<RectTransform> FindScreens(Transform canvasRoot)
        {
            var found = new List<RectTransform>();
            foreach (string name in Screens)
            {
                Transform t = SceneBinder.Find(canvasRoot, name);
                if (t is RectTransform rect) found.Add(rect);
            }
            return found;
        }

        /// <summary>
        /// O espaco de desenho e o tamanho que as telas tem hoje. Usamos o
        /// maior: se alguma ja tiver sido esticada na mao, ela viria com o
        /// tamanho do Game view e estragaria a conta.
        /// </summary>
        static Vector2 DesignSize(List<RectTransform> screens)
        {
            var sizes = new Dictionary<Vector2, int>();
            foreach (RectTransform screen in screens)
            {
                Vector2 size = screen.sizeDelta;
                if (size.x < 1f || size.y < 1f) continue;      // ja esticada: nao conta
                sizes.TryGetValue(size, out int count);
                sizes[size] = count + 1;
            }

            Vector2 best = Vector2.zero;
            int bestCount = 0;
            foreach (var pair in sizes)
                if (pair.Value > bestCount) { best = pair.Key; bestCount = pair.Value; }

            return best;
        }
    }
}
