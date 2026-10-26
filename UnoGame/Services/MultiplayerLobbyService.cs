using UnoGame.Api.DTOs;
using UnoGame.Api.Models;
using System.Security.Cryptography;
using System.Text;

namespace UnoGame.Api.Services;

public interface IMultiplayerLobbyService
{
    LobbyResponse GetLobby();
    OperationResult<JoinLobbyResponse> Join(string playerName, int opponentCount, bool hostLobby);
    OperationResult<GameViewDto> Start(int playerId);
    GameViewDto? GetGame(int playerId);
    OperationResult<GameViewDto> Play(int playerId, int cardId, CardColor? chosenColor);
    OperationResult<GameViewDto> Draw(int playerId);
    OperationResult<GameViewDto> Pass(int playerId);
    OperationResult<GameViewDto> SayUno(int playerId);
    OperationResult<GameViewDto> CatchUno(int playerId, int targetPlayerId);
    OperationResult<GameViewDto> ChallengeDrawFour(int playerId);
    OperationResult<GameViewDto> ChooseOpeningColor(int playerId, CardColor color);
    OperationResult<bool> LeaveLobby(int playerId);
    OperationResult<bool> ReturnToLobby(int playerId);
    bool RegisterConnection(int playerId, string reconnectToken, string connectionId, out string error);
    bool IsReconnectTokenValid(int playerId, string reconnectToken);
    bool IsConnectionAuthorized(int playerId, string connectionId);
    bool DisconnectConnection(string connectionId);
    bool ProcessExpiredDisconnections();
    IReadOnlyList<ConnectedGameSnapshot> GetConnectedGameViews(bool consumeEvents = true);
    GameViewDto? Restart(int playerId, bool resetScores = false);
}

public sealed class MultiplayerLobbyService : IMultiplayerLobbyService
{
    private const string LobbyId = MultiplayerConstants.LobbyId;
    private const int MaxOpponents = 3;
    private readonly object _gate = new();
    private readonly List<Player> _players = [];
    private readonly Dictionary<int, List<GameEventDto>> _recentEvents = [];
    private readonly Dictionary<int, ConnectionState> _connectionStates = [];
    // ConnectionState.IsForfeited and GameController.ForfeitedPlayers are two
    // coordinated sources of truth and must change together.
    private GameController? _game;
    private int? _hostPlayerId;
    private int _nextPlayerId = 1;
    private int _maxPlayers = MaxOpponents + 1;
    private int _roundNumber = 1;
    private readonly TimeSpan _reconnectGracePeriod;
    private readonly Func<DateTime> _utcNow;
    public const int ReconnectGracePeriodSeconds = 60;

    private GameController CreateGameController(
        IReadOnlyDictionary<Player, int>? previousScores = null,
        IReadOnlyCollection<Player>? forfeitedPlayers = null)
    {
        var players = _players.Cast<IPlayer>().ToList();
        var cards = new List<ICard>();
        var table = new Table(new Deck(), new DiscardPile());
        return new GameController(players, cards, table, previousScores, forfeitedPlayers);
    }

    private sealed class ConnectionState(string reconnectToken)
    {
        public string ReconnectToken { get; } = reconnectToken;
        public string? ConnectionId { get; set; }
        public DateTime? DisconnectedAtUtc { get; set; }
        public DateTime? DeadlineUtc { get; set; }
        public bool IsForfeited { get; set; }
        public bool IsConnected => ConnectionId is not null;
    }

    private sealed record PauseInfo(bool IsPaused, string? PausedPlayerName, int? SecondsRemaining);

    public MultiplayerLobbyService() : this(TimeSpan.FromSeconds(ReconnectGracePeriodSeconds))
    {
    }

