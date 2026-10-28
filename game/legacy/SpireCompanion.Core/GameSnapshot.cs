using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpireCompanion.Core;

public sealed record CardView(int Index, string Id, string Name, string Cost, bool Upgraded, string Text = "");
public sealed record ItemView(int Index, string Id, string Name, string Text = "");
public sealed record EnemyView(int Index, string Id, string Name, int Hp, int MaxHp, int Block, string Intent = "");
public sealed record RoomOptionView(string Id, string Name, string Detail, bool Locked);
public sealed record RoomView(string Kind, string Title, string Description, RoomOptionView[] Options)
{
    /// <summary>Runtime identity of the screen that produced this room; stable for one visit.</summary>
    public string Id { get; init; } = "";

    /// <summary>How many cards a selection screen wants; 1 for ordinary single-pick screens.</summary>
    public int Required { get; init; } = 1;
}

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

    /// <summary>Current run floor and ascension, used for tool queries and plan reviews.</summary>
    public int Floor { get; init; }
    public int Ascension { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this))));
}

