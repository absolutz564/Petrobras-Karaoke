using System.IO;
using Karaoke.Core;
using Karaoke.Data;
using UnityEditor;
using UnityEngine;

namespace Karaoke.EditorTools
{
    /// <summary>
    /// Gera um rascunho de melodia (JSON) a partir de um AudioClip, rodando o
    /// mesmo detector de pitch do jogo sobre o arquivo inteiro.
    ///
    /// Funciona bem com voz solo / a cappella / vocal isolado.
    /// Funciona MAL com mixagem completa: o detector segue o instrumento mais
    /// forte de cada instante, entao o rascunho sai picotado e pulando de
    /// registro. O relatorio abaixo do botao Analisar diz exatamente isso —
    /// cobertura baixa (menos de ~60%) ou notas medias muito curtas (menos de
    /// ~0,25s) significam que o material nao serve para extracao automatica.
    ///
    /// Menu: Karaoke > Importar melodia de um audio
    /// </summary>
    public class AudioChartImporterWindow : EditorWindow
    {
        AudioClip clip;
        string songId = "";
        string songTitle = "";
        string artist = "";
        bool copyToResources = true;

        readonly TranscriptionOptions options = new TranscriptionOptions();
        TranscriptionReport report;
        SongChartDto preview;
        Vector2 scroll;

        [MenuItem("Karaoke/Importar melodia de um audio", false, 50)]
        public static void Open()
        {
            var window = GetWindow<AudioChartImporterWindow>(true, "Importar melodia de um audio");
            window.minSize = new Vector2(460f, 620f);
            window.Show();
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.HelpBox(
                "Extrai a melodia de um audio e gera o JSON da musica.\n\n" +
                "Use voz solo, a cappella ou vocal isolado. Em mixagem completa " +
                "(bateria, baixo, sanfona, guitarra) o resultado nao presta: o " +
                "detector segue o instrumento mais forte a cada instante.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            clip = (AudioClip)EditorGUILayout.ObjectField("Audio", clip, typeof(AudioClip), false);
            if (EditorGUI.EndChangeCheck() && clip != null)
            {
                if (string.IsNullOrEmpty(songId)) songId = Sanitize(clip.name);
                if (string.IsNullOrEmpty(songTitle)) songTitle = clip.name;
                report = null;
                preview = null;
            }

            songId = EditorGUILayout.TextField("Id", songId);
            songTitle = EditorGUILayout.TextField("Titulo", songTitle);
            artist = EditorGUILayout.TextField("Artista", artist);
            copyToResources = EditorGUILayout.Toggle(
                new GUIContent("Copiar audio p/ Resources", "O jogo so consegue carregar audio que esteja dentro de uma pasta Resources."),
                copyToResources);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Analise", EditorStyles.boldLabel);
            options.detector = (DetectorKind)EditorGUILayout.EnumPopup("Algoritmo", options.detector);
            options.clarityThreshold = EditorGUILayout.Slider("Confianca minima", options.clarityThreshold, 0.2f, 0.95f);
            options.rmsThreshold = EditorGUILayout.Slider("Gate de silencio (RMS)", options.rmsThreshold, 0.001f, 0.1f);
            options.minHz = EditorGUILayout.FloatField("Freq. minima (Hz)", options.minHz);
            options.maxHz = EditorGUILayout.FloatField("Freq. maxima (Hz)", options.maxHz);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Notas", EditorStyles.boldLabel);
            options.minNoteSeconds = EditorGUILayout.Slider("Nota mais curta (s)", options.minNoteSeconds, 0.05f, 0.5f);
            options.maxJumpSemitones = EditorGUILayout.Slider("Variacao na nota (st)", options.maxJumpSemitones, 0.3f, 2f);
            options.silenceGapSeconds = EditorGUILayout.Slider("Silencio que corta (s)", options.silenceGapSeconds, 0.02f, 0.4f);
            options.lineGapSeconds = EditorGUILayout.Slider("Vao de nova linha (s)", options.lineGapSeconds, 0.3f, 3f);
            options.syllable = EditorGUILayout.TextField("Silaba de cada nota", options.syllable);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(clip == null))
            {
                if (GUILayout.Button("Analisar (nao grava nada)", GUILayout.Height(28f))) Analyze();
            }

            if (report != null)
            {
                EditorGUILayout.Space();
                MessageType type = Quality(report, out string verdict);
                EditorGUILayout.HelpBox(report + "\n\n" + verdict, type);

                if (preview != null && preview.notes != null && preview.notes.Length > 0)
                {
                    EditorGUILayout.LabelField("Primeiras notas", EditorStyles.boldLabel);
                    int count = Mathf.Min(12, preview.notes.Length);
                    for (int i = 0; i < count; i++)
                    {
                        SongNoteDto n = preview.notes[i];
                        EditorGUILayout.LabelField(string.Format("{0,7:0.00}s  dur {1,5:0.00}s  {2}",
                            n.beat, n.length, PitchUtils.NoteName(n.midi)));
                    }
                }
            }

            using (new EditorGUI.DisabledScope(preview == null || preview.notes == null || preview.notes.Length == 0))
            {
                if (GUILayout.Button("Gerar JSON em Resources/Songs", GUILayout.Height(32f))) Generate();
            }

            EditorGUILayout.EndScrollView();
        }

