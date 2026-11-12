using Godot;

namespace AppStein;

/// <summary>Readable names/rarities/descriptions of items ("kind:id"), taken from the Def registries. Used by prompts, toasts, HUD and the Guide.</summary>
public static class ItemInfo
{
    public static string RarityName(Rarity r) => r switch { Rarity.Rare => "Rare", Rarity.Legendary => "Legendary", _ => "Common" };
    public static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Legendary => new Color(1f, 0.6f, 0.2f), Rarity.Rare => new Color(0.45f, 0.72f, 1f), _ => new Color(0.82f, 0.78f, 0.7f),
    };

    /// <summary>"Swift Stein (Common)" / "Rope" / "Gold".</summary>
    public static string Title(string item)
    {
        int c = item.IndexOf(':'); if (c < 0) return item;
        string kind = item.Substring(0, c), id = item.Substring(c + 1);
        if (kind == "stein") { var d = Steins.Get(id); return d == null ? id : $"{d.Name} ({RarityName(d.Rarity)})"; }
        if (kind == "util") return Utilities.Get(id)?.Name ?? id;
        return Ores.Get(id)?.Name ?? id;
    }

    public static string Description(string item)
    {
        int c = item.IndexOf(':'); if (c < 0) return "";
        string kind = item.Substring(0, c), id = item.Substring(c + 1);
        if (kind == "stein") { var d = Steins.Get(id); return d == null ? "" : d.Description + $" (Fed to the Goblin: weighs {Steins.TributeOf(id)}.)"; }
        if (kind == "util") return Utilities.Get(id)?.Description ?? "";
        var o = Ores.Get(id); return o == null ? "" : $"Weighs {o.TributeWeight} on the Scale when fed to the Goblin.";
    }

    /// <summary>Title, then the description on the next line (for prompts).</summary>
    public static string Full(string item)
    {
        string d = Description(item);
        return d == "" ? Title(item) : Title(item) + "\n" + d;
    }

    /// <summary>One line for toasts: "Swift Stein (Common): You move 20% faster."</summary>
    public static string Line(string item)
    {
        string d = Description(item);
        return d == "" ? Title(item) : Title(item) + ": " + d;
    }
}
