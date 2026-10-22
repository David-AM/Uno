using System.Collections.Concurrent;
using UnoGame.Api.DTOs;
using UnoGame.Api.Models;

namespace UnoGame.Api.Services;

public interface IGameService
{
    GameViewDto Create(CreateGameRequest request);
    GameViewDto? Get(string id);
    GameViewDto? Play(string id, PlayCardRequest request, out string? error);
    GameViewDto? Draw(string id, out string? error);
    GameViewDto? Pass(string id, out string? error);
    GameViewDto? SayUno(string id, out string? error);
    GameViewDto? CatchUno(string id, int targetPlayerId, out string? error);
    GameViewDto? ChallengeDrawFour(string id, out string? error);
    GameViewDto? Restart(string id, bool resetScores = false);
}

public sealed class GameService : IGameService
{
    private sealed class Session(string id, Player human, List<Player> players, GameController game)
    {
        public string Id { get; } = id;
        public Player Human { get; } = human;
        public List<Player> Players { get; } = players;
        public GameController Game { get; set; } = game;
        public string? Message { get; set; }
        public string? AlertMessage { get; set; }
        public int AlertVersion { get; set; }
        public int RoundNumber { get; set; } = 1;
        public List<PlayerPlayDto> RecentPlays { get; } = [];
        public List<GameEventDto> RecentEvents { get; } = [];
        public object Gate { get; } = new();
    }
    private readonly ConcurrentDictionary<string, Session> _games = new();

