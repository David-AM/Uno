namespace UnoGame.Api.Models;

public sealed class Lobby(int id, IPlayer player, string name, bool isPublic, int maxPlayers = 4)
{
    private readonly List<IPlayer> _players = [player];
    public int Id { get; } = id;
    public string Name { get; } = name;
    public bool IsPublic { get; } = isPublic;
    public bool IsPlaying { get; private set; }
    public int MaxPlayers { get; } = maxPlayers;
    public int MinPlayers { get; } = 2;
    public IReadOnlyList<IPlayer> Players => _players;
    public IPlayer CreatedBy { get; } = player;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public GameController? Game { get; private set; }
    public event Action<IPlayer>? OnPlayerJoined;
    public event Action<IPlayer>? OnPlayerLeft;
    public event Action<GameController>? OnGameStarted;
    public bool Join(IPlayer player)
    {
        if (IsPlaying || IsFull() || _players.Any(p => p.Id == player.Id)) return false;
        _players.Add(player); OnPlayerJoined?.Invoke(player); return true;
    }
    public bool Leave(IPlayer player)
    {
        var existing = _players.FirstOrDefault(p => p.Id == player.Id);
        if (existing is null || IsPlaying) return false;
        _players.Remove(existing); OnPlayerLeft?.Invoke(existing); return true;
    }
    public GameController StartGame(IPlayer requester)
    {
        if (requester.Id != CreatedBy.Id || _players.Count < MinPlayers || IsPlaying)
            throw new InvalidOperationException("Only the lobby host can start a full lobby.");
        IsPlaying = true; Game = new GameController(_players.OfType<Player>().ToList()); Game.StartGame();
        OnGameStarted?.Invoke(Game); return Game;
    }
    public bool IsFull() => _players.Count >= MaxPlayers;
}
