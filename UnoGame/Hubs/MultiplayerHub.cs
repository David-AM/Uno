using Microsoft.AspNetCore.SignalR;
using UnoGame.Api.DTOs;
using UnoGame.Api.Models;
using UnoGame.Api.Services;

namespace UnoGame.Api.Hubs;

public sealed class MultiplayerHub(IMultiplayerLobbyService lobby) : Hub
{
    public const string Group = "demo-lobby";

    public async Task<GameViewDto?> Register(int playerId, string reconnectToken)
    {
        if (!lobby.RegisterConnection(playerId, reconnectToken, Context.ConnectionId, out var error))
        {
            throw new HubException(error);
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, Group);
        await Broadcast();
        return lobby.GetGame(playerId);
    }
    public async Task Start(int playerId) => await Execute(playerId, () => lobby.Start(playerId));
    public async Task Play(int playerId, int cardId, CardColor? chosenColor) => await Execute(playerId, () => lobby.Play(playerId, cardId, chosenColor));
    public async Task Draw(int playerId) => await Execute(playerId, () => lobby.Draw(playerId));
    public async Task Pass(int playerId) => await Execute(playerId, () => lobby.Pass(playerId));
    public async Task SayUno(int playerId) => await Execute(playerId, () => lobby.SayUno(playerId));
    public async Task CatchUno(int playerId, int targetPlayerId) => await Execute(playerId, () => lobby.CatchUno(playerId, targetPlayerId));
    public async Task ChallengeDrawFour(int playerId) => await Execute(playerId, () => lobby.ChallengeDrawFour(playerId));
    public async Task ChooseOpeningColor(int playerId, CardColor color) => await Execute(playerId, () => lobby.ChooseOpeningColor(playerId, color));
    public async Task Restart(int playerId, bool resetScores = false)
    {
        if (!lobby.IsConnectionAuthorized(playerId, Context.ConnectionId)) await Clients.Caller.SendAsync("actionError", "You are disconnected from the match.");
        else if (lobby.Restart(playerId, resetScores) is null) await Clients.Caller.SendAsync("actionError", "Only the host can restart the game.");
        await Broadcast();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (lobby.DisconnectConnection(Context.ConnectionId)) await Broadcast();
        await base.OnDisconnectedAsync(exception);
    }

    private async Task Execute(int playerId, Func<(GameViewDto? Result, string? Error)> action)
    {
        if (!lobby.IsConnectionAuthorized(playerId, Context.ConnectionId))
        {
            await Clients.Caller.SendAsync("actionError", "You are disconnected from the match.");
            return;
        }
        var result = action();
        if (result.Error is not null) await Clients.Caller.SendAsync("actionError", result.Error);
        await Broadcast();
    }

    private async Task Broadcast()
    {
        await Clients.Group(Group).SendAsync("lobbyUpdated", lobby.GetLobby());
        foreach (var connection in lobby.GetConnectedGameViews())
            await Clients.Client(connection.ConnectionId).SendAsync("gameUpdated", connection.Game);
    }
}
