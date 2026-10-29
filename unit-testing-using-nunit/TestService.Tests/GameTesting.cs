using System.Reflection;
using UnoGame.Api.Models;

namespace TestService.Tests;

[TestFixture]
public sealed class GameControllerTest
{
    private static (List<Player> Players, Table Table, GameController Game) CreateGame(int playerCount = 2)
    {
        var players = Enumerable.Range(1, playerCount).Select(id => new Player(id, $"Player {id}")).ToList();
        var table = new Table(new Deck(), new DiscardPile());
        var game = new GameController(players.Cast<IPlayer>().ToList(), table);
        return (players, table, game);
    }

    private sealed class FakeTable : ITable
    {
        public Deck DrawPile { get; } = new();
        public DiscardPile DiscardPile { get; } = new();
        public CardColor ActiveColor { get; set; }
    }

    #region Create Game
    [Test]
    public void Constructor_ValidatesArguments_And_PlayerCount()
    {
        var table = new Table(new Deck(), new DiscardPile());

        Assert.That(() => new GameController(null!, table), Throws.ArgumentNullException);
        Assert.That(() => new GameController([new Player(1, "Solo")], table), Throws.ArgumentException);
        Assert.That(() => new GameController(Enumerable.Range(1, 5).Select(id => (IPlayer)new Player(id, $"Player {id}")).ToList(), table), Throws.ArgumentException);
        Assert.That(() => new GameController([new Player(1, "P1"), new Player(2, "P2")], new FakeTable()), Throws.ArgumentException);
    }

