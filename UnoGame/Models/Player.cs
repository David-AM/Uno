namespace UnoGame.Api.Models;

public interface IPlayer { int Id { get; } string Name { get; } }

public sealed class Player(int id, string name) : IPlayer
{
    public int Id { get; } = id;
    public string Name { get; set; } = name;
    public override string ToString() => Name;
}
