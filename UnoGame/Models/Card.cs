namespace UnoGame.Api.Models;

public sealed record Card(int Id, int? Value, int ScoreValue, CardColor Color, ActionType? Action) : ICard
{
    public override string ToString() => Action?.ToString() ?? Value?.ToString() ?? "Wild";
}
