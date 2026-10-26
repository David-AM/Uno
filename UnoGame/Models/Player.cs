namespace UnoGame.Api.Models;

public sealed class Player(int id, string name) : IPlayer
{
    public int Id { get; } = id;
    public string Name { get; set; } = name;
    public override string ToString() => Name;
}
