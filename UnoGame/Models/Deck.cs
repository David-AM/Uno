namespace UnoGame.Api.Models;

public sealed class Deck : IDeck
{
    public List<Card> Cards { get; } = [];

    public int TotalCards => Cards.Count;
}
