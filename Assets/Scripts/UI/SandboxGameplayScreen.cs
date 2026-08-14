using System;
using System.Collections.Generic;
using System.Text;
using Karaoke.Audio;
using Karaoke.Core;
using Karaoke.Data;
using Karaoke.Scoring;
using UnityEngine;
using UnityEngine.UI;

namespace Karaoke.UI
{
    /// <summary>
    /// Tela de jogo do modo de teste: pauta rolando com as notas esperadas, o
    /// rastro do que voce canta, a letra com destaque e o placar.
    ///
    /// A pauta e o ponto forte deste modo — da para VER se a nota esperada bate
    /// com o que voce esta cantando, coisa que a tela de producao (so letra)
    /// nao mostra.
    /// </summary>
    public class SandboxGameplayScreen
    {
        const int TrailCapacity = 320;
        const float NoteHeight = 18f;

        public RectTransform Root { get; private set; }
        public event Action<SongChart, ScoreEngine> Finished;
        public event Action Aborted;

        readonly KaraokeSettings settings;
        readonly PitchTracker tracker;
        readonly AudioSource audioSource;

        RectTransform lane, notesRoot, trailRoot;
        Image playhead, cursor, progressFill;
        Text titleText, scoreText, multiplierText, percentText, lyricText, nextText, countdownText, cursorLabel, statusText;

        readonly List<Image> noteViews = new List<Image>();
        Image[] trailViews;
        float[] trailTime, trailMidi;
        bool[] trailActive;
        int trailCursor;

        SongChart chart;
        ScoreEngine engine;
        float[] noteAccuracy;
        float songTime, countdown;
        bool playing, counting;
        float lowMidi, highMidi;
        int lyricCursor;
        readonly LatencySweep sweep = new LatencySweep();
        readonly StringBuilder builder = new StringBuilder(128);

        public SandboxGameplayScreen(Transform parent, KaraokeSettings settings, PitchTracker tracker, AudioSource audioSource)
        {
            this.settings = settings;
            this.tracker = tracker;
            this.audioSource = audioSource;
            Build(parent);
        }

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);

