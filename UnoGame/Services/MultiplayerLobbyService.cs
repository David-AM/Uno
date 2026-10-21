using UnoGame.Api.DTOs;
using UnoGame.Api.Models;

namespace UnoGame.Api.Services;

public interface IMultiplayerLobbyService
{
    LobbyResponse GetLobby();
    (JoinLobbyResponse? Result, string? Error) Join(string playerName, int opponentCount, bool hostLobby);
    (GameViewDto? Result, string? Error) Start(int playerId);
    GameViewDto? GetGame(int playerId);
    (GameViewDto? Result, string? Error) Play(int playerId, int cardId, CardColor? chosenColor);
    (GameViewDto? Result, string? Error) Draw(int playerId);
    (GameViewDto? Result, string? Error) Pass(int playerId);
    (GameViewDto? Result, string? Error) SayUno(int playerId);
    (GameViewDto? Result, string? Error) CatchUno(int playerId, int targetPlayerId);
    GameViewDto? Restart(int playerId, bool resetScores = false);
    void RemoveDisconnectedPlayer(int playerId);
}

public sealed class MultiplayerLobbyService : IMultiplayerLobbyService
{
    private const string LobbyId = "demo";
    private const int MaxOpponents = 3;
    private readonly object _gate = new();
    private readonly List<Player> _players = [];
    private readonly Dictionary<int, List<GameEventDto>> _recentEvents = [];
    private GameController? _game;
    private int _nextPlayerId = 1;
    private int _maxPlayers = MaxOpponents + 1;
    private int _roundNumber = 1;

    public LobbyResponse GetLobby() { lock (_gate) return Lobby(); }

    public (JoinLobbyResponse? Result, string? Error) Join(string playerName, int opponentCount, bool hostLobby)
    {
        lock (_gate)
        {
            if (_game is not null) return (null, "The game has already started.");
            if (_players.Count == 0 && !hostLobby) return (null, "There is no lobby available to join.");
            if (_players.Count > 0 && hostLobby) return (null, "A lobby already exists. Join it or wait for it to close.");
            if (_players.Count == 0) _maxPlayers = Math.Clamp(opponentCount, 1, MaxOpponents) + 1;
            if (_players.Count >= _maxPlayers) return (null, "The lobby is full.");
            var player = new Player(_nextPlayerId++, playerName.Trim());
            _players.Add(player);
            _recentEvents[player.Id] = [];
            return (new JoinLobbyResponse(player.Id, _players.Count == 1, Lobby(), null), null);
        }
    }

    public (GameViewDto? Result, string? Error) Start(int playerId)
    {
        lock (_gate)
        {
            if (_players.Count < 2) return (null, "At least 2 players are required to start.");
            if (_players[0].Id != playerId) return (null, "Only the host can start the game.");
            if (_game is not null) return (null, "The game has already started.");
            _game = new GameController(_players);
            _roundNumber = 1;
            WatchGameEvents(_game);
            _game.StartGame();
            QueueTableEvent();
            return (View(playerId, consumeEvents: false), null);
        }
    }

    public GameViewDto? GetGame(int playerId)
    {
        lock (_gate) return _game is null || Find(playerId) is null ? null : View(playerId);
    }

