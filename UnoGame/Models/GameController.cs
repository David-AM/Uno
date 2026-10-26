namespace UnoGame.Api.Models;

public sealed class GameController
{
    private readonly List<Player> _players;
    private readonly Dictionary<Player, List<Card>> _cardInHands;
    private readonly Dictionary<Player, int> _playerScores;
    private readonly Dictionary<Player, bool> _hasSaidUno;
    private readonly HashSet<Player> _forfeitedPlayers = [];
    private readonly List<Card> _cards;
    private int _currentPlayerIndex;
    private TurnDirection _direction = TurnDirection.Clockwise;
    private int _nextDrawPenalty;
    private Player? _drawFourPlayer;
    private CardColor _drawFourActiveColor;
    private bool _openingColorPending;
    private bool _skipNext;
    private bool _hasDrawnThisTurn;
    private Card? _drawnCardThisTurn;
    private bool _matchEnded;
    private int _nextCardId;
    private readonly Table _table;
    private readonly Random _random = new();
    public const int WinningScore = 500;
    public event Action<Player>? OnTurnChanged;
    public event Action<Player, Card>? OnCardPlayed;
    public event Action<Player, Card>? OnCardDrawn;
    public event Action<Player, int>? OnUnoPenalty;
    public event Action<Player>? OnGameEnded;
    public IReadOnlyList<Player> Players => _players;
    public Table Table => _table;
    public Player? Winner { get; private set; }
    public bool IsGameOver => Winner is not null;
    public bool IsMatchOver => _matchEnded || _players.Where(IsEligible).Any(player => _playerScores[player] >= WinningScore);
    public TurnDirection Direction => _direction;
    public bool HasDrawnThisTurn => _hasDrawnThisTurn;
    public int PendingDrawCount => _nextDrawPenalty;
    public bool RequiresOpeningColorChoice => _openingColorPending && ReferenceEquals(GetCurrentPlayer(), _players[_currentPlayerIndex]);
    public bool CanChallengeDrawFour(Player player) => _nextDrawPenalty == 4 && _drawFourPlayer is not null && ReferenceEquals(GetCurrentPlayer(), player);

    public GameController(
        List<IPlayer> players,
        List<ICard> cards,
        ITable table,
        IReadOnlyDictionary<Player, int>? previousScores = null,
        IReadOnlyCollection<Player>? forfeitedPlayers = null)
    {
        if (players is null)
        {
            throw new ArgumentNullException(nameof(players));
        }

        if (cards is null)
        {
            throw new ArgumentNullException(nameof(cards));
        }

        if (table is not Table concreteTable)
        {
            throw new ArgumentException("The table must be a Table instance.", nameof(table));
        }

        if (players.Count < 2 || players.Count > 4)
        {
            throw new ArgumentException("A game needs 2 to 4 players.", nameof(players));
        }

        _players = players.Select(player => player as Player
            ?? throw new ArgumentException("All players must be Player instances.", nameof(players))).ToList();
        _cards = cards.Select(card => card as Card
            ?? throw new ArgumentException("All cards must be Card instances.", nameof(cards))).ToList();
        _cardInHands = _players.ToDictionary(p => p, _ => new List<Card>());
        _playerScores = _players.ToDictionary(p => p, p => previousScores is not null && previousScores.TryGetValue(p, out var score) ? score : 0);
        _hasSaidUno = _players.ToDictionary(p => p, _ => false);
        if (forfeitedPlayers is not null)
        {
            _forfeitedPlayers.UnionWith(forfeitedPlayers);
        }
        _table = concreteTable;
    }

