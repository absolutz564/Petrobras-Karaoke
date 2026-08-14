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
    /// Telas do modo de teste: montadas por codigo, sem depender de nenhuma
    /// arte. Servem para experimentar as musicas rapidamente (inclusive numa
    /// cena vazia) enquanto a cena de producao usa a sua arte.
    /// </summary>
    public class SandboxMenuScreen
    {
        public RectTransform Root { get; private set; }
        public event Action<SongChart> SongChosen;
        public event Action PitchLabRequested;

        readonly List<SongChart> songs;
        readonly PitchTracker tracker;
        Text micLabel;
        Image levelFill;

        public SandboxMenuScreen(Transform parent, List<SongChart> songs, PitchTracker tracker)
        {
            this.songs = songs;
            this.tracker = tracker;
            Build(parent);
        }

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);

        void Build(Transform parent)
        {
            Root = UIBuilder.NewRect(parent, "SandboxMenu");
            UIBuilder.Stretch(Root);
            UIBuilder.Stretch(UIBuilder.NewImage(Root, "Background", Palette.Background).rectTransform);

            Text title = UIBuilder.NewText(Root, "Title", "KARAOKE  -  modo de teste", 72, Palette.Accent,
                                           TextAnchor.MiddleCenter, FontStyle.Bold);
            UIBuilder.SetAnchors(title.rectTransform, new Vector2(0f, 0.86f), new Vector2(1f, 0.98f), Vector2.zero, Vector2.zero);

            Text subtitle = UIBuilder.NewText(Root, "Subtitle", "escolha uma musica (ou tecle 1 a 6)", 30, Palette.TextDim,
                                              TextAnchor.MiddleCenter);
            UIBuilder.SetAnchors(subtitle.rectTransform, new Vector2(0f, 0.80f), new Vector2(1f, 0.86f), Vector2.zero, Vector2.zero);

            RectTransform grid = UIBuilder.NewRect(Root, "Grid");
            UIBuilder.SetAnchors(grid, new Vector2(0.06f, 0.24f), new Vector2(0.94f, 0.78f), Vector2.zero, Vector2.zero);

            int columns = songs.Count <= 4 ? 2 : 3;
            int rows = Mathf.Max(1, Mathf.CeilToInt(songs.Count / (float)columns));
            var spacing = new Vector2(24f, 20f);

            GridLayoutGroup layout = UIBuilder.Ensure<GridLayoutGroup>(grid.gameObject);
            layout.cellSize = new Vector2((1690f - spacing.x * (columns - 1)) / columns,
                                          Mathf.Min(190f, (580f - spacing.y * (rows - 1)) / rows));
            layout.spacing = spacing;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;
            layout.childAlignment = TextAnchor.MiddleCenter;

            for (int i = 0; i < songs.Count; i++) BuildCard(grid, songs[i], i);
            for (int i = grid.childCount - 1; i >= songs.Count; i--) UIBuilder.Discard(grid.GetChild(i).gameObject);

            RectTransform footer = UIBuilder.NewRect(Root, "Footer");
            UIBuilder.SetAnchors(footer, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.21f), Vector2.zero, Vector2.zero);
            UIBuilder.Stretch(UIBuilder.NewImage(footer, "Bg", Palette.Panel).rectTransform);

            micLabel = UIBuilder.NewText(footer, "MicLabel", "", 24, Palette.TextDim, TextAnchor.UpperLeft);
            UIBuilder.SetAnchors(micLabel.rectTransform, new Vector2(0f, 0.45f), new Vector2(0.6f, 1f),
                                 new Vector2(24f, 0f), new Vector2(0f, -14f));

            RectTransform levelBg = UIBuilder.NewImage(footer, "LevelBg", new Color(0f, 0f, 0f, 0.5f)).rectTransform;
            UIBuilder.SetAnchors(levelBg, new Vector2(0f, 0.18f), new Vector2(0.6f, 0.38f), new Vector2(24f, 0f), Vector2.zero);
            levelFill = UIBuilder.NewImage(levelBg, "LevelFill", Palette.Good);
            UIBuilder.SetAnchors(levelFill.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);

            UIBuilder.NewButton(footer, "PitchLab", "Testar microfone (F1)", Palette.Accent2, 24,
                () => { if (PitchLabRequested != null) PitchLabRequested(); })
                .GetComponent<RectTransform>().Apply(new Vector2(0.63f, 0.22f), new Vector2(0.87f, 0.78f));

            UIBuilder.NewButton(footer, "Quit", "Sair", new Color(0.35f, 0.15f, 0.22f, 1f), 24, Application.Quit)
                .GetComponent<RectTransform>().Apply(new Vector2(0.89f, 0.22f), new Vector2(1f, 0.78f),
                                                     Vector2.zero, new Vector2(-24f, 0f));
        }

        void BuildCard(Transform parent, SongChart song, int index)
        {
            SongChart captured = song;
            Button card = UIBuilder.NewButton(parent, "Song_" + index, null, Palette.PanelSoft, 24,
                () => { if (SongChosen != null) SongChosen(captured); });

            RectTransform rt = card.GetComponent<RectTransform>();

            Text number = UIBuilder.NewText(rt, "Number", (index + 1).ToString(), 48, Palette.Accent,
                                            TextAnchor.MiddleCenter, FontStyle.Bold);
            UIBuilder.SetAnchors(number.rectTransform, Vector2.zero, new Vector2(0.14f, 1f), Vector2.zero, Vector2.zero);

            Text title = UIBuilder.NewText(rt, "Title", song.Title, 32, Palette.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            UIBuilder.SetAnchors(title.rectTransform, new Vector2(0.14f, 0.52f), new Vector2(1f, 0.92f), Vector2.zero, new Vector2(-14f, 0f));

            Text artist = UIBuilder.NewText(rt, "Artist", song.Artist + "   -   " + song.Estilo, 24, Palette.TextDim, TextAnchor.UpperLeft);
            UIBuilder.SetAnchors(artist.rectTransform, new Vector2(0.14f, 0.28f), new Vector2(1f, 0.52f), Vector2.zero, new Vector2(-14f, 0f));

            string info = string.Format("{0} notas   -   {1} silabas   -   {2}s",
                song.Notes.Count, song.Syllables.Count, Mathf.RoundToInt(song.EndTime));
            Text meta = UIBuilder.NewText(rt, "Meta", info, 21, Palette.Accent2, TextAnchor.UpperLeft);
            UIBuilder.SetAnchors(meta.rectTransform, new Vector2(0.14f, 0.06f), new Vector2(1f, 0.28f), Vector2.zero, new Vector2(-14f, 0f));
        }

        public void Tick(float dt)
        {
            if (tracker != null && tracker.Mic != null)
            {
                bool on = tracker.Mic.IsRecording;
                micLabel.text = on
                    ? "microfone: " + tracker.Mic.Device + "   (" + tracker.Detector.Name + ")"
                    : "microfone inativo - o jogo roda, mas sem pontuacao";
                micLabel.color = on ? Palette.TextDim : Palette.Bad;

                levelFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(tracker.Level * 8f), 1f);
                levelFill.color = tracker.Voiced ? Palette.Good : Palette.PanelSoft;
            }

            for (int i = 0; i < songs.Count && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && SongChosen != null) { SongChosen(songs[i]); return; }
        }
    }

    /// <summary>Resultado do modo de teste: estrelas, pontos e estatisticas.</summary>
    public class SandboxResultScreen
    {
        public RectTransform Root { get; private set; }
        public event Action RetryRequested;
        public event Action MenuRequested;

        readonly Func<int, Sprite> starSprite;
        Image art;
        Text titleText, starsText, labelText, scoreText, statsText;

        public SandboxResultScreen(Transform parent, Func<int, Sprite> starSprite)
        {
            this.starSprite = starSprite;
            Build(parent);
        }

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);

        void Build(Transform parent)
        {
            Root = UIBuilder.NewRect(parent, "SandboxResult");
            UIBuilder.Stretch(Root);
            UIBuilder.Stretch(UIBuilder.NewImage(Root, "Background", Palette.Background).rectTransform);

            RectTransform panel = UIBuilder.NewImage(Root, "Panel", Palette.Panel).rectTransform;
            UIBuilder.SetAnchors(panel, new Vector2(0.2f, 0.12f), new Vector2(0.8f, 0.9f), Vector2.zero, Vector2.zero);

            titleText = UIBuilder.NewText(panel, "Song", "", 30, Palette.TextDim, TextAnchor.MiddleCenter);
            UIBuilder.SetAnchors(titleText.rectTransform, new Vector2(0f, 0.88f), new Vector2(1f, 0.98f), Vector2.zero, Vector2.zero);

            art = UIBuilder.NewImage(panel, "StarArt", Color.white);
            UIBuilder.SetAnchors(art.rectTransform, new Vector2(0.15f, 0.45f), new Vector2(0.85f, 0.86f), Vector2.zero, Vector2.zero);
            art.preserveAspect = true;
            art.gameObject.SetActive(false);

            starsText = UIBuilder.NewText(panel, "Stars", "", 92, new Color(1f, 0.82f, 0.2f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UIBuilder.SetAnchors(starsText.rectTransform, new Vector2(0f, 0.60f), new Vector2(1f, 0.86f), Vector2.zero, Vector2.zero);

            labelText = UIBuilder.NewText(panel, "Label", "", 46, Palette.Good, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIBuilder.SetAnchors(labelText.rectTransform, new Vector2(0f, 0.50f), new Vector2(1f, 0.60f), Vector2.zero, Vector2.zero);

            scoreText = UIBuilder.NewText(panel, "Score", "", 58, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIBuilder.SetAnchors(scoreText.rectTransform, new Vector2(0f, 0.33f), new Vector2(1f, 0.45f), Vector2.zero, Vector2.zero);

            statsText = UIBuilder.NewText(panel, "Stats", "", 26, Palette.TextDim, TextAnchor.UpperCenter);
            UIBuilder.SetAnchors(statsText.rectTransform, new Vector2(0f, 0.18f), new Vector2(1f, 0.33f), Vector2.zero, Vector2.zero);

            UIBuilder.NewButton(panel, "Retry", "Cantar de novo", Palette.Accent2, 28,
                () => { if (RetryRequested != null) RetryRequested(); })
                .GetComponent<RectTransform>().Apply(new Vector2(0.1f, 0.05f), new Vector2(0.48f, 0.15f));

            UIBuilder.NewButton(panel, "Menu", "Escolher outra", Palette.PanelSoft, 28,
                () => { if (MenuRequested != null) MenuRequested(); })
                .GetComponent<RectTransform>().Apply(new Vector2(0.52f, 0.05f), new Vector2(0.9f, 0.15f));
        }

        public void Show(SongChart chart, ScoreEngine engine)
        {
            titleText.text = chart.Title + "  -  " + chart.Artist;

            int stars = engine.Stars;
            Sprite sprite = starSprite != null ? starSprite(stars) : null;

            art.gameObject.SetActive(sprite != null);
            art.sprite = sprite;
            starsText.gameObject.SetActive(sprite == null);
            labelText.gameObject.SetActive(sprite == null);

            if (sprite == null)
            {
                starsText.text = new string('★', stars) + new string('☆', 5 - stars);
                labelText.text = engine.RatingLabel;
                labelText.color = stars >= 4 ? Palette.Good : stars >= 3 ? Palette.Warn : Palette.Bad;
            }

            scoreText.text = engine.FinalPoints + " / " + ScoreEngine.MaxPoints + " pontos";
            statsText.text = string.Format("{0:0.0}% de afinacao   |   melhor sequencia {1}   |   {2}/{3} notas\nmultiplicador maximo x{4}",
                engine.Percent, engine.BestCombo, engine.NotesHit, engine.NotesCompleted, engine.Multiplier);
        }

        public void Tick(float dt)
        {
            if (Input.GetKeyDown(KeyCode.Escape) && MenuRequested != null) MenuRequested();
        }
    }
}
