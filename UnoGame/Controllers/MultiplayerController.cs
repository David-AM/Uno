using Microsoft.AspNetCore.Mvc;
using UnoGame.Api.DTOs;
using UnoGame.Api.Services;

namespace UnoGame.Api.Controllers;

[ApiController]
[Route("api/lobby")]
public sealed class MultiplayerController(IMultiplayerLobbyService lobby) : ControllerBase
{
    [HttpGet]
    public ActionResult<LobbyResponse> Get() => Ok(lobby.GetLobby());
    [HttpPost("join")]
    public ActionResult<JoinLobbyResponse> Join(JoinLobbyRequest request)
    {
        var result = lobby.Join(request.PlayerName, request.OpponentCount, request.HostLobby);
        return result.Result is not null ? Ok(result.Result) : Conflict(new { error = result.Error });
    }
    [HttpPost("start")]
    public ActionResult<GameViewDto> Start(MultiplayerPlayerRequest request) => Action(lobby.Start(request.PlayerId));
    [HttpGet("game/{playerId:int}")]
    public ActionResult<GameViewDto> Game(int playerId) => lobby.GetGame(playerId) is { } game ? Ok(game) : NotFound(new { error = "The game is not available." });
    [HttpPost("game/play")]
    public ActionResult<GameViewDto> Play(MultiplayerPlayRequest request) => Action(lobby.Play(request.PlayerId, request.CardId, request.ChosenColor));
    [HttpPost("game/draw")]
    public ActionResult<GameViewDto> Draw(MultiplayerPlayerRequest request) => Action(lobby.Draw(request.PlayerId));
    [HttpPost("game/pass")]
    public ActionResult<GameViewDto> Pass(MultiplayerPlayerRequest request) => Action(lobby.Pass(request.PlayerId));
    [HttpPost("game/uno")]
    public ActionResult<GameViewDto> Uno(MultiplayerPlayerRequest request) => Action(lobby.SayUno(request.PlayerId));
    [HttpPost("game/catch-uno")]
    public ActionResult<GameViewDto> CatchUno(MultiplayerCatchUnoRequest request) => Action(lobby.CatchUno(request.PlayerId, request.TargetPlayerId));
    [HttpPost("restart")]
    public ActionResult<GameViewDto> Restart(MultiplayerPlayerRequest request) => lobby.Restart(request.PlayerId) is { } game ? Ok(game) : NotFound(new { error = "Only the host can restart the game." });
    private ActionResult<GameViewDto> Action((GameViewDto? Result, string? Error) result) => result.Result is null ? NotFound(new { error = result.Error }) : result.Error is not null ? BadRequest(new { error = result.Error }) : Ok(result.Result);
}
