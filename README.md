# Karaokê com pontuação por pitch (Unity 2022.3 LTS)

Sistema completo de karaokê: captura de microfone, detecção de pitch em C#, comparação com uma
curva de referência da melodia e pontuação em tempo real, com menu de 6 músicas, tela de jogo
estilo SingStar e tela de resultado.

**Não há nenhum arquivo de áudio no projeto:** a melodia de cada música é sintetizada em runtime
(senoides + harmônicos + contagem inicial de metrônomo), então tudo é jogável e testável desde o
primeiro Play.

---

## Como rodar

Três opções — todas funcionam:

1. **Cena montada e visível no editor** (recomendado): menu **Karaoke → Montar cena completa**.
   Cria câmera, EventSystem, canvas e as quatro telas como objetos de verdade na Hierarchy — o menu
   já aparece no Game view sem dar Play, e dá para reposicionar/recolorir tudo pelo Inspector.
   Rode de novo depois de mexer no código de UI para atualizar a hierarquia (é idempotente: reaproveita
   o que já existe em vez de duplicar).
2. **Direto:** aperte **Play** em qualquer cena (mesmo vazia). `KaraokeApp` se instala sozinho via
   `RuntimeInitializeOnLoadMethod` e monta tudo em runtime.
3. **Cena nova do zero** (para gerar build): menu **Karaoke → Criar cena principal**. Cria
   `Assets/Scenes/Karaoke.unity` já montada e adiciona ao Build Settings.

> Como isso funciona: os construtores de UI em [UIBuilder.cs](Assets/Scripts/UI/UIBuilder.cs) procuram
> o filho pelo nome antes de criar. O mesmo `KaraokeApp.Compose()` monta a cena no editor e, no Play,
> apenas reencontra os objetos e religa as referências. Os pools pesados (320 blocos do rastro de
> pitch, 140 barras do Pitch Lab) são criados só em runtime, para não inchar a cena salva.

No Windows, se o medidor de volume não se mexer: *Configurações → Privacidade → Microfone* e libere
o acesso para aplicativos de desktop.

### Controles

| Ação | Como |
|---|---|
| Escolher música | clique no card ou tecla `1`–`6` |
| Voltar / sair da música | `ESC` |
| Testar microfone e detector | botão **Testar microfone** (Pitch Lab) |
| Trocar dispositivo de entrada | botão **Trocar microfone** |

---

## Arquitetura

```
Assets/Scripts/
  Core/
    PitchUtils.cs         Hz <-> MIDI, nomes de nota, cents, distância em semitons (com oitava opcional)
    KaraokeSettings.cs    todos os parâmetros ajustáveis (Inspector do GameObject Karaoke)
  Audio/
    MicrophoneCapture.cs  Microphone.Start + leitura da janela mais recente do ring buffer
    IPitchDetector.cs     contrato: janela de samples -> (frequência, clareza, RMS, voiced)
    PitchDetectorBase.cs  gate de silêncio, decimação, remoção de DC, interpolação parabólica
    AutocorrelationPitchDetector.cs   autocorrelação normalizada (NSDF) + escolha de pico anti-oitava
    YinPitchDetector.cs   YIN (CMNDF) — troca sem mexer em mais nada
    PitchTracker.cs       microfone + detector + filtro de mediana; publica pitch em MIDI por frame
    GuideToneSynth.cs     gera o AudioClip da melodia e tons de referência
  Data/
    SongChart.cs          DTO em batidas -> modelo em segundos (notas, letras, linhas)
    SongLibrary.cs        carrega tudo de Resources/Songs (.json ou .txt)
    UltraStarImporter.cs  leitor de arquivos UltraStar (.txt)
  Scoring/
    ScoreEngine.cs        comparação pitch cantado x esperado, acurácia ponderada por tempo
  UI/
    UIBuilder.cs          fábrica de UI por código (nenhum prefab necessário)
    MenuScreen.cs         grade de músicas + medidor de microfone ao vivo
    GameplayScreen.cs     pauta rolante, notas, rastro do pitch, letra, placar
    ResultScreen.cs       nota final, %, sequência, distribuição por nota
    PitchLabScreen.cs     teste isolado: Hz, nota, cents, RMS, clareza, histórico, troca de algoritmo
  App/
    KaraokeApp.cs         bootstrap + máquina de estados (menu / jogo / resultado / lab)

Assets/Editor/
  KaraokeSceneSetup.cs        criar cena, validar músicas
  PitchDetectorSelfTest.cs    testa os detectores com sinais sintéticos (sem microfone)
  ScoringSimulation.cs        simula cantores (perfeito, oitava abaixo, fora do tom, mudo)

Assets/Resources/Songs/       uma pasta por música: JSON do mapa + stem instrumental ("- Geral")
Assets/SongSources/           stems de voz ("- Voz") — só para autoria, FORA de Resources
Assets/SongsArchive/          músicas antigas, fora do menu
```