    public void StartGame()
    {
        _table.DrawPile.Cards.Clear();
        _table.DiscardPile.Cards.Clear();
        foreach (var hand in _cardInHands.Values)
        {
            hand.Clear();
        }

        foreach (var player in _hasSaidUno.Keys)
        {
            _hasSaidUno[player] = false;
        }
        _currentPlayerIndex = _players.FindIndex(IsEligible);
        _direction = TurnDirection.Clockwise;
        _nextDrawPenalty = 0;
        _drawFourPlayer = null;
        _drawFourActiveColor = default;
        _openingColorPending = false;
        _skipNext = false;
        _hasDrawnThisTurn = false;
        _drawnCardThisTurn = null;
        _matchEnded = false;
        Winner = null;
        InitializeStandardDeck();
        ShuffleDeck();
        foreach (var player in _players.Where(IsEligible))
        {
            for (var i = 0; i < 7; i++)
            {
                DrawCardFromDeckOnly(player);
            }
        }
        var first = DrawCardFromDeckOnly();
        // Wild Draw Four cannot be the opening discard. A regular Wild stays
        // visible and lets the first player choose the active color.
        while (first is not null && first.Action == ActionType.WildDrawFour)
        {
            _table.DrawPile.Cards.Add(first);
            ShuffleDeck();
            first = DrawCardFromDeckOnly();
        }
        if (first is null)
        {
            throw new InvalidOperationException("Unable to deal an opening card.");
        }
        _table.DiscardPile.Cards.Add(first);
        if (first.Action == ActionType.Wild)
        {
            _table.ActiveColor = CardColor.Wild;
            _openingColorPending = true;
        }
        else
        {
            _table.ActiveColor = first.Color;
            ApplyOpeningCard(first);
        }
        OnTurnChanged?.Invoke(GetCurrentPlayer());
    }
    private void InitializeStandardDeck()
    {
        _nextCardId = 0;

        foreach (var color in new[] { CardColor.Red, CardColor.Yellow, CardColor.Green, CardColor.Blue })
        {
            _table.DrawPile.Cards.Add(new Card(_nextCardId++, 0, 0, color, null));
            for (var value = 1; value <= 9; value++)
            {
                for (var copy = 0; copy < 2; copy++)
                {
                    _table.DrawPile.Cards.Add(new Card(_nextCardId++, value, value, color, null));
                }
            }
            foreach (var action in new[] { ActionType.Skip, ActionType.Reverse, ActionType.DrawTwo })
            {
                for (var copy = 0; copy < 2; copy++)
                {
                    _table.DrawPile.Cards.Add(new Card(_nextCardId++, null, 20, color, action));
                }
            }
        }
        for (var i = 0; i < 4; i++)
        {
            _table.DrawPile.Cards.Add(new Card(_nextCardId++, null, 50, CardColor.Wild, ActionType.Wild));
            _table.DrawPile.Cards.Add(new Card(_nextCardId++, null, 50, CardColor.Wild, ActionType.WildDrawFour));
        }
    }
    public bool CanPlayThisCard(Card card)
    {
        if (_nextDrawPenalty > 0 || _openingColorPending)
        {
            return false;
        }
        if (_table.DiscardPile.Cards.Count == 0)
        {
            return true;
        }
        var top = GetTopCard()!;
        // A Wild Draw Four may be played provisionally. The required player can
        // challenge it, and the player's hand is checked at challenge time.
        if (card.Color == CardColor.Wild)
        {
            return true;
        }
        return CardMatchesActivePlay(card, top, _table.ActiveColor);
    }
    public bool ChooseOpeningColor(Player player, CardColor color)
    {
        if (!_openingColorPending || !ReferenceEquals(GetCurrentPlayer(), player)
            || color is not (CardColor.Red or CardColor.Blue or CardColor.Green or CardColor.Yellow))
        {
            return false;
        }
        _table.ActiveColor = color;
        _openingColorPending = false;
        OnTurnChanged?.Invoke(player);
        return true;
    }
    public bool PlayCard(Player player, Card card, CardColor? chosenColor)
    {
        if (IsGameOver || !ReferenceEquals(GetCurrentPlayer(), player) || !HasCardInHand(player, card) ||
            (_hasDrawnThisTurn && (_drawnCardThisTurn is null || card.Id != _drawnCardThisTurn.Id)) || !CanPlayThisCard(card))
        {
            return false;
        }
        if (card.Color == CardColor.Wild && chosenColor is not (CardColor.Red or CardColor.Blue or CardColor.Green or CardColor.Yellow))
        {
            return false;
        }
        var previousActiveColor = _table.ActiveColor;
        RemoveCardFromHand(player, card);
        _table.DiscardPile.Cards.Add(card);
        _table.ActiveColor = card.Color == CardColor.Wild ? chosenColor!.Value : card.Color;
        OnCardPlayed?.Invoke(player, card);
        switch (card.Action)
        {
            case ActionType.Reverse:
                ReverseTurnDirection();
                // In a two-player game, Reverse has the same effect as Skip:
                // the other player is skipped and the current player plays again.
                if (EligiblePlayerCount == 2)
                {
                    _skipNext = true;
                }
                break;
            case ActionType.Skip:
                _skipNext = true;
                break;
            case ActionType.DrawTwo:
                _nextDrawPenalty += 2;
                break;
            case ActionType.WildDrawFour:
                _nextDrawPenalty = 4;
                // A final Wild Draw Four has no challenge window, but its
                // penalty still applies before the round score is calculated.
                if (_cardInHands[player].Count > 0)
                {
                    _drawFourPlayer = player;
                    _drawFourActiveColor = previousActiveColor;
                }
                break;
        }
        NextTurn();
        CheckGameWinner();
        return true;
    }
    public Card? DrawCard(Player player)
    {
        if (IsGameOver || !ReferenceEquals(GetCurrentPlayer(), player))
        {
            return null;
        }
        if (_nextDrawPenalty > 0)
        {
            var count = _nextDrawPenalty;
            _nextDrawPenalty = 0;
            _drawFourPlayer = null;
            ForceDrawCards(player, count);
            NextTurn();
            CheckGameWinner();
            return null;
        }
        var card = DrawCardFromDeckOnly(player);
        _hasSaidUno[player] = false;
        _hasDrawnThisTurn = true;
        _drawnCardThisTurn = card;
        if (card is not null)
        {
            OnCardDrawn?.Invoke(player, card);
        }
        return card;
    }
    public bool ChallengeDrawFour(Player challenger, out bool challengeSucceeded)
    {
        challengeSucceeded = false;
        if (IsGameOver || !CanChallengeDrawFour(challenger))
        {
            return false;
        }
        var playedBy = _drawFourPlayer!;
        challengeSucceeded = HasPlayableNonWildCard(GetPlayerHand(playedBy), GetTopCard()!, _drawFourActiveColor);
        _nextDrawPenalty = 0;
        _drawFourPlayer = null;
        if (challengeSucceeded)
        {
            ForceDrawCards(playedBy, 4);
            CheckGameWinner();
            OnTurnChanged?.Invoke(GetCurrentPlayer());
        }
        else
        {
            ForceDrawCards(challenger, 6);
            NextTurn();
            CheckGameWinner();
        }
        return true;
    }
    public void PassTurn(Player player)
    {
        if (!ReferenceEquals(GetCurrentPlayer(), player))
            throw new InvalidOperationException("It is not this player's turn.");
        if (!_hasDrawnThisTurn)
        {
            throw new InvalidOperationException("Draw a card before passing.");
        }
        _hasSaidUno[player] = false;
        NextTurn();
    }
    public void SayUno(Player player)
    {
        if (ReferenceEquals(GetCurrentPlayer(), player) && GetPlayerHand(player).Count == 2)
        {
            _hasSaidUno[player] = true;
        }
    }
    public void CatchPlayer(Player target)
    {
        if (GetPlayerHand(target).Count == 1 && !_hasSaidUno[target])
        {
            ForceDrawCards(target, 4);
            OnUnoPenalty?.Invoke(target, 4);
        }
    }
    public Player? CheckGameWinner()
    {
        var winner = _players.FirstOrDefault(p => IsEligible(p) && _cardInHands[p].Count == 0);
        if (winner is not null)
        {
            Winner = winner;
            var score = CalculateRoundScore(winner);
            _playerScores[winner] += score;
            if (_playerScores[winner] >= WinningScore)
            {
                _matchEnded = true;
                OnGameEnded?.Invoke(winner);
            }
        }
        return winner;
    }

