using System;
using System.Collections.Generic;

namespace AC2IconPatcher;

/// <summary>
/// Crta ikonice tastera. Sve je proceduralno - nista se ne preuzima iz igre ni
/// od drugih modova, pa nema pitanja oko licenci.
///
/// Slova su u 5x7 pikselnoj mrezi, sto na 32x32 ikonici izgleda cisto i uklapa
/// se u stil igre (originalne ikonice su takodje pikselaste).
/// </summary>
public static class IconRenderer
{
    /// <summary>
    /// Koja ikonica u igri odgovara kojem tasteru, prema podrazumijevanim
    /// kontrolama AC2 na PC-u.
    /// </summary>
    public static readonly Dictionary<string, string> IconKeys = new()
    {
        ["PC_icon_dpad-forward_Map"]  = "W",
        ["PC_icon_dpad-backward_Map"] = "S",
        ["PC_icon_dpad-left_Map"]     = "A",
        ["PC_icon_dpad-right_Map"]    = "D",
        ["PC_icon_head_Map"]          = "E",
        ["PC_icon_armedhand_Map"]     = "LMB",
        ["PC_icon_highprofile_Map"]   = "RMB",
        ["PC_icon_openhand_Map"]      = "SHIFT",
        ["PC_icon_feet_Map"]          = "SPACE",
        ["PC_icon_weaponwheel_Map"]   = "Q",
        ["PC_icon_targetlock_Map"]    = "F",
        ["PC_icon_actioncamera_Map"]  = "T",
        ["PC_icon_centercamera_Map"]  = "C",
        ["PC_icon_firstpersoncamera_Map"] = "9",
    };