    public MultiplayerLobbyService(IConfiguration configuration) : this(
        TimeSpan.FromSeconds(configuration.GetValue("Multiplayer:ReconnectGracePeriodSeconds", ReconnectGracePeriodSeconds)))
    {
    }
    public MultiplayerLobbyService(TimeSpan reconnectGracePeriod, Func<DateTime>? utcNow = null)
    {
        _reconnectGracePeriod = reconnectGracePeriod;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public LobbyResponse GetLobby()
    {
        lock (_gate)
        {
            return Lobby();
        }
    }

    public OperationResult<JoinLobbyResponse> Join(string playerName, int opponentCount, bool hostLobby)
    {
        lock (_gate)
        {
            var normalizedName = playerName.Trim();
            if (_game is not null)
            {
                var returningPlayer = _players.FirstOrDefault(player =>
                    string.Equals(player.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
                if (returningPlayer is null)
                {
                    return OperationResult<JoinLobbyResponse>.Failure("The game has already started. Only an existing player can rejoin.");
                }

                var connectionState = _connectionStates[returningPlayer.Id];
                if (connectionState.IsForfeited)
                {
                    return OperationResult<JoinLobbyResponse>.Failure("You missed the 60-second reconnection window. You have forfeited this match.");
                }

                return OperationResult<JoinLobbyResponse>.Success(new JoinLobbyResponse(
                    returningPlayer.Id,
                    _hostPlayerId == returningPlayer.Id,
                    Lobby(),
                    View(returningPlayer.Id, consumeEvents: false),
                    connectionState.ReconnectToken));
            }
            if (_players.Any(player => string.Equals(player.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                return OperationResult<JoinLobbyResponse>.Failure("That name is already in the player list. Choose another name.");
            }

            if (_players.Count == 0 && !hostLobby)
            {
                return OperationResult<JoinLobbyResponse>.Failure("There is no lobby available to join.");
            }

            if (_players.Count > 0 && hostLobby)
            {
                return OperationResult<JoinLobbyResponse>.Failure("A lobby already exists. Join it or wait for it to close.");
            }

            if (_players.Count == 0)
            {
                _maxPlayers = Math.Clamp(opponentCount, 1, MaxOpponents) + 1;
            }

            if (_players.Count >= _maxPlayers)
            {
                return OperationResult<JoinLobbyResponse>.Failure("The lobby is full.");
            }
            var player = new Player(_nextPlayerId++, normalizedName);
            _players.Add(player);
            _hostPlayerId ??= player.Id;
            _recentEvents[player.Id] = [];
            var reconnectToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _connectionStates[player.Id] = new ConnectionState(reconnectToken);
            return OperationResult<JoinLobbyResponse>.Success(new JoinLobbyResponse(player.Id, _players.Count == 1, Lobby(), null, reconnectToken));
        }
    }

    public OperationResult<bool> LeaveLobby(int playerId)
    {
        lock (_gate)
        {
            if (_game is not null)
            {
                return OperationResult<bool>.Failure("The game has already started.");
            }

            if (Find(playerId) is null)
            {
                return OperationResult<bool>.Failure("You are not in the lobby.");
            }
            RemovePlayerLocked(playerId);
            return OperationResult<bool>.Success(true);
        }
    }

    public OperationResult<bool> ReturnToLobby(int playerId)
    {
        lock (_gate)
        {
            if (_game is null || !_game.IsMatchOver)
            {
                return OperationResult<bool>.Failure("The game has not ended.");
            }

            if (Find(playerId) is null)
            {
                return OperationResult<bool>.Failure("You are not in the game.");
            }

            _game = null;
            _players.Clear();
            _hostPlayerId = null;
            _recentEvents.Clear();
            _connectionStates.Clear();
            _nextPlayerId = 1;
            _maxPlayers = MaxOpponents + 1;
            _roundNumber = 1;
            return OperationResult<bool>.Success(true);
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
        lock (_gate)
        {
            return _connectionStates.TryGetValue(playerId, out var state)
                && TokenMatches(state, reconnectToken)
                && !state.IsForfeited;
        }
    }

    public bool IsConnectionAuthorized(int playerId, string connectionId)
    {
        lock (_gate)
        {
            return _connectionStates.TryGetValue(playerId, out var state)
                && state.ConnectionId == connectionId
                && !state.IsForfeited;
        }
    }

    public bool DisconnectConnection(string connectionId)
    {
        lock (_gate)
        {
            var connection = _connectionStates.FirstOrDefault(pair => pair.Value.ConnectionId == connectionId);
            if (connection.Value is null)
            {
                return false;
            }
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
            if (_game is null || _game.IsGameOver)
            {
                return false;
            }
            var expiredIds = _connectionStates
                .Where(pair => !pair.Value.IsConnected && !pair.Value.IsForfeited && pair.Value.DeadlineUtc <= _utcNow())
                .Select(pair => pair.Key)
                .ToHashSet();
            if (expiredIds.Count == 0)
            {
                return false;
            }

            var expiredPlayers = _players.Where(player => expiredIds.Contains(player.Id)).ToArray();
            // Remove stale events before processing forfeitures. ForfeitPlayer
            // will then enqueue the immediate turn-change event for the next
            // eligible player.
            foreach (var events in _recentEvents.Values)
            {
                events.Clear();
            }
            foreach (var player in expiredPlayers)
            {
                _connectionStates[player.Id].IsForfeited = true;
                _game.ForfeitPlayer(player);
            }

            if (expiredPlayers.Any(player => player.Id == _hostPlayerId))
            {
                _hostPlayerId = _players.FirstOrDefault(_game.IsEligible)?.Id;
            }

            // Keep forfeited players in the lobby/game state so the remaining
            // hands, scores, and player ordering are not rebuilt or reset.
            foreach (var player in expiredPlayers)
            {
                QueueEvent(new GameEventDto("forfeit", player.Id, player.Name, null,
                    $"{player.Name} forfeited and will no longer take turns."));
            }
            return true;
        }
    }

    public IReadOnlyList<ConnectedGameSnapshot> GetConnectedGameViews(bool consumeEvents = true)
    {
        lock (_gate)
        {
            if (_game is null)
            {
                return Array.Empty<ConnectedGameSnapshot>();
            }
            return _connectionStates
                .Where(pair => pair.Value.ConnectionId is not null)
                .Select(pair => new ConnectedGameSnapshot(pair.Value.ConnectionId!, View(pair.Key, consumeEvents, includeEvents: consumeEvents)))
                .ToArray();
        }
    }

    public OperationResult<GameViewDto> Start(int playerId)
    {
        lock (_gate)
        {
            if (_players.Count < 2)
            {
                return OperationResult<GameViewDto>.Failure("At least 2 players are required to start.");
            }

            if (_hostPlayerId != playerId)
            {
                return OperationResult<GameViewDto>.Failure("Only the host can start the game.");
            }

            if (_game is not null)
            {
                return OperationResult<GameViewDto>.Failure("The game has already started.");
            }
            _game = CreateGameController();
            _roundNumber = 1;
            WatchGameEvents(_game);
            _game.StartGame();
            foreach (var state in _connectionStates.Values.Where(state => !state.IsConnected && !state.IsForfeited))
            {
                state.DisconnectedAtUtc = _utcNow();
                state.DeadlineUtc = state.DisconnectedAtUtc.Value.Add(_reconnectGracePeriod);
            }
            QueueTableEvent();
            return OperationResult<GameViewDto>.Success(View(playerId, consumeEvents: false));
        }
    }

    public GameViewDto? GetGame(int playerId)
    {
        lock (_gate)
        {
            return _game is null || Find(playerId) is null ? null : View(playerId);
        }
    }

    public OperationResult<GameViewDto> Play(int playerId, int cardId, CardColor? chosenColor)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error))
            {
                return OperationResult<GameViewDto>.Failure(error);
            }
            var card = _game!.GetPlayerHand(player).FirstOrDefault(c => c.Id == cardId);
            if (card is null)
            {
                return OperationResult<GameViewDto>.Failure("That card is not in your hand.");
            }

            if (!_game.PlayCard(player, card, chosenColor))
            {
                return OperationResult<GameViewDto>.Failure("That card cannot be played now.");
            }
            return OperationResult<GameViewDto>.Success(View(playerId, consumeEvents: false));
        }
    }

    public OperationResult<GameViewDto> Draw(int playerId)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error))
            {
                return OperationResult<GameViewDto>.Failure(error);
            }