        void Build(Transform parent)
        {
            Root = UIBuilder.NewRect(parent, "SandboxGameplay");
            UIBuilder.Stretch(Root);
            UIBuilder.Stretch(UIBuilder.NewImage(Root, "Background", Palette.Background).rectTransform);

            RectTransform progressBg = UIBuilder.NewImage(Root, "ProgressBg", new Color(1f, 1f, 1f, 0.08f)).rectTransform;
            UIBuilder.SetAnchors(progressBg, new Vector2(0f, 0.985f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            progressFill = UIBuilder.NewImage(progressBg, "ProgressFill", Palette.Accent);
            UIBuilder.SetAnchors(progressFill.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);

            titleText = UIBuilder.NewText(Root, "Title", "", 34, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIBuilder.SetAnchors(titleText.rectTransform, new Vector2(0.03f, 0.90f), new Vector2(0.55f, 0.98f), Vector2.zero, Vector2.zero);

            statusText = UIBuilder.NewText(Root, "Status", "", 22, Palette.Bad, TextAnchor.MiddleLeft);
            UIBuilder.SetAnchors(statusText.rectTransform, new Vector2(0.03f, 0.85f), new Vector2(0.6f, 0.90f), Vector2.zero, Vector2.zero);

            scoreText = UIBuilder.NewText(Root, "Score", "", 56, Palette.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            UIBuilder.SetAnchors(scoreText.rectTransform, new Vector2(0.6f, 0.89f), new Vector2(0.97f, 0.99f), Vector2.zero, Vector2.zero);

            multiplierText = UIBuilder.NewText(Root, "Multiplier", "x1", 34, Palette.Warn, TextAnchor.MiddleRight, FontStyle.Bold);
            UIBuilder.SetAnchors(multiplierText.rectTransform, new Vector2(0.6f, 0.84f), new Vector2(0.97f, 0.89f), Vector2.zero, Vector2.zero);

            percentText = UIBuilder.NewText(Root, "Percent", "", 24, Palette.TextDim, TextAnchor.MiddleLeft);
            UIBuilder.SetAnchors(percentText.rectTransform, new Vector2(0.35f, 0.84f), new Vector2(0.6f, 0.89f), Vector2.zero, Vector2.zero);

            lane = UIBuilder.NewRect(Root, "Lane");
            UIBuilder.SetAnchors(lane, new Vector2(0.03f, 0.34f), new Vector2(0.97f, 0.82f), Vector2.zero, Vector2.zero);
            UIBuilder.Stretch(UIBuilder.NewImage(lane, "LaneBg", Palette.Lane).rectTransform);
            UIBuilder.Ensure<RectMask2D>(lane.gameObject);

            for (int i = 1; i <= 4; i++)
            {
                float f = i / 5f;
                RectTransform line = UIBuilder.NewImage(lane, "Grid" + i, new Color(1f, 1f, 1f, 0.05f)).rectTransform;
                UIBuilder.SetAnchors(line, new Vector2(0f, f), new Vector2(1f, f), new Vector2(0f, -1f), new Vector2(0f, 1f));
            }

            trailRoot = UIBuilder.NewRect(lane, "Trail");
            UIBuilder.Stretch(trailRoot);
            notesRoot = UIBuilder.NewRect(lane, "Notes");
            UIBuilder.Stretch(notesRoot);

            playhead = UIBuilder.NewImage(lane, "Playhead", new Color(1f, 1f, 1f, 0.35f));
            UIBuilder.SetAnchors(playhead.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f));

            cursor = UIBuilder.NewImage(lane, "Cursor", Palette.Accent2);
            UIBuilder.SetFixed(cursor.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));

            cursorLabel = UIBuilder.NewText(lane, "CursorLabel", "", 22, Palette.Accent2, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIBuilder.SetFixed(cursorLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(150f, 30f));

            countdownText = UIBuilder.NewText(lane, "Countdown", "", 130, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIBuilder.Stretch(countdownText.rectTransform);

            lyricText = UIBuilder.NewText(Root, "Lyric", "", 46, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIBuilder.SetAnchors(lyricText.rectTransform, new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.30f), Vector2.zero, Vector2.zero);

            nextText = UIBuilder.NewText(Root, "NextLyric", "", 32, Palette.TextDim, TextAnchor.MiddleCenter);
            UIBuilder.SetAnchors(nextText.rectTransform, new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.18f), Vector2.zero, Vector2.zero);

            Text hint = UIBuilder.NewText(Root, "Hint", "ESC = voltar   |   F1 = testar microfone", 22, Palette.TextDim, TextAnchor.MiddleCenter);
            UIBuilder.SetAnchors(hint.rectTransform, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.08f), Vector2.zero, Vector2.zero);
        }

        void BuildTrailPool()
        {
            trailViews = new Image[TrailCapacity];
            trailTime = new float[TrailCapacity];
            trailMidi = new float[TrailCapacity];
            trailActive = new bool[TrailCapacity];

            for (int i = 0; i < TrailCapacity; i++)
            {
                Image img = UIBuilder.NewImage(trailRoot, "Trail" + i, Palette.Good);
                UIBuilder.SetFixed(img.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9f, 9f));
                img.gameObject.SetActive(false);
                trailViews[i] = img;
            }
        }

        // -------------------------------------------------------------- jogo

        public void Begin(SongChart newChart)
        {
            chart = newChart;
            engine = new ScoreEngine(chart, settings);
            engine.NoteFinished += OnNoteFinished;

            noteAccuracy = new float[chart.Notes.Count];
            for (int i = 0; i < noteAccuracy.Length; i++) noteAccuracy[i] = -1f;

            titleText.text = chart.Title + "  <color=#8f8fb0>" + chart.Artist + "</color>";
            ComputeLaneRange();
            BuildNoteViews();
            if (trailViews == null) BuildTrailPool();
            ClearTrail();

            AudioClip clip = string.IsNullOrEmpty(chart.AudioResource) ? null : Resources.Load<AudioClip>(chart.AudioResource);
            if (clip == null && settings.guideTones) clip = GuideToneSynth.Build(chart, 0f, 44100, settings.guideVolume);

            audioSource.Stop();
            audioSource.clip = clip;

            songTime = 0f;
            lyricCursor = 0;
            sweep.Clear();
            countdown = Mathf.Max(1, settings.readyCountdown);
            counting = true;
            playing = false;
            if (tracker != null) tracker.Reset();

            statusText.text = tracker != null && tracker.Mic != null && tracker.Mic.IsRecording
                ? ""
                : "sem microfone - a pontuacao fica em zero";
        }

        public void Stop()
        {
            playing = false;
            counting = false;
            audioSource.Stop();
        }

        void OnNoteFinished(NoteScore result)
        {
            if (noteAccuracy != null && result.Index >= 0 && result.Index < noteAccuracy.Length)
                noteAccuracy[result.Index] = result.Accuracy;
        }

        void ComputeLaneRange()
        {
            int count = chart.Notes.Count;
            var sorted = new float[count];
            for (int i = 0; i < count; i++) sorted[i] = chart.Notes[i].Midi;
            Array.Sort(sorted);

            lowMidi = sorted[Mathf.Clamp(Mathf.FloorToInt(count * 0.05f), 0, count - 1)] - 3f;
            highMidi = sorted[Mathf.Clamp(Mathf.CeilToInt(count * 0.95f) - 1, 0, count - 1)] + 3f;

            if (highMidi - lowMidi < 12f)
            {
                float center = (highMidi + lowMidi) * 0.5f;
                lowMidi = center - 6f;
                highMidi = center + 6f;
            }
        }

        void BuildNoteViews()
        {
            for (int i = 0; i < noteViews.Count; i++)
                if (noteViews[i] != null) UIBuilder.Discard(noteViews[i].gameObject);
            noteViews.Clear();

            for (int i = 0; i < chart.Notes.Count; i++)
            {
                Image img = UIBuilder.NewImage(notesRoot, "Note" + i, Palette.NoteIdle);
                UIBuilder.SetFixed(img.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(10f, NoteHeight));
                noteViews.Add(img);
            }
        }

        void ClearTrail()
        {
            for (int i = 0; i < TrailCapacity; i++)
            {
                trailActive[i] = false;
                trailViews[i].gameObject.SetActive(false);
            }
            trailCursor = 0;
        }

        public void Tick(float dt)
        {
            if (chart == null) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Stop();
                if (Aborted != null) Aborted();
                return;
            }

            if (counting)
            {
                countdown -= dt;
                countdownText.text = Mathf.Max(1, Mathf.CeilToInt(countdown)).ToString();
                if (countdown <= 0f)
                {
                    counting = false;
                    playing = true;
                    countdownText.text = "";
                    if (audioSource.clip != null) audioSource.Play();
                }
                return;
            }
            if (!playing) return;

            songTime += dt;
            if (audioSource.clip != null && audioSource.isPlaying)
            {
                float fromAudio = audioSource.time;
                if (Mathf.Abs(fromAudio - songTime) > 0.04f) songTime = fromAudio;
            }

            engine.Feed(songTime - settings.micLatencySeconds, dt, tracker.SmoothedMidi, tracker.Voiced);
            sweep.Record(songTime, dt, tracker.SmoothedMidi, tracker.Voiced);

            UpdateNotes();
            UpdateCursor();
            UpdateLyrics();
            UpdateHud();

            if (songTime > chart.EndTime + 1.2f)
            {
                playing = false;
                engine.Finish();
                audioSource.Stop();

                // mede a latencia real com o que acabou de ser cantado
                Debug.Log(sweep.Report(chart, settings));

                if (Finished != null) Finished(chart, engine);
            }
        }