> `Resources/` entra inteiro no build, usado ou não. Por isso os stems de voz — que só servem para
> extrair melodia e alinhar letra, nunca tocam em jogo — moram em `Assets/SongSources/`, espelhando
> a mesma organização por pasta. Isso tira 57 MB do build.

### Fluxo por frame

```
Microphone (ring buffer)
   -> MicrophoneCapture.ReadLatest(2048 samples)
   -> detector (decimação 2x -> 1024 @ 22.05 kHz) -> frequência + clareza + RMS
   -> PitchTracker: mediana de 5 frames -> pitch em MIDI
   -> ScoreEngine.Feed(tempo da música - latência, dt, midi, voiced)
   -> GameplayScreen: pauta, rastro, letra, placar
```

---

## Como funciona a detecção

Autocorrelação normalizada (NSDF, base do McLeod Pitch Method):

```
nsdf(lag) = 2 * Σ x[i]·x[i+lag] / Σ (x[i]² + x[i+lag]²)
```

- A normalização deixa o resultado em `[-1, 1]` independente do volume — isso já serve como
  medida de confiança (`clarity`), usada para separar voz de ruído.
- Em vez do maior pico (que costuma cair uma oitava abaixo), escolhe-se o **primeiro** pico local
  que atinge 90% do maior — o truque clássico contra erro de oitava.
- Decimação 2x antes da análise: custo 4x menor, e 11 kHz de Nyquist continua muito acima da
  fundamental de qualquer voz.
- Interpolação parabólica no pico dá resolução abaixo de uma amostra.

O YIN (`YinPitchDetector`) está implementado e é intercambiável: mude `detector` em
`KaraokeSettings` ou clique em **Trocar algoritmo** dentro do Pitch Lab.

Medido pelo auto-teste com ondas harmônicas (fundamental + 3 harmônicos), de E2 (82 Hz) a A5 (880 Hz),
com e sem ruído a ~20 dB de SNR: erro **abaixo de 1,5 cent** nos dois algoritmos; silêncio e ruído
branco são rejeitados (clareza ~0,1).

## Como funciona a pontuação

- A acurácia é acumulada em **segundos**, não em frames — o placar não muda entre 30 e 144 fps.
- Erro ≤ `perfectSemitones` (0,7) vale 1,0 e decai linearmente até 0 em `maxSemitones` (2,5).
- Frames de silêncio ou de pitch não confiável **não somam nada** e não punem: a nota simplesmente
  vale menos.
- Notas longas valem mais (peso = duração / duração total da música).
- `octaveAgnostic` (ligado): cantar a melodia uma oitava acima ou abaixo pontua igual — essencial
  quando a referência não está na tessitura do jogador.

**Placar de 0 a 50 pontos:** 45 vêm da afinação e 5 do bônus de melhor sequência.

| Estrelas | Rótulo | Pontos |
|---|---|---|
| 5 | INCRÍVEL! | 45 a 50 |
| 4 | MANDOU BEM | 38 a 44 |
| 3 | FOI BEM | 28 a 37 |
| 2 | QUASE LÁ | 18 a 27 |
| 1 | TENTE OUTRA VEZ | 0 a 17 |

As faixas estão em `ScoreEngine.StarsFor()` / `LabelFor()`, um lugar só.

### Arte das estrelas

