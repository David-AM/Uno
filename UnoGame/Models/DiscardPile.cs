namespace UnoGame.Api.Models;

public sealed class DiscardPile : IDiscardPile
{
    public List<Card> Cards { get; } = [];
}
