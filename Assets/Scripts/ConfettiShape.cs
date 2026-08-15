using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Uma peca de confete desenhada na mao.
///
/// Um Image sem sprite so sabe desenhar retangulo — por isso as pecas saiam
/// todas quadradas. Aqui montamos os vertices nos mesmos, entao a fita pode
/// ser um paralelogramo inclinado (papel picado de verdade) e a bolinha pode
/// ser redonda mesmo. Continua sem precisar de nenhuma imagem no projeto.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class ConfettiShape : MaskableGraphic
{
    public enum Kind { Fita, Bolinha }

    public Kind formato = Kind.Fita;

    /// <summary>
    /// Inclinacao da fita. X desliza a aresta de cima na horizontal (em
    /// fracao da altura) e Y levanta a aresta da direita (em fracao da
    /// largura). Com os dois em zero volta a ser um retangulo.
    /// </summary>
    public Vector2 inclinacao = new Vector2(1.2f, 0.2f);

    [Tooltip("Lados da bolinha. 12 ja fica redonda o suficiente no tamanho de um confete.")]
    public int lados = 12;

    /// <summary>Define forma e inclinacao de uma vez, remontando a malha.</summary>
    public void Configure(Kind kind, Vector2 skew)
    {
        formato = kind;
        inclinacao = skew;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0f || r.height <= 0f) return;

        if (formato == Kind.Bolinha) Circle(vh, r);
        else Parallelogram(vh, r);
    }

    void Parallelogram(VertexHelper vh, Rect r)
    {
        // metade do deslocamento para cada lado, para a peca continuar
        // centrada — assim ela gira em torno do proprio meio
        float dx = r.height * inclinacao.x * 0.5f;
        float dy = r.width * inclinacao.y * 0.5f;

        Color32 c = color;
        vh.AddVert(new Vector3(r.xMin - dx, r.yMin - dy), c, new Vector2(0f, 0f));
        vh.AddVert(new Vector3(r.xMin + dx, r.yMax - dy), c, new Vector2(0f, 1f));
        vh.AddVert(new Vector3(r.xMax + dx, r.yMax + dy), c, new Vector2(1f, 1f));
        vh.AddVert(new Vector3(r.xMax - dx, r.yMin + dy), c, new Vector2(1f, 0f));

        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }

    void Circle(VertexHelper vh, Rect r)
    {
        int seg = Mathf.Max(6, lados);
        Vector2 center = r.center;
        float rx = r.width * 0.5f;
        float ry = r.height * 0.5f;
        Color32 c = color;

        vh.AddVert(new Vector3(center.x, center.y), c, new Vector2(0.5f, 0.5f));
        for (int i = 0; i <= seg; i++)
        {
            float a = 2f * Mathf.PI * i / seg;
            vh.AddVert(new Vector3(center.x + Mathf.Cos(a) * rx, center.y + Mathf.Sin(a) * ry),
                       c, new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
        }
        for (int i = 1; i <= seg; i++) vh.AddTriangle(0, i, i + 1);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        lados = Mathf.Max(6, lados);
    }
#endif
}