        void UpdateNotes()
        {
            float laneW = lane.rect.width;
            float pps = settings.pixelsPerSecond;
            float headX = laneW * 0.28f;

            playhead.rectTransform.offsetMin = new Vector2(headX, 0f);
            playhead.rectTransform.offsetMax = new Vector2(headX + 3f, 0f);

            for (int i = 0; i < noteViews.Count; i++)
            {
                SongNote note = chart.Notes[i];
                float x = headX + (note.StartTime - songTime) * pps;
                float w = Mathf.Max(6f, note.Duration * pps);

                Image img = noteViews[i];
                bool visible = x < laneW + 40f && x + w > -40f;
                if (img.gameObject.activeSelf != visible) img.gameObject.SetActive(visible);
                if (!visible) continue;

                img.rectTransform.sizeDelta = new Vector2(w, NoteHeight);
                img.rectTransform.anchoredPosition = new Vector2(x, MidiToY(note.Midi));

                if (i == engine.ActiveIndex)
                    img.color = engine.OnPitch ? Palette.Good : Color.Lerp(Palette.Warn, Palette.NoteIdle, 0.35f);
                else if (noteAccuracy[i] >= 0f)
                    img.color = Color.Lerp(new Color(0.35f, 0.2f, 0.3f, 0.85f), Palette.Good, noteAccuracy[i]);
                else
                    img.color = Palette.NoteIdle;
            }
        }