    // 5x7 font, jedan string po redu, '#' je upaljen piksel
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['C'] = [".####", "#....", "#....", "#....", "#....", "#....", ".####"],
        ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
        ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['G'] = [".####", "#....", "#....", "#..##", "#...#", "#...#", ".###."],
        ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['I'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
        ['J'] = ["####.", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
        ['K'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
        ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
        ['M'] = ["#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#", "#...#"],
        ['N'] = ["#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#"],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['Q'] = [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
        ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
        ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
        ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['V'] = ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
        ['W'] = ["#...#", "#...#", "#...#", "#...#", "#.#.#", "##.##", "#...#"],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['Y'] = ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
        ['Z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
        ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['3'] = ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
        ['4'] = ["#...#", "#...#", "#...#", "#####", "....#", "....#", "....#"],
        ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ['6'] = [".###.", "#....", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "....#", ".###."],
    };

    private static readonly (byte r, byte g, byte b) Ink = (235, 235, 235);
    private static readonly (byte r, byte g, byte b) BadgeDark = (38, 38, 38);
    private static readonly (byte r, byte g, byte b) BadgeLight = (78, 78, 78);
    private static readonly (byte r, byte g, byte b) Ring = (150, 150, 150);
    private static readonly (byte r, byte g, byte b) Accent = (190, 40, 40);

    /// <summary>Crta ikonicu zadate oznake. Vraca RGBA u normalnoj orijentaciji.</summary>
    public static byte[] Render(int w, int h, string label)
    {
        var px = new byte[w * h * 4];
        DrawBadge(px, w, h);

        switch (label.ToUpperInvariant())
        {
            case "LMB": DrawMouse(px, w, h, left: true); break;
            case "RMB": DrawMouse(px, w, h, left: false); break;
            case "SPACE": DrawWideKey(px, w, h, arrow: 0, hFactor: 0.32); break;
            case "SHIFT": DrawWideKey(px, w, h, arrow: 1, hFactor: 0.54); break;
            case "CTRL": DrawText(px, w, h, "CTL", small: true); break;
            case "ALT": DrawText(px, w, h, "ALT", small: true); break;
            case "TAB": DrawText(px, w, h, "TAB", small: true); break;
            default:
                if (label.Length == 1) DrawText(px, w, h, label, small: false);
                else DrawText(px, w, h, label.Length > 3 ? label[..3] : label, small: true);
                break;
        }
        return px;
    }

    private static void Set(byte[] px, int w, int h, int x, int y, (byte r, byte g, byte b) c, byte a = 255)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        int o = (y * w + x) * 4;
        px[o] = c.r; px[o + 1] = c.g; px[o + 2] = c.b; px[o + 3] = a;
    }

    /// <summary>Okrugli "zeton" u stilu originalnih ikonica igre.</summary>
    private static void DrawBadge(byte[] px, int w, int h)
    {
        double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
        double outer = Math.Min(w, h) / 2.0 - 0.5;
        double inner = outer - Math.Max(1.5, outer * 0.11);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d > outer) continue;

                if (d > inner)
                {
                    // prsten, malo svjetliji pri vrhu da ima dubine
                    double t = 1.0 - (y / (double)h) * 0.45;
                    var c = ((byte)(Ring.r * t), (byte)(Ring.g * t), (byte)(Ring.b * t));
                    Set(px, w, h, x, y, c, (byte)(d > outer - 0.6 ? 190 : 255));
                }
                else
                {
                    // blagi gradijent od vrha ka dnu
                    double t = y / (double)h;
                    var c = ((byte)(BadgeLight.r + (BadgeDark.r - BadgeLight.r) * t),
                             (byte)(BadgeLight.g + (BadgeDark.g - BadgeLight.g) * t),
                             (byte)(BadgeLight.b + (BadgeDark.b - BadgeLight.b) * t));
                    Set(px, w, h, x, y, c);
                }
            }
    }

    private static void DrawText(byte[] px, int w, int h, string text, bool small)
    {
        int scale = small ? Math.Max(1, w / 24) : Math.Max(1, w / 12);
        int gw = 5 * scale, gh = 7 * scale, gap = scale;
        int total = text.Length * gw + (text.Length - 1) * gap;
        int x0 = (w - total) / 2, y0 = (h - gh) / 2;

        int cx = x0;
        foreach (char ch in text)
        {
            if (Font.TryGetValue(char.ToUpperInvariant(ch), out var rows))
                for (int ry = 0; ry < 7; ry++)
                    for (int rx = 0; rx < 5; rx++)
                        if (rows[ry][rx] == '#')
                            for (int sy = 0; sy < scale; sy++)
                                for (int sx = 0; sx < scale; sx++)
                                    Set(px, w, h, cx + rx * scale + sx, y0 + ry * scale + sy, Ink);
            cx += gw + gap;
        }
    }

    /// <summary>Siroka tipka (Space / Shift). arrow: 0 = bez strelice, 1 = strelica gore.</summary>
    private static void DrawWideKey(byte[] px, int w, int h, int arrow, double hFactor = 0.34)
    {
        int kw = (int)(w * 0.62), kh = (int)(h * hFactor);
        int x0 = (w - kw) / 2, y0 = (h - kh) / 2;

        for (int x = x0; x < x0 + kw; x++)
        {
            Set(px, w, h, x, y0, Ink);
            Set(px, w, h, x, y0 + kh - 1, Ink);
        }
        for (int y = y0; y < y0 + kh; y++)
        {
            Set(px, w, h, x0, y, Ink);
            Set(px, w, h, x0 + kw - 1, y, Ink);
        }

        if (arrow == 0)
        {
            // crtica u sredini = razmaknica
            int bw = kw / 2, by = y0 + kh - 3;
            for (int x = x0 + (kw - bw) / 2; x < x0 + (kw + bw) / 2; x++) Set(px, w, h, x, by, Ink);
        }
        else
        {
            // strelica gore = Shift
            int acx = w / 2;
            int top = y0 + 2, bottom = y0 + kh - 3;
            int triH = Math.Max(3, (bottom - top) / 2 + 1);

            for (int i = 0; i < triH; i++)          // trougao
                for (int x = acx - i; x <= acx + i; x++)
                    Set(px, w, h, x, top + i, Ink);

            int stemW = Math.Max(1, triH / 3);
            for (int y = top + triH; y <= bottom; y++)   // stablo
                for (int x = acx - stemW; x <= acx + stemW; x++)
                    Set(px, w, h, x, y, Ink);
        }
    }

    /// <summary>Obris misa sa oznacenim lijevim ili desnim dugmetom.</summary>
    private static void DrawMouse(byte[] px, int w, int h, bool left)
    {
        int mw = (int)(w * 0.42), mh = (int)(h * 0.56);
        int x0 = (w - mw) / 2, y0 = (h - mh) / 2;
        int split = y0 + mh / 3;

        for (int y = y0; y < y0 + mh; y++)
            for (int x = x0; x < x0 + mw; x++)
            {
                bool edge = x == x0 || x == x0 + mw - 1 || y == y0 || y == y0 + mh - 1;
                // zaobljeni uglovi
                bool corner = (x <= x0 + 1 || x >= x0 + mw - 2) && (y <= y0 + 1 || y >= y0 + mh - 2);
                if (corner) continue;

                bool inButton = y < split && (left ? x < x0 + mw / 2 : x > x0 + mw / 2);
                if (inButton) Set(px, w, h, x, y, Accent);
                else if (edge) Set(px, w, h, x, y, Ink);
            }

        for (int x = x0 + 1; x < x0 + mw - 1; x++) Set(px, w, h, x, split, Ink);
        int cxm = x0 + mw / 2;
        for (int y = y0 + 1; y < split; y++) Set(px, w, h, cxm, y, Ink);
        // kabl
        for (int y = y0 - 3; y < y0; y++) Set(px, w, h, cxm, y, Ink);
    }
}
