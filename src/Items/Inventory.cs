using System;
using System.Collections.Generic;

namespace AppStein;

/// <summary>Server-side authoritative inventory; Encode() is the wire format sent only to the owner.</summary>
public class Inventory
{
    public const int BaseSack = 4, SteinSlots = 2, UtilSlots = 2;
    public List<string> Sack = new();
    public List<string> Steins = new();
    public string[] Utils = new string[UtilSlots];
    public int Selected = 0;
    /// <summary>Highlighted stein slot: the one dropped when a new stein is picked up with full slots.</summary>
    public int SteinSel = 0;

    public string Held => Utils[Selected] ?? "";

    public void Clear()
    {
        Sack.Clear(); Steins.Clear(); Utils = new string[UtilSlots]; Selected = 0; SteinSel = 0;
    }

    public void GiveStartKit()
    {
        Utils[0] = "pickaxe"; Utils[1] = "lantern"; Selected = 1;
    }

    public bool IsEmpty()
    {
        if (Sack.Count > 0 || Steins.Count > 0) return false;
        foreach (var u in Utils) if (!string.IsNullOrEmpty(u)) return false;
        return true;
    }

    public int FreeUtilSlot()
    {
        for (int i = 0; i < Utils.Length; i++) if (string.IsNullOrEmpty(Utils[i])) return i;
        return -1;
    }

    /// <summary>Wire format: sack|steins|util0,util1|selected|steinSel</summary>
    public string Encode() =>
        string.Join(",", Sack) + "|" + string.Join(",", Steins) + "|" + (Utils[0] ?? "") + "," + (Utils[1] ?? "") + "|" + Selected + "|" + SteinSel;

    public static Inventory Decode(string s)
    {
        var inv = new Inventory();
        var p = s.Split('|');
        if (p.Length < 4) return inv;
        if (p[0] != "") inv.Sack.AddRange(p[0].Split(','));
        if (p[1] != "") inv.Steins.AddRange(p[1].Split(','));
        var u = p[2].Split(',');
        for (int i = 0; i < u.Length && i < UtilSlots; i++) inv.Utils[i] = u[i];
        int.TryParse(p[3], out inv.Selected);
        if (p.Length > 4) int.TryParse(p[4], out inv.SteinSel);
        return inv;
    }

    /// <summary>Serialises everything into a flat "kind:id" list used for loot bags (server only).</summary>
    public List<string> ToItemList()
    {
        var l = new List<string>();
        foreach (var o in Sack) l.Add("ore:" + o);
        foreach (var s in Steins) l.Add("stein:" + s);
        foreach (var u in Utils) if (!string.IsNullOrEmpty(u)) l.Add("util:" + u);
        return l;
    }
}
