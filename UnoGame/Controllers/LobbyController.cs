using Microsoft.AspNetCore.Mvc;
using UnoGame.Api.DTOs;
using UnoGame.Api.Services;

namespace UnoGame.Api.Controllers;

[ApiController]
[Route("api/lobby")]
public sealed class LobbyController(IMultiplayerLobbyService lobby) : ControllerBase
{
    [HttpGet]
    public ActionResult<LobbyResponse> Get() => Ok(lobby.GetLobby());
    [HttpPost("join")]
    public ActionResult<JoinLobbyResponse> Join(JoinLobbyRequest request)
    {
        var result = lobby.Join(request.PlayerName, request.OpponentCount, request.HostLobby);
        return result.Result is not null ? Ok(result.Result) : Conflict(new { error = result.Error });
    }
    [HttpGet("game/{playerId:int}")]
    public ActionResult<GameViewDto> Game(int playerId, [FromQuery] string reconnectToken) =>
        lobby.IsReconnectTokenValid(playerId, reconnectToken) && lobby.GetGame(playerId) is { } game
            ? Ok(game) : NotFound(new { error = "The game is not available." });
}
