using UnoGame.Api.DTOs;
using UnoGame.Api.Models;
using System.Security.Cryptography;
using System.Text;

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
    (GameViewDto? Result, string? Error) ChallengeDrawFour(int playerId);
    (GameViewDto? Result, string? Error) ChooseOpeningColor(int playerId, CardColor color);
    (bool Result, string? Error) LeaveLobby(int playerId);
    bool RegisterConnection(int playerId, string reconnectToken, string connectionId, out string error);
    bool IsReconnectTokenValid(int playerId, string reconnectToken);
    bool IsConnectionAuthorized(int playerId, string connectionId);
    bool DisconnectConnection(string connectionId);
    bool ProcessExpiredDisconnections();
    IReadOnlyList<(string ConnectionId, GameViewDto Game)> GetConnectedGameViews(bool consumeEvents = true);
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
    private readonly Dictionary<int, ConnectionState> _connectionStates = [];
    private GameController? _game;
    private int _nextPlayerId = 1;
    private int _maxPlayers = MaxOpponents + 1;
    private int _roundNumber = 1;
    private readonly TimeSpan _reconnectGracePeriod;
    private readonly Func<DateTime> _utcNow;
    public const int ReconnectGracePeriodSeconds = 60;

    private sealed class ConnectionState(string reconnectToken)
    {
        public string ReconnectToken { get; } = reconnectToken;
        public string? ConnectionId { get; set; }
        public DateTime? DisconnectedAtUtc { get; set; }
        public DateTime? DeadlineUtc { get; set; }
        public bool IsForfeited { get; set; }
        public bool IsConnected => ConnectionId is not null;
    }

    public MultiplayerLobbyService() : this(TimeSpan.FromSeconds(ReconnectGracePeriodSeconds)) { }
    public MultiplayerLobbyService(IConfiguration configuration) : this(
        TimeSpan.FromSeconds(configuration.GetValue("Multiplayer:ReconnectGracePeriodSeconds", ReconnectGracePeriodSeconds))) { }
    public MultiplayerLobbyService(TimeSpan reconnectGracePeriod, Func<DateTime>? utcNow = null)
    {
        _reconnectGracePeriod = reconnectGracePeriod;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public LobbyResponse GetLobby() { lock (_gate) return Lobby(); }

    public (JoinLobbyResponse? Result, string? Error) Join(string playerName, int opponentCount, bool hostLobby)
    {
        lock (_gate)
        {
            var normalizedName = playerName.Trim();
            if (_game is not null)
            {
                var returningPlayer = _players.FirstOrDefault(player =>
                    string.Equals(player.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
                if (returningPlayer is null)
                    return (null, "The game has already started. Only an existing player can rejoin.");

                var connectionState = _connectionStates[returningPlayer.Id];
                if (connectionState.IsForfeited)
                    return (null, "You missed the 60-second reconnection window. You have forfeited this match.");

                return (new JoinLobbyResponse(
                    returningPlayer.Id,
                    _players[0].Id == returningPlayer.Id,
                    Lobby(),
                    View(returningPlayer.Id, consumeEvents: false),
                    connectionState.ReconnectToken), null);
            }
            if (_players.Count == 0 && !hostLobby) return (null, "There is no lobby available to join.");
            if (_players.Count > 0 && hostLobby) return (null, "A lobby already exists. Join it or wait for it to close.");
            if (_players.Count == 0) _maxPlayers = Math.Clamp(opponentCount, 1, MaxOpponents) + 1;
            if (_players.Count >= _maxPlayers) return (null, "The lobby is full.");
            var player = new Player(_nextPlayerId++, normalizedName);
            _players.Add(player);
            _recentEvents[player.Id] = [];
            var reconnectToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _connectionStates[player.Id] = new ConnectionState(reconnectToken);
            return (new JoinLobbyResponse(player.Id, _players.Count == 1, Lobby(), null, reconnectToken), null);
        }
    }

    public (bool Result, string? Error) LeaveLobby(int playerId)
    {
        lock (_gate)
        {
            if (_game is not null) return (false, "The game has already started.");
            if (Find(playerId) is null) return (false, "You are not in the lobby.");
            RemovePlayerLocked(playerId);
            return (true, null);
        }
    }

    public bool RegisterConnection(int playerId, string reconnectToken, string connectionId, out string error)
    {
        lock (_gate)
        {
            error = string.Empty;
            if (!_connectionStates.TryGetValue(playerId, out var state) || !TokenMatches(state, reconnectToken))
            {
                error = "The reconnect identity is invalid.";
                return false;
            }
            if (state.IsForfeited)
            {
                error = "You missed the 60-second reconnection window. You have forfeited this match.";
                return false;
            }
            // Keep the reconnect token valid after a round or match ends so a
            // refreshed client can still return to the result screen. A player
            // who missed the reconnect grace period is still rejected above.
            state.ConnectionId = connectionId;
            state.DisconnectedAtUtc = null;
            state.DeadlineUtc = null;
            return true;
        }
    }

    public bool IsReconnectTokenValid(int playerId, string reconnectToken)
    {
        lock (_gate) return _connectionStates.TryGetValue(playerId, out var state) && TokenMatches(state, reconnectToken) && !state.IsForfeited;
    }

    public bool IsConnectionAuthorized(int playerId, string connectionId)
    {
        lock (_gate) return _connectionStates.TryGetValue(playerId, out var state) && state.ConnectionId == connectionId && !state.IsForfeited;
    }

    public bool DisconnectConnection(string connectionId)
    {
        lock (_gate)
        {
            var connection = _connectionStates.FirstOrDefault(pair => pair.Value.ConnectionId == connectionId);
            if (connection.Value is null) return false;
            var state = connection.Value;
            if (_game is null)
            {
                RemovePlayerLocked(connection.Key);
                return true;
            }
            state.ConnectionId = null;
            if (_game is not null && !_game.IsGameOver)
            {
                state.DisconnectedAtUtc = _utcNow();
                state.DeadlineUtc = state.DisconnectedAtUtc.Value.Add(_reconnectGracePeriod);
            }
            return true;
        }
    }

    public bool ProcessExpiredDisconnections()
    {
        lock (_gate)
        {
            if (_game is null || _game.IsGameOver) return false;
            var expiredIds = _connectionStates
                .Where(pair => !pair.Value.IsConnected && !pair.Value.IsForfeited && pair.Value.DeadlineUtc <= _utcNow())
                .Select(pair => pair.Key)
                .ToHashSet();
            if (expiredIds.Count == 0) return false;

            var expiredPlayers = _players.Where(player => expiredIds.Contains(player.Id)).ToArray();
            foreach (var player in expiredPlayers) _connectionStates[player.Id].IsForfeited = true;
            var remainingPlayers = _players.Where(player => !expiredIds.Contains(player.Id)).ToList();
            if (remainingPlayers.Count > 0)
                _game.EndByForfeit(remainingPlayers);

            foreach (var player in expiredPlayers)
            {
                _players.Remove(player);
                _recentEvents.Remove(player.Id);
                _connectionStates.Remove(player.Id);
            }

            // The first remaining player becomes the new host. Restart() will
            // use this ordering when the host starts the next round.
            _players.Clear();
            _players.AddRange(remainingPlayers);
            foreach (var events in _recentEvents.Values) events.Clear();
            QueueEvent(new GameEventDto("alert", null, null, null,
                $"{string.Join(", ", expiredPlayers.Select(player => player.Name))} forfeited. Their card scores were awarded to the remaining players. The host can start the next round."));
            return true;
        }
    }

    public IReadOnlyList<(string ConnectionId, GameViewDto Game)> GetConnectedGameViews(bool consumeEvents = true)
    {
        lock (_gate)
        {
            if (_game is null) return Array.Empty<(string ConnectionId, GameViewDto Game)>();
            return _connectionStates
                .Where(pair => pair.Value.ConnectionId is not null)
                .Select(pair => (pair.Value.ConnectionId!, View(pair.Key, consumeEvents, includeEvents: consumeEvents)))
                .ToArray();
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
            foreach (var state in _connectionStates.Values.Where(state => !state.IsConnected && !state.IsForfeited))
            {
                state.DisconnectedAtUtc = _utcNow();
                state.DeadlineUtc = state.DisconnectedAtUtc.Value.Add(_reconnectGracePeriod);
            }
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

    public (GameViewDto? Result, string? Error) ChallengeDrawFour(int playerId)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error)) return (null, error);
            if (!_game!.CanChallengeDrawFour(player)) return (View(playerId), "You can only challenge the player who just played a Wild Draw Four.");
            _game.ChallengeDrawFour(player, out var succeeded);
            QueueEvent(new GameEventDto("alert", player.Id, player.Name, null,
                succeeded ? $"{player.Name}'s challenge succeeded. The Wild Draw Four player drew 4 cards." : $"{player.Name}'s challenge failed. {player.Name} drew 6 cards."));
            return (View(playerId, consumeEvents: false), null);
        }
    }

    public (GameViewDto? Result, string? Error) ChooseOpeningColor(int playerId, CardColor color)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error)) return (null, error);
            if (!_game!.ChooseOpeningColor(player, color)) return (View(playerId), "Choose a valid color before playing.");
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
            foreach (var state in _connectionStates.Values.Where(state => !state.IsConnected && !state.IsForfeited))
            {
                state.DisconnectedAtUtc = _utcNow();
                state.DeadlineUtc = state.DisconnectedAtUtc.Value.Add(_reconnectGracePeriod);
            }
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

            RemovePlayerLocked(playerId);
        }
    }

    private void RemovePlayerLocked(int playerId)
    {
        var player = _players.FirstOrDefault(p => p.Id == playerId);
        if (player is null) return;
        _players.Remove(player);
        _recentEvents.Remove(playerId);
        _connectionStates.Remove(playerId);
        if (_players.Count == 0)
        {
            _nextPlayerId = 1;
            _maxPlayers = MaxOpponents + 1;
        }
    }

    private bool TryGetGamePlayer(int id, out Player player, out string error)
    {
        player = Find(id)!;
        error = string.Empty;
        if (_game is null) error = "The game has not started.";
        else if (player is null) error = "That player is not in the lobby.";
        else if (!_connectionStates.TryGetValue(id, out var state) || !state.IsConnected || state.IsForfeited) error = "You are disconnected from the match.";
        else if (IsMatchPaused()) error = "The match is paused while a player reconnects.";
        return error.Length == 0;
    }

    private Player? Find(int id) => _players.FirstOrDefault(p => p.Id == id);
    private LobbyResponse Lobby() => new(LobbyId, _game is not null, 2, _maxPlayers,
        _players.FirstOrDefault()?.Id, _players.Select(p =>
        {
            var state = _connectionStates[p.Id];
            var seconds = !state.IsConnected && state.DeadlineUtc is { } deadline
                ? Math.Max(0, (int)Math.Ceiling((deadline - _utcNow()).TotalSeconds)) : (int?)null;
            return new LobbyPlayerResponse(p.Id, p.Name, state.IsConnected, seconds);
        }).ToArray());

    private GameViewDto View(int humanPlayerId, bool consumeEvents = true, bool includeEvents = true)
    {
        var human = Find(humanPlayerId) ?? throw new InvalidOperationException("The player is not in the lobby.");
        GameController game = _game ?? throw new InvalidOperationException("The game has not started.");
        var playerEvents = _recentEvents.TryGetValue(humanPlayerId, out var queuedEvents) ? queuedEvents : null;
        var events = includeEvents && playerEvents is not null ? playerEvents.ToArray() : Array.Empty<GameEventDto>();
        if (consumeEvents && playerEvents is not null) playerEvents.Clear();
        var pausedPlayer = _connectionStates.FirstOrDefault(pair => !pair.Value.IsConnected && !pair.Value.IsForfeited);
        var isPaused = game is not null && !game.IsGameOver && pausedPlayer.Key != 0;
        var pausedState = pausedPlayer.Key == 0 ? null : pausedPlayer.Value;
        var remaining = isPaused && pausedState?.DeadlineUtc is { } deadline
            ? Math.Max(0, (int)Math.Ceiling((deadline - _utcNow()).TotalSeconds)) : (int?)null;
        var currentPlayer = game!.IsGameOver ? null : game.GetCurrentPlayer();
        var openingColorPending = !game.IsGameOver && game.RequiresOpeningColorChoice;
        var canChallenge = !game.IsGameOver && game.CanChallengeDrawFour(human);
        return new GameViewDto(LobbyId, game!.IsMatchOver ? "completed" : game.IsGameOver ? "round-complete" : "playing",
            game.Winner?.Name, human.Id,
            _players.Select(p =>
            {
                var state = _connectionStates.TryGetValue(p.Id, out var connectionState) ? connectionState : null;
                return new PlayerDto(p.Id, p.Name, game.GetPlayerHand(p).Count,
                    p == currentPlayer, p == human, game.GetPlayerScore(p),
                    state?.IsConnected == true, state?.IsForfeited == true);
            }).ToArray(),
            game.GetPlayerHand(human).Select(CardDto.From).ToArray(),
            game.GetTopCard() is { } top ? CardDto.From(top) : null, game.GetActiveColor(),
            game.Table.DrawPile.TotalCards, game.HasDrawnThisTurn, canChallenge, game.PendingDrawCount, openingColorPending && currentPlayer == human, game.Direction.ToString(), null, null, 0,
            events.Where(e => e.Type == "play" && e.PlayerId is not null)
                .Select(e => new PlayerPlayDto(e.PlayerId!.Value, e.PlayerName!, e.Card!)).ToArray(), events, _roundNumber,
            isPaused, pausedPlayer.Key == 0 ? null : _players.First(p => p.Id == pausedPlayer.Key).Name, remaining);
    }

    private bool IsMatchPaused() => _game is not null && !_game.IsGameOver && _connectionStates.Values.Any(state => !state.IsConnected && !state.IsForfeited);
    private static bool TokenMatches(ConnectionState state, string reconnectToken) =>
        !string.IsNullOrEmpty(reconnectToken) && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(state.ReconnectToken), Encoding.UTF8.GetBytes(reconnectToken));

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
        game.OnGameEnded += player => QueueEvent(new GameEventDto(
            "alert", player.Id, player.Name, null, $"{player.Name}'s round has ended."));
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
