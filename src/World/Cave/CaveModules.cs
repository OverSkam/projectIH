using System;
using System.Collections.Generic;
using System.Linq;

namespace AppStein;

/// <summary>
/// Hand-built 14x14 room module (cell = 2 m). Door sockets are the two middle cells of a side (index 6,7); a side is "door capable"
/// when both are open. Unused doors are sealed by the generator, modules can be rotated in 90 degree steps.
/// Legend: # rock | . floor 0 | w water floor -1.2 | : ledge -2 | ; pit floor -5 | x crawl (ceiling 1.7) | W thin (ethereal) wall
///   8 6 2 4 = ramp that RISES towards N / E / S / W (run of ramp cells between two flat cells, linear slope)
///   markers: v vein, o ore, m monster, D dragon, t trap, l lever, L loot, C chest, S shrine; Q loot / O ore on pit floor, K loot on ledge.
/// </summary>
public class CaveModule
{
    public const int Size = 14;
    public string Name;
    public string[] Map;
    public float Ceil = 5f;
    public float Weight = 1f;
    public int MinRound = 1, Max = 99;
    public string Role = "";            // "", camp, shrine, nook, lair
    public bool[] Doors;                // N E S W

    public static bool[] DoorsOf(string[] m) => new[]
    {
        m[0][6] != '#' && m[0][7] != '#', m[6][13] != '#' && m[7][13] != '#', m[13][6] != '#' && m[13][7] != '#', m[6][0] != '#' && m[7][0] != '#',
    };

    public static string[] Rotate(string[] m)
    {
        var r = new string[Size];
        for (int row = 0; row < Size; row++)
        {
            var ch = new char[Size];
            for (int col = 0; col < Size; col++)
            {
                char c = m[Size - 1 - col][row];
                ch[col] = c switch { '8' => '6', '6' => '2', '2' => '4', '4' => '8', _ => c };
            }
            r[row] = new string(ch);
        }
        return r;
    }

    public string[] Rotated(int rot) { var m = Map; for (int i = 0; i < rot; i++) m = Rotate(m); return m; }

    public void Init()
    {
        if (Map.Length != Size || Map.Any(l => l.Length != Size)) throw new Exception($"module {Name}: map must be {Size}x{Size}");
        Doors = DoorsOf(Map);
    }

    public static readonly List<CaveModule> All = new()
    {
        new CaveModule { Name = "camp", Role = "camp", Ceil = 6.5f, Map = new[] {
            "######..######", "####......####", "###........###", "##..........##", "#............#", "#............#", ".............." , "..............",
            "#............#", "#............#", "##..........##", "###........###", "####......####", "######..######" } },
        new CaveModule { Name = "cavern", Weight = 3, Ceil = 5.5f, Map = new[] {
            "######..######", "####......####", "###..v.....###", "##..........##", "##....##....##", "#............#", "..............", "..............",
            "#...o........#", "##..........##", "##.....m....##", "###........###", "####......####", "######..######" } },
        new CaveModule { Name = "tunnel", Weight = 2.5f, Ceil = 4.5f, Map = new[] {
            "######..######", "######..######", "######..######", "#####....#####", "#####v...#####", "######..######", "######m.######", "######..######",
            "######...o####", "######..######", "#####....#####", "#####.o..#####", "######..######", "######..######" } },
        new CaveModule { Name = "crossroads", Weight = 2, Ceil = 5f, Map = new[] {
            "######..######", "######..######", "######..######", "#####v...#####", "####......####", "####..L...####", "..............", "..............",
            "####....m.####", "####......####", "#####....#####", "######..######", "######..######", "######..######" } },
        new CaveModule { Name = "gallery", Weight = 2, Ceil = 4.5f, Map = new[] {
            "##############", "##############", "###v.v.v.v.###", "##..........##", "#............#", "#............#", "..............", "......m.......",
            "#............#", "#...o........#", "##..........##", "###v.v.v.v.###", "##############", "##############" } },
        new CaveModule { Name = "pit", Weight = 2, Max = 2, Ceil = 5.5f, Map = new[] {
            "######..######", "####......####", "###........###", "##..........##", "##.8;;;;8.v.##", "##.8;;;;8...##", "##.8;Q;;:...##", "##.8;;O;:...##",
            "##.8;;;;K...##", "##.;;;;;;...##", "##..........##", "###........###", "####......####", "######..######" } },
        new CaveModule { Name = "chasm", Weight = 1.5f, Max = 2, MinRound = 2, Ceil = 6.5f, Map = new[] {
            "##############", "##############", "#.v.;;;;...v.#", "#...;;;;.....#", "#...;;;;.....#", "#...;Q;;.....#", "..............", "....;;;;......",
            "#...;;;;.....#", "#...;;;;####.#", "#...;;;;6666.#", "#...;;;;######", "##############", "##############" } },
        new CaveModule { Name = "flooded", Weight = 1.5f, Max = 2, Ceil = 5f, Map = new[] {
            "######..######", "####..m...####", "###.v......###", "##....88....##", "##....88....##", "##..wwwwww..##", "##..wwwwww..##", "##..wwwwww..##",
            "##..wwwwww..##", "##..wwwwww..##", "##....22....##", "##....22....##", "####..o...####", "######..######" } },
        new CaveModule { Name = "crawl", Weight = 1.5f, Max = 2, Ceil = 4.5f, Map = new[] {
            "######..######", "######..######", "######..######", "#####v...#####", "######..######", "######..######", "######..xxxL##", "######..######",
            "#Lxxxx..######", "######..######", "######..######", "######..######", "######..######", "######..######" } },
        new CaveModule { Name = "pillars", Weight = 2, Ceil = 7.5f, Map = new[] {
            "######..######", "####.v....####", "###..#..#..###", "##..........##", "##.#..##..#.##", "#............#", "..............", "..............",
            "#............#", "##.#..##..#.##", "##....o.....##", "###..#..#..###", "####....m.####", "######..######" } },
        new CaveModule { Name = "shrine", Role = "shrine", Weight = 0, Ceil = 6f, Map = new[] {
            "##############", "##############", "####......####", "###........###", "###...S....###", "###........###", "####......####", "#####....#####",
            "######..######", "######..######", "######..######", "######..######", "######..######", "######..######" } },
        new CaveModule { Name = "nook", Role = "nook", Weight = 0, Ceil = 5f, Map = new[] {
            "##############", "#####....#####", "#####LC..#####", "######WW######", "######..######", "#####....#####", "####......####", "####..v...####",
            "#####....#####", "######..######", "######..######", "######..######", "######..######", "######..######" } },
        new CaveModule { Name = "lair", Role = "lair", Weight = 0, Ceil = 9f, Map = new[] {
            "######..######", "####......####", "###........###", "##..........##", "#....L..L....#", "#..L......L..#", "......D.......", ".....LL.......",
            "#..L......L..#", "#....L..L....#", "##..........##", "###........###", "####......####", "######..######" } },
        new CaveModule { Name = "traps", Weight = 2, Max = 2, Ceil = 4.5f, Map = new[] {
            "######..######", "######..######", "######..######", "######t.######", "######..######", "#####l..######", "######..######", "######.t######",
            "######..######", "######..######", "######t.######", "######.L######", "######..######", "######..######" } },
    };

    static CaveModule() { foreach (var m in All) m.Init(); }
    public static CaveModule Get(string name) => All.First(m => m.Name == name);
}
