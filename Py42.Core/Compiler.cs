using System.Globalization;
using System.Text;

namespace Py42.Core;

public sealed class CompileOptions
{
    /// <summary>Nom du label global du programme principal (7 caractères max).</summary>
    public string ProgramName { get; set; } = "MAIN";
    /// <summary>Ajoute RAD en tête si le programme utilise la trigonométrie (Python travaille en radians).</summary>
    public bool Radians { get; set; } = true;
    /// <summary>Met les noms de variables et de labels en majuscules (plus simple à taper sur la calculatrice).</summary>
    public bool UpperCase { get; set; } = true;
}

public sealed class CompileResult
{
    public required List<Instr> Code { get; init; }
    public required List<string> Warnings { get; init; }
    /// <summary>Labels globaux exécutables (programme principal puis fonctions).</summary>
    public required List<string> Entries { get; init; }
    public required List<string> Variables { get; init; }
    public int MaxStack { get; init; }
    public int LocalLabels { get; init; }

    /// <summary>
    /// Listing texte. header = ligne « 00 { Prgm } » comme dans les exports Free42 :
    /// dm42editor (RpnParser) ne reconnaît un programme qu'après une ligne entre accolades.
    /// </summary>
    public string Listing(bool lineNumbers = true, bool header = true)
    {
        var sb = new StringBuilder();
        int width = Code.Count >= 100 ? 3 : 2;
        if (header) sb.Append(lineNumbers ? new string('0', width) + " " : "").Append("{ Prgm }\n");
        for (int i = 0; i < Code.Count; i++)
        {
            if (lineNumbers) sb.Append((i + 1).ToString().PadLeft(width, '0')).Append(' ');
            sb.Append(Code[i]).Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>Traduit l'arbre Python en programme RPN HP42S / DM42 / Free42.</summary>
public sealed partial class Compiler
{
    readonly CompileOptions o;
    readonly List<Instr> code = new();
    readonly List<string> warnings = new();
    readonly Dictionary<string, Def> funcs = new();
    readonly HashSet<string> sourceNames = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> assignedAnywhere = new();
    readonly Dictionary<string, string> varBack = new();   // nom HP -> nom source
    readonly Dictionary<string, string> labelBack = new();
    readonly HashSet<string> truncWarned = new();
    readonly HashSet<string> hpVars = new();
    readonly Stack<(int Brk, int Cont)> loops = new();
    Dictionary<string, string> rename = new();
    readonly Dictionary<string, int> retCount = new();                  // nombre de valeurs renvoyées par fonction
    readonly Dictionary<string, List<string>> mvars = new();            // label global -> variables MVAR
    string curGlobal = "";
    Def? fn;
    int labelN, tempN, curLine, maxStack;
    bool usesTrig;

    Compiler(CompileOptions o) { this.o = o; }

    public static CompileResult Compile(string src, CompileOptions? options = null)
        => new Compiler(options ?? new CompileOptions()).Run(src);

    // ======================================================================
    //  Tables
    // ======================================================================
    static readonly Dictionary<string, string> Unary1 = new()
    {
        ["sqrt"] = "SQRT", ["sin"] = "SIN", ["cos"] = "COS", ["tan"] = "TAN",
        ["asin"] = "ASIN", ["acos"] = "ACOS", ["atan"] = "ATAN",
        ["sinh"] = "SINH", ["cosh"] = "COSH", ["tanh"] = "TANH",
        ["asinh"] = "ASINH", ["acosh"] = "ACOSH", ["atanh"] = "ATANH",
        ["exp"] = "E↑X", ["expm1"] = "E↑X-1", ["log"] = "LN", ["log1p"] = "LN1+X", ["log10"] = "LOG",
        ["abs"] = "ABS", ["fabs"] = "ABS", ["int"] = "IP", ["trunc"] = "IP",
        ["factorial"] = "N!", ["gamma"] = "GAMMA", ["radians"] = "→RAD", ["degrees"] = "→DEG", ["float"] = ""
    };
    static readonly HashSet<string> TrigFns = new() { "sin", "cos", "tan", "asin", "acos", "atan", "atan2" };
    static readonly HashSet<string> Binary2 = new() { "pow", "atan2", "hypot", "max", "min" };
    static readonly Dictionary<string, string> Inv = new() { ["<"] = ">=", [">"] = "<=", ["<="] = ">", [">="] = "<", ["=="] = "!=", ["!="] = "==" };
    static readonly Dictionary<string, string> Flip = new() { ["<"] = ">", [">"] = "<", ["<="] = ">=", [">="] = "<=", ["=="] = "==", ["!="] = "!=" };
    static readonly Dictionary<string, string> Sym = new() { ["<"] = "<", [">"] = ">", ["<="] = "≤", [">="] = "≥", ["=="] = "=", ["!="] = "≠" };
    static readonly Dictionary<string, int> RclOps = new() { ["+"] = 0, ["-"] = 1, ["*"] = 2, ["/"] = 3 };

    // ======================================================================
    //  Point d'entrée
    // ======================================================================
    CompileResult Run(string src)
    {
        var ast = Parser.Parse(Dedent(Lexer.BlankDocstrings(src)));
        var main = new List<Stmt>();
        foreach (var s in ast)
        {
            if (s is Def d)
            {
                if (funcs.ContainsKey(d.Name)) throw new CompileError($"La fonction « {d.Name} » est définie deux fois", d.Line);
                funcs[d.Name] = d;
            }
            else main.Add(s);
        }
        if (main.Count == 0 && funcs.Count == 0) throw new CompileError("Le programme est vide", 1);

        Analyse(main);
        InferTypes(main);

        var entryIdx = new List<int>();
        var entries = new List<string>();
        if (main.Count > 0)
        {
            string pn = Label(o.ProgramName.Trim().Length > 0 ? o.ProgramName.Trim() : "MAIN");
            if (funcs.Keys.Any(f => Label(f) == pn))
                throw new CompileError($"Le nom du programme « {pn} » est déjà celui d'une fonction", 0);
            curLine = main[0].Line;
            entryIdx.Add(code.Count);
            Emit(K.LblG, pn);
            entries.Add(pn);
            curGlobal = pn;
            rename = new();
            fn = null;
            GenBlock(main);
            curLine = main[^1].Line;
            Emit(K.Rtn);
        }
        foreach (var d in funcs.Values)
        {
            fn = d;
            rename = renames[d.Name];
            curLine = d.Line;
            entryIdx.Add(code.Count);
            Emit(K.LblG, Label(d.Name));
            entries.Add(Label(d.Name));
            curGlobal = Label(d.Name);
            for (int i = d.Params.Count - 1; i >= 0; i--)
            {
                Emit(K.Sto, Var(d.Params[i]));
                if (i > 0) Emit(K.Stk, "R↓");
            }
            GenBlock(d.Body);
            if (d.Body.Count == 0 || d.Body[^1] is not Return) { curLine = d.Line; Emit(K.Rtn); }
        }
        curLine = 0;
        Emit(K.End);

        // Après chaque label global : les MVAR (obligatoirement juste après le label), puis les modes
        // (RAD, RECT, CPXRES) au début du programme principal, sinon au début de chaque fonction.
        var modes = new List<string>();
        if (usesTrig && o.Radians) modes.Add("RAD");
        if (usesComplex) modes.Add("RECT");
        if (usesCmath) modes.Add("CPXRES");
        var radAt = modes.Count > 0 ? (main.Count > 0 ? new List<int> { entryIdx[0] } : entryIdx) : new List<int>();
        for (int i = entryIdx.Count - 1; i >= 0; i--)
        {
            int at = entryIdx[i];
            var lbl = code[at];
            var ins = new List<Instr>();
            if (mvars.TryGetValue(lbl.Arg, out var mv))
                foreach (var v in mv) ins.Add(new Instr(K.Mvar, v, 0, lbl.Line));
            if (radAt.Contains(at)) foreach (var m in modes) ins.Add(new Instr(K.Mode, m, 0, lbl.Line));
            code.InsertRange(at + 1, ins);
        }

        Peephole();
        int locals = RenumberLabels();

        return new CompileResult
        {
            Code = code,
            Warnings = warnings,
            Entries = entries,
            Variables = hpVars.OrderBy(v => v, StringComparer.Ordinal).ToList(),
            MaxStack = maxStack,
            LocalLabels = locals
        };
    }

    // ======================================================================
    //  Analyse des portées (variables locales des fonctions)
    // ======================================================================
    sealed class Info
    {
        public readonly HashSet<string> Assigned = new(), Used = new(), Globals = new(), Calls = new();
        public readonly List<int> Returns = new();
        public readonly HashSet<string> Menu = new();   // variables de varmenu : noms visibles sur la calculatrice
        public IEnumerable<string> All => Assigned.Concat(Used);
    }

    readonly Dictionary<string, Dictionary<string, string>> renames = new();

    void Analyse(List<Stmt> main)
    {
        var mainInfo = new Info();
        Collect(main, mainInfo);
        var infos = new Dictionary<string, Info>();
        var locals = new Dictionary<string, HashSet<string>>();
        foreach (var d in funcs.Values)
        {
            var inf = new Info();
            foreach (var p in d.Params) inf.Assigned.Add(p);
            Collect(d.Body, inf);
            foreach (var g in inf.Globals)
                if (d.Params.Contains(g)) throw new CompileError($"« {g} » est à la fois paramètre et global", d.Line);
            infos[d.Name] = inf;
            // les variables de varmenu gardent leur nom (affiché sur les touches) : elles sont globales
            locals[d.Name] = new HashSet<string>(inf.Assigned.Where(n => !inf.Globals.Contains(n) && !inf.Menu.Contains(n)));
            var counts = inf.Returns.Distinct().ToList();
            if (counts.Count(c => c > 0) > 1)
                throw new CompileError($"La fonction « {d.Name} » renvoie un nombre de valeurs différent selon les return", d.Line);
            retCount[d.Name] = counts.Count == 0 ? 0 : counts.Max();
        }

        foreach (var n in mainInfo.All) sourceNames.Add(n);
        foreach (var i in infos.Values) foreach (var n in i.All) sourceNames.Add(n);
        foreach (var n in mainInfo.Assigned) assignedAnywhere.Add(n);
        foreach (var i in infos.Values) foreach (var n in i.Assigned) assignedAnywhere.Add(n);

        // Récursivité : les variables HP42 sont globales, donc interdite
        var state = new Dictionary<string, int>();
        void Visit(string f, List<string> path)
        {
            if (state.TryGetValue(f, out var st))
            {
                if (st == 1)
                {
                    var cycle = string.Join(" → ", path.SkipWhile(x => x != f).Append(f));
                    throw new CompileError($"Appel récursif ({cycle}) : impossible sur HP42, les variables sont globales. Réécrivez avec une boucle.", funcs[f].Line);
                }
                return;
            }
            state[f] = 1;
            path.Add(f);
            foreach (var c in infos[f].Calls) Visit(c, path);
            path.RemoveAt(path.Count - 1);
            state[f] = 2;
        }
        foreach (var f in funcs.Keys) Visit(f, new List<string>());

        // Renommer les variables locales qui entrent en conflit avec un autre contexte
        foreach (var d in funcs.Values)
        {
            var map = new Dictionary<string, string>();
            var elsewhere = new HashSet<string>(mainInfo.All, StringComparer.OrdinalIgnoreCase);
            foreach (var other in funcs.Values)
                if (other.Name != d.Name) foreach (var n in infos[other.Name].All) elsewhere.Add(n);
            string tag = new string(d.Name.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant();
            if (tag.Length == 0) tag = "F";
            foreach (var n in locals[d.Name].OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!elsewhere.Contains(n)) continue;
                string baseName = n[..Math.Min(n.Length, 7 - 1 - tag.Length)];
                string cand = baseName + "_" + tag;
                int k = 2;
                while (sourceNames.Contains(cand) || map.ContainsValue(cand))
                {
                    string suf = k.ToString();
                    cand = baseName[..Math.Min(baseName.Length, 7 - 1 - tag.Length - suf.Length)] + "_" + tag + suf;
                    k++;
                }
                map[n] = cand;
            }
            renames[d.Name] = map;
            if (map.Count > 0)
                warnings.Add($"Fonction {d.Name} : variable(s) locale(s) renommée(s) pour ne pas écraser les autres ({string.Join(", ", map.Select(kv => $"{kv.Key} → {Case(kv.Value)}"))})");
        }
    }

    void Collect(List<Stmt> body, Info inf)
    {
        foreach (var s in body)
        {
            switch (s)
            {
                case Assign a:
                    foreach (var t in a.Targets) inf.Assigned.Add(t);
                    foreach (var v in a.Values) CollectE(v, inf);
                    break;
                case AugAssign a: inf.Assigned.Add(a.Target); inf.Used.Add(a.Target); CollectE(a.Value, inf); break;
                case ExprStmt e: CollectE(e.E, inf); break;
                case If i: CollectE(i.Cond, inf); Collect(i.Body, inf); Collect(i.Else, inf); break;
                case While w: CollectE(w.Cond, inf); Collect(w.Body, inf); Collect(w.Else, inf); break;
                case For f: inf.Assigned.Add(f.Var); CollectE(f.Start, inf); CollectE(f.Stop, inf); Collect(f.Body, inf); Collect(f.Else, inf); break;
                case Return r: inf.Returns.Add(r.Values.Count); foreach (var v in r.Values) CollectE(v, inf); break;
                case Assert a: CollectE(a.Cond, inf); break;
                case IndexAssign ia: CollectE(ia.Target, inf); CollectE(ia.Value, inf); break;
                case ForEach fe: inf.Assigned.Add(fe.Var); CollectE(fe.Seq, inf); Collect(fe.Body, inf); Collect(fe.Else, inf); break;
                case Global g: foreach (var n in g.Names) inf.Globals.Add(n); break;
                case Def d: throw new CompileError($"Fonction imbriquée « {d.Name} » : définissez toutes les fonctions au premier niveau", d.Line);
            }
        }
    }

    void CollectE(Expr e, Info inf)
    {
        switch (e)
        {
            case Name n: inf.Used.Add(n.Id); break;
            case Unary u: CollectE(u.A, inf); break;
            case Not n: CollectE(n.A, inf); break;
            case Bin b: CollectE(b.A, inf); CollectE(b.B, inf); break;
            case Cmp c: CollectE(c.A, inf); CollectE(c.B, inf); break;
            case BoolOp b: CollectE(b.A, inf); CollectE(b.B, inf); break;
            case Index ix: CollectE(ix.Target, inf); foreach (var p in ix.Parts) CollectE(p, inf); break;
            case Slice sl: if (sl.Start != null) CollectE(sl.Start, inf); if (sl.Stop != null) CollectE(sl.Stop, inf); break;
            case Attr at: CollectE(at.Target, inf); break;
            case ListLit ll: foreach (var x in ll.Items) CollectE(x, inf); break;
            case TupleLit tl: foreach (var x in tl.Items) CollectE(x, inf); break;
            case Call c:
                if (funcs.ContainsKey(c.Func)) inf.Calls.Add(c.Func);
                if (!funcs.ContainsKey(c.Func) && c.Func != "input" && Hp42.VarCommands.TryGetValue(c.Func, out var vc))
                {
                    foreach (var a in c.Args)
                        if (a is Name vn) { if (vc.Assigns) inf.Assigned.Add(vn.Id); else inf.Used.Add(vn.Id); }
                    break;
                }
                if (!funcs.ContainsKey(c.Func) && (Hp42.LabelCommands.ContainsKey(c.Func) || IsKeyCommand(c.Func)))
                {
                    break;   // nom de fonction, pas une variable
                }
                if (c.Func == "varmenu")
                {
                    foreach (var a in c.Args)
                        if (a is Name vn) { inf.Assigned.Add(vn.Id); inf.Menu.Add(vn.Id); }
                        else if (a is Str vs) { inf.Assigned.Add(vs.Value); inf.Menu.Add(vs.Value); }
                    break;
                }
                foreach (var a in c.Args) CollectE(a, inf);
                break;
        }
    }

    // ======================================================================
    //  Noms HP42
    // ======================================================================
    string Case(string n) => o.UpperCase ? n.ToUpperInvariant() : n;

    string Var(string src)
    {
        string mapped = rename.TryGetValue(src, out var r) ? r : src;
        string n = Case(mapped);
        if (n.Length > 7)
        {
            string t = n[..7];
            if (truncWarned.Add(src)) warnings.Add($"Variable « {src} » tronquée en « {t} » (7 caractères max sur HP42)");
            n = t;
        }
        if (varBack.TryGetValue(n, out var other) && other != mapped)
            throw new CompileError($"Les variables « {other} » et « {mapped} » donnent le même nom HP42 « {n} ». Renommez l'une d'elles.", curLine);
        varBack[n] = mapped;
        hpVars.Add(n);
        return n;
    }

    string Label(string src)
    {
        string n = Case(src);
        if (n.Length > 7)
        {
            string t = n[..7];
            if (truncWarned.Add("lbl:" + src)) warnings.Add($"Label « {src} » tronqué en « {t} » (7 caractères max)");
            n = t;
        }
        if (labelBack.TryGetValue(n, out var other) && other != src)
            throw new CompileError($"Les fonctions « {other} » et « {src} » donnent le même label HP42 « {n} »", curLine);
        labelBack[n] = src;
        return n;
    }

    string NewTemp()
    {
        string t;
        do { tempN++; t = "T" + tempN; } while (sourceNames.Contains(t) || varBack.ContainsKey(t));
        varBack[t] = t;
        hpVars.Add(t);
        return t;
    }

    int NewLabel() => ++labelN;

    Instr Emit(K k, string arg = "", int n = 0)
    {
        var i = new Instr(k, arg, n, curLine);
        code.Add(i);
        return i;
    }

    bool IsUser(string f) => funcs.ContainsKey(f);
    bool allowMulti, allowVoid;
    bool IsConstName(Name n) => !n.Raw && (n.Id == "pi" || n.Id == "e") && !assignedAnywhere.Contains(n.Id);

    static string FmtNum(double v, string? raw)
    {
        if (raw != null)
        {
            string s = raw.ToUpperInvariant().Replace("E+", "E");
            if (s.StartsWith('.')) s = "0" + s;
            if (s.Contains('.') && !s.Contains('E')) s = s.TrimEnd('.');
            s = s.Replace(".E", "E");
            return s;
        }
        if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("R", CultureInfo.InvariantCulture).Replace("E+", "E");
    }

    // ======================================================================
    //  Profondeur de pile (algorithme de Sethi-Ullman, pile de 4 niveaux)
    // ======================================================================
    static int Pair(int l, int r) => l >= r ? Math.Max(l, r + 1) : Math.Max(r, l + 1);

    bool RclOpt(Bin b) => b.B is Name n && !IsConstName(n) && RclOps.ContainsKey(b.Op);

    static string? SqOpt(Bin b) => b.Op == "**" && b.B is Num n
        ? n.Value switch { 2 => "X↑2", 0.5 => "SQRT", -1 => "1/X", _ => null }
        : null;

    int Need(Expr e) => e switch
    {
        Num or Const or Name => 1,
        Str s when s.Value.Length > 6 => throw new CompileError($"Texte « {s.Value} » trop long : une variable ou un élément de matrice HP42 contient 6 caractères au plus", s.Line),
        Str => 1,
        Unary u => Need(u.A),
        Bin b when CpxConst(b) != null => 2,
        Bin b when RclOpt(b) => Math.Max(Need(b.A), 1),
        Bin b when SqOpt(b) != null => Need(b.A),
        Bin b => Pair(Need(b.A), Need(b.B)),
        Call c => NeedCall(c),
        Cmp or BoolOp or Not => throw new CompileError("Une comparaison ne peut servir que comme condition de if ou while", e.Line),
        _ => NeedExt(e)
    };

    int NeedCall(Call c)
    {
        if (IsUser(c.Func))
        {
            Arity(c, funcs[c.Func].Params.Count);
            int rc = retCount[c.Func];
            if (rc == 0 && !allowVoid) throw new CompileError($"La fonction « {c.Func} » ne renvoie pas de valeur (pas de return avec valeur)", c.Line);
            if (rc > 1 && !allowMulti)
                throw new CompileError($"« {c.Func} » renvoie {rc} valeurs : utilisez {string.Join(", ", Enumerable.Range(1, rc).Select(i => "v" + i))} = {c.Func}(...)", c.Line);
            int m = rc;
            for (int i = 0; i < c.Args.Count; i++) m = Math.Max(m, i + Need(c.Args[i]));
            return m;
        }
        if (Hp42.FlagTests.ContainsKey(c.Func))
            throw new CompileError($"{c.Func}() est un test de drapeau : utilisez-le dans une condition (if {c.Func}(21): ...)", c.Line);
        if (Hp42.TypeTests.ContainsKey(c.Func))
            throw new CompileError($"{c.Func}() est un test : utilisez-le dans une condition (if {c.Func}(x): ...)", c.Line);
        if (IsHpStatement(c))
            throw new CompileError($"{c.Func}() est une instruction, elle ne renvoie pas de valeur", c.Line);
        switch (c.Func)
        {
            case "menu": throw new CompileError("menu() s'utilise seul : choix = menu(\"A\", \"B\", ...)", c.Line);
            case "varmenu": throw new CompileError("varmenu() est une instruction : varmenu(a, b, c)", c.Line);
            case "prompt": throw new CompileError("prompt() s'utilise seul : x = prompt(\"TEXTE\") ou prompt(\"TEXTE\")", c.Line);
            case "input": throw new CompileError("input() s'utilise seul : x = input(...) ou x = float(input(...))", c.Line);
            case "print": throw new CompileError("print() ne renvoie pas de valeur", c.Line);
            case "dmy": throw new CompileError("dmy() renvoie 3 valeurs : j, m, a = dmy(date)", c.Line);
            case "round":
                throw new CompileError("round() n'a pas d'équivalent direct sur HP42. Utilisez int() (partie entière, IP), floor() ou ceil().", c.Line);
            case "floor": case "ceil":
                throw new CompileError($"{c.Func}() n'est pas encore accepté à cet endroit : calculez-le d'abord dans une variable (n = {c.Func}(x))", c.Line);
        }
        int mn = NeedMatCall(c);
        if (mn >= 0) return mn;
        if (Unary1.ContainsKey(c.Func)) { Arity(c, 1); return Need(c.Args[0]); }
        if (Binary2.Contains(c.Func)) { Arity(c, 2); return Pair(Need(c.Args[0]), Need(c.Args[1])); }
        if (Hp42.Lookup(c.Func, c.Args.Count, out var hp))
        {
            if (hp.Imm != Imm.None && hp.Returns) { ImmArg(c, hp.Imm); return 1; }   // RCL(12)
            if (hp.Imm != Imm.None || !hp.Returns)
                throw new CompileError($"{c.Func}() ne renvoie pas de valeur : utilisez-le comme instruction", c.Line);
            Arity(c, hp.Args);
            if (hp.Results > 1 && !allowMulti)
                throw new CompileError($"{c.Func}() renvoie {hp.Results} valeurs : écrivez a, b = {c.Func}(...)", c.Line);
            int m = hp.Results;
            for (int i = 0; i < c.Args.Count; i++) m = Math.Max(m, i + Need(c.Args[i]));
            return m;
        }
        if (c.Func is "append" or "pop")
            throw new CompileError($"liste.{c.Func}() s'utilise seul sur sa ligne (ou x = liste.pop())", c.Line);
        throw new CompileError($"Fonction « {c.Func} » inconnue. Définissez-la avec def, ou utilisez une fonction de math disponible.", c.Line);
    }

    static void Arity(Call c, int n)
    {
        if (c.Args.Count != n)
            throw new CompileError($"{c.Func}() attend {n} argument(s), {c.Args.Count} donné(s)", c.Line);
    }

    void CheckDepth(Expr e, int baseDepth)
    {
        int n = baseDepth + Need(e);
        if (n > 4)
            throw new CompileError($"Expression trop complexe pour la pile à 4 niveaux X Y Z T (il en faudrait {n}). Calculez une partie dans une variable intermédiaire.", e.Line);
        maxStack = Math.Max(maxStack, n);
    }

    // ======================================================================
    //  Expressions
    // ======================================================================

    /// <summary>
    /// Calcule à l'avance les appels de fonctions utilisateur imbriqués dans une expression
    /// (XEQ peut vider la pile) et les remplace par une variable temporaire.
    /// </summary>
    Expr Prepare(Expr e, bool root)
    {
        switch (e)
        {
            case Call c when IsUser(c.Func):
            {
                Arity(c, funcs[c.Func].Params.Count);
                var args = c.Args.Select(a => Prepare(a, false)).ToList();
                var nc = c with { Args = args };
                if (root) return nc;
                args = args.Select((a, i) => Fit(a, 4 - i)).ToList();
                for (int i = 0; i < args.Count; i++) { CheckDepth(args[i], i); Gen(args[i]); }
                Emit(K.Xeq, Label(c.Func));
                string t = NewTemp();
                Emit(K.Sto, t);
                if (matrixFuncs.Contains(c.Func)) matrixNames.Add(t);
                return new Name(t, c.Line, true);
            }
            case Call { Func: "array" or "asarray" or "mat" or "copy", Args.Count: 1 } ac when !IsUser(ac.Func): return Prepare(ac.Args[0], root);
            case Call { Func: "solve", Args.Count: 2 } sc:
                // solve(A, b) = inv(A) @ b
                return Prepare(new Bin("@", new Call("inv", new() { sc.Args[0] }, sc.Line), sc.Args[1], sc.Line), root);
            case Call { Func: "floor" or "ceil" } fc when !IsUser(fc.Func):
            {
                // floor(x) = x - (x MOD 1), ceil(x) = x + (-x MOD 1) : le MOD de la HP-42S a le signe du diviseur,
                // donc x MOD 1 est toujours dans [0, 1[, y compris pour x négatif (pas de test ni de saut)
                Arity(fc, 1);
                int l = fc.Line;
                if (Parser.ConstValue(fc.Args[0]) is double cv)
                {
                    // constante : calculée dès la conversion
                    double r = fc.Func == "floor" ? Math.Floor(cv) : Math.Ceiling(cv);
                    var n = new Num(Math.Abs(r), Math.Abs(r).ToString(System.Globalization.CultureInfo.InvariantCulture), l);
                    return r < 0 ? new Unary("-", n, l) : n;
                }
                var x = Prepare(fc.Args[0], false);
                if (!(x is Num or Name or Const or Unary { A: Num or Name or Const }))
                {
                    // x sert deux fois : calculé une seule fois dans une variable temporaire
                    var px = Fit(x, 4);
                    CheckDepth(px, 0);
                    Gen(px);
                    string t = NewTemp();
                    Emit(K.Sto, t);
                    x = new Name(t, l, true);
                }
                var one = new Num(1, "1", l);
                return fc.Func == "floor"
                    ? new Bin("-", x, new Bin("%", x, one, l), l)
                    : new Bin("+", x, new Bin("%", x is Unary { Op: "-" } nx ? nx.A : new Unary("-", x, l), one, l), l);
            }
            case Call c: return c with { Args = c.Args.Select(a => a is Str ? a : Prepare(a, false)).ToList() };
            case ListLit l: return HoistList(l);
            case TupleLit t: throw new CompileError("Les tuples ne servent qu'à zeros((lignes, colonnes))", t.Line);
            case Index ix: return PrepareIndex(ix);
            case Attr a: return a with { Target = Prepare(a.Target, false) };
            case Bin b: { var a = Prepare(b.A, false); var bb = Prepare(b.B, false); return b with { A = a, B = bb }; }
            case Unary u: return u with { A = Prepare(u.A, false) };
            default: return e;
        }
    }

    void GenValue(Expr e)
    {
        var p = Fit(Prepare(e, true), 4);
        CheckDepth(p, 0);
        Gen(p);
    }

    void GenConst(string id)
    {
        if (id == "pi") Emit(K.Const, "PI");
        else { Emit(K.Num, "1"); Emit(K.Fn, "E↑X"); }
    }

    void Gen(Expr e)
    {
        switch (e)
        {
            case Num n: Emit(K.Num, FmtNum(n.Value, n.Raw)); break;
            case Const c: GenConst(c.Id); break;
            case Name n when n.Raw: Emit(K.Rcl, n.Id); break;
            case Name n when IsConstName(n): GenConst(n.Id); break;
            case Name n: Emit(K.Rcl, Var(n.Id)); break;
            case Unary { Op: "-", A: Num m }: Emit(K.Num, "-" + FmtNum(m.Value, m.Raw)); break;
            case Unary u:
                Gen(u.A);
                if (u.Op == "-") Emit(K.Fn, "+/-");
                break;
            case Bin b: GenBin(b); break;
            case Call c: GenCall(c); break;
            default: if (!GenExt(e)) Need(e); break; // sinon lève l'erreur adaptée
        }
    }

    void GenBin(Bin b)
    {
        if (CpxConst(b) is { } cc)
        {
            Emit(K.Num, cc.ReRaw);
            Emit(K.Num, cc.ImRaw);
            Emit(K.Cmd, "COMPLEX");
            return;
        }
        if (b.Op == "*" && IsMat(b.A) && IsMat(b.B))
            throw new CompileError("a * b entre deux matrices : utilisez a @ b (produit matriciel). Le produit élément par élément n'existe pas sur la HP-42S.", b.Line);
        if (b.Op == "/" && IsMat(b.B))
            throw new CompileError("Division par une matrice : utilisez solve(A, b) ou inv(A) @ b", b.Line);
        var sq = SqOpt(b);
        if (sq != null) { Gen(b.A); Emit(K.Fn, sq); return; }
        if (RclOpt(b))
        {
            Gen(b.A);
            var n = (Name)b.B;
            Emit(K.RclOp, n.Raw ? n.Id : Var(n.Id), RclOps[b.Op]);
            return;
        }
        bool swapped = GenPair(b.A, b.B);
        bool commutative = b.Op is "+" || (b.Op == "*" && !(IsMat(b.A) && IsMat(b.B)));
        if (swapped && !commutative) Emit(K.Stk, "X<>Y");
        switch (b.Op)
        {
            case "+": Emit(K.Op, "+"); break;
            case "-": Emit(K.Op, "-"); break;
            case "*": case "@": Emit(K.Op, "×"); break;
            case "/": Emit(K.Op, "÷"); break;
            case "**": Emit(K.Op, "Y↑X"); break;
            case "%": Emit(K.Op, "MOD"); break;
            case "//":
                Emit(K.Op, "÷");
                Emit(K.Fn, "IP");
                if (!warnedFloorDiv) { warnedFloorDiv = true; warnings.Add($"Ligne {b.Line} : « // » devient ÷ IP (troncature vers zéro) : résultat différent de Python pour les nombres négatifs"); }
                break;
            default: throw new CompileError($"Opérateur « {b.Op} » non pris en charge", b.Line);
        }
    }
    bool warnedFloorDiv;

    /// <summary>Empile a puis b (ou b puis a si c'est moins profond). Renvoie vrai si inversé (X=a, Y=b).</summary>
    bool GenPair(Expr a, Expr b)
    {
        if (Need(a) >= Need(b)) { Gen(a); Gen(b); return false; }
        Gen(b); Gen(a); return true;
    }

    void GenCall(Call c)
    {
        if (IsUser(c.Func))
        {
            foreach (var a in c.Args) Gen(a);
            Emit(K.Xeq, Label(c.Func));
            return;
        }
        if (GenMatCall(c)) return;
        NeedCall(c); // vérifications
        if (!Unary1.ContainsKey(c.Func) && !Binary2.Contains(c.Func) && Hp42.Lookup(c.Func, c.Args.Count, out var hp))
        {
            if (hp.Imm != Imm.None) { Emit(K.Cmd, Hp42.WithImm(hp, ImmArg(c, hp.Imm))); return; }
            foreach (var a in c.Args) Gen(a);
            EmitHp(hp.Rpn);
            return;
        }
        if (TrigFns.Contains(c.Func)) usesTrig = true;
        if (Unary1.TryGetValue(c.Func, out var op))
        {
            Gen(c.Args[0]);
            if (op.Length > 0) Emit(K.Fn, op);
            return;
        }
        bool sw = GenPair(c.Args[0], c.Args[1]);
        switch (c.Func)
        {
            case "pow":
                if (sw) Emit(K.Stk, "X<>Y");
                Emit(K.Op, "Y↑X");
                break;
            case "atan2": // atan2(y, x) : Y=y, X=x puis →POL donne X=r, Y=θ
                if (sw) Emit(K.Stk, "X<>Y");
                Emit(K.Stk, "→POL");
                Emit(K.Stk, "R↓");
                break;
            case "hypot":
                Emit(K.Stk, "→POL");
                Emit(K.Stk, "STO ST Y");
                Emit(K.Stk, "R↓");
                break;
            case "max":
            case "min":
                Emit(K.Test, c.Func == "max" ? "X<Y?" : "X>Y?");
                Emit(K.Stk, "X<>Y");
                Emit(K.Stk, "STO ST Y");
                Emit(K.Stk, "R↓");
                break;
        }
    }

    // ======================================================================
    //  Conditions
    // ======================================================================

    /// <summary>Saute vers label si la condition vaut « when ».</summary>
    void JumpIf(Expr c, bool when, int label)
    {
        switch (c)
        {
            case Not n: JumpIf(n.A, !when, label); return;
            case BoolOp { Op: "and" } b when !when:
                JumpIf(b.A, false, label); JumpIf(b.B, false, label); return;
            case BoolOp { Op: "and" } b:
            {
                int skip = NewLabel();
                JumpIf(b.A, false, skip); JumpIf(b.B, true, label); Emit(K.LblL, n: skip); return;
            }
            case BoolOp { Op: "or" } b when when:
                JumpIf(b.A, true, label); JumpIf(b.B, true, label); return;
            case BoolOp { Op: "or" } b:
            {
                int ok = NewLabel();
                JumpIf(b.A, true, ok); JumpIf(b.B, false, label); Emit(K.LblL, n: ok); return;
            }
            case Num n:
                if ((n.Value != 0) == when) Emit(K.Gto, n: label);
                return;
            case Cmp cmp: GenCmp(cmp, when, label); return;
            case Call tc when !IsUser(tc.Func) && Hp42.TypeTests.TryGetValue(tc.Func, out var trpn):
            {
                Arity(tc, 1);
                var arg = Fit(Prepare(tc.Args[0], true), 4);
                CheckDepth(arg, 0);
                Gen(arg);
                if (when) { Emit(K.Test, trpn); Emit(K.Gto, n: label); }
                else
                {
                    int skip = NewLabel();
                    Emit(K.Test, trpn);
                    Emit(K.Gto, n: skip);
                    Emit(K.Gto, n: label);
                    Emit(K.LblL, n: skip);
                }
                return;
            }
            case Call fc when Hp42.FlagTests.TryGetValue(fc.Func, out var ft):
            {
                int flag = ImmArg(fc, Imm.Flag);
                if (when) { Emit(K.Test, $"{ft.Rpn} {flag:00}"); Emit(K.Gto, n: label); }
                else if (ft.Negated != "") { Emit(K.Test, $"{ft.Negated} {flag:00}"); Emit(K.Gto, n: label); }
                else
                {
                    // FS?C / FC?C n'ont pas d'inverse : sauter par-dessus le GTO
                    int skip = NewLabel();
                    Emit(K.Test, $"{ft.Rpn} {flag:00}");
                    Emit(K.Gto, n: skip);
                    Emit(K.Gto, n: label);
                    Emit(K.LblL, n: skip);
                }
                return;
            }
            default:
            {
                var p = Fit(Prepare(c, true), 4);
                CheckDepth(p, 0);
                Gen(p);
                Emit(K.Test, when ? "X≠0?" : "X=0?");
                Emit(K.Gto, n: label);
                return;
            }
        }
    }

    static bool IsZero(Expr e) => e is Num { Value: 0 };

    void GenCmp(Cmp c, bool when, int label)
    {
        string op = when ? c.Op : Inv[c.Op];
        var a = Prepare(c.A, false);
        var b = Prepare(c.B, false);
        if (IsZero(b)) { a = Fit(a, 4); CheckDepth(a, 0); Gen(a); Emit(K.Test, $"X{Sym[op]}0?"); }
        else if (IsZero(a)) { b = Fit(b, 4); CheckDepth(b, 0); Gen(b); Emit(K.Test, $"X{Sym[Flip[op]]}0?"); }
        else
        {
            (a, b) = FitPair(a, b, 4);
            int need = Pair(Need(a), Need(b));
            if (need > 4) throw new CompileError("Comparaison trop complexe pour la pile à 4 niveaux. Utilisez une variable intermédiaire.", c.Line);
            maxStack = Math.Max(maxStack, need);
            bool sw = GenPair(a, b);
            // non inversé : Y=a X=b, donc « a op b » s'écrit « X flip(op) Y »
            Emit(K.Test, sw ? $"X{Sym[op]}Y?" : $"X{Sym[Flip[op]]}Y?");
        }
        Emit(K.Gto, n: label);
    }

    // ======================================================================
    //  Instructions
    // ======================================================================
    void GenBlock(List<Stmt> body) { foreach (var s in body) GenStmt(s); }

    static bool IsInput(Expr e) =>
        e is Call { Func: "input" } ||
        e is Call { Func: "float" or "int", Args: [Call { Func: "input" }] };

    static bool EndsWithJump(List<Stmt> b) => b.Count > 0 && b[^1] is Return or Break or Continue;

    void GenStmt(Stmt s)
    {
        curLine = s.Line;
        spillN = 0;   // les variables S1, S2... servent d'une instruction à l'autre
        switch (s)
        {
            case Import: break;
            case IndexAssign ia: GenIndexAssign(ia); break;
            case ForEach fe: GenForEach(fe); break;
            case Assign { Targets.Count: 1, Values: [ListLit ll] } a: GenListLit(Var(a.Targets[0]), ll); break;
            case Assign { Targets.Count: 1, Values: [Call { Func: "array" or "asarray" or "mat", Args: [ListLit al] }] } a:
                GenListLit(Var(a.Targets[0]), al);
                break;
            case Assign { Targets.Count: 1 } a:
            {
                var v = a.Values[0];
                if (IsInput(v)) { Emit(K.Input, Var(a.Targets[0])); break; }
                if (v is Call { Func: "menu" } mc) { GenMenu(mc); Emit(K.Sto, Var(a.Targets[0])); break; }
                if (v is Call { Func: "prompt" } pc) { GenPrint(pc, prompt: true); Emit(K.Sto, Var(a.Targets[0])); break; }
                if (IsMenuOrPrompt(v)) throw new CompileError("menu() et prompt() s'utilisent seuls : x = menu(...)", a.Line);
                if (v is Call { Func: "pop" } pop && !IsUser("pop")) { GenPop(pop, Var(a.Targets[0])); break; }
                GenValue(v);
                Emit(K.Sto, Var(a.Targets[0]));
                break;
            }
            case Assign { Values: [Call { Func: "dmy" } dc] } a when !IsUser("dmy"):
                GenDmy(dc, a);
                break;
            case Assign { Values: [Call uc] } a when IsUser(uc.Func):
            {
                // v1, v2 = f(...) : la fonction laisse ses valeurs sur la pile (la dernière dans X)
                int rc = retCount[uc.Func];
                if (rc != a.Targets.Count)
                    throw new CompileError($"« {uc.Func} » renvoie {rc} valeur(s), {a.Targets.Count} variable(s) à remplir", a.Line);
                var p = Prepare(uc, true);
                allowMulti = true;
                try { CheckDepth(p, 0); Gen(p); }
                finally { allowMulti = false; }
                for (int i = a.Targets.Count - 1; i >= 0; i--)
                {
                    Emit(K.Sto, Var(a.Targets[i]));
                    if (i > 0) Emit(K.Stk, "R↓");
                }
                break;
            }
            case Assign { Values: [Call hc] } a when a.Targets.Count > 1 && !IsUser(hc.Func)
                                                    && Hp42.Lookup(hc.Func, hc.Args.Count, out var hpm) && hpm.Results > 1:
            {
                // l, c = whatDIM(m) ; my, mx = MEAN() : la première variable reçoit Y, la dernière X
                if (hpm.Results != a.Targets.Count)
                    throw new CompileError($"{hc.Func}() renvoie {hpm.Results} valeurs, {a.Targets.Count} variable(s) à remplir", a.Line);
                allowMulti = true;
                try
                {
                    var p = Fit(Prepare(hc, true), 4);
                    CheckDepth(p, 0);
                    Gen(p);
                }
                finally { allowMulti = false; }
                for (int i = a.Targets.Count - 1; i >= 0; i--)
                {
                    Emit(K.Sto, Var(a.Targets[i]));
                    if (i > 0) Emit(K.Stk, "R↓");
                }
                break;
            }
            case Assign a:
            {
                if (a.Values.Count != a.Targets.Count)
                    throw new CompileError($"{a.Targets.Count} variable(s) mais {a.Values.Count} valeur(s)", a.Line);
                if (a.Values.Any(IsInput)) throw new CompileError("input() doit être affecté seul : x = input(...)", a.Line);
                if (a.Values.Any(IsMenuOrPrompt)) throw new CompileError("menu() et prompt() s'utilisent seuls : x = menu(...)", a.Line);
                var ps = a.Values.Select(v => Prepare(v, false)).ToList();
                ps = ps.Select((x, i) => Fit(x, 4 - i)).ToList();
                for (int i = 0; i < ps.Count; i++) { CheckDepth(ps[i], i); Gen(ps[i]); }
                for (int i = ps.Count - 1; i >= 0; i--)
                {
                    Emit(K.Sto, Var(a.Targets[i]));
                    if (i > 0) Emit(K.Stk, "R↓");
                }
                break;
            }
            case AugAssign a when RclOps.TryGetValue(a.Op, out int opi):
                GenValue(a.Value);
                Emit(K.StoOp, Var(a.Target), opi);
                break;
            case AugAssign a:
                GenStmt(new Assign(new() { a.Target }, new() { new Bin(a.Op, new Name(a.Target, a.Line), a.Value, a.Line) }, a.Line));
                break;
            case ExprStmt { E: Call { Func: "print" } c }: GenPrint(c); break;
            case ExprStmt { E: Call { Func: "prompt" } c }: GenPrint(c, prompt: true); break;
            case ExprStmt { E: Call { Func: "varmenu" } c }: GenVarMenu(c); break;
            case ExprStmt { E: Call { Func: "menu" } c }:
                throw new CompileError("Récupérez le choix du menu : choix = menu(\"A\", \"B\", ...)", c.Line);
            case ExprStmt { E: Call { Func: "alpha" } c }: GenPrint(c, view: false); break;
            case ExprStmt { E: Call c } when !IsUser(c.Func) && c.Func.Equals("AVIEW", StringComparison.OrdinalIgnoreCase) && c.Args.Count > 0:
                GenPrint(c);
                break;
            case ExprStmt { E: Call c } when IsHpStatement(c): GenHpStatement(c); break;
            case ExprStmt { E: Call c } when !IsUser(c.Func) && Hp42.TypeTests.ContainsKey(c.Func):
                throw new CompileError($"{c.Func}() est un test : utilisez-le dans une condition (if {c.Func}(x): ...)", c.Line);
            case ExprStmt { E: Call c } when !IsUser(c.Func) && Hp42.FlagTests.ContainsKey(c.Func):
                throw new CompileError($"{c.Func}() est un test : utilisez-le dans une condition (if {c.Func}(n): ...)", c.Line);
            case ExprStmt { E: Call c } when !IsUser(c.Func) && !Unary1.ContainsKey(c.Func) && !Binary2.Contains(c.Func)
                                             && Hp42.Lookup(c.Func, c.Args.Count, out var hp):
                GenCommand(c, hp);
                break;
            case ExprStmt { E: Call { Func: "input" } }: Emit(K.Stop); break;
            case ExprStmt { E: Call { Func: "append" } ap } when !IsUser("append"): GenAppend(ap); break;
            case ExprStmt { E: Call { Func: "pop" } pp } when !IsUser("pop"): GenPop(pp, null); break;
            case ExprStmt { E: Call c } when IsUser(c.Func):
            {
                // procédure appelée seule : f(...) — les valeurs éventuellement renvoyées restent sur la pile
                allowMulti = allowVoid = true;
                try { GenValue(c); }
                finally { allowMulti = allowVoid = false; }
                break;
            }
            case ExprStmt { E: Call uc }:
                NeedCall(uc); // fonction inconnue → erreur claire
                warnings.Add($"Ligne {uc.Line} : résultat de {uc.Func}() inutilisé, appel ignoré");
                break;
            case ExprStmt e:
                warnings.Add($"Ligne {e.Line} : expression sans effet, ignorée");
                break;
            case If i: GenIf(i); break;
            case While w:
            {
                // while ... else : le bloc else s'exécute si la boucle se termine sans break
                int top = NewLabel(), exit = NewLabel(), done = w.Else.Count > 0 ? NewLabel() : exit;
                Emit(K.LblL, n: top);
                JumpIf(w.Cond, false, done);
                loops.Push((exit, top));
                GenBlock(w.Body);
                loops.Pop();
                curLine = w.Line;
                Emit(K.Gto, n: top);
                if (w.Else.Count > 0) { Emit(K.LblL, n: done); GenBlock(w.Else); }
                Emit(K.LblL, n: exit);
                break;
            }
            case For f: GenFor(f); break;
            case Return r:
                if (fn == null) throw new CompileError("return en dehors d'une fonction", r.Line);
                if (r.Values.Count == 1) GenValue(r.Values[0]);
                else if (r.Values.Count > 1)
                {
                    var ps = r.Values.Select(v => Prepare(v, false)).ToList();
                    ps = ps.Select((x, i) => Fit(x, 4 - i)).ToList();
                    for (int i = 0; i < ps.Count; i++) { CheckDepth(ps[i], i); Gen(ps[i]); }
                }
                Emit(K.Rtn);
                break;
            case Break b:
                if (loops.Count == 0) throw new CompileError("break en dehors d'une boucle", b.Line);
                Emit(K.Gto, n: loops.Peek().Brk);
                break;
            case Continue c:
                if (loops.Count == 0) throw new CompileError("continue en dehors d'une boucle", c.Line);
                Emit(K.Gto, n: loops.Peek().Cont);
                break;
            case Global: break;
            case Assert a:
            {
                int ok = NewLabel();
                JumpIf(a.Cond, true, ok);
                string msg = a.Message ?? $"ASSERT L{a.Line}";
                GenPrint(new Call("print", new() { new Str(msg, a.Line) }, a.Line));
                Emit(K.Stop);
                Emit(K.LblL, n: ok);
                break;
            }
            case Def d: throw new CompileError($"Fonction imbriquée « {d.Name} » non prise en charge", d.Line);
        }
    }

    void GenIf(If s)
    {
        int next = NewLabel();
        JumpIf(s.Cond, false, next);
        GenBlock(s.Body);
        if (s.Else.Count > 0)
        {
            int end = NewLabel();
            curLine = s.Line;
            if (!EndsWithJump(s.Body)) Emit(K.Gto, n: end);
            Emit(K.LblL, n: next);
            GenBlock(s.Else);
            Emit(K.LblL, n: end);
        }
        else
        {
            curLine = s.Line;
            Emit(K.LblL, n: next);
        }
    }

    void GenFor(For f)
    {
        GenValue(f.Start);
        Emit(K.Sto, Var(f.Var));

        // La borne est évaluée une seule fois, comme en Python
        Expr stop = f.Stop;
        bool simple = Parser.ConstValue(stop) != null
            || (stop is Name sn && !AssignedIn(f.Body, sn.Id) && sn.Id != f.Var);
        if (!simple)
        {
            GenValue(stop);
            string t = NewTemp();
            Emit(K.Sto, t);
            stop = new Name(t, f.Line, true);
        }

        int top = NewLabel(), cont = NewLabel(), exit = NewLabel(), done = NewLabel();
        Emit(K.LblL, n: top);
        JumpIf(new Cmp(f.Step > 0 ? "<" : ">", new Name(f.Var, f.Line), stop, f.Line), false, done);
        loops.Push((exit, cont));
        GenBlock(f.Body);
        loops.Pop();
        curLine = f.Line;
        Emit(K.LblL, n: cont);
        Emit(K.Num, FmtNum(f.Step, null));
        Emit(K.StoOp, Var(f.Var), 0);
        Emit(K.Gto, n: top);
        // fin normale : comme en Python, la variable garde la dernière valeur parcourue
        Emit(K.LblL, n: done);
        Emit(K.Num, FmtNum(f.Step, null));
        Emit(K.StoOp, Var(f.Var), 1);
        GenBlock(f.Else);
        Emit(K.LblL, n: exit);
    }

    bool AssignedIn(List<Stmt> body, string name)
    {
        var inf = new Info();
        Collect(body, inf);
        return inf.Assigned.Contains(name);
    }

    static bool IsMenuOrPrompt(Expr e) => e is Call { Func: "menu" or "prompt" };

    /// <summary>Argument immédiat constant : FIX(2), SF(21), TONE(5)...</summary>
    int ImmArg(Call c, Imm kind)
    {
        var (min, max) = Hp42.Range(kind);
        if (c.Args.Count != 1)
            throw new CompileError($"{c.Func}() attend 1 argument (nombre entier de {min} à {max})", c.Line);
        var v = Parser.ConstValue(c.Args[0]);
        if (v == null || v != Math.Floor(v.Value) || v < min || v > max)
            throw new CompileError($"{c.Func}() attend un nombre entier constant de {min} à {max}", c.Line);
        return (int)v.Value;
    }

    /// <summary>Commande HP42S utilisée comme instruction : BEEP(), FIX(2), PIXEL(l, c)...</summary>
    void GenCommand(Call c, Hp42Cmd hp)
    {
        if (hp.Imm != Imm.None && hp.Args > 0)
        {
            // STO(x, n) : la valeur sur la pile, le registre en argument immédiat
            if (c.Args.Count != hp.Args + 1)
                throw new CompileError($"{c.Func}() attend {hp.Args + 1} arguments : {c.Func}(valeur, registre)", c.Line);
            var reg = ImmArg(c with { Args = c.Args.Skip(hp.Args).ToList() }, hp.Imm);
            var pv = Fit(Prepare(c.Args[0], true), 4);
            CheckDepth(pv, 0);
            Gen(pv);
            Emit(K.Cmd, Hp42.WithImm(hp, reg));
            return;
        }
        if (hp.Imm != Imm.None) { Emit(K.Cmd, Hp42.WithImm(hp, ImmArg(c, hp.Imm))); return; }
        Arity(c, hp.Args);
        var ps = c.Args.Select(a => Prepare(a, false)).ToList();
        ps = ps.Select((x, i) => Fit(x, 4 - i)).ToList();
        for (int i = 0; i < ps.Count; i++) { CheckDepth(ps[i], i); Gen(ps[i]); }
        EmitHp(hp.Rpn);
    }

    /// <summary>
    /// choix = menu("ADD", "SUB", ...) : menu programmable sur les touches du haut.
    /// Renvoie 1 à 6 selon la touche, 0 pour EXIT. R/S sans choix réaffiche le menu.
    /// </summary>
    void GenMenu(Call c)
    {
        if (c.Args.Count is < 1 or > 6)
            throw new CompileError("menu() accepte de 1 à 6 choix (une rangée de touches)", c.Line);
        var labels = new List<string>();
        foreach (var a in c.Args)
        {
            if (a is not Str st) throw new CompileError("Les choix de menu() doivent être des chaînes constantes", c.Line);
            if (st.Value.Length == 0) throw new CompileError("Un choix de menu() ne peut pas être vide", c.Line);
            if (st.Value.Length > 15) throw new CompileError($"Choix « {st.Value} » trop long (15 caractères max)", c.Line);
            if (st.Value.Length > 6 && warnedMenu.Add(c.Line))
                warnings.Add($"Ligne {c.Line} : libellé de menu de plus de 6 caractères, il sera tronqué à l'écran");
            labels.Add(st.Value.Replace('"', '\''));
        }
        int wait = NewLabel(), end = NewLabel(), exit = NewLabel();
        var keys = labels.Select(_ => NewLabel()).ToList();
        Emit(K.Cmd, "CLMENU");
        for (int i = 0; i < labels.Count; i++)
        {
            Emit(K.AStr, labels[i]);
            Emit(K.KeyGto, (i + 1).ToString(), keys[i]);
        }
        Emit(K.KeyGto, "9", exit);           // touche EXIT
        Emit(K.LblL, n: wait);
        Emit(K.Cmd, "MENU");
        Emit(K.Stop);
        Emit(K.Gto, n: wait);                // R/S sans choisir : on réaffiche
        for (int i = 0; i < labels.Count; i++)
        {
            Emit(K.LblL, n: keys[i]);
            Emit(K.Num, (i + 1).ToString());
            Emit(K.Gto, n: end);
        }
        Emit(K.LblL, n: exit);
        Emit(K.Num, "0");
        Emit(K.LblL, n: end);
        Emit(K.Cmd, "CLMENU");
        Emit(K.Cmd, "EXITALL");
    }
    readonly HashSet<int> warnedMenu = new();

    /// <summary>
    /// varmenu(a, b, c) : menu de saisie des variables (MVAR + VARMENU). L'utilisateur tape une valeur,
    /// appuie sur la touche de la variable pour la ranger, puis R/S pour continuer.
    /// </summary>
    void GenVarMenu(Call c)
    {
        if (c.Args.Count == 0) throw new CompileError("varmenu() attend au moins une variable : varmenu(a, b)", c.Line);
        var names = new List<string>();
        foreach (var a in c.Args)
        {
            string n = a switch
            {
                Name nm when !nm.Raw => nm.Id,
                Str st => st.Value,
                _ => throw new CompileError("varmenu() attend des noms de variables : varmenu(a, b, c)", c.Line)
            };
            names.Add(Var(n));
        }
        if (mvars.TryGetValue(curGlobal, out var prev))
        {
            if (!prev.SequenceEqual(names))
                throw new CompileError($"Un seul varmenu() différent par fonction (« {curGlobal} » en a déjà un)", c.Line);
        }
        else mvars[curGlobal] = names;
        Emit(K.VarMenu, curGlobal);
        Emit(K.Stop);
        Emit(K.Cmd, "EXITALL");
    }

    /// <summary>print(...) → ALPHA + AVIEW ; prompt(...) → ALPHA + PROMPT (arrêt, saisie d'une valeur, R/S).</summary>
    void GenPrint(Call c, bool prompt = false, bool view = true)
    {
        var args = c.Args;
        if (args.Count == 0)
        {
            if (prompt) { Emit(K.Cmd, "PROMPT"); return; }
            Emit(K.Cla);
            if (view) Emit(K.AView);
            return;
        }
        if (args.Count == 1 && args[0] is not Str && !prompt && view)
        {
            var p = Prepare(args[0], true);
            if (p is Name n && !IsConstName(n)) { Emit(K.ViewVar, n.Raw ? n.Id : Var(n.Id)); return; }
            p = Fit(p, 4);
            CheckDepth(p, 0);
            Gen(p);
            Emit(K.ViewX);
            return;
        }

        // Calculer d'abord les appels de fonctions (ils peuvent eux-mêmes utiliser ALPHA)
        var ps = args.Select(a => a is Str ? a : Prepare(a, false)).ToList();
        var pending = new StringBuilder();
        bool started = false;
        int total = 0;
        void Flush()
        {
            string txt = pending.ToString().Replace('"', '\'');
            pending.Clear();
            // 15 caractères max pour "texte", 14 pour ⊢"texte" (l'octet 7F compte dans la longueur)
            int i = 0;
            while (i < txt.Length)
            {
                int len = Math.Min(started ? 14 : 15, txt.Length - i);
                Emit(started ? K.AStrAppend : K.AStr, txt.Substring(i, len));
                started = true;
                i += len;
            }
        }
        for (int i = 0; i < ps.Count; i++)
        {
            if (i > 0) { pending.Append(' '); total++; }
            if (ps[i] is Str s) { pending.Append(s.Value); total += s.Value.Length; continue; }
            Flush();
            if (!started) { Emit(K.Cla); started = true; }
            if (ps[i] is Name n && !IsConstName(n)) Emit(K.ArclVar, n.Raw ? n.Id : Var(n.Id));
            else { var f = Fit(ps[i], 4); CheckDepth(f, 0); Gen(f); Emit(K.ArclX); }
            total += 12;
        }
        Flush();
        if (prompt) Emit(K.Cmd, "PROMPT");
        else if (view) Emit(K.AView);
        if (total > 44) warnings.Add($"Ligne {c.Line} : texte affiché probablement plus long que les 44 caractères du registre ALPHA");
    }

    // ======================================================================
    //  Optimisations et numérotation des labels
    // ======================================================================
    bool Unconditional(int i) =>
        (code[i].Kind is K.Gto or K.Rtn) && (i == 0 || code[i - 1].Kind != K.Test);

    void Peephole()
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            // Code mort après un saut ou un RTN inconditionnel
            for (int i = 0; i < code.Count; i++)
            {
                if (!Unconditional(i)) continue;
                while (i + 1 < code.Count && code[i + 1].Kind is not (K.LblL or K.LblG or K.End))
                { code.RemoveAt(i + 1); changed = true; }
            }
            // GTO nn juste avant LBL nn
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].Kind == K.Gto && code[i + 1].Kind == K.LblL && code[i + 1].N == code[i].N
                    && (i == 0 || code[i - 1].Kind != K.Test))
                { code.RemoveAt(i); changed = true; }
            }
            // Labels locaux inutilisés
            var used = new HashSet<int>(code.Where(x => x.IsJump).Select(x => x.N));
            int before = code.Count;
            code.RemoveAll(x => x.Kind == K.LblL && !used.Contains(x.N));
            if (code.Count != before) changed = true;
            // Deux labels identiques consécutifs : fusionner
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].Kind == K.LblL && code[i + 1].Kind == K.LblL)
                {
                    int keep = code[i].N, drop = code[i + 1].N;
                    foreach (var x in code) if (x.IsJump && x.N == drop) x.N = keep;
                    code.RemoveAt(i + 1);
                    changed = true;
                }
            }
        }
    }

    int RenumberLabels()
    {
        var map = new Dictionary<int, int>();
        foreach (var x in code)
            if (x.Kind == K.LblL) map[x.N] = map.Count + 1;
        if (map.Count > 99)
            throw new CompileError($"Le programme a besoin de {map.Count} labels locaux, la HP42 n'en offre que 99 (00 à 99). Découpez-le en plusieurs fonctions.", 0);
        foreach (var x in code)
            if (x.Kind is K.LblL || x.IsJump) x.N = map[x.N];
        return map.Count;
    }
}
