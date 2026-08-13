using UnityEngine;
using UnityEngine.UI;

namespace Karaoke.UI
{
    /// <summary>
    /// Cole este componente em cada botao de musica da tela de selecao.
    ///
    /// O sprite normal e capturado sozinho do proprio Image no Awake — voce so
    /// precisa arrastar a arte do estado SELECIONADO. O id da musica tambem sai
    /// do nome do objeto ("asabrancabutton" -> "asa_branca"), entao so preencha
    /// songId se o nome do botao nao seguir esse padrao.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class SongButtonBinding : MonoBehaviour
    {
        [Tooltip("Arte do botao quando a musica esta escolhida.")]
        public Sprite selectedSprite;

        [Tooltip("Deixe vazio para deduzir do nome do objeto.")]
        public string songId = "";

        Image image;
        Sprite normalSprite;

        public string SongId => songId;

        void Awake()
        {
            image = GetComponent<Image>();
            normalSprite = image.sprite;
            if (string.IsNullOrEmpty(songId)) songId = IdFromName(name);
        }

        public void SetSelected(bool selected)
        {
            if (image == null) image = GetComponent<Image>();
            if (selected && selectedSprite != null) image.sprite = selectedSprite;
            else if (!selected && normalSprite != null) image.sprite = normalSprite;

            // sem sprite de selecao, ao menos escurece/clareia para dar retorno visual
            if (selectedSprite == null) image.color = selected ? Color.white : new Color(0.82f, 0.82f, 0.82f, 1f);
        }

        /// <summary>"asabrancabutton" / "asa_branca_button" -> "asabranca" (comparavel ao id do chart).</summary>
        public static string IdFromName(string value)
        {
            string lower = (value ?? "").ToLowerInvariant();
            if (lower.EndsWith("button")) lower = lower.Substring(0, lower.Length - "button".Length);

            var sb = new System.Text.StringBuilder();
            foreach (char c in lower)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
