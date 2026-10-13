using Avalonia.Media;

namespace BookDepthIQ.Dom;

/// <summary>
/// The colour scheme: every colour the DOM draws is a named token (defaults = the Vest panel's
/// web/styles.css, so both DOMs look the same). The user can change any token in Settings › Colours.
/// Each token owns ONE brush that every control shares; changing the token changes that brush's colour,
/// so the whole window repaints live without being rebuilt. Two tokens never share a colour value
/// (a duplicate is nudged by 1 in blue, invisible), so <see cref="B(Color)"/> can map a colour back to
/// its token's brush.
/// </summary>
public static class Pal
{
    public sealed record Token(string Key, string Group, string Label, Color Default, string? Tip = null);

    public static readonly Token[] Tokens =
    {
        new("bg", "Surfaces", "Background", Color.Parse("#0b0e13")),
        new("panel", "Surfaces", "Header & panels", Color.Parse("#121722")),
        new("card", "Surfaces", "Cards", Color.Parse("#0f141d")),
        new("field", "Surfaces", "Input fields", Color.Parse("#0a0e15")),
        new("edge", "Surfaces", "Card & field borders", Color.Parse("#1c2433")),
        new("line", "Surfaces", "Column dividers", Color.Parse("#1f2633")),
        new("rowLine", "Surfaces", "Ladder row lines", Color.Parse("#10141c")),
        new("hover", "Surfaces", "Hover row", Color.Parse("#1a2130")),

        new("text", "Text", "Text", Color.Parse("#d7dde8")),
        new("muted", "Text", "Labels", Color.Parse("#7b8597")),
        new("accent", "Text", "Accent", Color.Parse("#38bdf8"), "brand, selected controls, group titles"),
        new("segOn", "Text", "Selected background", Color.Parse("#16324a")),

        new("bid", "Market", "Bid / buyers", Color.Parse("#22d3ee")),
        new("ask", "Market", "Ask / sellers", Color.Parse("#fb923c")),
        new("pos", "Market", "Profit / up", Color.Parse("#34d399")),
        new("neg", "Market", "Loss / down", Color.Parse("#f87171")),
        new("warn", "Market", "Highlight / warning", Color.Parse("#fbbf24"), "last price outline, walls, absorption, warnings"),
        new("lastBox", "Market", "Last price box", Color.Parse("#2a3142")),

        new("vwap", "Chart lines", "VWAP + σ bands", Color.Parse("#a78bfa")),
        new("npoc", "Chart lines", "Naked POC", Color.Parse("#f472b6")),
        new("or", "Chart lines", "Opening range", Color.Parse("#eab308")),
        new("stUp", "Chart lines", "SuperTrend up", Color.Parse("#34d399")),
        new("stDn", "Chart lines", "SuperTrend down", Color.Parse("#f87171")),
        new("noChase", "Chart lines", "No-chase line", Color.Parse("#fbbf24")),
        new("range", "Chart lines", "Chop range lines", Color.Parse("#7b8597")),
        new("stackBuy", "Chart lines", "Buy stack zone", Color.Parse("#22d3ee")),
        new("stackSell", "Chart lines", "Sell stack zone", Color.Parse("#fb923c")),

        new("profBar", "Profile · Δ · heat", "Profile bars", Color.Parse("#606a7a")),
        new("profVa", "Profile · Δ · heat", "Profile value area", Color.Parse("#96a0b2")),
        new("profPoc", "Profile · Δ · heat", "Profile POC", Color.Parse("#e4e8f0")),
        new("vaBand", "Profile · Δ · heat", "Value-area band", Color.Parse("#e879f9")),
        new("deltaUp", "Profile · Δ · heat", "Δ buyers", Color.Parse("#22d3ee")),
        new("deltaDn", "Profile · Δ · heat", "Δ sellers", Color.Parse("#fb923c")),
        new("heatBid", "Profile · Δ · heat", "Heat bid size", Color.Parse("#22d3ee")),
        new("heatAsk", "Profile · Δ · heat", "Heat offer size", Color.Parse("#fb923c")),
        new("heatHot", "Profile · Δ · heat", "Heat outsized level", Color.Parse("#fbbf24")),

        new("buyBtn", "Buttons", "BUY MKT", Color.Parse("#0e3a44")),
        new("sellBtn", "Buttons", "SELL MKT", Color.Parse("#44250e")),
        new("flatBtn", "Buttons", "FLATTEN", Color.Parse("#4a1414")),
        new("killBtn", "Buttons", "KILL", Color.Parse("#5a1414")),
        new("btn", "Buttons", "Other buttons", Color.Parse("#1a2130")),

        new("bigBuy", "Tape & log", "Big buy print", Color.Parse("#38bdf8")),
        new("bigSell", "Tape & log", "Big sell print", Color.Parse("#f59e5b")),
        new("logOk", "Tape & log", "Log: sent / filled", Color.Parse("#2ec4b6")),
        new("logBlocked", "Tape & log", "Log: warned / blocked", Color.Parse("#f0b429")),
        new("logBad", "Tape & log", "Log: rejected", Color.Parse("#ff5c5c")),
    };

