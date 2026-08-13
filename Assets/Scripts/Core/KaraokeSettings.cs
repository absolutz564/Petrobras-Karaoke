using System;
using UnityEngine;

namespace Karaoke.Core
{
    public enum DetectorKind
    {
        Autocorrelation = 0,
        Yin = 1
    }

    /// <summary>
    /// Todos os parametros ajustaveis do jogo em um lugar. Exposto no Inspector
    /// pelo componente KaraokeApp (basta selecionar o GameObject "Karaoke").
    /// </summary>
    [Serializable]
    public class KaraokeSettings
    {
        [Header("Deteccao de pitch")]
        public DetectorKind detector = DetectorKind.Autocorrelation;

        [Tooltip("Amostras analisadas por frame. 2048 @44.1kHz = ~46ms de janela.")]
        public int windowSize = 2048;

        [Tooltip("Decimacao antes da analise (2 = metade da taxa). Reduz o custo de CPU pela metade ao quadrado sem prejudicar voz.")]
        [Range(1, 4)] public int decimation = 2;

        [Tooltip("Faixa de busca. Voz humana cantada fica entre ~75 Hz (baixo) e ~1100 Hz (soprano).")]
        public float minHz = 70f;
        public float maxHz = 1100f;

        [Tooltip("Abaixo deste RMS o frame e tratado como silencio e ignorado na pontuacao.")]
        public float rmsThreshold = 0.012f;

        [Tooltip("Confianca minima do detector (0..1) para aceitar o pitch.")]
        [Range(0f, 1f)] public float clarityThreshold = 0.6f;

        [Tooltip("Tamanho do filtro de mediana aplicado ao pitch (em frames). Remove saltos de oitava esporadicos.")]
        [Range(1, 9)] public int smoothingWindow = 5;

        [Header("Pontuacao")]
        [Tooltip("Erro em semitons ainda considerado 100% certo.")]
        public float perfectSemitones = 0.7f;

        [Tooltip("Erro em semitons a partir do qual o frame vale zero.")]
        public float maxSemitones = 2.5f;

        [Tooltip("Aceita a nota certa em qualquer oitava.")]
        public bool octaveAgnostic = true;

        [Tooltip("Latencia do caminho microfone->analise. Desloca a comparacao no tempo. Ajuste ouvindo/vendo se o acerto 'atrasa'.")]
        public float micLatencySeconds = 0.05f;

        [Header("Multiplicador")]
        [Tooltip("Notas certas seguidas para subir um nivel do multiplicador.")]
        public int notesPerMultiplierStep = 8;

        [Tooltip("Teto do multiplicador (x1 ate este valor).")]
        [Range(1, 10)] public int maxMultiplier = 5;

        /// <summary>
        /// Quanto o multiplicador pesa no placar final. O numero na tela nao
        /// muda (x1..x5); isto controla so o impacto nos pontos.
        ///
        /// Medido com o vocal original das 6 musicas: em 1.0 o proprio cantor
        /// da faixa tirava 46 a 63 pontos percentuais, porque uma unica nota
        /// errada quebra a sequencia e a conta e normalizada por uma sequencia
        /// perfeita. Em 0.5 a punicao por quebra cai pela metade.
        /// </summary>
        [Tooltip("Peso do multiplicador nos pontos. 0 = so enfeite, 1 = punicao maxima por quebrar a sequencia.")]
        [Range(0f, 1f)] public float multiplierWeight = 0.5f;

        [Header("Contagem inicial da partida")]
        [Tooltip("Segundos de 'prepare-se' antes de a musica comecar.")]
        public int readyCountdown = 3;

        [Header("Audio guia")]
        [Tooltip("Sintetiza a melodia em senoides quando a musica nao tem audio proprio.")]
        public bool guideTones = true;
        [Range(0f, 1f)] public float guideVolume = 0.22f;
        [Tooltip("Contagem inicial (cliques de metronomo) antes da primeira nota.")]
        public float countInSeconds = 2f;

        [Header("Visual")]
        [Tooltip("Velocidade de rolagem da pauta, em pixels por segundo (resolucao de referencia 1920x1080).")]
        public float pixelsPerSecond = 220f;
    }
}
