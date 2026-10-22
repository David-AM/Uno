namespace UnoGame.Api.Models;

public sealed class GameController
{
    private readonly List<Player> _players;
    private readonly Dictionary<Player, List<Card>> _cardInHands;
    private readonly Dictionary<Player, int> _playerScores;
    private readonly Dictionary<Player, bool> _hasSaidUno;
    private int _currentPlayerIndex;
    private TurnDirection _direction = TurnDirection.Clockwise;
    private int _nextDrawPenalty;
    private Player? _drawFourPlayer;
    private CardColor _drawFourActiveColor;
    private bool _skipNext;
    private bool _hasDrawnThisTurn;
    private int _nextCardId;
    private readonly Table _table;
    private readonly Random _random;
    public const int WinningScore = 500;
    public event Action<Player>? OnTurnChanged;
    public event Action<Player, Card>? OnCardPlayed;
    public event Action<CardColor>? OnColorChanged;
    public event Action<Player, int>? OnCardsDrawn;
    public event Action<Player, Card>? OnCardDrawn;
    public event Action<Player, int>? OnPenaltyCardsDrawn;
    public event Action<Player, int>? OnUnoPenalty;
    public event Action<Player, int>? OnRoundEnded;
    public event Action<Player>? OnGameEnded;
    public event Action<Player>? OnPlayerCardRemainOne;
    public IReadOnlyList<Player> Players => _players;
    public Table Table => _table;
    public Player? Winner { get; private set; }
    public bool IsGameOver => Winner is not null;
    public bool IsMatchOver => _players.Any(player => _playerScores[player] >= WinningScore);
    public int CurrentPlayerIndex => _currentPlayerIndex;
    public TurnDirection Direction => _direction;
    public bool HasDrawnThisTurn => _hasDrawnThisTurn;
    public int PendingDrawCount => _nextDrawPenalty;
    public bool CanChallengeDrawFour(Player player) => _nextDrawPenalty == 4 && _drawFourPlayer is not null && ReferenceEquals(GetCurrentPlayer(), player);

    public GameController(List<Player> players, Random? random = null, IReadOnlyDictionary<Player, int>? previousScores = null)
    {
        if (players.Count < 2 || players.Count > 10) throw new ArgumentException("A game needs 2 to 10 players.");
        _players = players;
        _cardInHands = players.ToDictionary(p => p, _ => new List<Card>());
        _playerScores = players.ToDictionary(p => p, p => previousScores is not null && previousScores.TryGetValue(p, out var score) ? score : 0);
        _hasSaidUno = players.ToDictionary(p => p, _ => false);
        _random = random ?? Random.Shared;
        _table = new Table(new Deck(), new DiscardPile());
    }