A tela final mostra um sprite por faixa. Duas formas de fornecer, na ordem em que são procuradas:

1. **Slots no Inspector** do GameObject `Karaoke`: `umaEstrela` … `cincoEstrelas`.
2. **Convenção em Resources**: `Assets/Resources/UI/estrelas1.png` … `estrelas5.png`.

Sem nenhum dos dois, a tela desenha as estrelas com texto (★★★☆☆) e o rótulo — nunca fica vazia.
Quando o sprite existe, o texto sai de cena, porque a arte já traz estrelas e rótulo desenhados.

Verificado por simulação (menu **Karaoke → Simular pontuação**), nas 6 músicas:

| Desvio do cantor | Pontos | Estrelas |
|---|---|---|
| afinado / uma oitava acima ou abaixo | 49–50 | 5 |
| meio tom (dentro da tolerância) | 49–50 | 5 |
| 1 semitom | 42–43 | 4 |
| 1,5 semitom | 30 | 3 |
| 2 semitons | 12–13 | 1 |
| 3 semitons / mudo | 0 | 1 |

---

## As 6 músicas

| # | Música | Tipo | Âmbito |
|---|---|---|---|
| 1 | Escala Maior | exercício de afinação | Dó4–Dó5 |
| 2 | Notas Longas | exercício de sustentação (3 s por nota) | Dó4–Lá4 |
| 3 | Brilha, Brilha, Estrelinha | tradicional (domínio público) | Dó4–Lá4 |
| 4 | Irmão João (*Frère Jacques*) | tradicional (domínio público) | Sol3–Lá4 |
| 5 | Maria e o Cordeirinho (*Mary Had a Little Lamb*) | tradicional (domínio público) | Dó4–Sol4 |
| 6 | Ode à Alegria (Beethoven) | domínio público, vocalize em "lá" | Dó4–Sol4 |

As **melodias** (alturas e ritmos) são fiéis. A **divisão de sílabas** das letras em português é
aproximada em algumas frases — ela é apenas exibida, não afeta a pontuação. Trocar por versões
melhores é só editar o JSON.

### Adicionar uma música

Solte um arquivo em `Assets/Resources/Songs/`. O menu se adapta à quantidade (a grade é de 3 colunas).

**Formato JSON** (tempos em batidas; 1 batida = 1 semínima):

```json
{
  "id": "minha-musica",
  "title": "Minha Música",
  "artist": "Alguém",
  "difficulty": "Médio",
  "audioResource": "",
  "order": 7,
  "bpm": 120,
  "gap": 1.5,
  "notes": [
    { "beat": 0, "length": 0.9, "midi": 60, "text": "Pri", "line": 0 },
    { "beat": 1, "length": 1.9, "midi": 64, "text": "meira", "line": 0 }
  ]
}
```

- `midi`: 60 = Dó central. `gap`: segundos de espera antes da batida 0. `line`: agrupa as sílabas
  em frases na tela.
- `audioResource`: caminho de um `AudioClip` dentro de uma pasta `Resources` (sem extensão) — por
  exemplo `Audio/minha_musica` para `Assets/Resources/Audio/minha_musica.mp3`. Com áudio próprio,
  o tom guia e a contagem inicial são desligados e `gap` passa a ser o alinhamento com a gravação.

### Extrair a melodia de um áudio automaticamente

Menu **Karaoke → Importar melodia de um áudio**: arraste um `AudioClip`, clique em **Analisar** e,
se o relatório for bom, em **Gerar JSON**. A janela copia o áudio para `Assets/Resources/Audio/`
e escreve o JSON já apontando para ele.

Isso roda o mesmo detector de pitch do jogo sobre o arquivo inteiro e agrupa medições estáveis em
notas ([AudioTranscriber.cs](Assets/Scripts/Data/AudioTranscriber.cs)). O JSON sai com `bpm: 60`
de propósito — assim 1 batida = 1 segundo e dá para editar os tempos direto em segundos.

