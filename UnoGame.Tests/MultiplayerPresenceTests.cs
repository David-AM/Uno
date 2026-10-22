using UnoGame.Api.Models;
using UnoGame.Api.Services;
using Xunit;

namespace UnoGame.Tests;

public sealed class MultiplayerPresenceTests
{
    [Fact]
    public void DisconnectingCurrentPlayerPausesMatchAndBlocksActions()
    {
        var now = DateTime.UtcNow;
        var (service, host, member) = StartMatch(() => now);

        Assert.True(service.DisconnectConnection(host.ConnectionId));
        var view = service.GetGame(member.PlayerId)!;

        Assert.True(view.IsPaused);
        Assert.Equal(host.Name, view.DisconnectedPlayer);
        Assert.NotNull(view.ReconnectSecondsRemaining);
        Assert.Contains(view.Players, player => player.Id == host.PlayerId && !player.IsConnected);
        Assert.Null(service.Play(host.PlayerId, 1, null).Result);
        Assert.NotNull(service.Play(member.PlayerId, 1, null).Error);
    }

    [Fact]
    public void DisconnectingNonCurrentPlayerAlsoPausesMatch()
    {
        var now = DateTime.UtcNow;
        var (service, host, member) = StartMatch(() => now);

        Assert.True(service.DisconnectConnection(member.ConnectionId));
        Assert.True(service.GetGame(host.PlayerId)!.IsPaused);
    }

    [Fact]
    public void PlayerCanReconnectBeforeDeadlineWithSameGameState()
    {
        var now = DateTime.UtcNow;
        var (service, host, member) = StartMatch(() => now);
        var before = service.GetGame(host.PlayerId)!;
        Assert.True(service.DisconnectConnection(member.ConnectionId));

        Assert.True(service.RegisterConnection(member.PlayerId, member.Token, "member-reconnected", out var error));
        Assert.Empty(error);
        var after = service.GetGame(host.PlayerId)!;

        Assert.False(after.IsPaused);
        Assert.Equal(before.TopCard?.Id, after.TopCard?.Id);
        Assert.Equal(before.DrawPileCount, after.DrawPileCount);
        Assert.Equal(before.Players.Single(player => player.IsCurrentPlayer).Id,
            after.Players.Single(player => player.IsCurrentPlayer).Id);
    }

    [Fact]
    public void ExpiredDisconnectForfeitsMatchOnlyOnce()
    {
        var now = DateTime.UtcNow;
        var (service, host, member) = StartMatch(() => now, TimeSpan.FromSeconds(60));
        Assert.True(service.DisconnectConnection(member.ConnectionId));

        now = now.AddSeconds(61);
        Assert.True(service.ProcessExpiredDisconnections());
        Assert.False(service.ProcessExpiredDisconnections());
        var view = service.GetGame(host.PlayerId)!;
        Assert.Equal("round-complete", view.Status);
        Assert.Equal(host.Name, view.Winner);
        Assert.False(service.RegisterConnection(member.PlayerId, member.Token, "late-connection", out _));
    }

    [Fact]
    public void DuplicateDisconnectAndInvalidReconnectAreIdempotentAndSafe()
    {
        var now = DateTime.UtcNow;
        var (service, host, member) = StartMatch(() => now);

        Assert.True(service.DisconnectConnection(member.ConnectionId));
        Assert.False(service.DisconnectConnection(member.ConnectionId));
        Assert.False(service.RegisterConnection(member.PlayerId, "invalid-token", "attacker", out _));
        Assert.False(service.IsConnectionAuthorized(member.PlayerId, member.ConnectionId));
        Assert.True(service.IsConnectionAuthorized(host.PlayerId, host.ConnectionId));
    }

    [Fact]
    public void MultipleDisconnectedPlayersKeepMatchPausedUntilGraceHandling()
    {
        var now = DateTime.UtcNow;
        var (service, host, member) = StartMatch(() => now, TimeSpan.FromSeconds(60), includeThirdPlayer: true, includeFourthPlayer: true);
        var third = service.GetLobby().Players.Single(player => player.Id == 3);
        Assert.True(service.DisconnectConnection(member.ConnectionId));
        Assert.True(service.DisconnectConnection("third-connection"));

        Assert.True(service.GetGame(host.PlayerId)!.IsPaused);
        now = now.AddSeconds(61);
        Assert.True(service.ProcessExpiredDisconnections());
        var roundComplete = service.GetGame(host.PlayerId)!;
        Assert.Equal("round-complete", roundComplete.Status);
        Assert.Equal(2, roundComplete.Players.Count);
        Assert.Equal(1, roundComplete.RoundNumber);
        Assert.Equal("Lin", third.Name);
        Assert.True(roundComplete.Players.All(player => player.Score > 0));
        Assert.Equal(roundComplete.Players[0].Score, roundComplete.Players[1].Score);
        Assert.Equal(host.PlayerId, service.GetLobby().HostPlayerId);
        var nextRound = service.Restart(host.PlayerId)!;
        Assert.Equal("playing", nextRound.Status);
        Assert.Equal(2, nextRound.RoundNumber);
    }

    [Fact]
    public void RemainingPlayerBecomesHostWhenOriginalHostForfeits()
    {
        var now = DateTime.UtcNow;
        var (service, host, member) = StartMatch(() => now, includeThirdPlayer: true);
        Assert.True(service.DisconnectConnection(host.ConnectionId));

        now = now.AddSeconds(61);
        Assert.True(service.ProcessExpiredDisconnections());

        Assert.Equal(member.PlayerId, service.GetLobby().HostPlayerId);
        Assert.NotNull(service.Restart(member.PlayerId));
    }

    private static (MultiplayerLobbyService Service, TestPlayer Host, TestPlayer Member) StartMatch(
        Func<DateTime> clock, TimeSpan? grace = null, bool includeThirdPlayer = false, bool includeFourthPlayer = false)
    {
        var service = grace is { } period ? new MultiplayerLobbyService(period, clock) : new MultiplayerLobbyService(TimeSpan.FromSeconds(60), clock);
        var opponentCount = includeFourthPlayer ? 3 : includeThirdPlayer ? 2 : 1;
        var hostJoin = service.Join("Host", opponentCount, true).Result!;
        var memberJoin = service.Join("Grace", opponentCount, false).Result!;
        var thirdJoin = includeThirdPlayer ? service.Join("Lin", 2, false).Result : null;
        var fourthJoin = includeFourthPlayer ? service.Join("Nora", 3, false).Result : null;
        service.RegisterConnection(hostJoin.PlayerId, hostJoin.ReconnectToken, "host-connection", out _);
        service.RegisterConnection(memberJoin.PlayerId, memberJoin.ReconnectToken, "member-connection", out _);
        if (includeThirdPlayer)
            service.RegisterConnection(thirdJoin!.PlayerId, thirdJoin.ReconnectToken, "third-connection", out _);
        if (includeFourthPlayer)
            service.RegisterConnection(fourthJoin!.PlayerId, fourthJoin.ReconnectToken, "fourth-connection", out _);
        service.Start(hostJoin.PlayerId);
        return (service, new TestPlayer(hostJoin.PlayerId, "Host", hostJoin.ReconnectToken, "host-connection"),
            new TestPlayer(memberJoin.PlayerId, "Grace", memberJoin.ReconnectToken, "member-connection"));
    }

    private sealed record TestPlayer(int PlayerId, string Name, string Token, string ConnectionId);
}
