using System.Globalization;
using System.Text.RegularExpressions;

namespace Py42.Core;

/// <summary>
/// Listing HP-42S / DM42 écrit à la main (onglet de DM42 Editor, fichier Free42) relu en instructions du simulateur
/// de Py42. Numéros de pas, en-tête { … } et commentaires (// # @) sont ignorés. Les instructions que le simulateur
/// ne connaît pas restent des commandes libres : il les signale (« n'est pas simulé ») et continue.
/// </summary>
public static class RpnListing
{
    public sealed record Result(List<Instr> Code, List<string> Entries, List<string> Warnings);

    static readonly Regex StepRx = new(@"^\d+(?:\s+|▸|▶)(?=\S)");
    static readonly Regex NumRx = new(@"^[-+]?(\d+\.?\d*|\.\d+)([Eᴇe][-+]?\d+)?$");
    static readonly Regex TestRx = new(@"^X(=|≠|<|>|≤|≥|<>|<=|>=|!=)(0|Y)\?$", RegexOptions.IgnoreCase);

    public static Result Parse(string text)
    {
        var code = new List<Instr>();
        var entries = new List<string>();
        var warnings = new List<string>();
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            string s = StripComment(lines[li]).Trim();
            if (s.Length == 0 || s.StartsWith("{")) continue;
            s = StepRx.Replace(s, "").Trim();
            if (s.Length == 0 || s == ".END.") { if (s == ".END.") code.Add(new Instr(K.End, line: li + 1)); continue; }
            var ins = Read(s, warnings, li + 1);
            ins.Line = li + 1;   // ligne de l'éditeur (1-based), pour situer une erreur
            code.Add(ins);
            if (ins.Kind == K.LblG) entries.Add(ins.Arg);
        }
        return new Result(code, entries, warnings);
    }

    /// <summary>Retire // # @ hors des chaînes "…".</summary>
    static string StripComment(string s)
    {
        bool q = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') q = !q;
            if (q) continue;
            if (s[i] == '#' || s[i] == '@' || (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')) return s[..i];
        }
        return s;
    }

    static string Unq(string a) => a.Length >= 2 && a[0] == '"' && a[^1] == '"' ? a[1..^1] : a;

    /// <summary>Label local : 00-99, A-J et a-e (codés au-delà de 99).</summary>
    static int? Local(string a)
    {
        if (int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return n;
        if (a.Length == 1 && char.IsLetter(a[0])) return 1000 + a[0];
        return null;
    }

    static readonly string[] OpNames = { "+", "-", "×", "÷" };
    static int OpIndex(string o) => o switch { "+" => 0, "-" => 1, "×" or "*" or "x" => 2, "÷" or "/" => 3, _ => -1 };

    static Instr Read(string s, List<string> warnings, int line)
    {
        if (NumRx.IsMatch(s)) return new Instr(K.Num, s.Replace('e', 'E'));
        if (s.StartsWith("⊢\"") || s.StartsWith("├\"")) return new Instr(K.AStrAppend, Unq(s[1..]));
        if (s.StartsWith("\"")) return new Instr(K.AStr, Unq(s));

        int sp = s.IndexOf(' ');
        string op = (sp < 0 ? s : s[..sp]).ToUpperInvariant();
        string arg = sp < 0 ? "" : s[(sp + 1)..].Trim();
        bool ind = arg.StartsWith("IND ", StringComparison.OrdinalIgnoreCase);
        bool st = arg.StartsWith("ST ", StringComparison.OrdinalIgnoreCase);

        switch (op)
        {
            case "LBL":
                if (arg.StartsWith("\"")) return new Instr(K.LblG, Unq(arg));
                if (Local(arg) is int ll) return new Instr(K.LblL, n: ll);
                break;
            case "GTO":
                if (arg.StartsWith("\"")) return new Instr(K.Gto, Unq(arg));
                if (Local(arg) is int gl) return new Instr(K.Gto, n: gl);
                break;
            case "XEQ":
                if (arg.StartsWith("\"")) return new Instr(K.Xeq, Unq(arg));
                if (Local(arg) is int xl) return new Instr(K.Xeq, n: xl);
                break;
            case "RTN": return new Instr(K.Rtn);
            case "END": return new Instr(K.End);
            case "STOP": case "R/S": return new Instr(K.Stop);
            case "PI": case "Π": return new Instr(K.Const, "PI");
            case "ENTER": case "ENTER↑": return new Instr(K.Stk, "ENTER");
            case "R↓": case "RDN": case "RV": return new Instr(K.Stk, "R↓");
            case "R↑": case "RUP": case "R^": return new Instr(K.Stk, "R↑");
            case "X<>Y": case "X⇄Y": case "SWAP": return new Instr(K.Stk, "X<>Y");
            case "→POL": case "->POL": return new Instr(K.Stk, "→POL");
            case "→REC": case "->REC": return new Instr(K.Stk, "→REC");
            case "+": case "-": case "×": case "÷": case "*": case "/": case "X":
                if (arg.Length == 0) return new Instr(K.Op, OpNames[OpIndex(op == "X" ? "×" : op)]);
                break;
            case "Y↑X": case "Y^X": return new Instr(K.Op, "Y↑X");
            case "MOD": return new Instr(K.Op, "MOD");
            case "CHS": case "+/-": return new Instr(K.Fn, "+/-");
            case "X^2": return new Instr(K.Fn, "X↑2");
            case "E^X": return new Instr(K.Fn, "E↑X");
            case "CLA": return new Instr(K.Cla);
            case "AVIEW": return new Instr(K.AView);
            case "MVAR": return new Instr(K.Mvar, Unq(arg));
            case "VARMENU": return new Instr(K.VarMenu, Unq(arg));
            case "ISG": case "DSE":
                if (!ind && !st && arg.Length > 0) return new Instr(K.Test, op + " " + Unq(arg));
                break;
            case "FS?": case "FC?": case "FS?C": case "FC?C":
                if (!ind && arg.Length > 0) return new Instr(K.Test, op + " " + arg.PadLeft(2, '0'));
                break;
            case "MAT?": case "CPX?": case "REAL?": case "STR?":
                return new Instr(K.Test, op);
            case "KEY":
            {
                // KEY n GTO nn : menu local ; KEY n GTO/XEQ "NOM" : commande libre du simulateur
                var w = arg.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 3 && w[1].Equals("GTO", StringComparison.OrdinalIgnoreCase) && Local(w[2]) is int kl)
                    return new Instr(K.KeyGto, w[0], kl);
                return new Instr(K.Cmd, "KEY " + arg);
            }
        }

        if (TestRx.IsMatch(op))
        {
            string t = op.Replace("<>", "≠").Replace("!=", "≠").Replace("<=", "≤").Replace(">=", "≥");
            return new Instr(K.Test, t);
        }

        // RCL, STO, VIEW, ARCL, INPUT et leurs variantes STO+ RCL× … sur une variable ou un registre numéroté
        if (!ind && arg.Length > 0)
        {
            string name = Unq(arg);
            if (st)
            {
                string reg = arg[3..].Trim().ToUpperInvariant();
                if (op == "VIEW" && reg == "X") return new Instr(K.ViewX);
                if (op == "ARCL" && reg == "X") return new Instr(K.ArclX);
                if (op == "STO" && reg == "Y") return new Instr(K.Stk, "STO ST Y");
            }
            else
            {
                switch (op)
                {
                    case "RCL": return new Instr(K.Rcl, name);
                    case "STO": return new Instr(K.Sto, name);
                    case "VIEW": return new Instr(K.ViewVar, name);
                    case "ARCL": return new Instr(K.ArclVar, name);
                    case "INPUT": return new Instr(K.Input, name);
                }
                if ((op.StartsWith("STO") || op.StartsWith("RCL")) && op.Length == 4 && OpIndex(op[3..]) is int oi and >= 0)
                    return new Instr(op.StartsWith("STO") ? K.StoOp : K.RclOp, name, oi);
            }
        }
        if (ind || st)
        {
            warnings.Add($"Ligne {line} : « {s} » (adressage {(ind ? "IND" : "ST")}) n'est pas simulé");
            return new Instr(K.Cmd, "·" + s);   // nom inconnu : le simulateur le signale et continue
        }

        if (sp < 0 && Hp42.FnOps.Contains(op)) return new Instr(K.Fn, op);
        return new Instr(K.Cmd, sp < 0 ? op : op + " " + arg);
    }
}