    public void StartGame() => StartRound();
    public void StartRound()
    {
        _table.DrawPile.Cards.Clear(); _table.DiscardPile.Cards.Clear();
        foreach (var hand in _cardInHands.Values) hand.Clear();
        foreach (var player in _hasSaidUno.Keys) _hasSaidUno[player] = false;
        _currentPlayerIndex = 0; _direction = TurnDirection.Clockwise; _nextDrawPenalty = 0; _drawFourPlayer = null; _drawFourActiveColor = default; _skipNext = false; _hasDrawnThisTurn = false; Winner = null;
        InitializeStandardDeck(); ShuffleDeck();
        foreach (var player in _players) for (var i = 0; i < 7; i++) DrawCardFromDeckOnly(player);
        var first = DrawCardFromDeckOnly();
        // Wild Draw Four cannot be the opening discard. Return any Wild card
        // to the draw pile and choose another opening card.
        while (first is not null && first.Color == CardColor.Wild)
        {
            _table.DrawPile.Cards.Add(first);
            ShuffleDeck();
            first = DrawCardFromDeckOnly();
        }
        if (first is null) throw new InvalidOperationException("Unable to deal an opening card.");
        _table.DiscardPile.Cards.Add(first); _table.ActiveColor = first.Color;
        ApplyOpeningCard(first); OnTurnChanged?.Invoke(GetCurrentPlayer());
    }
    private void InitializeStandardDeck()
    {
        _nextCardId = 0;
        foreach (var color in new[] { CardColor.Red, CardColor.Yellow, CardColor.Green, CardColor.Blue })
        {
            _table.DrawPile.Cards.Add(new Card(_nextCardId++, 0, 0, color, null));
            for (var value = 1; value <= 9; value++) for (var copy = 0; copy < 2; copy++) _table.DrawPile.Cards.Add(new Card(_nextCardId++, value, value, color, null));
            foreach (var action in new[] { ActionType.Skip, ActionType.Reverse, ActionType.DrawTwo })
                for (var copy = 0; copy < 2; copy++) _table.DrawPile.Cards.Add(new Card(_nextCardId++, null, 20, color, action));
        }
        for (var i = 0; i < 4; i++) { _table.DrawPile.Cards.Add(new Card(_nextCardId++, null, 50, CardColor.Wild, ActionType.Wild)); _table.DrawPile.Cards.Add(new Card(_nextCardId++, null, 50, CardColor.Wild, ActionType.WildDrawFour)); }
    }
    public bool CanPlayThisCard(Card card)
    {
        if (_nextDrawPenalty > 0) return false;
        if (_table.DiscardPile.Cards.Count == 0) return true;
        var top = GetTopCard()!;
        // A Wild Draw Four may be played provisionally. The required player can
        // challenge it, and the player's hand is checked at challenge time.
        if (card.Color == CardColor.Wild) return true;
        return card.Color == _table.ActiveColor || (card.Value is not null && card.Value == top.Value) || (card.Action is not null && card.Action == top.Action);
    }
    public bool PlayCard(Player player, Card card, CardColor? chosenColor)
    {
        if (IsGameOver || !ReferenceEquals(GetCurrentPlayer(), player) || !HasCardInHand(player, card) || !CanPlayThisCard(card)) return false;
        if (card.Color == CardColor.Wild && chosenColor is not (CardColor.Red or CardColor.Blue or CardColor.Green or CardColor.Yellow)) return false;
        var previousActiveColor = _table.ActiveColor;
        RemoveCardFromHand(player, card); _table.DiscardPile.Cards.Add(card);
        _table.ActiveColor = card.Color == CardColor.Wild ? chosenColor!.Value : card.Color;
        if (card.Color == CardColor.Wild) OnColorChanged?.Invoke(_table.ActiveColor);
        OnCardPlayed?.Invoke(player, card);
        if (_cardInHands[player].Count == 1)
        {
            if (_hasSaidUno[player]) OnPlayerCardRemainOne?.Invoke(player);
            // A player who reaches one card without calling UNO is vulnerable
            // to a catch, but the penalty is not applied automatically.
            else OnPlayerCardRemainOne?.Invoke(player);
        }
        // A final Wild Draw Four wins immediately; only non-final plays can be challenged.
        if ((card.Action != ActionType.WildDrawFour || _cardInHands[player].Count == 0) && CheckGameWinner() is not null) return true;
        switch (card.Action)
        {
            case ActionType.Reverse:
                ReverseTurnDirection();
                // In a two-player game, Reverse has the same effect as Skip:
                // the other player is skipped and the current player plays again.
                if (_players.Count == 2) _skipNext = true;
                break;
            case ActionType.Skip: _skipNext = true; break;
            case ActionType.DrawTwo: _nextDrawPenalty += 2; break;
            case ActionType.WildDrawFour:
                _nextDrawPenalty = 4; _drawFourPlayer = player; _drawFourActiveColor = previousActiveColor;
                break;
        }
        NextTurn(); return true;
    }
    public Card? DrawCard(Player player)
    {
        if (IsGameOver || !ReferenceEquals(GetCurrentPlayer(), player)) return null;
        if (_nextDrawPenalty > 0)
        {
            var count = _nextDrawPenalty;
            _nextDrawPenalty = 0; _drawFourPlayer = null;
            ForceDrawCards(player, count);
            OnPenaltyCardsDrawn?.Invoke(player, count);
            NextTurn();
            CheckGameWinner();
            return null;
        }
        var card = DrawCardFromDeckOnly(player);
        _hasSaidUno[player] = false;
        _hasDrawnThisTurn = true;
        if (card is not null) { OnCardDrawn?.Invoke(player, card); OnCardsDrawn?.Invoke(player, 1); }
        return card;
    }
    public bool ChallengeDrawFour(Player challenger, out bool challengeSucceeded)
    {
        challengeSucceeded = false;
        if (IsGameOver || !CanChallengeDrawFour(challenger)) return false;
        var playedBy = _drawFourPlayer!;
        challengeSucceeded = HasPlayableNonWildCard(GetPlayerHand(playedBy), GetTopCard()!, _drawFourActiveColor);
        _nextDrawPenalty = 0; _drawFourPlayer = null;
        if (challengeSucceeded)
        {
            ForceDrawCards(playedBy, 4);
            OnPenaltyCardsDrawn?.Invoke(playedBy, 4);
            CheckGameWinner();
            OnTurnChanged?.Invoke(GetCurrentPlayer());
        }
        else
        {
            ForceDrawCards(challenger, 6);
            OnPenaltyCardsDrawn?.Invoke(challenger, 6);
            NextTurn();
            CheckGameWinner();
        }
        return true;
    }
    public void PassTurn(Player player) { if (!ReferenceEquals(GetCurrentPlayer(), player)) throw new InvalidOperationException("It is not this player's turn."); if (!_hasDrawnThisTurn) throw new InvalidOperationException("Draw a card before passing."); _hasSaidUno[player] = false; NextTurn(); }
    public void SayUno(Player player) { if (ReferenceEquals(GetCurrentPlayer(), player) && GetPlayerHand(player).Count == 2) _hasSaidUno[player] = true; }
    public void CatchPlayer(Player caller, Player target)
    {
        if (GetPlayerHand(target).Count == 1 && !_hasSaidUno[target])
        {
            ForceDrawCards(target, 4);
            OnUnoPenalty?.Invoke(target, 4);
        }
    }
    public Player? CheckGameWinner()
    {
        var winner = _players.FirstOrDefault(p => _cardInHands[p].Count == 0);
        if (winner is not null) { Winner = winner; var score = _players.Where(p => p != winner).Sum(p => _cardInHands[p].Sum(c => c.ScoreValue)); _playerScores[winner] += score; OnRoundEnded?.Invoke(winner, score); if (_playerScores[winner] >= WinningScore) OnGameEnded?.Invoke(winner); }
        return winner;
    }
    public void NextTurn()
    {
        var steps = _skipNext ? 2 : 1; _skipNext = false;
        _hasDrawnThisTurn = false;
        _currentPlayerIndex = (_currentPlayerIndex + (int)_direction * steps + _players.Count) % _players.Count;
        if (_nextDrawPenalty > 0 && _drawFourPlayer is null)
        {
            var target = GetCurrentPlayer();
            var count = _nextDrawPenalty;
            _nextDrawPenalty = 0;
            for (var i = 0; i < count; i++) if (DrawCardFromDeckOnly(target) is { } card) OnCardDrawn?.Invoke(target, card);
            OnCardsDrawn?.Invoke(target, count);
            OnPenaltyCardsDrawn?.Invoke(target, count);
            _currentPlayerIndex = (_currentPlayerIndex + (int)_direction + _players.Count) % _players.Count;
        }
        OnTurnChanged?.Invoke(GetCurrentPlayer());
    }
    private Card? DrawCardFromDeckOnly(Player? player = null)
    {
        if (_table.DrawPile.Cards.Count == 0) ReshuffleDiscardIntoDraw();
        if (_table.DrawPile.Cards.Count == 0) return null;
        var index = _table.DrawPile.Cards.Count - 1; var card = _table.DrawPile.Cards[index]; _table.DrawPile.Cards.RemoveAt(index);
        if (player is not null) _cardInHands[player].Add(card);
        return card;
    }
    public void ReverseTurnDirection() => _direction = _direction == TurnDirection.Clockwise ? TurnDirection.CounterClockwise : TurnDirection.Clockwise;
    public void SkipNextTurn() => _skipNext = true;
    public void ForceDrawCards(Player target, int count) { for (var i = 0; i < count; i++) if (DrawCardFromDeckOnly(target) is { } card) OnCardDrawn?.Invoke(target, card); OnCardsDrawn?.Invoke(target, count); }
    public bool RemoveCardFromHand(Player player, Card card) => _cardInHands.TryGetValue(player, out var hand) && hand.Remove(card);
    public bool HasCardInHand(Player player, Card card) => _cardInHands.TryGetValue(player, out var hand) && hand.Any(c => c.Id == card.Id);
    public List<Card> GetPlayerHand(Player player) => _cardInHands.TryGetValue(player, out var hand) ? hand : [];
    public void ShuffleDeck() { for (var i = _table.DrawPile.Cards.Count - 1; i > 0; i--) { var j = _random.Next(i + 1); (_table.DrawPile.Cards[i], _table.DrawPile.Cards[j]) = (_table.DrawPile.Cards[j], _table.DrawPile.Cards[i]); } }
    public void ReshuffleDiscardIntoDraw() { if (_table.DiscardPile.Cards.Count <= 1) return; var top = _table.DiscardPile.Cards[^1]; _table.DrawPile.Cards.AddRange(_table.DiscardPile.Cards.Take(_table.DiscardPile.Cards.Count - 1)); _table.DiscardPile.Cards.Clear(); _table.DiscardPile.Cards.Add(top); ShuffleDeck(); }
    public Player GetCurrentPlayer() => _players[_currentPlayerIndex];
    public CardColor GetActiveColor() => _table.ActiveColor;
    public Player GetNextPlayer() => _players[(_currentPlayerIndex + (int)_direction + _players.Count) % _players.Count];
    public Card? GetTopCard() => _table.DiscardPile.Cards.LastOrDefault();
    public int CalculateRoundScore(Player winner) => _players.Where(p => p != winner).Sum(p => _cardInHands[p].Sum(c => c.ScoreValue));
    public int GetPlayerScore(Player player) => _playerScores[player];
    public IReadOnlyDictionary<Player, int> GetPlayerScores() => _playerScores;
    private static bool HasPlayableNonWildCard(IEnumerable<Card> hand, Card top, CardColor activeColor) => hand.Any(card => card.Color != CardColor.Wild &&
        (card.Color == activeColor || (card.Value is not null && card.Value == top.Value) || (card.Action is not null && card.Action == top.Action)));
    private void ApplyOpeningCard(Card card) { if (card.Action == ActionType.Reverse) ReverseTurnDirection(); else if (card.Action == ActionType.Skip) NextTurn(); else if (card.Action == ActionType.DrawTwo) { ForceDrawCards(GetCurrentPlayer(), 2); NextTurn(); } }
}