    [Test]
    public void NewGame_ExposesInitialTableAndPlayerState()
    {
        var (players, table, game) = CreateGame();

        Assert.That(game.Players, Is.EquivalentTo(players));
        Assert.That(game.Table, Is.SameAs(table));
        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[0]));
        Assert.That(game.GetActiveColor(), Is.EqualTo(CardColor.Red));
        Assert.That(game.GetTopCard(), Is.Null);
        Assert.That(game.GetPlayerScores()[players[0]], Is.Zero);
    }

    [Test]
    public void StartGame_DealsSevenCards_And_OpeningDiscard()
    {
        var (players, table, game) = CreateGame(3);
        game.StartGame();
        Assert.That(players.All(player => game.GetPlayerHand(player).Count >= 7), Is.True);
        Assert.That(table.DiscardPile.Cards, Has.Count.EqualTo(1));
        Assert.That(players, Does.Contain(game.GetCurrentPlayer()));
    }

    private static void SetPrivateField<T>(GameController game, string fieldName, T value)
    {
        var field = typeof(GameController).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(game, value);
    }

    [Test]
    public void ChooseOpeningColor_OnlyAcceptsCurrentPlayer_And_ValidColor()
    {
        var (players, table, game) = CreateGame();
        SetPrivateField(game, "_openingColorPending", true);
        table.ActiveColor = CardColor.Wild;

        Assert.That(game.ChooseOpeningColor(players[1], CardColor.Red), Is.False);
        Assert.That(game.ChooseOpeningColor(players[0], CardColor.Wild), Is.False);
        Assert.That(game.ChooseOpeningColor(players[0], CardColor.Green), Is.True);
        Assert.That(game.GetActiveColor(), Is.EqualTo(CardColor.Green));
        Assert.That(game.RequiresOpeningColorChoice, Is.False);
    }
    #endregion

    #region Play Card
    [Test]
    public void CanPlayThisCard_Matches_Color_Value_Action_And_Wild()
    {
        var (_, table, game) = CreateGame();
        table.DiscardPile.Cards.Add(new Card(Id: 1, Value: 7, ScoreValue: 7, Color: CardColor.Red, Action: null));
        table.ActiveColor = CardColor.Red;

        Assert.That(game.CanPlayThisCard(new Card(Id: 2, Value: 2, ScoreValue: 2, Color: CardColor.Red, Action: null)), Is.True);
        Assert.That(game.CanPlayThisCard(new Card(Id: 3, Value: 7, ScoreValue: 7, Color: CardColor.Blue, Action: null)), Is.True);
        Assert.That(game.CanPlayThisCard(new Card(Id: 5, Value: 4, ScoreValue: 4, Color: CardColor.Blue, Action: null)), Is.False);
        Assert.That(game.CanPlayThisCard(new Card(Id: 6, Value: null, ScoreValue: 50, Color: CardColor.Wild, Action: ActionType.Wild)), Is.True);
    }

    [Test]
    public void PlayCard_Updates_Table_Hand_And_Turn()
    {
        var (players, table, game) = CreateGame();
        var card = new Card(Id: 1, Value: 5, ScoreValue: 5, Color: CardColor.Red, Action: null);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 2, ScoreValue: 2, Color: CardColor.Red, Action: null));
        table.ActiveColor = CardColor.Red;
        game.GetPlayerHand(players[0]).Add(card);
        Assert.That(game.PlayCard(players[0], card, null), Is.True);
        Assert.That(game.HasCardInHand(players[0], card), Is.False);
        Assert.That(game.GetTopCard(), Is.EqualTo(card));
        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[1]));
    }

    [Test]
    public void PlayCard_Rejects_WrongTurn_And_Invalid_WildColor()
    {
        var (players, table, game) = CreateGame();
        var wild = new Card(Id: 1, Value: null, ScoreValue: 50, Color: CardColor.Wild, Action: ActionType.Wild);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 2, ScoreValue: 2, Color: CardColor.Red, Action: null));
        table.ActiveColor = CardColor.Red;
        game.GetPlayerHand(players[0]).Add(wild);

        Assert.That(game.PlayCard(players[1], wild, CardColor.Red), Is.False);
        Assert.That(game.PlayCard(players[0], wild, CardColor.Wild), Is.False);
        Assert.That(game.PlayCard(players[0], wild, CardColor.Yellow), Is.True);
    }

    [Test]
    public void PlayCard_Skip_SkipsTheNextPlayer()
    {
        var (players, table, game) = CreateGame(3);
        var skip = new Card(Id: 1, Value: null, ScoreValue: 20, Color: CardColor.Red, Action: ActionType.Skip);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 5, ScoreValue: 5, Color: CardColor.Red, Action: null));
        table.ActiveColor = CardColor.Red;
        game.GetPlayerHand(players[0]).Add(skip);
        game.GetPlayerHand(players[1]).Add(new Card(Id: 3, Value: 1, ScoreValue: 1, Color: CardColor.Blue, Action: null));
        game.GetPlayerHand(players[2]).Add(new Card(Id: 4, Value: 2, ScoreValue: 2, Color: CardColor.Blue, Action: null));

        Assert.That(game.PlayCard(players[0], skip, null), Is.True);

        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[2]));
    }

    [Test]
    public void PlayCard_DrawTwo_DrawsPenalty_And_AdvancesPlayer()
    {
        var (players, table, game) = CreateGame(3);
        var drawTwo = new Card(Id: 1, Value: null, ScoreValue: 20, Color: CardColor.Red, Action: ActionType.DrawTwo);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 5, ScoreValue: 5, Color: CardColor.Red, Action: null));
        table.ActiveColor = CardColor.Red;
        table.DrawPile.Cards.AddRange([
            new Card(Id: 3, Value: 1, ScoreValue: 1, Color: CardColor.Green, Action: null),
            new Card(Id: 4, Value: 2, ScoreValue: 2, Color: CardColor.Yellow, Action: null)]);
        game.GetPlayerHand(players[0]).Add(drawTwo);
        game.GetPlayerHand(players[1]).Add(new Card(Id: 5, Value: 1, ScoreValue: 1, Color: CardColor.Blue, Action: null));
        game.GetPlayerHand(players[2]).Add(new Card(Id: 6, Value: 2, ScoreValue: 2, Color: CardColor.Blue, Action: null));

        Assert.That(game.PlayCard(players[0], drawTwo, null), Is.True);

        Assert.That(game.GetPlayerHand(players[1]), Has.Count.EqualTo(3));
        Assert.That(game.PendingDrawCount, Is.Zero);
        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[2]));
    }

    [Test]
    public void PlayCard_WildDrawFour_AsLastCard_CannotBeChallenged()
    {
        var (players, table, game) = CreateGame();
        var drawFour = new Card(Id: 1, Value: null, ScoreValue: 50, Color: CardColor.Wild, Action: ActionType.WildDrawFour);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 5, ScoreValue: 5, Color: CardColor.Red, Action: null));
        table.ActiveColor = CardColor.Red;
        game.GetPlayerHand(players[0]).Add(drawFour);
        table.DrawPile.Cards.AddRange(Enumerable.Range(10, 4)
        .Select(id => new Card(Id: id, Value: id, ScoreValue: id, Color: CardColor.Green, Action: null)));

        Assert.That(game.PlayCard(players[0], drawFour, CardColor.Blue), Is.True);
        Assert.That(game.CanChallengeDrawFour(players[1]), Is.False);
        Assert.That(game.GetPlayerHand(players[1]), Has.Count.EqualTo(4));
        Assert.That(game.IsGameOver, Is.True);
        Assert.That(game.Winner, Is.EqualTo(players[0]));
    }
    #endregion

    #region Draw Card
    [Test]
    public void DrawCard_DrawsForCurrentPlayer_And_MarksTurn()
    {
        var (players, table, game) = CreateGame();
        table.DrawPile.Cards.Add(new Card(Id: 1, Value: 9, ScoreValue: 9, Color: CardColor.Green, Action: null));
        var drawn = game.DrawCard(players[0]);

        Assert.That(drawn, Is.Not.Null);
        Assert.That(game.HasDrawnThisTurn, Is.True);
        Assert.That(game.GetPlayerHand(players[0]), Does.Contain(drawn));
        Assert.That(game.DrawCard(players[1]), Is.Null);
    }

    [Test]
    public void PlayCard_Rejects_A_DifferentCard_AfterDrawingThisTurn()
    {
        var (players, table, game) = CreateGame();
        var heldCard = new Card(Id: 1, Value: 3, ScoreValue: 3, Color: CardColor.Red, Action: null);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 2, ScoreValue: 2, Color: CardColor.Red, Action: null));
        table.ActiveColor = CardColor.Red;
        table.DrawPile.Cards.Add(new Card(Id: 3, Value: 9, ScoreValue: 9, Color: CardColor.Blue, Action: null));
        game.GetPlayerHand(players[0]).Add(heldCard);

        game.DrawCard(players[0]);
        Assert.That(game.PlayCard(players[0], heldCard, null), Is.False);
    }

    [Test]
    public void DrawCard_WhenNoCardsRemainAnywhere_ReturnsNull()
    {
        var (players, _, game) = CreateGame();
        var drawn = game.DrawCard(players[0]);

        Assert.That(drawn, Is.Null);
        Assert.That(game.HasDrawnThisTurn, Is.True);
        Assert.That(game.GetPlayerHand(players[0]), Is.Empty);
    }
    #endregion

    #region Challenge Draw Four
    [Test]
    public void ChallengeDrawFour_Success_WhenPlayer_HaveMatchingCard()
    {
        var (players, table, game) = CreateGame();
        var drawFour = new Card(Id: 1, Value: null, ScoreValue: 50, Color: CardColor.Wild, Action: ActionType.WildDrawFour);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 5, ScoreValue: 5, Color: CardColor.Red, Action: null));
        table.ActiveColor = CardColor.Red;
        game.GetPlayerHand(players[0]).Add(drawFour);
        game.GetPlayerHand(players[0]).Add(new Card(Id: 3, Value: 9, ScoreValue: 9, Color: CardColor.Red, Action: null));
        game.GetPlayerHand(players[1]).Add(new Card(Id: 5, Value: 1, ScoreValue: 1, Color: CardColor.Blue, Action: null));
        table.DrawPile.Cards.AddRange(Enumerable.Range(10, 4)
            .Select(id => new Card(Id: id, Value: id, ScoreValue: id, Color: CardColor.Green, Action: null)));
        Assert.That(game.PlayCard(players[0], drawFour, CardColor.Blue), Is.True);
        Assert.That(game.CanChallengeDrawFour(players[1]), Is.True);
        Assert.That(game.ChallengeDrawFour(players[1], out var challengeSucceeded), Is.True);

        Assert.That(challengeSucceeded, Is.True);
        Assert.That(game.GetPlayerHand(players[0]), Has.Count.EqualTo(5));
    }

    [Test]

    public void ChallengeDrawFour_Failure_WhenPlayer_DoesNotHaveMatchingCard()
    {
        var (players, table, game) = CreateGame();
        var drawFour = new Card(Id: 1, Value: null, ScoreValue: 50, Color: CardColor.Wild, Action: ActionType.WildDrawFour);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 5, ScoreValue: 5, Color: CardColor.Red, Action: null));
        table.ActiveColor = CardColor.Red;
        game.GetPlayerHand(players[0]).Add(drawFour);
        game.GetPlayerHand(players[0]).Add(new Card(Id: 4, Value: 9, ScoreValue: 9, Color: CardColor.Blue, Action: null));
        game.GetPlayerHand(players[1]).Add(new Card(Id: 6, Value: 1, ScoreValue: 1, Color: CardColor.Blue, Action: null));
        table.DrawPile.Cards.AddRange(Enumerable.Range(20, 6)
            .Select(id => new Card(Id: id, Value: id, ScoreValue: id, Color: CardColor.Green, Action: null)));

        Assert.That(game.PlayCard(players[0], drawFour, CardColor.Blue), Is.True);
        Assert.That(game.ChallengeDrawFour(players[1], out var failedChallenge), Is.True);
        Assert.That(failedChallenge, Is.False);
        Assert.That(game.GetPlayerHand(players[1]), Has.Count.EqualTo(7));
    }

    [Test]
    public void DrawCard_DecliningToChallenge_AppliesDrawFourPenalty()
    {
        var (players, table, game) = CreateGame();
        var drawFour = new Card(Id: 1, Value: null, ScoreValue: 50, Color: CardColor.Wild, Action: ActionType.WildDrawFour);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 5, ScoreValue: 5, Color: CardColor.Red, Action: null));
        table.ActiveColor = CardColor.Red;
        game.GetPlayerHand(players[0]).Add(drawFour);
        game.GetPlayerHand(players[0]).Add(new Card(Id: 3, Value: 9, ScoreValue: 9, Color: CardColor.Red, Action: null));
        game.GetPlayerHand(players[1]).Add(new Card(Id: 4, Value: 1, ScoreValue: 1, Color: CardColor.Blue, Action: null));
        table.DrawPile.Cards.AddRange(Enumerable.Range(10, 4)
            .Select(id => new Card(Id: id, Value: id, ScoreValue: id, Color: CardColor.Green, Action: null)));

        Assert.That(game.PlayCard(players[0], drawFour, CardColor.Blue), Is.True);
        var drawn = game.DrawCard(players[1]);

        Assert.That(drawn, Is.Null);
        Assert.That(game.GetPlayerHand(players[1]), Has.Count.EqualTo(5));
        Assert.That(game.PendingDrawCount, Is.Zero);
        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[0]));
    }

    [Test]
    public void ChallengeDrawFour_WithNoPendingPenalty_ReturnsFalse()
    {
        var (players, _, game) = CreateGame();
        var result = game.ChallengeDrawFour(players[0], out var challengeSucceeded);

        Assert.That(result, Is.False);
        Assert.That(challengeSucceeded, Is.False);
    }
    #endregion

    [Test]
    public void PassTurn_RequiresDraw_And_AdvancesAfterDrawing()
    {
        var (players, table, game) = CreateGame();
        table.DrawPile.Cards.Add(new Card(Id: 1, Value: 9, ScoreValue: 9, Color: CardColor.Green, Action: null));

        Assert.That(() => game.PassTurn(players[1]), Throws.InvalidOperationException);
        Assert.That(() => game.PassTurn(players[0]), Throws.InvalidOperationException);

        game.DrawCard(players[0]);
        game.PassTurn(players[0]);

        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[1]));
    }

    [Test]
    public void SayUno_And_CatchUno_Availability()
    {
        var (players, _, game) = CreateGame();
        var hand = game.GetPlayerHand(players[0]);
        for (var id = 1; id <= 2; id++) hand.Add(new Card(Id: id, Value: id, ScoreValue: id, Color: CardColor.Red, Action: null));
        game.SayUno(players[0]);

        game.CatchPlayer(players[0]);

        Assert.That(hand, Has.Count.EqualTo(2));
    }

    [Test]
    public void CatchPlayer_WithOneCard_AppliesFourCardPenalty()
    {
        var (players, table, game) = CreateGame();
        var hand = game.GetPlayerHand(players[0]);
        hand.Add(new Card(Id: 1, Value: 1, ScoreValue: 1, Color: CardColor.Red, Action: null));
        table.DrawPile.Cards.AddRange(Enumerable.Range(2, 4)
            .Select(id => new Card(Id: id, Value: id, ScoreValue: id, Color: CardColor.Blue, Action: null)));

        game.CatchPlayer(players[0]);

        Assert.That(hand, Has.Count.EqualTo(5));
    }

    #region Check Game Winner
    [Test]
    public void CheckGameWinner_CalculatesRoundScore_And_Winner()
    {
        var (players, _, game) = CreateGame();
        game.GetPlayerHand(players[1]).Add(new Card(Id: 1, Value: 7, ScoreValue: 7, Color: CardColor.Red, Action: null));
        game.GetPlayerHand(players[1]).Add(new Card(Id: 2, Value: null, ScoreValue: 20, Color: CardColor.Blue, Action: ActionType.Skip));
        var winner = game.CheckGameWinner();

        Assert.That(winner, Is.EqualTo(players[0]));
        Assert.That(game.Winner, Is.EqualTo(players[0]));
        Assert.That(game.IsGameOver, Is.True);
        Assert.That(game.GetPlayerScore(players[0]), Is.EqualTo(27));
    }

    [Test]
    public void CheckGameWinner_WhenScoreReachesWinningScore_EndsMatch()
    {
        var players = new List<Player> { new(1, "Player 1"), new(2, "Player 2") };
        var table = new Table(new Deck(), new DiscardPile());
        var previousScores = new Dictionary<Player, int> { [players[0]] = 499, [players[1]] = 0 };
        var game = new GameController(players.Cast<IPlayer>().ToList(), table, previousScores);
        game.GetPlayerHand(players[1]).Add(new Card(Id: 1, Value: 1, ScoreValue: 1, Color: CardColor.Red, Action: null));

        Assert.That(game.CheckGameWinner(), Is.EqualTo(players[0]));
        Assert.That(game.GetPlayerScore(players[0]) >= 500);
        Assert.That(game.IsMatchOver, Is.True);
    }
    #endregion

    #region Forfeit Player
    [Test]
    public void ForfeitPlayer_RemovesPlayer_And_CanEndMatch()
    {
        var (players, _, game) = CreateGame();

        Assert.That(game.ForfeitPlayer(new Player(99, "Unknown")), Is.False);
        Assert.That(game.ForfeitPlayer(players[0]), Is.True);
        Assert.That(game.IsEligible(players[0]), Is.False);
        Assert.That(game.EligiblePlayerCount, Is.EqualTo(1));
        Assert.That(game.Winner, Is.EqualTo(players[1]));
        Assert.That(game.IsMatchOver, Is.True);
        Assert.That(game.ForfeitPlayer(players[0]), Is.False);
    }

    [Test]
    public void Constructor_ForfeitedPlayers_MarksThemIneligible()
    {
        var players = new List<Player> { new(1, "Player 1"), new(2, "Player 2"), new(3, "Player 3") };
        var table = new Table(new Deck(), new DiscardPile());
        var forfeited = new List<Player> { players[1] };
        var game = new GameController(players.Cast<IPlayer>().ToList(), table, forfeitedPlayers: forfeited);

        Assert.That(game.IsEligible(players[1]), Is.False);
        Assert.That(game.EligiblePlayerCount, Is.EqualTo(2));
        Assert.That(game.ForfeitedPlayers, Does.Contain(players[1]));
    }

    [Test]
    public void ForfeitPlayer_WithThreePlayers_LeavesRemainingPlayersActive()
    {
        var (players, _, game) = CreateGame(3);

        Assert.That(game.ForfeitPlayer(players[0]), Is.True);
        Assert.That(game.EligiblePlayerCount, Is.EqualTo(2));
        Assert.That(game.Winner, Is.Null);
        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[1]));
    }

    [Test]
    public void ForfeitPlayer_WhenNotTheCurrentPlayer_DoesNotResetTurnState()
    {
        var (players, table, game) = CreateGame(3);
        table.DrawPile.Cards.Add(new Card(Id: 1, Value: 9, ScoreValue: 9, Color: CardColor.Green, Action: null));
        game.DrawCard(players[0]);

        Assert.That(game.ForfeitPlayer(players[2]), Is.True);
        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[0]));
        Assert.That(game.HasDrawnThisTurn, Is.True);
        Assert.That(game.EligiblePlayerCount, Is.EqualTo(2));
    }
    #endregion

    [Test]
    public void NextTurn_And_ForceDrawCards()
    {
        var (players, table, game) = CreateGame();
        table.DrawPile.Cards.Add(new Card(Id: 1, Value: 1, ScoreValue: 1, Color: CardColor.Red, Action: null));
        table.DrawPile.Cards.Add(new Card(Id: 2, Value: 2, ScoreValue: 2, Color: CardColor.Blue, Action: null));
        game.ForceDrawCards(players[0], 2);
        game.NextTurn();

        Assert.That(game.GetPlayerHand(players[0]), Has.Count.EqualTo(2));
        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[1]));
    }

    #region Reverse Card
    [Test]
    public void Reverse_FlipsDirection_WithThreeOrMorePlayers()
    {
        var (players, table, game) = CreateGame(playerCount: 3);
        var reverse = new Card(Id: 1, Value: null, ScoreValue: 20, Color: CardColor.Red, Action: ActionType.Reverse);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 5, ScoreValue: 5, Color: CardColor.Blue, Action: null));
        table.ActiveColor = CardColor.Red;
        game.GetPlayerHand(players[0]).Add(reverse);

        game.PlayCard(players[0], reverse, null);

        Assert.That(game.Direction, Is.EqualTo(TurnDirection.CounterClockwise));
        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[2]));
    }

    [Test]
    public void PlayCard_Reverse_WithTwoPlayers_LetsCurrentPlayerPlayAgain()
    {
        var (players, table, game) = CreateGame();
        var reverse = new Card(Id: 1, Value: null, ScoreValue: 20, Color: CardColor.Red, Action: ActionType.Reverse);
        table.DiscardPile.Cards.Add(new Card(Id: 2, Value: 5, ScoreValue: 5, Color: CardColor.Blue, Action: null));
        table.ActiveColor = CardColor.Red;
        game.GetPlayerHand(players[0]).Add(reverse);
        game.GetPlayerHand(players[1]).Add(new Card(Id: 3, Value: 1, ScoreValue: 1, Color: CardColor.Blue, Action: null));

        Assert.That(game.PlayCard(players[0], reverse, null), Is.True);
        Assert.That(game.Direction, Is.EqualTo(TurnDirection.CounterClockwise));
        Assert.That(game.GetCurrentPlayer(), Is.EqualTo(players[0]));
    }
    #endregion

    [Test]
    public void HasCardInHand_And_RemoveCardFromHand_TrackHandMembership()
    {
        var (players, table, game) = CreateGame();
        var card = new Card(Id: 1, Value: 1, ScoreValue: 1, Color: CardColor.Red, Action: null);
        table.DrawPile.Cards.Add(card);

        Assert.That(game.HasCardInHand(players[0], card), Is.False);
        Assert.That(game.RemoveCardFromHand(players[0], card), Is.False);

        game.GetPlayerHand(players[0]).Add(card);

        Assert.That(game.HasCardInHand(players[0], card), Is.True);
        Assert.That(game.RemoveCardFromHand(players[0], card), Is.True);
    }

    [Test]
    public void GetPlayerHand_StartsEmpty()
    {
        var (players, _, game) = CreateGame();
        Assert.That(game.GetPlayerHand(players[0]), Is.Empty);
    }

    [Test]
    public void Shuffle_And_Reshuffle()
    {
        var (_, table, game) = CreateGame();
        var cards = Enumerable.Range(1, 4)
            .Select(id => new Card(Id: id, Value: id, ScoreValue: id, Color: CardColor.Red, Action: null)).ToList();
        table.DrawPile.Cards.AddRange(cards);
        game.ShuffleDeck();
        table.DiscardPile.Cards.AddRange(table.DrawPile.Cards);
        table.DrawPile.Cards.Clear();
        game.ReshuffleDiscardIntoDraw();

        Assert.That(table.DrawPile.Cards, Has.Count.EqualTo(3));
        Assert.That(table.DiscardPile.Cards, Has.Count.EqualTo(1));
    }

    [Test]
    public void IsEligible_And_EligiblePlayerCount_SeeActivePlayers()
    {
        var (players, _, game) = CreateGame();
        var strangerNotInGame = new Player(99, "Unknown");

        Assert.That(game.IsEligible(players[0]), Is.True);
        Assert.That(game.IsEligible(strangerNotInGame), Is.False);
        Assert.That(game.EligiblePlayerCount, Is.EqualTo(2));
    }

    [Test]
    public void StateProperties_ReportInitialValues()
    {
        var (_, _, game) = CreateGame();

        Assert.That(game.Direction, Is.EqualTo(TurnDirection.Clockwise));
        Assert.That(game.PendingDrawCount, Is.Zero);
        Assert.That(game.HasDrawnThisTurn, Is.False);
        Assert.That(game.RequiresOpeningColorChoice, Is.False);
        Assert.That(game.CanChallengeDrawFour(new Player(1, "Player 1")), Is.False);
        Assert.That(game.ForfeitedPlayers, Is.Empty);
        Assert.That(game.IsMatchOver, Is.False);
    }
}