            if (_game!.GetCurrentPlayer() != player)
            {
                return OperationResult<GameViewDto>.Failure("It is not your turn.");
            }
            _game.DrawCard(player);
            return OperationResult<GameViewDto>.Success(View(playerId, consumeEvents: false));
        }
    }

    public OperationResult<GameViewDto> Pass(int playerId)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error))
            {
                return OperationResult<GameViewDto>.Failure(error);
            }

            if (_game!.GetCurrentPlayer() != player)
            {
                return OperationResult<GameViewDto>.Failure("It is not your turn.");
            }

            if (!_game.HasDrawnThisTurn)
            {
                return OperationResult<GameViewDto>.Failure("Draw a card before passing.");
            }
            _game.PassTurn(player);
            return OperationResult<GameViewDto>.Success(View(playerId, consumeEvents: false));
        }
    }

    public OperationResult<GameViewDto> SayUno(int playerId)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error))
            {
                return OperationResult<GameViewDto>.Failure(error);
            }
            if (_game!.GetCurrentPlayer() != player || _game.GetPlayerHand(player).Count != 2)
            {
                return OperationResult<GameViewDto>.Failure("Call UNO on your turn with two cards.");
            }
            _game.SayUno(player);
            QueueEvent(new GameEventDto(
                "alert", player.Id, player.Name, null, $"{player.Name} called UNO."));
            return OperationResult<GameViewDto>.Success(View(playerId, consumeEvents: false));
        }
    }

    public OperationResult<GameViewDto> CatchUno(int playerId, int targetPlayerId)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error))
            {
                return OperationResult<GameViewDto>.Failure(error);
            }
            var target = Find(targetPlayerId);
            if (target is null)
            {
                return OperationResult<GameViewDto>.Failure("That player does not exist.");
            }
            _game!.CatchPlayer(target);
            return OperationResult<GameViewDto>.Success(View(playerId, consumeEvents: false));
        }
    }

    public OperationResult<GameViewDto> ChallengeDrawFour(int playerId)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error))
            {
                return OperationResult<GameViewDto>.Failure(error);
            }

            if (!_game!.CanChallengeDrawFour(player))
            {
                return OperationResult<GameViewDto>.Failure("You can only challenge the player who just played a Wild Draw Four.");
            }
            _game.ChallengeDrawFour(player, out var succeeded);
            QueueEvent(new GameEventDto("alert", player.Id, player.Name, null,
                succeeded ? $"{player.Name}'s challenge succeeded. The Wild Draw Four player drew 4 cards." : $"{player.Name}'s challenge failed. {player.Name} drew 6 cards."));
            return OperationResult<GameViewDto>.Success(View(playerId, consumeEvents: false));
        }
    }

    public OperationResult<GameViewDto> ChooseOpeningColor(int playerId, CardColor color)
    {
        lock (_gate)
        {
            if (!TryGetGamePlayer(playerId, out var player, out var error))
            {
                return OperationResult<GameViewDto>.Failure(error);
            }

            if (!_game!.ChooseOpeningColor(player, color))
            {
                return OperationResult<GameViewDto>.Failure("Choose a valid color before playing.");
            }
            return OperationResult<GameViewDto>.Success(View(playerId, consumeEvents: false));
        }
    }

    public GameViewDto? Restart(int playerId, bool resetScores = false)
    {
        lock (_gate)
        {
            if (_game is null || _players.Count == 0 || _hostPlayerId != playerId)
            {
                return null;
            }

            if (_game.IsMatchOver)
            {
                return null;
            }
            var previousScores = resetScores ? null : _game.GetPlayerScores();
            _roundNumber = resetScores ? 1 : _roundNumber + 1;
            foreach (var events in _recentEvents.Values)
            {
                events.Clear();
            }
            _game = CreateGameController(previousScores, _game.ForfeitedPlayers);
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

    private void RemovePlayerLocked(int playerId)
    {
        var player = _players.FirstOrDefault(p => p.Id == playerId);
        if (player is null)
        {
            return;
        }
        _players.Remove(player);
        _recentEvents.Remove(playerId);
        _connectionStates.Remove(playerId);
        if (_hostPlayerId == playerId)
        {
            _hostPlayerId = _players.FirstOrDefault()?.Id;
        }
        if (_players.Count == 0)
        {
            _hostPlayerId = null;
            _nextPlayerId = 1;
            _maxPlayers = MaxOpponents + 1;
        }
    }

    private bool TryGetGamePlayer(int id, out Player player, out string error)
    {
        player = Find(id)!;
        error = string.Empty;
        if (_game is null)
        {
            error = "The game has not started.";
        }
        else if (player is null)
        {
            error = "That player is not in the lobby.";
        }
        else if (!_connectionStates.TryGetValue(id, out var state) || !state.IsConnected || state.IsForfeited)
        {
            error = "You are disconnected from the match.";
        }
        else if (IsMatchPaused())
        {
            error = "The match is paused while a player reconnects.";
        }
        return error.Length == 0;
    }

    private Player? Find(int id) => _players.FirstOrDefault(p => p.Id == id);
    private LobbyResponse Lobby() => new(LobbyId, _game is not null, 2, _maxPlayers,
        _hostPlayerId, _players.Select(p =>
        {
            var state = _connectionStates[p.Id];
            var seconds = !state.IsConnected && state.DeadlineUtc is { } deadline
                ? Math.Max(0, (int)Math.Ceiling((deadline - _utcNow()).TotalSeconds)) : (int?)null;
            return new LobbyPlayerResponse(p.Id, p.Name, state.IsConnected, seconds);
        }).ToArray());

    private GameViewDto View(int humanPlayerId, bool consumeEvents = true, bool includeEvents = true)
    {
        var human = Find(humanPlayerId) ?? throw new InvalidOperationException("The player is not in the lobby.");
        var game = _game ?? throw new InvalidOperationException("The game has not started.");
        var events = GetEventsForPlayer(humanPlayerId, includeEvents, consumeEvents);
        var pauseInfo = GetPauseInfo(game);
        var currentPlayer = game.IsGameOver ? null : game.GetCurrentPlayer();

        return new GameViewDto(
            GameId: LobbyId,
            Status: GetStatus(game),
            Winner: game.Winner?.Name,
            HumanPlayerId: human.Id,
            Players: BuildPlayerList(game, currentPlayer, human),
            Hand: game.GetPlayerHand(human).Select(CardDto.From).ToArray(),
            TopCard: game.GetTopCard() is { } top ? CardDto.From(top) : null,
            ActiveColor: game.GetActiveColor(),
            DrawPileCount: game.Table.DrawPile.TotalCards,
            HasDrawnThisTurn: game.HasDrawnThisTurn,
            CanChallengeDrawFour: !game.IsGameOver && game.CanChallengeDrawFour(human),
            PendingDrawCount: game.PendingDrawCount,
            RequiresOpeningColorChoice: !game.IsGameOver && game.RequiresOpeningColorChoice && currentPlayer == human,
            Direction: game.Direction.ToString(),
            RecentEvents: events,
            RoundNumber: _roundNumber,
            IsPaused: pauseInfo.IsPaused,
            DisconnectedPlayer: pauseInfo.PausedPlayerName,
            ReconnectSecondsRemaining: pauseInfo.SecondsRemaining);
    }

    private GameEventDto[] GetEventsForPlayer(int playerId, bool includeEvents, bool consumeEvents)
    {
        if (!_recentEvents.TryGetValue(playerId, out var queuedEvents))
        {
            return Array.Empty<GameEventDto>();
        }

        var events = includeEvents ? queuedEvents.ToArray() : Array.Empty<GameEventDto>();
        if (consumeEvents)
        {
            queuedEvents.Clear();
        }

        return events;
    }

    private PauseInfo GetPauseInfo(GameController game)
    {
        var disconnected = _connectionStates
            .FirstOrDefault(pair => !pair.Value.IsConnected && !pair.Value.IsForfeited);

        if (game.IsGameOver || disconnected.Value is null)
        {
            return new PauseInfo(false, null, null);
        }

        var secondsRemaining = disconnected.Value.DeadlineUtc is { } deadline
            ? Math.Max(0, (int)Math.Ceiling((deadline - _utcNow()).TotalSeconds))
            : (int?)null;
        var playerName = _players.First(player => player.Id == disconnected.Key).Name;

        return new PauseInfo(true, playerName, secondsRemaining);
    }

    private static string GetStatus(GameController game)
    {
        if (game.IsMatchOver)
        {
            return "completed";
        }

        if (game.IsGameOver)
        {
            return "round-complete";
        }

        return "playing";
    }

    private PlayerDto[] BuildPlayerList(GameController game, Player? currentPlayer, Player human)
    {
        return _players.Select(player =>
        {
            _connectionStates.TryGetValue(player.Id, out var state);
            return new PlayerDto(
                Id: player.Id,
                Name: player.Name,
                CardCount: game.GetPlayerHand(player).Count,
                IsCurrentPlayer: player == currentPlayer,
                IsHuman: player == human,
                Score: game.GetPlayerScore(player),
                IsConnected: state?.IsConnected == true,
                IsForfeited: state?.IsForfeited == true);
        }).ToArray();
    }

    private bool IsMatchPaused() => _game is not null && !_game.IsGameOver && _connectionStates.Values.Any(state => !state.IsConnected && !state.IsForfeited);
    private static bool TokenMatches(ConnectionState state, string reconnectToken) =>
        !string.IsNullOrEmpty(reconnectToken) && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(state.ReconnectToken), Encoding.UTF8.GetBytes(reconnectToken));

    private void WatchGameEvents(GameController game)
    {
        game.OnCardPlayed += (player, card) => QueueEvent(new GameEventDto(
            "play", player.Id, player.Name, CardDto.From(card), $"{player.Name} played a card to the discard pile."));
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

        foreach (var events in _recentEvents.Values)
        {
            events.Add(gameEvent);
        }
    }
}
