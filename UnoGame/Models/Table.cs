namespace UnoGame.Api.Models;

public sealed class Table(Deck drawPile, DiscardPile discardPile) : ITable
{
    public Deck DrawPile { get; } = drawPile;
    public DiscardPile DiscardPile { get; } = discardPile;
    public CardColor ActiveColor { get; set; }
}
