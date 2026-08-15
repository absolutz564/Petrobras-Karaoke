using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ConfettiSystem : MonoBehaviour
{
    [Tooltip("Se ligado, o efeito de confete começa automaticamente quando o componente é ativado.")]
    public bool playOnAwake = false;

    public int confettiCount = 80;
    public float fallSpeedMin = 280f;
    public float fallSpeedMax = 560f;

    [Header("Tamanho")]
    /// <summary>
    /// Unico numero que da para mexer com o jogo rodando: as pecas mudam de
    /// tamanho na hora, sem precisar recomecar o efeito. Os tamanhos base
    /// abaixo so valem quando as pecas nascem.
    /// </summary>
    [Tooltip("Multiplica o tamanho de todas as peças. Pode arrastar durante o Play: muda na hora.")]
    [Range(0.2f, 8f)] public float escala = 2.5f;

    [Tooltip("Diâmetro das peças redondas, em pixels. X = mínimo, Y = máximo.")]
    public Vector2 tamanhoBolinha = new Vector2(7f, 13f);

    [Tooltip("Largura das peças compridas, em pixels. X = mínimo, Y = máximo.")]
    public Vector2 larguraFita = new Vector2(14f, 24f);

    [Tooltip("Altura das peças compridas, em pixels. X = mínimo, Y = máximo.")]
    public Vector2 alturaFita = new Vector2(5f, 11f);

    [Header("Formato")]
    [Tooltip("Quanto a fita é inclinada. 0 = retângulo reto; 1,2 = papel picado bem torto.")]
    [Range(0f, 2.5f)] public float inclinacaoMaxima = 1.2f;

    [Tooltip("Fração das peças que sai redonda em vez de fita.")]
    [Range(0f, 1f)] public float proporcaoDeBolinhas = 0.30f;

    private struct PieceData
    {
        public RectTransform rt;
        public float speed;
        public float wobbleOffset;
        public float rotSpeed;
        /// <summary>Tamanho sorteado, antes da escala. Guardado para a escala poder mudar em tempo real.</summary>
        public Vector2 baseSize;
    }

    private List<PieceData> pieces = new List<PieceData>();
    private bool active = false;
    private RectTransform canvasRect;
    private float escalaAplicada = -1f;

    private Color[] colors = {
        new Color(0.95f, 0.18f, 0.18f),
        new Color(0.20f, 0.50f, 1.00f),
        new Color(0.10f, 0.78f, 0.30f),
        new Color(1.00f, 0.85f, 0.00f),
        new Color(1.00f, 0.48f, 0.00f),
        new Color(0.78f, 0.20f, 0.80f),
    };

    void Awake()
    {
        Canvas c = GetComponentInParent<Canvas>();
        if (c) canvasRect = c.GetComponent<RectTransform>();
    }

    void OnEnable()
    {
        if (playOnAwake)
            PlayConfetti();
    }

    void OnDisable()
    {
        active = false;
        StopAllCoroutines();
        ClearPieces();
    }

    public void PlayConfetti()
    {
        StopAllCoroutines();
        ClearPieces();
        active = true;
        SpawnPieces();
        StartCoroutine(AnimateLoop());
    }

    public void StopConfetti()
    {
        active = false;
        StopAllCoroutines();
        ClearPieces();
    }

    private void SpawnPieces()
    {
        float canvasW = canvasRect != null ? canvasRect.rect.width  : 1080f;
        float canvasH = canvasRect != null ? canvasRect.rect.height : 1920f;

        for (int i = 0; i < confettiCount; i++)
        {
            GameObject go = new GameObject($"C_{i}");
            go.transform.SetParent(transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            ConfettiShape shape = go.AddComponent<ConfettiShape>();
            shape.color = colors[i % colors.Length];
            shape.raycastTarget = false;

            bool isDot = Random.value < proporcaoDeBolinhas;
            Vector2 baseSize;
            if (isDot)
            {
                float d = Random.Range(tamanhoBolinha.x, tamanhoBolinha.y);
                baseSize = new Vector2(d, d);
                shape.Configure(ConfettiShape.Kind.Bolinha, Vector2.zero);
            }
            else
            {
                baseSize = new Vector2(Random.Range(larguraFita.x, larguraFita.y),
                                       Random.Range(alturaFita.x, alturaFita.y));

                // cada fita torta para um lado e num grau diferente: e o que
                // faz o monte parecer papel picado e nao um monte de tijolos
                shape.Configure(ConfettiShape.Kind.Fita,
                                new Vector2(Random.Range(inclinacaoMaxima * 0.4f, inclinacaoMaxima) *
                                            (Random.value < 0.5f ? 1f : -1f),
                                            Random.Range(-0.25f, 0.25f)));
            }
            rt.sizeDelta = baseSize * escala;

            rt.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));

            float startY = Random.Range(-canvasH * 0.3f, canvasH * 0.6f);
            rt.anchoredPosition = new Vector2(
                Random.Range(-canvasW / 2f, canvasW / 2f),
                startY
            );

            pieces.Add(new PieceData
            {
                rt           = rt,
                speed        = Random.Range(fallSpeedMin, fallSpeedMax),
                wobbleOffset = Random.Range(0f, Mathf.PI * 2f),
                rotSpeed     = Random.Range(80f, 220f) * (Random.value < 0.5f ? 1f : -1f),
                baseSize     = baseSize,
            });
        }

        escalaAplicada = escala;
    }

    /// <summary>Reaplica a escala em todas as pecas ja existentes.</summary>
    private void ApplyScale()
    {
        escalaAplicada = escala;
        foreach (var p in pieces)
            if (p.rt != null) p.rt.sizeDelta = p.baseSize * escala;
    }

    private IEnumerator AnimateLoop()
    {
        float elapsed = 0f;
        float canvasW = canvasRect != null ? canvasRect.rect.width  : 1080f;
        float canvasH = canvasRect != null ? canvasRect.rect.height : 1920f;

        while (active)
        {
            float dt = Time.deltaTime;
            elapsed += dt;

            // Mexeu na escala no Inspector com o jogo rodando: aplica na hora,
            // sem refazer as pecas. E assim que da para achar o tamanho certo.
            if (escala != escalaAplicada) ApplyScale();

            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (p.rt == null) continue;

                Vector2 pos = p.rt.anchoredPosition;
                pos.y -= p.speed * dt;
                pos.x += Mathf.Sin(elapsed * 1.8f + p.wobbleOffset) * 22f * dt;
                p.rt.anchoredPosition = pos;
                p.rt.Rotate(0f, 0f, p.rotSpeed * dt);

                if (pos.y < -canvasH / 2f - 40f)
                {
                    // so o balanco e sorteado de novo; o resto da peca (tamanho
                    // inclusive) continua o mesmo
                    p.wobbleOffset = Random.Range(0f, Mathf.PI * 2f);
                    pieces[i] = p;
                    p.rt.anchoredPosition = new Vector2(
                        Random.Range(-canvasW / 2f, canvasW / 2f),
                        canvasH / 2f + Random.Range(0f, 60f)
                    );
                }
            }

            yield return null;
        }
    }

    private void ClearPieces()
    {
        foreach (var p in pieces)
            if (p.rt != null) Destroy(p.rt.gameObject);
        pieces.Clear();
    }
}