    public (GameViewDto? Result, string? Error) Play(int playerId, int cardId, CardColor? chosenColor)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error)) return (null, error);
            var card = _game!.GetPlayerHand(player).FirstOrDefault(c => c.Id == cardId);
            if (card is null) return (View(playerId), "That card is not in your hand.");
            if (!_game.PlayCard(player, card, chosenColor)) return (View(playerId), "That card cannot be played now.");
            return (View(playerId, consumeEvents: false), null);
        }
    }

    public (GameViewDto? Result, string? Error) Draw(int playerId)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error)) return (null, error);
            if (_game!.GetCurrentPlayer() != player) return (View(playerId), "It is not your turn.");
            _game.DrawCard(player);
            return (View(playerId, consumeEvents: false), null);
        }
    }

    public (GameViewDto? Result, string? Error) Pass(int playerId)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error)) return (null, error);
            if (_game!.GetCurrentPlayer() != player) return (View(playerId), "It is not your turn.");
            if (!_game.HasDrawnThisTurn) return (View(playerId), "Draw a card before passing.");
            _game.PassTurn(player);
            return (View(playerId, consumeEvents: false), null);
        }
    }

    public (GameViewDto? Result, string? Error) SayUno(int playerId)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error)) return (null, error);
            if (_game!.GetCurrentPlayer() != player || _game.GetPlayerHand(player).Count != 2)
                return (View(playerId), "Call UNO on your turn with two cards.");
            _game.SayUno(player);
            QueueEvent(new GameEventDto(
                "alert", player.Id, player.Name, null, $"{player.Name} called UNO."));
            return (View(playerId, consumeEvents: false), null);
        }
    }

    public (GameViewDto? Result, string? Error) CatchUno(int playerId, int targetPlayerId)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error)) return (null, error);
            var target = Find(targetPlayerId);
            if (target is null) return (View(playerId), "That player does not exist.");
            _game!.CatchPlayer(player, target);
            return (View(playerId, consumeEvents: false), null);
        }
    }

    public GameViewDto? Restart(int playerId, bool resetScores = false)
    {
        lock (_gate)
        {
            if (_game is null || _players.Count == 0 || _players[0].Id != playerId) return null;
            var newMatch = resetScores || _game.IsMatchOver;
            var previousScores = newMatch ? null : _game.GetPlayerScores();
            _roundNumber = newMatch ? 1 : _roundNumber + 1;
            foreach (var events in _recentEvents.Values) events.Clear();
            _game = new GameController(_players, previousScores: previousScores);
            WatchGameEvents(_game);
            _game.StartGame();
            QueueTableEvent();
            return View(playerId, consumeEvents: false);
        }
    }

    public void RemoveDisconnectedPlayer(int playerId)
    {
        lock (_gate)
        {
            // Players cannot be removed safely after a game starts because the
            // active GameController still owns their hand and turn order.
            if (_game is not null) return;

            var player = _players.FirstOrDefault(p => p.Id == playerId);
            if (player is null) return;
            _players.Remove(player);
            _recentEvents.Remove(playerId);
            if (_players.Count == 0)
            {
                _nextPlayerId = 1;
                _maxPlayers = MaxOpponents + 1;
            }
        }
    }

    private bool TryGetGamePlayer(int id, out Player player, out string error)
    {
        player = Find(id)!;
        error = string.Empty;
        if (_game is null) error = "The game has not started.";
        else if (player is null) error = "That player is not in the lobby.";
        return error.Length == 0;
    }

    private Player? Find(int id) => _players.FirstOrDefault(p => p.Id == id);
    private LobbyResponse Lobby() => new(LobbyId, _game is not null, 2, _maxPlayers,
        _players.FirstOrDefault()?.Id, _players.Select(p => new LobbyPlayerResponse(p.Id, p.Name)).ToArray());

    private GameViewDto View(int humanPlayerId, bool consumeEvents = true)
    {
        var human = Find(humanPlayerId)!;
        var game = _game!;
        var events = _recentEvents.TryGetValue(humanPlayerId, out var playerEvents) ? playerEvents.ToArray() : Array.Empty<GameEventDto>();
        if (consumeEvents && playerEvents is not null) playerEvents.Clear();
        return new GameViewDto(LobbyId, game.IsMatchOver ? "completed" : game.IsGameOver ? "round-complete" : "playing",
            game.Winner?.Name, human.Id,
            _players.Select(p => new PlayerDto(p.Id, p.Name, game.GetPlayerHand(p).Count,
                p == game.GetCurrentPlayer(), p == human, game.GetPlayerScore(p))).ToArray(),
            game.GetPlayerHand(human).Select(CardDto.From).ToArray(),
            game.GetTopCard() is { } top ? CardDto.From(top) : null, game.GetActiveColor(),
            game.Table.DrawPile.TotalCards, game.HasDrawnThisTurn, game.Direction.ToString(), null, null, 0,
            events.Where(e => e.Type == "play" && e.PlayerId is not null)
                .Select(e => new PlayerPlayDto(e.PlayerId!.Value, e.PlayerName!, e.Card!)).ToArray(), events, _roundNumber);
    }

    private void WatchGameEvents(GameController game)
    {
        game.OnCardPlayed += (player, card) => QueueEvent(new GameEventDto(
            "play", player.Id, player.Name, CardDto.From(card), $"{player.Name} played a card to the discard pile."));
        // Broadcast the animation event to every player, but do not include the
        // drawn card because opponents must not see another player's hand.
        game.OnCardDrawn += (player, _) => QueueEvent(new GameEventDto(
            "draw", player.Id, player.Name, null, $"{player.Name} drew a card from the draw pile."));
        game.OnUnoPenalty += (player, count) => QueueEvent(new GameEventDto(
            "alert", player.Id, player.Name, null, $"{player.Name} was caught without calling UNO and drew {count} cards."));
        game.OnTurnChanged += player => QueueEvent(new GameEventDto(
            "alert", player.Id, player.Name, null, $"{player.Name}'s turn."));
    }

    private void QueueTableEvent()
    {
        if (_game?.GetTopCard() is { } top)
            QueueEvent(new GameEventDto("table", null, null, CardDto.From(top), string.Empty));
    }

    private void QueueEvent(GameEventDto gameEvent, int? recipientPlayerId = null)
    {
        if (recipientPlayerId is { } recipient && _recentEvents.TryGetValue(recipient, out var recipientEvents))
        {
            recipientEvents.Add(gameEvent);
            return;
        }

        foreach (var events in _recentEvents.Values) events.Add(gameEvent);
    }
}
