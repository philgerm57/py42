using System.Reflection;
using System.Text.RegularExpressions;

namespace Py42.Core;

/// <summary>
/// Traduction des messages et de l'aide. Le français du code sert de clé : py42_en.txt donne, ligne par ligne,
/// « français TAB anglais », avec {0}, {1}… à la place des parties variables. Un texte absent reste en français.
/// </summary>
public static class Tr
{
    static readonly Dictionary<string, string> exact = new();
    static readonly List<(Regex Pattern, string En)> patterns = new();
    static bool english;

    /// <summary>Choisi par l'application au démarrage ; français par défaut.</summary>
    public static bool English
    {
        get => english;
        set { english = value; if (value && exact.Count == 0) Load(); }
    }

    static void Load()
    {
        using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("py42_en.txt");
        if (st == null) return;
        using var r = new StreamReader(st);
        var templates = new List<(string Fr, string En)>();
        while (r.ReadLine() is { } line)
        {
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            string fr = line[..tab], en = line[(tab + 1)..];
            if (fr.Contains("{0}")) templates.Add((fr, en));
            else exact[fr] = en;
        }
        // le modèle le plus long d'abord : le plus précis
        foreach (var (fr, en) in templates.OrderByDescending(t => t.Fr.Length))
        {
            string rx = Regex.Replace(Regex.Escape(fr), @"\\\{(\d+)}", m => $"(?<p{m.Groups[1].Value}>.*?)");
            patterns.Add((new Regex("^" + rx + "$", RegexOptions.Singleline), en));
        }
    }

    /// <summary>Traduit un texte : exact, ou d'après un modèle à parties variables.</summary>
    public static string T(string fr)
    {
        if (!english || string.IsNullOrEmpty(fr)) return fr;
        if (exact.TryGetValue(fr, out var en)) return en;
        foreach (var (rx, tpl) in patterns)
        {
            var m = rx.Match(fr);
            if (m.Success) return Regex.Replace(tpl, @"\{(\d+)}", g => m.Groups["p" + g.Groups[1].Value].Value);
        }
        // message suivi de ce que l'analyseur a trouvé : « ( » attendu (fin de ligne trouvée)
        var found = Regex.Match(fr, @"^(.+?)( \((?:fin de ligne trouvée|fin du fichier trouvée|indentation trouvée|fin de bloc trouvée|« .* » trouvé)\))$", RegexOptions.Singleline);
        return found.Success ? T(found.Groups[1].Value) + T(found.Groups[2].Value) : fr;
    }
}