        void Analyze()
        {
            float[] mono = ReadClip(clip);
            if (mono == null) return;

            try
            {
                EditorUtility.DisplayProgressBar("Karaoke", "Analisando " + clip.name + "...", 0.5f);
                preview = AudioTranscriber.Transcribe(mono, clip.frequency, options, out report);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        void Generate()
        {
            string resourcePath = ResolveAudioResourcePath();

            preview.id = string.IsNullOrEmpty(songId) ? Sanitize(clip.name) : songId;
            preview.title = string.IsNullOrEmpty(songTitle) ? clip.name : songTitle;
            preview.artist = artist;
            preview.difficulty = "Importada";
            preview.credits = "Melodia extraida automaticamente do audio - rascunho, revise as notas.";
            preview.audioResource = resourcePath;
            preview.order = 10;

            const string folder = "Assets/Resources/Songs";
            EnsureFolder(folder);
            string path = folder + "/" + preview.id + ".json";
            File.WriteAllText(path, JsonUtility.ToJson(preview, true));
            AssetDatabase.Refresh();

            Debug.Log("[Karaoke] Musica gerada: " + path + " (" + preview.notes.Length + " notas)" +
                      (string.IsNullOrEmpty(resourcePath)
                          ? " - SEM audio: o jogo vai tocar tons guia."
                          : " - audio: Resources/" + resourcePath));

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(path));
        }

        /// <summary>Garante que o audio esteja acessivel por Resources.Load e devolve o caminho relativo.</summary>
        string ResolveAudioResourcePath()
        {
            string assetPath = AssetDatabase.GetAssetPath(clip);
            int index = assetPath.LastIndexOf("/Resources/", System.StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                string relative = assetPath.Substring(index + "/Resources/".Length);
                return Path.ChangeExtension(relative, null);
            }

            if (!copyToResources) return "";

            EnsureFolder("Assets/Resources/Audio");
            string target = "Assets/Resources/Audio/" + Path.GetFileName(assetPath);
            if (!File.Exists(target) && !AssetDatabase.CopyAsset(assetPath, target))
            {
                Debug.LogWarning("[Karaoke] Nao consegui copiar o audio para " + target);
                return "";
            }
            AssetDatabase.Refresh();
            return "Audio/" + Path.GetFileNameWithoutExtension(assetPath);
        }

        /// <summary>
        /// Le as amostras do clip. Forca DecompressOnLoad + preload no importer,
        /// senao GetData devolve zeros em clips comprimidos ou em streaming.
        /// </summary>
        static float[] ReadClip(AudioClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer != null)
            {
                AudioImporterSampleSettings s = importer.defaultSampleSettings;
                if (s.loadType != AudioClipLoadType.DecompressOnLoad || !s.preloadAudioData)
                {
                    s.loadType = AudioClipLoadType.DecompressOnLoad;
                    s.preloadAudioData = true;
                    importer.defaultSampleSettings = s;
                    importer.SaveAndReimport();
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }
            }

            var interleaved = new float[clip.samples * clip.channels];
            if (!clip.GetData(interleaved, 0))
            {
                Debug.LogError("[Karaoke] Nao consegui ler as amostras de " + clip.name);
                return null;
            }
            return AudioTranscriber.ToMono(interleaved, clip.channels);
        }

        static MessageType Quality(TranscriptionReport r, out string verdict)
        {
            float averageNote = r.Notes > 0 ? r.CoverageSeconds / r.Notes : 0f;

            if (r.Notes == 0)
            {
                verdict = "Nenhuma nota saiu. Baixe a confianca minima e o gate de silencio, ou o audio nao tem pitch definido.";
                return MessageType.Error;
            }
            if (r.CoveragePercent < 55f || averageNote < 0.18f)
            {
                verdict = string.Format(
                    "Rascunho ruim (nota media de {0:0.00}s). Isto costuma significar mixagem completa: " +
                    "o detector pula entre baixo, percussao e voz. Use um vocal isolado, ou mapeie as notas na mao.",
                    averageNote);
                return MessageType.Error;
            }
            if (r.CoveragePercent < 75f || averageNote < 0.25f)
            {
                verdict = string.Format("Rascunho aproveitavel, mas revise (nota media de {0:0.00}s).", averageNote);
                return MessageType.Warning;
            }
            verdict = string.Format("Bom rascunho (nota media de {0:0.00}s). Ainda vale conferir no jogo.", averageNote);
            return MessageType.Info;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        static string Sanitize(string value)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in value.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            return sb.ToString().Trim('-');
        }
    }
}