    public bool ForfeitPlayer(Player player)
    {
        if (Winner is not null || !_players.Contains(player) || !_forfeitedPlayers.Add(player))
        {
            return false;
        }
        if (ReferenceEquals(GetCurrentPlayer(), player))
        {
            _nextDrawPenalty = 0;
            _drawFourPlayer = null;
            _skipNext = false;
            _hasDrawnThisTurn = false;
            _drawnCardThisTurn = null;
            MoveToNextEligible(1);
        }
        if (EligiblePlayerCount == 1)
        {
            Winner = _players.First(IsEligible);
            _matchEnded = true;
            OnGameEnded?.Invoke(Winner);
        }
        else if (EligiblePlayerCount > 1)
        {
            OnTurnChanged?.Invoke(GetCurrentPlayer());
        }
        return true;
    }
    public void NextTurn()
    {
        var steps = _skipNext ? 2 : 1;
        _skipNext = false;
        _hasDrawnThisTurn = false;
        _drawnCardThisTurn = null;
        MoveToNextEligible(steps);
        if (_nextDrawPenalty > 0 && _drawFourPlayer is null)
        {
            var target = GetCurrentPlayer();
            var count = _nextDrawPenalty;
            _nextDrawPenalty = 0;
            for (var i = 0; i < count; i++)
            {
                if (DrawCardFromDeckOnly(target) is { } card)
                {
                    OnCardDrawn?.Invoke(target, card);
                }
            }
            MoveToNextEligible(1);
        }
        OnTurnChanged?.Invoke(GetCurrentPlayer());
    }
    private Card? DrawCardFromDeckOnly(Player? player = null)
    {
        if (_table.DrawPile.Cards.Count == 0)
        {
            ReshuffleDiscardIntoDraw();
        }

        if (_table.DrawPile.Cards.Count == 0)
        {
            return null;
        }

        var index = _table.DrawPile.Cards.Count - 1;
        var card = _table.DrawPile.Cards[index];
        _table.DrawPile.Cards.RemoveAt(index);
        if (player is not null)
        {
            _cardInHands[player].Add(card);
        }
        return card;
    }
    public void ReverseTurnDirection()
    {
        _direction = _direction == TurnDirection.Clockwise
            ? TurnDirection.CounterClockwise
            : TurnDirection.Clockwise;
    }
    public void ForceDrawCards(Player target, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (DrawCardFromDeckOnly(target) is { } card)
            {
                OnCardDrawn?.Invoke(target, card);
            }
        }
    }
    public bool RemoveCardFromHand(Player player, Card card)
    {
        return _cardInHands.TryGetValue(player, out var hand) && hand.Remove(card);
    }

    public bool HasCardInHand(Player player, Card card)
    {
        return _cardInHands.TryGetValue(player, out var hand) && hand.Any(c => c.Id == card.Id);
    }

    public List<Card> GetPlayerHand(Player player)
    {
        return _cardInHands.TryGetValue(player, out var hand) ? hand : [];
    }
    public void ShuffleDeck()
    {
        for (var i = _table.DrawPile.Cards.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            var card = _table.DrawPile.Cards[i];
            _table.DrawPile.Cards[i] = _table.DrawPile.Cards[j];
            _table.DrawPile.Cards[j] = card;
        }
    }
    public void ReshuffleDiscardIntoDraw()
    {
        if (_table.DiscardPile.Cards.Count <= 1)
        {
            return;
        }
        var top = _table.DiscardPile.Cards[^1];
        _table.DrawPile.Cards.AddRange(_table.DiscardPile.Cards.Take(_table.DiscardPile.Cards.Count - 1));
        _table.DiscardPile.Cards.Clear();
        _table.DiscardPile.Cards.Add(top);
        ShuffleDeck();
    }
    public Player GetCurrentPlayer()
    {
        return _players[_currentPlayerIndex];
    }

    public CardColor GetActiveColor()
    {
        return _table.ActiveColor;
    }
    public Card? GetTopCard()
    {
        return _table.DiscardPile.Cards.LastOrDefault();
    }

    public int GetPlayerScore(Player player)
    {
        return _playerScores[player];
    }

    public IReadOnlyDictionary<Player, int> GetPlayerScores()
    {
        return _playerScores;
    }

    public IReadOnlyCollection<Player> ForfeitedPlayers => _forfeitedPlayers;

    public bool IsEligible(Player player)
    {
        return _players.Contains(player) && !_forfeitedPlayers.Contains(player);
    }

    public int EligiblePlayerCount => _players.Count(IsEligible);

    private int CalculateRoundScore(Player winner)
    {
        return _players
            .Where(player => IsEligible(player) && player != winner)
            .Sum(player => _cardInHands[player].Sum(card => card.ScoreValue));
    }

    private static bool CardMatchesActivePlay(Card card, Card top, CardColor activeColor)
    {
        return card.Color == activeColor
            || (card.Value is not null && card.Value == top.Value)
            || (card.Action is not null && card.Action == top.Action);
    }

    private static bool HasPlayableNonWildCard(IEnumerable<Card> hand, Card top, CardColor activeColor)
    {
        return hand.Any(card => card.Color != CardColor.Wild && CardMatchesActivePlay(card, top, activeColor));
    }
    private void MoveToNextEligible(int turns)
    {
        for (var turn = 0; turn < turns; turn++)
        {
            _currentPlayerIndex = FindNextEligibleIndex(_currentPlayerIndex);
        }
    }
    private int FindNextEligibleIndex(int currentIndex)
    {
        var index = currentIndex;
        do
        {
            index = (index + (int)_direction + _players.Count) % _players.Count;
        } while (!IsEligible(_players[index]));
        return index;
    }
    private void ApplyOpeningCard(Card card)
    {
        if (card.Action == ActionType.Reverse)
        {
            ReverseTurnDirection();
        }
        else if (card.Action == ActionType.Skip)
        {
            NextTurn();
        }
        else if (card.Action == ActionType.DrawTwo)
        {
            ForceDrawCards(GetCurrentPlayer(), 2);
            NextTurn();
        }
    }
}