**Funciona com voz solo, a cappella ou vocal isolado (stem). Não funciona com mixagem completa** —
com bateria, baixo e instrumentos harmônicos tocando junto, o detector segue o que estiver mais
forte a cada instante e o rascunho sai picotado, pulando de registro. A janela avisa quando é esse
o caso: cobertura abaixo de ~55% do tempo ou nota média abaixo de ~0,18 s significa material
inadequado para extração automática.

**Formato UltraStar** (`.txt`): funciona direto, sem conversão — o parser aceita `#TITLE`, `#ARTIST`,
`#BPM`, `#GAP` e linhas `:` / `*` / `-` / `E`, com a convenção do formato (batida = 1/4 e pitch 0 = MIDI 60).
É o caminho para trazer músicas já mapeadas pela comunidade.

---

## Ajustes (Inspector do GameObject `Karaoke`)

| Campo | Padrão | Para quê |
|---|---|---|
| `detector` | Autocorrelation | trocar para `Yin` se quiser mais estabilidade em notas longas |
| `windowSize` | 2048 | maior = mais preciso no grave, mais latência (~46 ms a 44,1 kHz) |
| `decimation` | 2 | custo de CPU; 1 = sem decimação |
| `minHz` / `maxHz` | 70 / 1100 | faixa de busca (voz cantada) |
| `rmsThreshold` | 0,012 | gate de silêncio — aumente em ambiente ruidoso |
| `clarityThreshold` | 0,6 | confiança mínima; aumente se aparecerem notas fantasmas |
| `smoothingWindow` | 5 | mediana em frames; mata saltos de oitava isolados |
| `perfectSemitones` / `maxSemitones` | 0,7 / 2,5 | dificuldade da afinação |
| `octaveAgnostic` | true | aceitar a nota certa em qualquer oitava |
| `micLatencySeconds` | 0,05 | **calibre isto** se o acerto parecer atrasado (ver abaixo) |
| `guideTones` / `guideVolume` | true / 0,22 | melodia sintetizada |
| `countInSeconds` | 2 | contagem inicial |
| `pixelsPerSecond` | 220 | velocidade da pauta |

### Calibrar a latência

Abra o **Pitch Lab**, clique em **Tocar Lá 440** com o volume das caixas alto o suficiente para o
microfone captar: deve aparecer `Lá4` com poucos cents de desvio. Depois, numa música, se você canta
no tempo e o bloco só fica verde no fim da nota, aumente `micLatencySeconds` (0,05 → 0,08).
Com fone de ouvido, valores baixos (0,02–0,04) costumam ser melhores.

---

## Ferramentas de teste (sem entrar em Play)

| Menu | O que faz |
|---|---|
| **Karaoke → Testar detectores (sinais sintéticos)** | roda os dois algoritmos em 9 frequências de E2 a A5, mais ruído e silêncio; critério de 20 cents |
| **Karaoke → Simular pontuação (sem microfone)** | simula 5 tipos de cantor nas 6 músicas a 60 fps |
| **Karaoke → Validar musicas de Resources_Songs** | lista o que foi carregado, com contagem de notas, duração e âmbito |
| **Karaoke → Importar melodia de um áudio** | extrai um rascunho de melodia de um `AudioClip` (veja a seção acima) |
| **Pitch Lab** (em Play) | teste ao vivo do microfone: Hz, nota, cents, RMS, clareza, histórico, troca de algoritmo e de janela |

---

## Limitações conhecidas e próximos passos

- **Sem persistência de recordes** — o placar não é salvo entre sessões.
- **Um jogador por vez** — não há dueto/multiplayer; o `ScoreEngine` já é isolado por instância,
  então dois trackers e dois motores resolveriam.
- **Sem notas douradas / rap / freestyle** do UltraStar (linhas `F` são ignoradas na importação).
- **Sem visual de "pitch bend"**: o rastro é discreto (um quadradinho por frame), não uma curva contínua.
- **A letra é exibida, não sincronizada por caractere** — o destaque é por sílaba (nota).
- **UI em `UnityEngine.UI` legado (`Text`)** e não TextMeshPro, de propósito: TMP exige importar os
  *Essential Resources* antes de o projeto compilar visualmente. Migrar depois é troca localizada
  em `UIBuilder`.
