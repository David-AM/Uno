using Microsoft.AspNetCore.SignalR;
using UnoGame.Api.DTOs;
using UnoGame.Api.Models;
using UnoGame.Api.Services;

namespace UnoGame.Api.Hubs;

public sealed class MultiplayerHub(IMultiplayerLobbyService lobby) : Hub
{
    private const string Group = "demo-lobby";
    private static readonly Dictionary<int, string> Connections = [];
    private static readonly Dictionary<string, int> ConnectionPlayers = [];
    private static readonly object ConnectionGate = new();

    public async Task Register(int playerId)
    {
        lock (ConnectionGate)
        {
            Connections[playerId] = Context.ConnectionId;
            ConnectionPlayers[Context.ConnectionId] = playerId;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, Group);
        await Clients.Caller.SendAsync("gameUpdated", lobby.GetGame(playerId));
        await Broadcast();
    }
    public async Task Start(int playerId) { await Execute(lobby.Start(playerId)); }
    public async Task Play(int playerId, int cardId, CardColor? chosenColor) => await Execute(lobby.Play(playerId, cardId, chosenColor));
    public async Task Draw(int playerId) => await Execute(lobby.Draw(playerId));
    public async Task Pass(int playerId) => await Execute(lobby.Pass(playerId));
    public async Task SayUno(int playerId) => await Execute(lobby.SayUno(playerId));
    public async Task CatchUno(int playerId, int targetPlayerId) => await Execute(lobby.CatchUno(playerId, targetPlayerId));
    public async Task ChallengeDrawFour(int playerId) => await Execute(lobby.ChallengeDrawFour(playerId));
    public async Task Restart(int playerId, bool resetScores = false)
    {
        if (lobby.Restart(playerId, resetScores) is null) await Clients.Caller.SendAsync("actionError", "Only the host can restart the game.");
        await Broadcast();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        int? disconnectedPlayer = null;
        lock (ConnectionGate)
        {
            if (ConnectionPlayers.Remove(Context.ConnectionId, out var playerId))
            {
                disconnectedPlayer = playerId;
                if (Connections.TryGetValue(playerId, out var connectionId) && connectionId == Context.ConnectionId)
                    Connections.Remove(playerId);
                if (ConnectionPlayers.Values.Contains(playerId)) disconnectedPlayer = null;
            }
        }
        if (disconnectedPlayer is { } id)
        {
            lobby.RemoveDisconnectedPlayer(id);
            await Broadcast();
        }
        await base.OnDisconnectedAsync(exception);
    }

    private async Task Execute((GameViewDto? Result, string? Error) result)
    {
        if (result.Error is not null) await Clients.Caller.SendAsync("actionError", result.Error);
        await Broadcast();
    }

    private async Task Broadcast()
    {
        await Clients.Group(Group).SendAsync("lobbyUpdated", lobby.GetLobby());
        KeyValuePair<int, string>[] connections;
        lock (ConnectionGate) connections = Connections.ToArray();
        foreach (var connection in connections)
            if (lobby.GetGame(connection.Key) is { } game)
                await Clients.Client(connection.Value).SendAsync("gameUpdated", game);
    }
}
