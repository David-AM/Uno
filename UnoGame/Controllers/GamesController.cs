using Microsoft.AspNetCore.Mvc;
using UnoGame.Api.DTOs;
using UnoGame.Api.Services;

namespace UnoGame.Api.Controllers;

[ApiController]
[Route("api/games")]
public sealed class GamesController(IGameService games) : ControllerBase
{
    [HttpPost]
    public ActionResult<GameViewDto> Create([FromBody] CreateGameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlayerName)) return BadRequest(new { error = "Player name is required." });
        var view = games.Create(request);
        return CreatedAtAction(nameof(Get), new { gameId = view.GameId }, view);
    }
    [HttpGet("{gameId}")]
    public ActionResult<GameViewDto> Get(string gameId) => games.Get(gameId) is { } view ? Ok(view) : NotFound(new { error = "Game not found." });
    [HttpPost("{gameId}/play")]
    public ActionResult<GameViewDto> Play(string gameId, PlayCardRequest request) => Result(games.Play(gameId, request, out var error), error);
    [HttpPost("{gameId}/draw")]
    public ActionResult<GameViewDto> Draw(string gameId) => Result(games.Draw(gameId, out var error), error);
    [HttpPost("{gameId}/pass")]
    public ActionResult<GameViewDto> Pass(string gameId) => Result(games.Pass(gameId, out var error), error);
    [HttpPost("{gameId}/uno")]
    public ActionResult<GameViewDto> Uno(string gameId) => Result(games.SayUno(gameId, out var error), error);
    [HttpPost("{gameId}/catch-uno")]
    public ActionResult<GameViewDto> CatchUno(string gameId, CatchUnoRequest request) => Result(games.CatchUno(gameId, request.PlayerId, out var error), error);
    [HttpPost("{gameId}/restart")]
    public ActionResult<GameViewDto> Restart(string gameId, [FromQuery] bool resetScores = false) => games.Restart(gameId, resetScores) is { } view ? Ok(view) : NotFound(new { error = "Game not found." });
    private ActionResult<GameViewDto> Result(GameViewDto? view, string? error) => view is null ? NotFound(new { error = "Game not found." }) : error is not null ? BadRequest(new { error }) : Ok(view);
}
