using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Karaoke.Data;
using UnityEditor;
using UnityEngine;

namespace Karaoke.EditorTools
{
    /// <summary>
    /// Mapeamento manual da letra: toca o vocal e voce marca cada silaba no
    /// tempo, batendo ESPACO.
    ///
    /// A extracao automatica acerta o tempo dos ataques, mas erra QUAL silaba
    /// cai em qual ataque quando o detector acha mais ataques do que silabas —
    /// e nenhum ajuste de limiar resolve isso sem reconhecer fala. Bater o
    /// ritmo na mao e o que a comunidade de karaoke faz, e leva poucos minutos
    /// por musica.
    ///
    /// Menu: Karaoke > Mapear letra (tap)
    /// </summary>
    public class LyricTapWindow : EditorWindow
    {
        const string SongsRoot = "Assets/Resources/Songs";
        const string SourcesRoot = "Assets/SongSources";

        [Serializable]
        class Entry
        {
            public float time;
            public string text;
            public int line;
            public bool marked;
        }

        // musica
        string[] chartPaths = new string[0];
        string[] chartNames = new string[0];
        int chartIndex;
        SongChartDto dto;
        string chartPath;
        AudioClip vocal;
        List<Entry> entries = new List<Entry>();

        // audio
        AudioSource source;
        float speed = 1f;

        // tap
        bool tapping;
        int tapCursor;
        float tapLatency = 0.12f;
        readonly List<float> undoStack = new List<float>();

        // ui
        Vector2 listScroll;
        int selected = -1;
        float[] envelope;
        float nudgeStep = 0.02f;
        float bulkShift = 0.05f;

        [MenuItem("Karaoke/Mapear letra (tap)", false, 6)]
        public static void Open()
        {
            var window = GetWindow<LyricTapWindow>(false, "Mapear letra");
            window.minSize = new Vector2(720f, 560f);
            window.Refresh();
            window.Show();
        }

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopAudio();
            if (source != null) DestroyImmediate(source.gameObject);
        }

        void OnEditorUpdate()
        {
            if (source != null && source.isPlaying) Repaint();
        }

        // ------------------------------------------------------------ dados

        void Refresh()
        {
            var paths = new List<string>();
            var names = new List<string>();

            if (Directory.Exists(SongsRoot))
            {
                foreach (string file in Directory.GetFiles(SongsRoot, "*.json", SearchOption.AllDirectories))
                {
                    paths.Add(file.Replace('\\', '/'));
                    names.Add(Path.GetFileNameWithoutExtension(file));
                }
            }

            chartPaths = paths.ToArray();
            chartNames = names.ToArray();
            if (chartPaths.Length > 0) Load(Mathf.Clamp(chartIndex, 0, chartPaths.Length - 1));
        }

        void Load(int index)
        {
            StopAudio();
            chartIndex = index;
            chartPath = chartPaths[index];
            dto = JsonUtility.FromJson<SongChartDto>(File.ReadAllText(chartPath));

            entries.Clear();
            if (dto.syllables != null)
                foreach (SyllableDto s in dto.syllables)
                    entries.Add(new Entry { time = s.time, text = s.text, line = s.line, marked = true });

            vocal = FindVocal(chartPath);
            envelope = vocal != null ? BuildEnvelope(vocal) : null;
            selected = -1;
            tapping = false;
            tapCursor = 0;
            undoStack.Clear();
        }