        void UpdateCursor()
        {
            float laneW = lane.rect.width;
            float headX = laneW * 0.28f;
            bool voiced = tracker != null && tracker.Voiced;

            cursor.gameObject.SetActive(voiced);
            cursorLabel.gameObject.SetActive(voiced);

            if (voiced)
            {
                float midi = FoldIntoRange(tracker.SmoothedMidi);
                float y = MidiToY(midi);
                cursor.rectTransform.anchoredPosition = new Vector2(headX, y);
                cursor.color = engine.OnPitch ? Palette.Good : Palette.Accent2;
                cursorLabel.rectTransform.anchoredPosition = new Vector2(headX + 22f, y + 24f);
                cursorLabel.text = PitchUtils.NoteName(tracker.SmoothedMidi);
                cursorLabel.color = cursor.color;

                int i = trailCursor;
                trailCursor = (trailCursor + 1) % TrailCapacity;
                trailActive[i] = true;
                trailTime[i] = songTime;
                trailMidi[i] = midi;
                trailViews[i].color = engine.OnPitch ? Palette.Good : new Color(0.6f, 0.6f, 0.75f, 0.75f);
                trailViews[i].gameObject.SetActive(true);
            }

            float pps = settings.pixelsPerSecond;
            for (int i = 0; i < TrailCapacity; i++)
            {
                if (!trailActive[i]) continue;
                float x = headX + (trailTime[i] - songTime) * pps;
                if (x < -20f)
                {
                    trailActive[i] = false;
                    trailViews[i].gameObject.SetActive(false);
                    continue;
                }
                trailViews[i].rectTransform.anchoredPosition = new Vector2(x, MidiToY(trailMidi[i]));
            }
        }

        /// <summary>Letra pelo TEMPO da musica, usando a lista de silabas do chart.</summary>
        void UpdateLyrics()
        {
            if (chart.Syllables.Count == 0) { lyricText.text = ""; nextText.text = ""; return; }

            // avanca a partir da ultima silaba conhecida em vez de varrer tudo
            if (lyricCursor >= chart.Syllables.Count || chart.Syllables[lyricCursor].StartTime > songTime) lyricCursor = 0;
            int current = -1;
            for (int i = lyricCursor; i < chart.Syllables.Count; i++)
            {
                if (chart.Syllables[i].StartTime > songTime) break;
                current = i;
            }
            if (current >= 0) lyricCursor = current;

            int line = current >= 0 ? chart.Syllables[current].Line : chart.Syllables[0].Line;
            lyricText.text = RenderLine(line, current);
            nextText.text = RenderLine(line + 1, -1);
        }

        string RenderLine(int line, int current)
        {
            string full = line >= 0 && line < chart.LyricLines.Count ? chart.LyricLines[line] : "";
            if (string.IsNullOrEmpty(full)) return "";

            int sung = 0;
            for (int i = 0; i < chart.Syllables.Count && i <= current; i++)
                if (chart.Syllables[i].Line == line) sung += chart.Syllables[i].Text.Length;
            sung = Mathf.Clamp(sung, 0, full.Length);

            builder.Length = 0;
            if (sung > 0) builder.Append("<color=#FFC72C>").Append(full, 0, sung).Append("</color>");
            if (sung < full.Length) builder.Append("<color=#FFFFFF>").Append(full, sung, full.Length - sung).Append("</color>");
            return builder.ToString();
        }

        void UpdateHud()
        {
            scoreText.text = Mathf.RoundToInt(engine.LiveScore) + "<size=28> / " + ScoreEngine.MaxPoints + "</size>";
            multiplierText.text = "x" + engine.Multiplier;
            multiplierText.color = engine.Multiplier > 1 ? Palette.Warn : Palette.TextDim;
            percentText.text = engine.NotesCompleted > 0
                ? string.Format("{0:0}% de afinacao   -   {1}/{2} notas", engine.Percent, engine.NotesHit, engine.NotesCompleted)
                : "";
            progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(songTime / Mathf.Max(0.1f, chart.EndTime)), 1f);
        }

        float MidiToY(float midi)
        {
            float h = lane.rect.height - 44f;
            return (Mathf.InverseLerp(lowMidi, highMidi, midi) - 0.5f) * h;
        }

        float FoldIntoRange(float midi)
        {
            if (midi <= 0f) return lowMidi;
            if (!settings.octaveAgnostic) return Mathf.Clamp(midi, lowMidi, highMidi);

            float m = midi;
            int guard = 0;
            while (m < lowMidi && guard++ < 8) m += 12f;
            while (m > highMidi && guard++ < 16) m -= 12f;
            return Mathf.Clamp(m, lowMidi, highMidi);
        }
    }
}
