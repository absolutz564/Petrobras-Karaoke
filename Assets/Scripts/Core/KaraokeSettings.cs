using System;
using UnityEngine;

namespace Karaoke.Core
{
    public enum DetectorKind
    {
        Autocorrelation = 0,
        Yin = 1
    }

    public enum Dificuldade
    {
        /// <summary>Para testar afinacao a serio. Exige a nota quase cheia e no tom.</summary>
        Rigida = 0,
        /// <summary>Meio termo: perdoa respiracao e um desvio de tom pequeno.</summary>
        Equilibrada = 1,
        /// <summary>Ativacao com publico: quem canta junto tira 4 ou 5 estrelas.</summary>
        Festa = 2,
        /// <summary>
        /// Criancas e quem nunca cantou: um degrau mais facil que Festa. Cada
        /// perfil de cantor sobe uma estrela, e mesmo bem desafinado se tira 2 —
        /// mas ainda diferencia, entao continua valendo a pena cantar melhor.
        /// </summary>
        Infantil = 4,
        /// <summary>Usa os valores digitados abaixo, sem sobrescrever.</summary>
        Personalizada = 3
    }

    /// <summary>
    /// Todos os parametros ajustaveis do jogo em um lugar. Exposto no Inspector
    /// pelo componente KaraokeApp (basta selecionar o GameObject "Karaoke").
    /// </summary>
    [Serializable]
    public class KaraokeSettings
    {
        /// <summary>
        /// Calibrado com cantores simulados (desvio de tom, cobertura da nota e
        /// atraso de entrada) contra as 6 musicas. Em "Festa": quem canta bem
        /// tira 5 estrelas, mediano 3, fraco 2, desafinado 1 — todas as faixas
        /// acontecem, que e o que faz o jogo valer numa ativacao.
        /// </summary>
        [Header("Dificuldade")]
        [Tooltip("Escolha o perfil; os numeros abaixo sao preenchidos por ele. Use Personalizada para mexer na mao.")]
        public Dificuldade dificuldade = Dificuldade.Festa;

        /// <summary>Aplica o perfil escolhido. Chamado no inicio do jogo.</summary>
        public void ApplyDifficulty()
        {
            switch (dificuldade)
            {
                case Dificuldade.Rigida:
                    perfectSemitones = 0.7f; maxSemitones = 2.5f;
                    fullCreditCoverage = 0.90f; multiplierWeight = 0.25f; notesPerMultiplierStep = 8;
                    break;
                case Dificuldade.Equilibrada:
                    perfectSemitones = 1.5f; maxSemitones = 4.0f;
                    fullCreditCoverage = 0.75f; multiplierWeight = 0.10f; notesPerMultiplierStep = 6;
                    break;
                case Dificuldade.Festa:
                    perfectSemitones = 2.5f; maxSemitones = 6.0f;
                    fullCreditCoverage = 0.60f; multiplierWeight = 0.05f; notesPerMultiplierStep = 4;
                    break;
                case Dificuldade.Infantil:
                    perfectSemitones = 3.0f; maxSemitones = 7.0f;
                    fullCreditCoverage = 0.50f; multiplierWeight = 0f; notesPerMultiplierStep = 3;
                    break;
            }
        }

        [Header("Deteccao de pitch")]
        public DetectorKind detector = DetectorKind.Autocorrelation;

        [Tooltip("Amostras analisadas por frame. 2048 @44.1kHz = ~46ms de janela.")]
        public int windowSize = 2048;

        [Tooltip("Decimacao antes da analise (2 = metade da taxa). Reduz o custo de CPU pela metade ao quadrado sem prejudicar voz.")]
        [Range(1, 4)] public int decimation = 2;

        [Tooltip("Faixa de busca. Voz humana cantada fica entre ~75 Hz (baixo) e ~1100 Hz (soprano).")]
        public float minHz = 70f;
        public float maxHz = 1100f;

        [Tooltip("Abaixo deste RMS o frame e tratado como silencio e ignorado na pontuacao. Com o gate automatico ligado, este valor vira o TETO do gate.")]
        public float rmsThreshold = 0.012f;

        /// <summary>
        /// O gate fixo era o motivo de microfone fraco nao pontuar: um headset
        /// encostado na boca passa de 0,012 de RMS com folga, mas o microfone
        /// embutido do notebook, ou um microfone de mao com ganho baixo, fica
        /// abaixo disso e a voz inteira era descartada como silencio.
        ///
        /// Ligado, o gate mede o ruido de fundo da sala e desce ate 4x acima
        /// dele — nunca sobe acima do rmsThreshold, entao em sala barulhenta o
        /// comportamento continua o de antes.
        /// </summary>
        [Tooltip("Abaixa o gate sozinho quando a sala esta silenciosa. Deixe ligado: microfone fraco nao passa no gate fixo.")]
        public bool gateAutomatico = true;

        [Tooltip("Multiplica o sinal do microfone antes da analise. Suba se o microfone for fraco (notebook, mic de mesa).")]
        [Range(1f, 20f)] public float ganhoDoMicrofone = 1f;

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

        /// <summary>
        /// Fracao da nota que basta cantar para ganhar credito total.
        ///
        /// Ninguem emite som na duracao inteira de uma nota: consoante,
        /// respiracao e ataque comem uma parte. Exigir 100% fazia um cantor bom
        /// tirar 0,76 de acuracia onde deveria tirar 1,0 — e, como isso quebra
        /// a sequencia, o efeito no placar era muito maior do que parece.
        /// </summary>
        [Tooltip("Cantar esta fracao da nota ja vale credito total. 0.75 = 3/4 da nota basta.")]
        [Range(0.4f, 1f)] public float fullCreditCoverage = 0.75f;

        /// <summary>
        /// Atraso entre cantar e o jogo enxergar o pitch: buffer do microfone,
        /// janela de analise de 46 ms e o filtro de mediana somam bem mais que
        /// os 50 ms que eu supunha antes. Errar isso faz um cantor afinado
        /// perder metade da duracao de cada nota.
        ///
        /// O valor certo varia por equipamento — o jogo mede sozinho no fim de
        /// cada musica e escreve a recomendacao no Console.
        /// </summary>
        [Tooltip("Atraso do caminho microfone->analise, em segundos. O Console recomenda o valor medido ao fim de cada musica.")]
        [Range(0f, 0.4f)] public float micLatencySeconds = 0.15f;

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
        [Range(0f, 1f)] public float multiplierWeight = 0.25f;

        [Header("Contagem inicial da partida")]
        [Tooltip("Segundos de 'prepare-se' antes de a musica comecar.")]
        public int readyCountdown = 10;

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