    /// <summary>Starting points; each one replaces the user's colours (then any token can be tuned).</summary>
    public static readonly (string Name, Dictionary<string, string> Colors)[] Presets =
    {
        ("Default", new()),
        ("Midnight", new()
        {
            ["bg"] = "#05070c", ["panel"] = "#0b1020", ["card"] = "#0a0f1c", ["field"] = "#060914", ["edge"] = "#18213a", ["line"] = "#1a2440",
            ["rowLine"] = "#0b1020", ["hover"] = "#141d36", ["accent"] = "#60a5fa", ["segOn"] = "#142a52", ["lastBox"] = "#1c2747", ["btn"] = "#141d36",
        }),
        ("Graphite", new()
        {
            ["bg"] = "#121212", ["panel"] = "#1b1b1d", ["card"] = "#18181a", ["field"] = "#101011", ["edge"] = "#2c2c30", ["line"] = "#2a2a2e",
            ["rowLine"] = "#19191b", ["hover"] = "#252528", ["text"] = "#e6e6e6", ["muted"] = "#8e8e93", ["accent"] = "#0a84ff", ["segOn"] = "#1c3352",
            ["lastBox"] = "#303034", ["btn"] = "#262629",
        }),
        ("High contrast", new()
        {
            ["bg"] = "#000000", ["panel"] = "#0a0a0a", ["card"] = "#050505", ["field"] = "#020202", ["edge"] = "#3a3a3a", ["line"] = "#333333",
            ["rowLine"] = "#1a1a1a", ["hover"] = "#262626", ["text"] = "#ffffff", ["muted"] = "#b0b0b0", ["accent"] = "#00b4ff", ["segOn"] = "#00324a",
            ["bid"] = "#00e5ff", ["ask"] = "#ff8c00", ["pos"] = "#00ff88", ["neg"] = "#ff4d4d", ["warn"] = "#ffd400", ["lastBox"] = "#303030",
            ["heatBid"] = "#00e5ff", ["heatAsk"] = "#ff8c00", ["deltaUp"] = "#00e5ff", ["deltaDn"] = "#ff8c00", ["btn"] = "#1f1f1f",
        }),
    };

    private static readonly Dictionary<string, SolidColorBrush> brushes = new();
    private static Dictionary<Color, SolidColorBrush> byColor = new();
    /// <summary>The colours as the user set them (before de-duplication), for the settings pickers.</summary>
    private static readonly Dictionary<string, Color> wanted = new();

    static Pal()
    {
        foreach (var t in Tokens) brushes[t.Key] = new SolidColorBrush(t.Default);
        Apply(new Dictionary<string, string>());
    }

    /// <summary>Sets every token: the user's override where there is one, else the default. Live.</summary>
    public static void Apply(IReadOnlyDictionary<string, string> overrides)
    {
        foreach (var t in Tokens)
            wanted[t.Key] = overrides.TryGetValue(t.Key, out var hex) && TryParse(hex, out var c) ? c : t.Default;
        Rekey();
    }

