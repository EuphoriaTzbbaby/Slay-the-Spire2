using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpireCompanion.Core;

public sealed record CardView(int Index, string Id, string Name, string Cost, bool Upgraded);
public sealed record ItemView(int Index, string Id, string Name);
public sealed record EnemyView(int Index, string Id, string Name, int Hp, int MaxHp, int Block);
public sealed record RoomOptionView(string Id, string Name, string Detail, bool Locked);
public sealed record RoomView(string Kind, string Title, string Description, RoomOptionView[] Options);

public sealed record GameSnapshot(string Scene, string Character, int Hp, int MaxHp, int Gold,
    string[] Hand, string CombatTurn, bool CanEndTurn, string BlockReason, string Revision = "")
{
    public int? Energy { get; init; }
    public int Block { get; init; }
    public CardView[] Cards { get; init; } = [];
    public CardView[] Deck { get; init; } = [];
    public ItemView[] Relics { get; init; } = [];
    public ItemView[] Potions { get; init; } = [];
    public EnemyView[] Enemies { get; init; } = [];
    /// <summary>Event, shop, rest site and similar screens: the options the player can actually pick.</summary>
    public RoomView? Room { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this))));
}