        /// <summary>O stem de voz mora fora de Resources, na pasta espelho.</summary>
        static AudioClip FindVocal(string chartPath)
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(chartPath));
            string dir = SourcesRoot + "/" + folder;
            if (!Directory.Exists(dir)) return null;

            foreach (string file in Directory.GetFiles(dir, "*.wav"))
                if (Path.GetFileNameWithoutExtension(file).EndsWith("- Voz", StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(file.Replace('\\', '/'));
            return null;
        }

        static float[] BuildEnvelope(AudioClip clip)
        {
            const int buckets = 2000;
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) return null;

            var env = new float[buckets];
            int per = Mathf.Max(1, data.Length / buckets);
            for (int i = 0; i < buckets; i++)
            {
                float peak = 0f;
                int start = i * per;
                for (int k = 0; k < per && start + k < data.Length; k += 4)
                    peak = Mathf.Max(peak, Mathf.Abs(data[start + k]));
                env[i] = peak;
            }
            return env;
        }

        // ------------------------------------------------------------ audio

        AudioSource Source()
        {
            if (source != null) return source;
            var go = new GameObject("__karaoke_preview") { hideFlags = HideFlags.HideAndDontSave };
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            return source;
        }

        void PlayFrom(float time)
        {
            if (vocal == null) return;
            AudioSource s = Source();
            s.clip = vocal;
            s.pitch = speed;
            s.time = Mathf.Clamp(time, 0f, vocal.length - 0.05f);
            s.Play();

            // Em algumas versoes o AudioSource nao soa no modo edicao; entao
            // tentamos tambem o player interno de preview do editor.
            if (!s.isPlaying) PreviewPlay(vocal, s.time);
        }

        void StopAudio()
        {
            if (source != null) source.Stop();
            PreviewStop();
        }

        float CurrentTime
        {
            get
            {
                if (source != null && source.isPlaying) return source.time;
                float preview = PreviewPosition();
                return preview >= 0f ? preview : (source != null ? source.time : 0f);
            }
        }

        bool IsPlaying => (source != null && source.isPlaying) || PreviewPlaying();

        // -------- player interno do editor (reflexao; nomes mudam por versao)

        static Type audioUtil;
        static Type AudioUtil()
        {
            if (audioUtil == null)
                audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            return audioUtil;
        }

        static void Invoke(string[] names, params object[] args)
        {
            Type type = AudioUtil();
            if (type == null) return;
            foreach (string name in names)
            {
                MethodInfo method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public);
                if (method == null) continue;
                try { method.Invoke(null, args); } catch { }
                return;
            }
        }

        static void PreviewPlay(AudioClip clip, float time)
        {
            int sample = Mathf.RoundToInt(time * clip.frequency);
            Invoke(new[] { "PlayPreviewClip", "PlayClip" }, clip, sample, false);
        }

        static void PreviewStop() => Invoke(new[] { "StopAllPreviewClips", "StopAllClips" });

        static float PreviewPosition()
        {
            Type type = AudioUtil();
            if (type == null) return -1f;
            foreach (string name in new[] { "GetPreviewClipPosition", "GetClipPosition" })
            {
                MethodInfo method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public);
                if (method == null) continue;
                try { return (float)method.Invoke(null, null); } catch { return -1f; }
            }
            return -1f;
        }

        static bool PreviewPlaying()
        {
            Type type = AudioUtil();
            if (type == null) return false;
            foreach (string name in new[] { "IsPreviewClipPlaying", "IsClipPlaying" })
            {
                MethodInfo method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public);
                if (method == null) continue;
                try { return (bool)method.Invoke(null, null); } catch { return false; }
            }
            return false;
        }

        // --------------------------------------------------------------- ui

        void OnGUI()
        {
            HandleKeys();

            if (chartPaths.Length == 0)
            {
                EditorGUILayout.HelpBox("Nenhuma musica em " + SongsRoot, MessageType.Warning);
                if (GUILayout.Button("Procurar de novo")) Refresh();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            int index = EditorGUILayout.Popup("Musica", chartIndex, chartNames);
            if (index != chartIndex) Load(index);
            if (GUILayout.Button("Recarregar", GUILayout.Width(90f))) Load(chartIndex);
            EditorGUILayout.EndHorizontal();

            if (vocal == null)
            {
                EditorGUILayout.HelpBox("Nao achei o stem de voz em " + SourcesRoot +
                                        ". O arquivo precisa terminar em '- Voz.wav'.", MessageType.Error);
                return;
            }

            DrawWaveform();
            DrawTransport();
            DrawTapPanel();
            DrawList();
            DrawTools();
        }

        void HandleKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (e.keyCode == KeyCode.Space && tapping)
            {
                Tap();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Backspace && tapping)
            {
                UndoTap();
                e.Use();
            }
        }

        void DrawWaveform()
        {
            Rect rect = GUILayoutUtility.GetRect(position.width, 120f);
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.14f, 0.12f));

            if (envelope != null)
            {
                Handles.color = new Color(0.45f, 0.85f, 0.5f);
                for (int x = 0; x < rect.width; x++)
                {
                    int bucket = Mathf.Clamp(Mathf.RoundToInt(x / rect.width * (envelope.Length - 1)), 0, envelope.Length - 1);
                    float half = envelope[bucket] * rect.height * 0.48f;
                    float cx = rect.x + x;
                    float cy = rect.y + rect.height * 0.5f;
                    Handles.DrawLine(new Vector3(cx, cy - half), new Vector3(cx, cy + half));
                }
            }

            // marcadores das silabas
            Handles.color = new Color(1f, 0.78f, 0.16f, 0.9f);
            for (int i = 0; i < entries.Count; i++)
            {
                float x = rect.x + entries[i].time / vocal.length * rect.width;
                float top = i == selected ? rect.y : rect.y + rect.height * 0.25f;
                Handles.color = i == selected ? Color.white : new Color(1f, 0.78f, 0.16f, 0.75f);
                Handles.DrawLine(new Vector3(x, top), new Vector3(x, rect.yMax));
            }

            // playhead
            float t = CurrentTime;
            Handles.color = Color.red;
            float px = rect.x + Mathf.Clamp01(t / vocal.length) * rect.width;
            Handles.DrawLine(new Vector3(px, rect.y), new Vector3(px, rect.yMax));

            // clique posiciona
            Event e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                float clicked = (e.mousePosition.x - rect.x) / rect.width * vocal.length;
                PlayFrom(clicked);
                e.Use();
            }
        }

        void DrawTransport()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("<< 5s", GUILayout.Width(60f))) PlayFrom(Mathf.Max(0f, CurrentTime - 5f));
            if (GUILayout.Button(IsPlaying ? "Pausar" : "Tocar", GUILayout.Width(80f)))
            {
                if (IsPlaying) StopAudio();
                else PlayFrom(CurrentTime);
            }
            if (GUILayout.Button("Parar", GUILayout.Width(60f))) { StopAudio(); }
            if (GUILayout.Button("5s >>", GUILayout.Width(60f))) PlayFrom(CurrentTime + 5f);

            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "{0:0.00}s / {1:0.00}s", CurrentTime, vocal.length),
                            GUILayout.Width(130f));

            GUILayout.Label("Velocidade", GUILayout.Width(70f));
            float newSpeed = GUILayout.HorizontalSlider(speed, 0.4f, 1.2f, GUILayout.Width(120f));
            if (!Mathf.Approximately(newSpeed, speed))
            {
                speed = Mathf.Round(newSpeed * 20f) / 20f;
                if (source != null) source.pitch = speed;
            }
            GUILayout.Label(speed.ToString("0.00", CultureInfo.InvariantCulture) + "x", GUILayout.Width(45f));

            EditorGUILayout.EndHorizontal();
        }

        void DrawTapPanel()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Marcacao", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(tapping))
            {
                if (GUILayout.Button("Marcar do inicio", GUILayout.Height(24f))) StartTapping(0);
                if (GUILayout.Button("Marcar da silaba selecionada", GUILayout.Height(24f)))
                    StartTapping(Mathf.Max(0, selected));
            }
            using (new EditorGUI.DisabledScope(!tapping))
            {
                if (GUILayout.Button("Terminar", GUILayout.Height(24f))) { tapping = false; StopAudio(); }
            }
            EditorGUILayout.EndHorizontal();

            tapLatency = EditorGUILayout.Slider(
                new GUIContent("Compensacao do toque", "Quanto o seu tempo de reacao adianta a marcacao. Sobe se as silabas ficarem atrasadas."),
                tapLatency, 0f, 0.35f);

            if (tapping)
            {
                string atual = tapCursor < entries.Count ? "'" + entries[tapCursor].text.Trim() + "'" : "(fim)";
                EditorGUILayout.HelpBox(
                    "ESPACO marca " + atual + " e avanca | BACKSPACE desfaz\n" +
                    "Silaba " + (tapCursor + 1) + " de " + entries.Count +
                    "   -   proximas: " + Preview(tapCursor, 8),
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Dica: baixe a velocidade para 0,6x na primeira passada. " +
                                        "A compensacao de toque corrige o atraso de reacao — nao tente compensar batendo antes.",
                                        MessageType.None);
            }
        }

        string Preview(int from, int count)
        {
            var sb = new StringBuilder();
            for (int i = from; i < entries.Count && i < from + count; i++) sb.Append(entries[i].text);
            return sb.ToString();
        }

        void StartTapping(int from)
        {
            tapping = true;
            tapCursor = Mathf.Clamp(from, 0, Math.Max(0, entries.Count - 1));
            undoStack.Clear();

            float start = tapCursor > 0 ? Mathf.Max(0f, entries[tapCursor - 1].time - 1f) : 0f;
            PlayFrom(start);
            Focus();
        }

        void Tap()
        {
            if (tapCursor >= entries.Count) { tapping = false; StopAudio(); return; }

            undoStack.Add(entries[tapCursor].time);
            entries[tapCursor].time = Mathf.Max(0f, CurrentTime - tapLatency);
            entries[tapCursor].marked = true;
            selected = tapCursor;
            tapCursor++;

            if (tapCursor >= entries.Count) { tapping = false; StopAudio(); }
            Repaint();
        }

        void UndoTap()
        {
            if (undoStack.Count == 0 || tapCursor == 0) return;
            tapCursor--;
            entries[tapCursor].time = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            selected = tapCursor;
            Repaint();
        }

        void DrawList()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Silabas (" + entries.Count + ")", EditorStyles.boldLabel);

            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.MinHeight(150f));
            int line = -1;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.line != line)
                {
                    line = entry.line;
                    string full = dto.lyricLines != null && line < dto.lyricLines.Length ? dto.lyricLines[line] : "";
                    EditorGUILayout.LabelField("linha " + line + "   " + full, EditorStyles.miniBoldLabel);
                }

                EditorGUILayout.BeginHorizontal();

                bool isCurrent = tapping && i == tapCursor;
                GUI.color = isCurrent ? Color.yellow : (i == selected ? Color.cyan : Color.white);
                if (GUILayout.Button(string.Format(CultureInfo.InvariantCulture, "{0,3}  {1,7:0.000}s  {2}",
                        i, entry.time, entry.text), EditorStyles.label, GUILayout.Width(280f)))
                {
                    selected = i;
                    PlayFrom(Mathf.Max(0f, entry.time - 0.5f));
                }
                GUI.color = Color.white;

                if (GUILayout.Button("-", GUILayout.Width(24f))) { entry.time = Mathf.Max(0f, entry.time - nudgeStep); selected = i; }
                if (GUILayout.Button("+", GUILayout.Width(24f))) { entry.time += nudgeStep; selected = i; }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawTools()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ajuste fino", EditorStyles.boldLabel);

            nudgeStep = EditorGUILayout.Slider("Passo dos botoes + / -", nudgeStep, 0.005f, 0.2f);

            EditorGUILayout.BeginHorizontal();
            bulkShift = EditorGUILayout.FloatField("Deslocar (segundos)", bulkShift);
            if (GUILayout.Button("Tudo", GUILayout.Width(60f))) Shift(0, bulkShift);
            if (GUILayout.Button("Da selecionada em diante", GUILayout.Width(180f))) Shift(Mathf.Max(0, selected), bulkShift);
            if (GUILayout.Button("Inverter sinal", GUILayout.Width(100f))) bulkShift = -bulkShift;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
            if (GUILayout.Button("Salvar no JSON", GUILayout.Height(30f))) Save();
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Gerar WAV de conferencia", GUILayout.Height(30f))) ExportCheck();
            EditorGUILayout.EndHorizontal();
        }

        void Shift(int from, float amount)
        {
            for (int i = from; i < entries.Count; i++)
                entries[i].time = Mathf.Max(0f, entries[i].time + amount);
        }

        // ------------------------------------------------------------ saida

        void Save()
        {
            entries.Sort((a, b) => a.time.CompareTo(b.time));

            var list = new List<SyllableDto>();
            foreach (Entry e in entries)
                list.Add(new SyllableDto { time = e.time, text = e.text, line = e.line });
            dto.syllables = list.ToArray();

            File.WriteAllText(chartPath, Serialize(dto), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(chartPath);
            Debug.Log("[Karaoke] Letra salva em " + chartPath + " (" + list.Count + " silabas).");
        }

        /// <summary>Escreve compacto, uma nota/silaba por linha, para o arquivo continuar legivel.</summary>
        static string Serialize(SongChartDto dto)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"id\": \"{Escape(dto.id)}\",");
            sb.AppendLine($"  \"title\": \"{Escape(dto.title)}\",");
            sb.AppendLine($"  \"artist\": \"{Escape(dto.artist)}\",");
            sb.AppendLine($"  \"estilo\": \"{Escape(dto.estilo)}\",");
            sb.AppendLine($"  \"rotulo\": \"{Escape(dto.rotulo)}\",");
            sb.AppendLine($"  \"difficulty\": \"{Escape(dto.difficulty)}\",");
            sb.AppendLine($"  \"credits\": \"{Escape(dto.credits)}\",");
            sb.AppendLine($"  \"audioResource\": \"{Escape(dto.audioResource)}\",");
            sb.AppendLine($"  \"order\": {dto.order},");
            sb.AppendLine($"  \"bpm\": {dto.bpm.ToString("0.###", inv)},");
            sb.AppendLine($"  \"gap\": {dto.gap.ToString("0.###", inv)},");

            sb.Append("  \"lyricLines\": [");
            if (dto.lyricLines != null)
                for (int i = 0; i < dto.lyricLines.Length; i++)
                {
                    sb.Append('"').Append(Escape(dto.lyricLines[i])).Append('"');
                    if (i < dto.lyricLines.Length - 1) sb.Append(", ");
                }
            sb.AppendLine("],");

            sb.AppendLine("  \"syllables\": [");
            if (dto.syllables != null)
                for (int i = 0; i < dto.syllables.Length; i++)
                {
                    SyllableDto s = dto.syllables[i];
                    sb.Append(string.Format(inv, "    {{ \"time\": {0,7:0.000}, \"text\": \"{1}\", \"line\": {2} }}",
                        s.time, Escape(s.text), s.line));
                    sb.AppendLine(i < dto.syllables.Length - 1 ? "," : "");
                }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"notes\": [");
            if (dto.notes != null)
                for (int i = 0; i < dto.notes.Length; i++)
                {
                    SongNoteDto n = dto.notes[i];
                    sb.Append(string.Format(inv, "    {{ \"beat\": {0,8:0.000}, \"length\": {1,5:0.000}, \"midi\": {2,2}, \"text\": \"\", \"line\": {3} }}",
                        n.beat, n.length, n.midi, n.line));
                    sb.AppendLine(i < dto.notes.Length - 1 ? "," : "");
                }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        static string Escape(string value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        void ExportCheck()
        {
            string dir = "Conferencia";
            Directory.CreateDirectory(dir);

            var data = new float[vocal.samples * vocal.channels];
            vocal.GetData(data, 0);

            int rate = vocal.frequency;
            int channels = Mathf.Max(1, vocal.channels);
            var mix = new float[vocal.samples];
            for (int i = 0; i < mix.Length; i++) mix[i] = data[i * channels];

            int clickLength = rate / 40;
            foreach (Entry e in entries)
            {
                int start = Mathf.RoundToInt(e.time * rate);
                for (int i = 0; i < clickLength; i++)
                {
                    int at = start + i;
                    if (at < 0 || at >= mix.Length) break;
                    float env = 1f - (float)i / clickLength;
                    mix[at] = Mathf.Clamp(mix[at] + Mathf.Sin(2f * Mathf.PI * 2000f * i / rate) * env * env * 0.5f, -1f, 1f);
                }
            }

            string path = Path.Combine(dir, Path.GetFileNameWithoutExtension(chartPath) + "_tap.wav");
            WriteWav(path, mix, rate);
            Debug.Log("[Karaoke] Conferencia gravada em " + path + " — ouca se cada clique cai na silaba certa.");
        }

        static void WriteWav(string path, float[] samples, int rate)
        {
            using var bw = new BinaryWriter(File.Create(path));
            int dataBytes = samples.Length * 2;
            bw.Write(new[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + dataBytes);
            bw.Write(new[] { 'W', 'A', 'V', 'E' });
            bw.Write(new[] { 'f', 'm', 't', ' ' });
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(rate);
            bw.Write(rate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(new[] { 'd', 'a', 't', 'a' });
            bw.Write(dataBytes);
            foreach (float s in samples) bw.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767));
        }
    }
}