    /// <summary>Sets one token. Live.</summary>
    public static void Set(string key, Color c)
    {
        wanted[key] = c;
        Rekey();
    }

    private static void Rekey()
    {
        var map = new Dictionary<Color, SolidColorBrush>();
        foreach (var t in Tokens)
        {
            var w = wanted[t.Key];
            var c = w;
            for (var k = 1; map.ContainsKey(c); k++)
                c = Color.FromArgb(w.A, w.R, w.G, (byte)Math.Clamp(w.B + (k % 2 == 1 ? (k + 1) / 2 : -k / 2), 0, 255));
            var br = brushes[t.Key];
            if (br.Color != c) br.Color = c;
            map[c] = br;
        }
        byColor = map;
    }

    /// <summary>The colour the user picked for a token (what the settings show).</summary>
    public static Color Wanted(string key) => wanted[key];
    public static Color Get(string key) => brushes[key].Color;
    /// <summary>Overrides that differ from the defaults, as hex (what Settings saves).</summary>
    public static Dictionary<string, string> Overrides() =>
        Tokens.Where(t => wanted[t.Key] != t.Default).ToDictionary(t => t.Key, t => Hex(wanted[t.Key]));

    public static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
    public static bool TryParse(string? s, out Color c)
    {
        c = default;
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return false;
        if (!s.StartsWith('#')) s = "#" + s;
        if (s.Length != 7 || !Color.TryParse(s, out c)) return false;
        return true;
    }

    public static Color Bg => Get("bg");
    public static Color Panel => Get("panel");
    public static Color Card => Get("card");
    public static Color Field => Get("field");
    public static Color Edge => Get("edge");
    public static Color Line => Get("line");
    public static Color Text => Get("text");
    public static Color Muted => Get("muted");
    public static Color Accent => Get("accent");
    public static Color SegOn => Get("segOn");
    public static Color Bid => Get("bid");
    public static Color Ask => Get("ask");
    public static Color Pos => Get("pos");
    public static Color Neg => Get("neg");
    public static Color Warn => Get("warn");
    public static Color LastBox => Get("lastBox");
    public static Color RowLine => Get("rowLine");
    public static Color Hover => Get("hover");
    public static Color Vwap => Get("vwap");
    public static Color Npoc => Get("npoc");
    public static Color Or => Get("or");
    public static Color StUp => Get("stUp");
    public static Color StDn => Get("stDn");
    public static Color NoChase => Get("noChase");
    public static Color Range => Get("range");
    public static Color StackBuy => Get("stackBuy");
    public static Color StackSell => Get("stackSell");
    public static Color ProfBar => Get("profBar");
    public static Color ProfVa => Get("profVa");
    public static Color ProfPoc => Get("profPoc");
    public static Color VaBand => Get("vaBand");
    public static Color DeltaUp => Get("deltaUp");
    public static Color DeltaDn => Get("deltaDn");
    public static Color HeatBid => Get("heatBid");
    public static Color HeatAsk => Get("heatAsk");
    public static Color HeatHot => Get("heatHot");
    public static Color BuyBtn => Get("buyBtn");
    public static Color SellBtn => Get("sellBtn");
    public static Color FlatBtn => Get("flatBtn");
    public static Color KillBtn => Get("killBtn");
    public static Color Btn => Get("btn");
    public static Color BigBuy => Get("bigBuy");
    public static Color BigSell => Get("bigSell");
    public static Color LogOk => Get("logOk");
    public static Color LogBlocked => Get("logBlocked");
    public static Color LogBad => Get("logBad");

    /// <summary>A token's colour returns that token's shared, live brush; any other colour a new brush.</summary>
    public static IBrush B(Color c) => byColor.TryGetValue(c, out var b) ? b : new SolidColorBrush(c);
    public static IBrush B(Color c, double opacity) => new SolidColorBrush(c, opacity);
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");
}
