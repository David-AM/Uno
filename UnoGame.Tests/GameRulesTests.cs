using UnoGame.Api.Models;
using Xunit;

namespace UnoGame.Tests;

public sealed class GameRulesTests
{
    [Fact]
    public void StartGame_DealsSevenCardsAndBuilds108CardDeck()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        var game = new GameController(players, new Random(42));

        game.StartGame();

        Assert.All(players, player => Assert.True(game.GetPlayerHand(player).Count >= 7));
        Assert.Equal(108, players.Sum(player => game.GetPlayerHand(player).Count) + game.Table.DrawPile.TotalCards + game.Table.DiscardPile.Cards.Count);
        Assert.NotNull(game.GetTopCard());
    }

    [Fact]
    public void StartGame_NeverTurnsUpWildDrawFourAsOpeningCard()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var game = new GameController(
                [new Player(1, "Ada"), new Player(2, "Grace")],
                new Random(seed));

            game.StartGame();

            Assert.NotEqual(ActionType.WildDrawFour, game.GetTopCard()?.Action);
        }
    }

    [Fact]
    public void CanPlayThisCard_MatchesActiveColorOrTopValue()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        var game = new GameController(players, new Random(5));
        game.StartGame();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(1000, 7, 7, CardColor.Red, null));
        game.Table.ActiveColor = CardColor.Red;

        Assert.True(game.CanPlayThisCard(new Card(1001, 2, 2, CardColor.Red, null)));
        Assert.True(game.CanPlayThisCard(new Card(1002, 7, 7, CardColor.Blue, null)));
        Assert.False(game.CanPlayThisCard(new Card(1003, 3, 3, CardColor.Blue, null)));
    }

    [Fact]
    public void PlayCard_RejectsWildWithoutChosenColor()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        var game = new GameController(players, new Random(8));
        game.StartGame();
        var wild = new Card(2000, null, 50, CardColor.Wild, ActionType.Wild);
        game.GetPlayerHand(players[0]).Add(wild);

        Assert.False(game.PlayCard(players[0], wild, null));
        Assert.True(game.HasCardInHand(players[0], wild));
    }

    [Fact]
    public void PlayCard_RejectsNonMatchingCard()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        var game = new GameController(players, new Random(15));
        game.StartGame();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(4000, 7, 7, CardColor.Red, null));
        game.Table.ActiveColor = CardColor.Red;
        var invalid = new Card(4001, 2, 2, CardColor.Blue, null);
        game.GetPlayerHand(players[0]).Add(invalid);

        Assert.False(game.PlayCard(players[0], invalid, null));
        Assert.True(game.HasCardInHand(players[0], invalid));
    }

    [Fact]
    public void WildDrawFour_CanBeChallengedWhenPlayerHasMatchingCard()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        var game = new GameController(players, new Random(18));
        game.StartGame();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(4100, 4, 4, CardColor.Green, null));
        game.Table.ActiveColor = CardColor.Green;
        var hand = game.GetPlayerHand(players[0]);
        hand.Clear();
        hand.Add(new Card(4101, 9, 9, CardColor.Green, null));
        var wildDrawFour = new Card(4102, null, 50, CardColor.Wild, ActionType.WildDrawFour);
        hand.Add(wildDrawFour);

        Assert.True(game.CanPlayThisCard(wildDrawFour));
        Assert.True(game.PlayCard(players[0], wildDrawFour, CardColor.Blue));
        Assert.True(game.CanChallengeDrawFour(players[1]));
        Assert.True(game.ChallengeDrawFour(players[1], out var succeeded));
        Assert.True(succeeded);
        Assert.Equal(5, game.GetPlayerHand(players[0]).Count);
    }

    [Fact]
    public void WildDrawFourPlayedByMember_CanBeChallengedByHost()
    {
        var players = new List<Player> { new(1, "Host"), new(2, "Member") };
        var game = new GameController(players, new Random(19));
        game.StartGame();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(4110, 4, 4, CardColor.Red, null));
        game.Table.ActiveColor = CardColor.Red;
        var hostCard = new Card(4111, 2, 2, CardColor.Red, null);
        var hostExtraCard = new Card(4113, 8, 8, CardColor.Blue, null);
        var memberDrawFour = new Card(4112, null, 50, CardColor.Wild, ActionType.WildDrawFour);
        var memberExtraCard = new Card(4114, 9, 9, CardColor.Green, null);
        game.GetPlayerHand(players[0]).Clear();
        game.GetPlayerHand(players[0]).AddRange([hostCard, hostExtraCard]);
        game.GetPlayerHand(players[1]).Clear();
        game.GetPlayerHand(players[1]).AddRange([memberDrawFour, memberExtraCard]);

        Assert.True(game.PlayCard(players[0], hostCard, null));
        Assert.True(game.PlayCard(players[1], memberDrawFour, CardColor.Blue));
        Assert.Same(players[0], game.GetCurrentPlayer());
        Assert.True(game.CanChallengeDrawFour(players[0]));
    }

    [Fact]
    public void FinalWildDrawFour_WinsImmediatelyWithoutChallenge()
    {
        var players = new List<Player> { new(1, "Host"), new(2, "Member") };
        var game = new GameController(players, new Random(20));
        game.StartGame();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(4120, 4, 4, CardColor.Red, null));
        game.Table.ActiveColor = CardColor.Red;
        game.GetPlayerHand(players[0]).Clear();
        game.GetPlayerHand(players[0]).AddRange([
            new Card(4121, 2, 2, CardColor.Red, null),
            new Card(4122, 8, 8, CardColor.Blue, null)]);
        game.GetPlayerHand(players[1]).Clear();
        game.GetPlayerHand(players[1]).Add(new Card(4123, null, 50, CardColor.Wild, ActionType.WildDrawFour));

        Assert.True(game.PlayCard(players[0], game.GetPlayerHand(players[0])[0], null));
        Assert.True(game.PlayCard(players[1], game.GetPlayerHand(players[1])[0], CardColor.Blue));
        Assert.True(game.IsGameOver);
        Assert.False(game.CanChallengeDrawFour(players[0]));
    }

    [Fact]
    public void DrawTwo_DrawsTwoAndMovesToFollowingPlayer()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace"), new(3, "Lin") };
        var game = new GameController(players, new Random(21));
        game.StartGame();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(4200, 3, 3, CardColor.Blue, null));
        game.Table.ActiveColor = CardColor.Blue;
        var drawTwo = new Card(4201, null, 20, CardColor.Blue, ActionType.DrawTwo);
        game.GetPlayerHand(players[0]).Add(drawTwo);
        var before = game.GetPlayerHand(players[1]).Count;

        Assert.True(game.PlayCard(players[0], drawTwo, null));
        Assert.Same(players[2], game.GetCurrentPlayer());
        Assert.Equal(before + 2, game.GetPlayerHand(players[1]).Count);
        Assert.Equal(0, game.PendingDrawCount);
    }

    [Fact]
    public void SkipAndReverse_ChangeTurnOrder()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace"), new(3, "Lin") };
        var skipGame = new GameController(players, new Random(24));
        skipGame.StartGame();
        skipGame.Table.DiscardPile.Cards.Clear();
        skipGame.Table.DiscardPile.Cards.Add(new Card(4300, 5, 5, CardColor.Yellow, null));
        skipGame.Table.ActiveColor = CardColor.Yellow;
        var skip = new Card(4301, null, 20, CardColor.Yellow, ActionType.Skip);
        skipGame.GetPlayerHand(players[0]).Add(skip);

        Assert.True(skipGame.PlayCard(players[0], skip, null));
        Assert.Same(players[2], skipGame.GetCurrentPlayer());

        var reverseGame = new GameController(players, new Random(25));
        reverseGame.StartGame();
        reverseGame.Table.DiscardPile.Cards.Clear();
        reverseGame.Table.DiscardPile.Cards.Add(new Card(4310, 5, 5, CardColor.Yellow, null));
        reverseGame.Table.ActiveColor = CardColor.Yellow;
        var reverse = new Card(4311, null, 20, CardColor.Yellow, ActionType.Reverse);
        reverseGame.GetPlayerHand(players[0]).Add(reverse);

        Assert.True(reverseGame.PlayCard(players[0], reverse, null));
        Assert.Equal(TurnDirection.CounterClockwise, reverseGame.Direction);
        Assert.Same(players[2], reverseGame.GetCurrentPlayer());
    }

    [Fact]
    public void CatchPlayer_PenalizesOnlyIfUnoWasNotDeclared()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        var game = new GameController(players, new Random(28));
        game.StartGame();
        var caller = players[0]; var target = players[1];
        game.GetPlayerHand(target).Clear();
        game.GetPlayerHand(target).Add(new Card(4400, 1, 1, CardColor.Red, null));

        game.CatchPlayer(caller, target);
        Assert.Equal(5, game.GetPlayerHand(target).Count);

        game.GetPlayerHand(target).Clear();
        game.GetPlayerHand(target).AddRange([new Card(4401, 1, 1, CardColor.Red, null), new Card(4402, 2, 2, CardColor.Red, null)]);
        game.NextTurn();
        game.SayUno(target);
        game.GetPlayerHand(target).RemoveAt(1);
        game.CatchPlayer(caller, target);
        Assert.Single(game.GetPlayerHand(target));
    }

    [Fact]
    public void PlayLastCard_DeclaresWinner()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        var game = new GameController(players, new Random(31));
        game.StartGame();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(4500, 6, 6, CardColor.Red, null));
        game.Table.ActiveColor = CardColor.Red;
        var lastCard = new Card(4501, 1, 1, CardColor.Red, null);
        game.GetPlayerHand(players[0]).Clear();
        game.GetPlayerHand(players[0]).Add(lastCard);

        Assert.True(game.PlayCard(players[0], lastCard, null));
        Assert.Same(players[0], game.Winner);
        Assert.True(game.IsGameOver);
    }

    [Fact]
    public void PlayLastDrawTwo_PenalizesNextPlayerBeforeScoring()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        var game = new GameController(players, new Random(32));
        game.StartGame();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(4510, 6, 6, CardColor.Red, null));
        game.Table.ActiveColor = CardColor.Red;
        var drawTwo = new Card(4511, null, 20, CardColor.Red, ActionType.DrawTwo);
        game.GetPlayerHand(players[0]).Clear();
        game.GetPlayerHand(players[0]).Add(drawTwo);
        game.GetPlayerHand(players[1]).Clear();
        game.GetPlayerHand(players[1]).Add(new Card(4512, 1, 1, CardColor.Blue, null));

        Assert.True(game.PlayCard(players[0], drawTwo, null));
        Assert.Same(players[0], game.Winner);
        Assert.Equal(3, game.GetPlayerHand(players[1]).Count);
        Assert.Equal(game.GetPlayerHand(players[1]).Sum(card => card.ScoreValue), game.GetPlayerScore(players[0]));
    }

    [Fact]
    public void PlayLastWildDrawFour_PenalizesNextPlayerBeforeScoringWithoutChallenge()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        var game = new GameController(players, new Random(33));
        game.StartGame();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(4520, 6, 6, CardColor.Red, null));
        game.Table.ActiveColor = CardColor.Red;
        var drawFour = new Card(4521, null, 50, CardColor.Wild, ActionType.WildDrawFour);
        game.GetPlayerHand(players[0]).Clear();
        game.GetPlayerHand(players[0]).Add(drawFour);
        game.GetPlayerHand(players[1]).Clear();
        game.GetPlayerHand(players[1]).Add(new Card(4522, 1, 1, CardColor.Blue, null));

        Assert.True(game.PlayCard(players[0], drawFour, CardColor.Blue));
        Assert.Same(players[0], game.Winner);
        Assert.Equal(5, game.GetPlayerHand(players[1]).Count);
        Assert.False(game.CanChallengeDrawFour(players[1]));
        Assert.Equal(game.GetPlayerHand(players[1]).Sum(card => card.ScoreValue), game.GetPlayerScore(players[0]));
    }

    [Fact]
    public void ReshuffleDiscardIntoDraw_PreservesTopDiscard()
    {
        var game = new GameController([new Player(1, "Ada"), new Player(2, "Grace")], new Random(12));
        game.StartGame();
        game.Table.DrawPile.Cards.Clear();
        var preserved = new Card(3000, 4, 4, CardColor.Green, null);
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.AddRange([new Card(3001, 1, 1, CardColor.Red, null), preserved]);

        game.ReshuffleDiscardIntoDraw();

        Assert.Same(preserved, game.GetTopCard());
        Assert.Single(game.Table.DrawPile.Cards);
    }

    [Fact]
    public void EmptyDrawPile_StillAllowsPlayerToPass()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        GameController? game = null;
        for (var seed = 1; seed <= 100 && game is null; seed++)
        {
            var candidate = new GameController(players, new Random(seed));
            candidate.StartGame();
            if (candidate.GetTopCard()?.Action is null && candidate.GetCurrentPlayer() == players[0]) game = candidate;
        }
        Assert.NotNull(game);
        game.Table.DrawPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(4600, 5, 5, CardColor.Red, null));
        game.Table.ActiveColor = CardColor.Red;

        var current = game.GetCurrentPlayer();
        var next = game.GetNextPlayer();
        Assert.Null(game.DrawCard(current));
        Assert.True(game.HasDrawnThisTurn);
        game.PassTurn(current);
        Assert.Same(next, game.GetCurrentPlayer());
    }

    [Fact]
    public void AfterDrawingPlayerCanOnlyPlayTheDrawnCard()
    {
        var players = new List<Player> { new(1, "Ada"), new(2, "Grace") };
        var game = new GameController(players, new Random(47));
        game.StartGame();
        game.Table.DiscardPile.Cards.Clear();
        game.Table.DiscardPile.Cards.Add(new Card(4700, 5, 5, CardColor.Red, null));
        game.Table.ActiveColor = CardColor.Red;
        var current = game.GetCurrentPlayer();
        var existingMatch = new Card(4701, 2, 2, CardColor.Red, null);
        var drawnMatch = new Card(4702, 5, 5, CardColor.Blue, null);
        game.GetPlayerHand(current).Clear();
        game.GetPlayerHand(current).Add(existingMatch);
        game.Table.DrawPile.Cards.Clear();
        game.Table.DrawPile.Cards.Add(drawnMatch);

        Assert.Same(drawnMatch, game.DrawCard(current));
        Assert.False(game.PlayCard(current, existingMatch, null));
        Assert.True(game.PlayCard(current, drawnMatch, null));
    }
}
