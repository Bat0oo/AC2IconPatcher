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
        ["PC_icon_dpad-forward_Map"] = "W",
        ["PC_icon_dpad-backward_Map"] = "S",
        ["PC_icon_dpad-left_Map"] = "A",
        ["PC_icon_dpad-right_Map"] = "D",
        ["PC_icon_head_Map"] = "E",
        ["PC_icon_armedhand_Map"] = "LMB",
        ["PC_icon_highprofile_Map"] = "RMB",
        ["PC_icon_openhand_Map"] = "SHIFT",
        ["PC_icon_feet_Map"] = "SPACE",
        ["PC_icon_weaponwheel_Map"] = "Q",
        ["PC_icon_targetlock_Map"] = "F",
        ["PC_icon_actioncamera_Map"] = "T",
        ["PC_icon_centercamera_Map"] = "C",
        ["PC_icon_firstpersoncamera_Map"] = "9",
        ["PC_icon_fists_Map"] = "4",
        ["PC_icon_hiddenblade_Map"] = "2",
        ["PC_icon_sword_Map"] = "3",
        ["PC_icon_shortblade_Map"] = "1",
        ["PC_icon_map_Map"] = "TAB",
        ["PC_icon_pause_Map"] = "ESC",
        ["PC_icon_pancamera-up_Map"] = "UP",
        ["PC_icon_pancamera-down_Map"] = "DOWN",
        ["PC_icon_pancamera-left_Map"] = "LEFT",
        ["PC_icon_pancamera-right_Map"] = "RIGHT",
    };

    /// <summary>
    /// Raspored HUD atlasa (HUD_Controls_0_Map, 128x256 = mreza 4x8 celija po 32px).
    /// Atlas sadrzi istu akciju u vise varijanti - Xbox, PlayStation i PC - pa se
    /// sve varijante zamjenjuju istom tipkom, da ne zavisi od toga koju igra bira.
    /// Raspored je procitan iz atlasa i identican je u svim regionalnim forgeovima
    /// (provjereno: Roma i San Marco imaju bajt u bajt isti atlas).
    /// Kljuc je (kolona, red); celije koje nisu navedene se ne diraju.
    /// </summary>
    public static readonly Dictionary<(int col, int row), string> AtlasCells = new()
    {
        [(0, 1)] = "F",
        [(1, 1)] = "E",
        [(2, 1)] = "E",
        [(0, 2)] = "F",
        [(1, 2)] = "E",
        [(2, 2)] = "E",
        [(3, 2)] = "E",
        [(1, 3)] = "SHIFT",
        [(2, 3)] = "SHIFT",
        [(3, 3)] = "E",
        [(0, 4)] = "SPACE",
        [(1, 4)] = "SHIFT",
        [(2, 4)] = "SHIFT",
        [(3, 4)] = "SHIFT",
        [(0, 5)] = "SPACE",
        [(1, 5)] = "SPACE",
        [(2, 5)] = "LMB",
        [(3, 5)] = "LMB",
        [(0, 6)] = "LMB",
        [(1, 6)] = "SPACE",
        [(2, 6)] = "SPACE",
        [(3, 6)] = "LMB",
        [(0, 7)] = "SHIFT",
        [(1, 7)] = "LMB",
        [(2, 7)] = "SPACE",
        [(3, 7)] = "LMB",
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
    public static byte[] Render(int w, int h, string label) => Render(w, h, label, 1.0);

    /// <summary>
    /// Kao gore, ali sa skaliranjem svjetline. Atlas ima svijetle i tamne varijante
    /// iste ikonice (aktivna / neaktivna), pa im zadrzavamo razliku.
    /// </summary>
    public static byte[] Render(int w, int h, string label, double brightness)
    {
        var px = new byte[w * h * 4];
        DrawBadge(px, w, h, brightness);

        switch (label.ToUpperInvariant())
        {
            case "LMB": DrawMouse(px, w, h, left: true); break;
            case "RMB": DrawMouse(px, w, h, left: false); break;
            case "SPACE": DrawWideKey(px, w, h, arrow: 0, hFactor: 0.32); break;
            case "SHIFT": DrawWideKey(px, w, h, arrow: 1, hFactor: 0.54); break;
            case "UP": DrawArrow(px, w, h, 0); break;
            case "DOWN": DrawArrow(px, w, h, 1); break;
            case "LEFT": DrawArrow(px, w, h, 2); break;
            case "RIGHT": DrawArrow(px, w, h, 3); break;
            case "ESC": DrawText(px, w, h, "ESC", small: true); break;
            case "TAB": DrawText(px, w, h, "TAB", small: true); break;
            case "CTRL": DrawText(px, w, h, "CTL", small: true); break;
            case "ALT": DrawText(px, w, h, "ALT", small: true); break;
            default:
                if (label.Length == 1) DrawText(px, w, h, label, small: false);
                else DrawText(px, w, h, label.Length > 3 ? label[..3] : label, small: true);
                break;
        }
        return px;
    }

    /// <summary>
    /// Ucrtava tipke u HUD atlas. Ulaz i izlaz su RGBA u normalnoj orijentaciji.
    /// Svjetlina svake celije se preuzima iz originala da se zadrzi razlika
    /// izmedju aktivne i neaktivne varijante.
    /// </summary>
    public static byte[] PatchAtlas(byte[] rgba, int w, int h)
    {
        var outp = (byte[])rgba.Clone();
        const int cell = 32;

        foreach (var kv in AtlasCells)
        {
            int x0 = kv.Key.col * cell, y0 = kv.Key.row * cell;
            if (x0 + cell > w || y0 + cell > h) continue;

            // prosjecna svjetlina originalne celije (samo neprozirni pikseli)
            double sum = 0; int n = 0;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    int o = ((y0 + y) * w + x0 + x) * 4;
                    if (rgba[o + 3] < 40) continue;
                    sum += (rgba[o] + rgba[o + 1] + rgba[o + 2]) / 3.0;
                    n++;
                }
            double avg = n > 0 ? sum / n : 60;
            double brightness = Math.Clamp(avg / 60.0, 0.65, 2.6);

            var icon = Render(cell, cell, kv.Value, brightness);
            for (int y = 0; y < cell; y++)
                Array.Copy(icon, y * cell * 4, outp, ((y0 + y) * w + x0) * 4, cell * 4);
        }
        return outp;
    }

    private static void Set(byte[] px, int w, int h, int x, int y, (byte r, byte g, byte b) c, byte a = 255)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        int o = (y * w + x) * 4;
        px[o] = c.r; px[o + 1] = c.g; px[o + 2] = c.b; px[o + 3] = a;
    }

    /// <summary>Okrugli "zeton" u stilu originalnih ikonica igre.</summary>
    private static void DrawBadge(byte[] px, int w, int h, double brightness = 1.0)
    {
        byte Scale(byte v) => (byte)Math.Clamp(v * brightness, 0, 255);
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
                    var c = (Scale((byte)(Ring.r * t)), Scale((byte)(Ring.g * t)), Scale((byte)(Ring.b * t)));
                    Set(px, w, h, x, y, c, (byte)(d > outer - 0.6 ? 190 : 255));
                }
                else
                {
                    // blagi gradijent od vrha ka dnu
                    double t = y / (double)h;
                    var c = (Scale((byte)(BadgeLight.r + (BadgeDark.r - BadgeLight.r) * t)),
                             Scale((byte)(BadgeLight.g + (BadgeDark.g - BadgeLight.g) * t)),
                             Scale((byte)(BadgeLight.b + (BadgeDark.b - BadgeLight.b) * t)));
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

    /// <summary>Strelica: 0 gore, 1 dolje, 2 lijevo, 3 desno.</summary>
    private static void DrawArrow(byte[] px, int w, int h, int dir)
    {
        int cx = w / 2, cy = h / 2;
        int len = (int)(Math.Min(w, h) * 0.34);
        int triH = Math.Max(3, len * 2 / 3);
        int stem = Math.Max(1, len / 4);

        for (int i = 0; i < triH; i++)
            for (int j = -i; j <= i; j++)
            {
                int x = dir switch { 0 or 1 => cx + j, 2 => cx - len + i, _ => cx + len - i };
                int y = dir switch { 0 => cy - len + i, 1 => cy + len - i, _ => cy + j };
                Set(px, w, h, x, y, Ink);
            }

        for (int i = triH; i <= len * 2; i++)
            for (int j = -stem; j <= stem; j++)
            {
                int x = dir switch { 0 or 1 => cx + j, 2 => cx - len + i, _ => cx + len - i };
                int y = dir switch { 0 => cy - len + i, 1 => cy + len - i, _ => cy + j };
                if (dir <= 1 ? Math.Abs(y - cy) <= len : Math.Abs(x - cx) <= len)
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