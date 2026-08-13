namespace Karaoke.App
{
    /// <summary>
    /// Nomes que o jogo procura no Canvas. Fonte unica: o binding em runtime e
    /// o verificador do editor leem daqui, entao renomear e mexer em um lugar so.
    ///
    /// A busca e por NOME, em qualquer profundidade — a arrumacao da hierarquia
    /// fica a seu criterio.
    /// </summary>
    public static class SceneNames
    {
        // telas (filhas do Canvas)
        public const string Menu = "Menu";
        public const string MusicSelect = "MusicSelect";
        public const string Game = "Game";
        public const string EndGame = "EndGame";
        public const string Ranking = "Ranking";

        // tela 1
        public const string MenuAdvanceButton = "Button";

        // tela 2
        public const string ConfirmButton = "ButtonConfirm";

        // tela 3
        public const string PointsText = "PointsText";
        public const string MultiplierText = "MultiplicadorText";
        public const string CounterImage = "CounterImage";
        public const string CounterText = "CounterText";
        public const string LyricLinePrefix = "LyricLine";   // LyricLine0..LyricLine3

        // tela 4
        public const string ScoreImage = "ScoreImage";
        public const string EndPointsText = "PointsText (1)";
        public const string RankingButton = "ButtonRanking";
        public const string SkipButton = "ButtonSkip";
        public const string RegisterModal = "RegisterRankingGameObject";
        public const string NameInput = "InputField (TMP)";
        public const string RegisterButton = "ButtonRegisterRanking";

        // tela 5
        public const string RankingForroList = "forrolist (1)";
        public const string RankingPiseiroList = "piseirolist (1)";
        public const string RankingSertanejoList = "sertanejolist (1)";
        public const string RankRowPrefix = "RankRow";       // RankRow0..RankRow9
        public const string RowPosText = "PosText";
        public const string RowNameText = "NomeText";
        public const string RowPointsText = "PontosText";
    }
}