    public GameViewDto Create(CreateGameRequest request)
    {
        var count = Math.Clamp(request.OpponentCount, 1, 3);
        var players = new List<Player> { new(1, string.IsNullOrWhiteSpace(request.PlayerName) ? "You" : request.PlayerName.Trim()) };
        players.AddRange(Enumerable.Range(1, count).Select(i => new Player(i + 1, new[] { "Maya", "Leo", "Nina" }[i - 1])));
        var game = new GameController(players); game.StartGame();
        var session = new Session(Guid.NewGuid().ToString("N"), players[0], players, game);
        WatchGameEvents(session, game);
        session.RecentEvents.Add(new GameEventDto("table", null, null, CardDto.From(game.GetTopCard()!), string.Empty, game.GetPlayerHand(session.Human).Select(CardDto.From).ToArray()));
        _games[session.Id] = session;
        RunAiTurns(session);
        return View(session, includeRecentPlays: true);
    }
    public GameViewDto? Get(string id) => _games.TryGetValue(id, out var s) ? View(s) : null;
    public GameViewDto? Play(string id, PlayCardRequest request, out string? error)
    {
        error = null; if (!_games.TryGetValue(id, out var s)) return null;
        lock (s.Gate)
        {
            var card = s.Game.GetPlayerHand(s.Human).FirstOrDefault(c => c.Id == request.CardId);
            if (card is null) { error = "That card is not in your hand."; return View(s); }
            s.RecentPlays.Clear();
            s.AlertMessage = null;
            if (!s.Game.PlayCard(s.Human, card, request.ChosenColor)) { error = "That card cannot be played now. Choose a matching card or a valid Wild color."; return View(s); }
            s.Message = $"You played {CardName(card)}.";
            RunAiTurns(s);
            return View(s, includeRecentPlays: true);
        }
    }
    public GameViewDto? Draw(string id, out string? error)
    {
        error = null; if (!_games.TryGetValue(id, out var s)) return null;
        lock (s.Gate)
        {
            if (s.Game.IsGameOver || s.Game.GetCurrentPlayer() != s.Human) { error = "It is not your turn."; return View(s); }
            s.AlertMessage = null;
            s.RecentPlays.Clear();
            var count = s.Game.GetPlayerHand(s.Human).Count;
            var card = s.Game.DrawCard(s.Human);
            s.Message = card is null && s.Game.GetPlayerHand(s.Human).Count > count ? "You drew the penalty cards." : card is null && s.Game.GetCurrentPlayer() == s.Human ? "The draw pile is empty. You may pass." : card is null ? "You drew the penalty cards." : "You drew a card. Play it if it matches, or pass.";
            if (s.Game.GetCurrentPlayer() != s.Human) RunAiTurns(s);
            return View(s, includeRecentPlays: true);
        }
    }
    public GameViewDto? Pass(string id, out string? error)
    {
        error = null; if (!_games.TryGetValue(id, out var s)) return null;
        lock (s.Gate)
        {
            if (s.Game.IsGameOver || s.Game.GetCurrentPlayer() != s.Human) { error = "It is not your turn."; return View(s); }
            if (!s.Game.HasDrawnThisTurn) { error = "Draw a card before passing."; return View(s); }
            s.RecentPlays.Clear();
            s.AlertMessage = null;
            s.Game.PassTurn(s.Human); s.Message = "You passed."; RunAiTurns(s); return View(s, includeRecentPlays: true);
        }
    }
    public GameViewDto? SayUno(string id, out string? error)
    {
        error = null; if (!_games.TryGetValue(id, out var s)) return null;
        lock (s.Gate) { if (s.Game.GetCurrentPlayer() != s.Human || s.Game.GetPlayerHand(s.Human).Count != 2) error = "Call UNO on your turn before playing with two cards."; else { s.Game.SayUno(s.Human); s.Message = "UNO called. Play a card to go down to one."; } return View(s); }
    }
    public GameViewDto? CatchUno(string id, int targetPlayerId, out string? error)
    {
        error = null; if (!_games.TryGetValue(id, out var s)) return null;
        lock (s.Gate)
        {
            var target = s.Players.FirstOrDefault(p => p.Id == targetPlayerId);
            if (target is null || target == s.Human || s.Game.GetPlayerHand(target).Count != 1) error = "That player cannot be caught out right now.";
            else { var before = s.Game.GetPlayerHand(target).Count; s.Game.CatchPlayer(s.Human, target); s.Message = s.Game.GetPlayerHand(target).Count > before ? $"You caught {target.Name} without calling UNO." : $"{target.Name} already called UNO."; }
            return View(s, includeRecentPlays: true);
        }
    }
    public GameViewDto? ChallengeDrawFour(string id, out string? error)
    {
        error = null; if (!_games.TryGetValue(id, out var s)) return null;
        lock (s.Gate)
        {
            if (!s.Game.ChallengeDrawFour(s.Human, out var succeeded)) { error = "You cannot challenge a Wild Draw Four right now."; return View(s); }
            s.Message = succeeded ? "Challenge succeeded. The Wild Draw Four player drew 4 cards." : "Challenge failed. You drew 6 cards.";
            if (!succeeded) RunAiTurns(s);
            return View(s, includeRecentPlays: true);
        }
    }
    public GameViewDto? Restart(string id, bool resetScores = false)
    {
        if (!_games.TryGetValue(id, out var s)) return null;
        lock (s.Gate) { var newMatch = resetScores || s.Game.IsMatchOver; s.RoundNumber = newMatch ? 1 : s.RoundNumber + 1; s.Game = newMatch ? new GameController(s.Players) : new GameController(s.Players, previousScores: s.Game.GetPlayerScores()); s.Game.StartGame(); s.RecentPlays.Clear(); s.RecentEvents.Clear(); s.AlertMessage = null; s.Message = newMatch ? "A new game has started." : "A new round has started."; WatchGameEvents(s, s.Game); s.RecentEvents.Add(new GameEventDto("table", null, null, CardDto.From(s.Game.GetTopCard()!), string.Empty, s.Game.GetPlayerHand(s.Human).Select(CardDto.From).ToArray())); RunAiTurns(s); return View(s, includeRecentPlays: true); }
    }
    private static void RunAiTurns(Session s)
    {
        var game = s.Game; var guard = 0;
        while (!game.IsGameOver && game.GetCurrentPlayer() != s.Human && guard++ < 12)
        {
            var ai = game.GetCurrentPlayer(); var hand = game.GetPlayerHand(ai);
            var playable = hand.Where(game.CanPlayThisCard).OrderByDescending(c => c.Action is null ? 0 : 1).FirstOrDefault();
            if (playable is null)
            {
                var drawn = game.DrawCard(ai); s.Message = $"{ai.Name} drew a card.";
                if (game.GetCurrentPlayer() != ai) continue;
                if (drawn is not null && game.CanPlayThisCard(drawn)) playable = drawn;
                else { game.PassTurn(ai); continue; }
            }
            if (hand.Count == 2) game.SayUno(ai);
            var color = playable.Color == CardColor.Wild ? hand.Where(c => c.Color != CardColor.Wild).GroupBy(c => c.Color).OrderByDescending(g => g.Count()).Select(g => (CardColor?)g.Key).FirstOrDefault() ?? CardColor.Red : (CardColor?)null;
            game.PlayCard(ai, playable, color); s.Message = $"{ai.Name} played {CardName(playable)}.";
        }
    }
    private static string CardName(Card card) => card.Color == CardColor.Wild ? card.Action == ActionType.WildDrawFour ? "Wild Draw Four" : "Wild" : $"{card.Color} {card.Action?.ToString() ?? card.Value?.ToString()}";
    private static void WatchGameEvents(Session session, GameController game)
    {
        game.OnCardDrawn += (player, card) => session.RecentEvents.Add(new GameEventDto("draw", player.Id, player.Name, CardDto.From(card), $"{player.Name} drew a card from the draw pile.", player == session.Human ? game.GetPlayerHand(player).Select(CardDto.From).ToArray() : null));
        game.OnCardsDrawn += (player, count) => AppendAlert(session, $"{player.Name} drew {count} {(count == 1 ? "card" : "cards")} from the draw pile.", queueEvent: false, hand: player == session.Human ? game.GetPlayerHand(player).Select(CardDto.From).ToArray() : null);
        game.OnPenaltyCardsDrawn += (player, count) => AppendAlert(session, $"{player.Name} drew {count} penalty cards.", queueEvent: false);
        game.OnUnoPenalty += (player, count) => AppendAlert(session, $"{player.Name} forgot to call UNO and drew {count} cards.");
        game.OnGameEnded += winner => AppendAlert(session, $"{winner.Name} reached {GameController.WinningScore} points and won the game.");
        game.OnCardPlayed += (player, card) =>
        {
            session.RecentPlays.Add(new PlayerPlayDto(player.Id, player.Name, CardDto.From(card)));
            session.RecentEvents.Add(new GameEventDto("play", player.Id, player.Name, CardDto.From(card), $"{player.Name} played {CardName(card)} to the discard pile.", player == session.Human ? game.GetPlayerHand(player).Select(CardDto.From).ToArray() : null));
            AppendAlert(session, $"{player.Name} played {CardName(card)} to the discard pile.", queueEvent: false);
        };
    }
    private static void AppendAlert(Session session, string message, bool queueEvent = true, IReadOnlyList<CardDto>? hand = null)
    {
        session.AlertMessage = session.AlertMessage is null ? message : $"{session.AlertMessage} {message}";
        session.AlertVersion++;
        if (queueEvent) session.RecentEvents.Add(new GameEventDto("alert", null, null, null, message, hand));
    }
    private static GameViewDto View(Session s, bool includeRecentPlays = false)
    {
        var g = s.Game; var hand = g.GetPlayerHand(s.Human);
        IReadOnlyList<PlayerPlayDto> recentPlays = includeRecentPlays ? s.RecentPlays.ToArray() : Array.Empty<PlayerPlayDto>();
        IReadOnlyList<GameEventDto> recentEvents = includeRecentPlays ? s.RecentEvents.ToArray() : Array.Empty<GameEventDto>();
        if (includeRecentPlays) s.RecentPlays.Clear();
        if (includeRecentPlays) s.RecentEvents.Clear();
        var status = g.IsMatchOver ? "completed" : g.IsGameOver ? "round-complete" : "playing";
        return new GameViewDto(s.Id, status, g.Winner?.Name, s.Human.Id,
            s.Players.Select(p => new PlayerDto(p.Id, p.Name, g.GetPlayerHand(p).Count, p == g.GetCurrentPlayer(), p == s.Human, g.GetPlayerScore(p))).ToArray(),
            hand.Select(CardDto.From).ToArray(), g.GetTopCard() is { } top ? CardDto.From(top) : null, g.GetActiveColor(), g.Table.DrawPile.TotalCards,
            g.HasDrawnThisTurn, g.CanChallengeDrawFour(s.Human), g.PendingDrawCount, g.Direction.ToString(), s.Message, s.AlertMessage, s.AlertVersion, recentPlays, recentEvents, s.RoundNumber);
    }
}
