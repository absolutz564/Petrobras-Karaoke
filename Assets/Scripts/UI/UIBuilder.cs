using System;
using UnityEngine;
using UnityEngine.UI;

namespace Karaoke.UI
{
    public static class Palette
    {
        public static readonly Color Background = new Color(0.055f, 0.05f, 0.11f, 1f);
        public static readonly Color Panel = new Color(0.13f, 0.12f, 0.24f, 0.96f);
        public static readonly Color PanelSoft = new Color(0.20f, 0.18f, 0.34f, 0.9f);
        public static readonly Color Lane = new Color(0.09f, 0.08f, 0.18f, 0.95f);
        public static readonly Color Accent = new Color(1f, 0.34f, 0.62f, 1f);
        public static readonly Color Accent2 = new Color(0.32f, 0.83f, 1f, 1f);
        public static readonly Color Good = new Color(0.34f, 0.94f, 0.55f, 1f);
        public static readonly Color Warn = new Color(1f, 0.80f, 0.30f, 1f);
        public static readonly Color Bad = new Color(1f, 0.42f, 0.38f, 1f);
        public static readonly Color Text = new Color(0.96f, 0.96f, 1f, 1f);
        public static readonly Color TextDim = new Color(0.68f, 0.68f, 0.82f, 1f);
        public static readonly Color NoteIdle = new Color(0.42f, 0.40f, 0.62f, 0.85f);
    }

    /// <summary>
    /// Fabrica de UI por codigo. Todo o visual do jogo e criado em runtime, o que
    /// evita depender de prefabs/cenas versionadas e deixa o projeto rodar em
    /// qualquer cena (inclusive vazia).
    /// </summary>
    public static class UIBuilder // metodos de extensao exigem classe estatica nao-generica
    {
        static Font font;

        /// <summary>
        /// Fonte embutida. Unity 2022+ renomeou a Arial interna para
        /// LegacyRuntime.ttf; caimos para a fonte do sistema se nada existir.
        /// </summary>
        public static Font DefaultFont
        {
            get
            {
                if (font != null) return font;
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (font == null) font = Font.CreateDynamicFontFromOSFont("Segoe UI", 24);
                if (font == null)
                {
                    string[] names = Font.GetOSInstalledFontNames();
                    if (names != null && names.Length > 0) font = Font.CreateDynamicFontFromOSFont(names[0], 24);
                }
                return font;
            }
        }

        /// <summary>
        /// Remove um objeto de UI para valer.
        ///
        /// Destroy() so acontece no fim do frame, e ate la o objeto continua
        /// achavel por Find() — o que faria NewRect reaproveitar justamente o
        /// que esta morrendo. Por isso desfiliamos antes de destruir.
        /// </summary>
        public static void Discard(GameObject go)
        {
            if (go == null) return;
            go.transform.SetParent(null, false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }

        /// <summary>Pega o componente se ja existir, senao adiciona. Evita duplicar ao remontar.</summary>
        public static T Ensure<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        /// <summary>
        /// Cria o filho — ou reaproveita o que ja existe com esse nome.
        ///
        /// E o que permite montar a hierarquia no editor e, no Play, o mesmo
        /// codigo apenas reencontrar os objetos e religar as referencias, em
        /// vez de duplicar tudo.
        /// </summary>
        public static RectTransform NewRect(Transform parent, string name)
        {
            if (parent != null)
            {
                Transform existing = parent.Find(name);
                if (existing != null)
                {
                    var found = existing as RectTransform;
                    if (found == null) found = existing.gameObject.AddComponent<RectTransform>();
                    found.localScale = Vector3.one;
                    return found;
                }
            }

            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 0;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;
            return rt;
        }

        /// <summary>Retangulo colorido. Image sem sprite ja desenha um quad branco tingido pela cor.</summary>
        public static Image NewImage(Transform parent, string name, Color color)
        {
            RectTransform rt = NewRect(parent, name);
            Image img = Ensure<Image>(rt.gameObject);
            img.sprite = null;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text NewText(Transform parent, string name, string content, int size, Color color,
                                   TextAnchor anchor = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            RectTransform rt = NewRect(parent, name);
            Text txt = Ensure<Text>(rt.gameObject);
            txt.font = DefaultFont;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = anchor;
            txt.text = content;
            txt.supportRichText = true;
            txt.raycastTarget = false;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            return txt;
        }

        public static Button NewButton(Transform parent, string name, string label, Color background,
                                       int fontSize, Action onClick)
        {
            Image img = NewImage(parent, name, background);
            img.raycastTarget = true;
            Button btn = Ensure<Button>(img.gameObject);
            btn.targetGraphic = img;

            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            if (!string.IsNullOrEmpty(label))
            {
                Text txt = NewText(img.transform, "Label", label, fontSize, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                Stretch(txt.rectTransform, 10f);
            }

            // listeners de codigo nao sao serializados: ao remontar, religa do zero
            btn.onClick.RemoveAllListeners();
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        /// <summary>Ocupa todo o pai, com uma margem opcional.</summary>
        public static void Stretch(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>Ancora nos dois cantos (fracoes 0..1) com offsets em pixels.</summary>
        public static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        /// <summary>Atalho para ancorar um elemento dentro do pai em fracoes 0..1.</summary>
        public static void Apply(this RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.Apply(min, max, Vector2.zero, Vector2.zero);
        }

        public static void Apply(this RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        /// <summary>Posiciona um elemento de tamanho fixo relativo a uma ancora.</summary>
        public static void SetFixed(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
        }
    }
}
