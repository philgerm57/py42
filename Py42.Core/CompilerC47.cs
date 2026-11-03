namespace Py42.Core;

/// <summary>
/// Cible C47 / R47 : if / while / for deviennent les structures IF ELSE ENDIF, DO WHILE ENDDO et FORᵀᴼᴾ NEXT
/// (AN0007, programmation structurée). Le test placé juste avant IF ou WHILE en est la condition ; une condition
/// composée (and, or, not) est d'abord calculée dans un indicateur local (.00 à .31, donnés par LocR).
/// Les structures sont écrites sans numéro : l'encodeur les numérote comme VALID.
/// </summary>
public sealed partial class Compiler
{
    int flagDepth;

    // ======================================================================
    //  Fonctions C47 (C47Functions) : table Python → instruction, constantes, appel direct c47.NOM(...)
    // ======================================================================

    /// <summary>Profondeur de pile d'un appel C47 (table, constante, appel direct), ou -1 si ce n'en est pas un.</summary>
    int NeedC47(Call c)
    {
        if (c.Func.StartsWith("const:"))
        {
            string n = c.Func[6..];
            if (!C47) throw new CompileError($"scipy.constants.{n} : constantes disponibles avec la cible C47 seulement", c.Line);
            if (C47Functions.FindConstant(n) == null)
                throw new CompileError($"scipy.constants.{n} n'est pas disponible ; constantes connues : {string.Join(", ", C47Functions.Constants.Select(k => k.Python))}", c.Line);
            return 1;
        }
        if (c.Func.StartsWith("stats:") && !C47)
            throw new CompileError($"scipy.stats.{c.Func[6..]}() : lois de probabilité disponibles avec la cible C47 seulement", c.Line);
        if (c.Func.StartsWith("c47:") && !C47)
            throw new CompileError($"c47.{c.Func[4..]}() : appel direct d'une instruction C47, cible C47 seulement", c.Line);
        if (!C47 || !(c.Func.StartsWith("c47:") || C47Functions.Find(c.Func, c.Args.Count) != null)) return -1;
        var args = c.Func == "c47:call" ? Direct(c).Args : C47Args(c);
        int m = 1;
        for (int i = 0; i < args.Count; i++) m = Math.Max(m, i + Need(args[i]));
        return m;
    }

    /// <summary>Émet un appel C47 : arguments empilés dans l'ordre (le dernier dans X), puis l'instruction.</summary>
    bool GenC47Call(Call c)
    {
        if (c.Func.StartsWith("const:"))
        {
            NeedC47(c);
            Emit(K.Fn, C47Functions.FindConstant(c.Func[6..])!.Rpn);
            return true;
        }
        if (!C47) return false;
        if (c.Func == "c47:call")
        {
            var (name, rest) = Direct(c);
            foreach (var a in rest) Gen(a);
            Emit(K.Fn, name);
            return true;
        }
        string? rpn = c.Func.StartsWith("c47:") ? c.Func[4..] : C47Functions.Find(c.Func, c.Args.Count)?.Rpn;
        if (rpn == null) return false;
        // atan2, ellipkinc… : Python calcule en radians, RAD en tête
        if (TrigFns.Contains(c.Func) || C47Functions.Find(c.Func, c.Args.Count)?.Angle == true) usesTrig = true;
        foreach (var a in C47Args(c)) Gen(a);
        Emit(K.Fn, rpn);
        return true;
    }

    /// <summary>
    /// scipy.stats.loi.méthode(x, paramètres…) : chaque paramètre dans son registre lettré (STO M, STO S…), x, puis
    /// l'instruction C47 (Normlₚ, Binom◭…). Le résultat est rangé dans une variable intermédiaire.
    /// </summary>
    Expr GenDistribution(Call c)
    {
        int l = c.Line;
        var parts = c.Func[6..].Split('.');
        var d = C47Functions.FindDistribution(parts[0])
            ?? throw new CompileError($"scipy.stats.{parts[0]} n'est pas disponible ; lois connues : {string.Join(", ", C47Functions.Distributions.Select(x => x.Python))}", l);
        if (c.Args.Count == 0) throw new CompileError($"{parts[0]}.{parts[1]}() attend x en premier argument", l);
        var pars = c.Args.Skip(1).ToList();
        if (pars.Count > d.Params.Length)
            throw new CompileError($"{parts[0]}.{parts[1]}() : {d.Params.Length} paramètre(s) au plus après x ({(Py42.Core.Tr.English ? d.En : d.Fr)})", l);
        if (d.Python == "norm" && pars.Count == 0) d = C47Functions.StdNormal;   // norm.pdf(x) : ϕₚ, Φ◭…
        string rpn = C47Functions.Method(d, parts[1])
            ?? throw new CompileError($"{parts[0]}.{parts[1]} : méthodes disponibles pdf (ou pmf), cdf, sf, ppf", l);
        for (int i = 0; i < d.Params.Length; i++)
        {
            string reg = d.Params[i];
            Expr v = i < pars.Count ? pars[i] : new Num(d.Defaults[i], null, l);
            if (reg == "")
            {
                // loc de scipy : la C47 n'a pas de décalage pour cette loi
                if (Parser.ConstValue(v) != 0) throw new CompileError($"{parts[0]}.{parts[1]}() : loc doit valoir 0 pour la C47", l);
                continue;
            }
            GenValue(v);
            if (reg.EndsWith("/")) Emit(K.Fn, "1/x");   // scale de scipy → taux λ
            if (reg.EndsWith("~")) { Emit(K.Fn, "CHS"); Emit(K.Num, "1"); Emit(K.Op, "+"); }   // 1 - p
            Emit(K.Cmd, "STO " + reg.TrimEnd('/', '~'));
        }
        GenValue(c.Args[0]);
        bool ppf = parts[1] == "ppf";
        // décalage de x : géométrique (Shift -1) ; sf discret : P(X > k) = P(X ≥ k + 1)
        int shift = (ppf ? 0 : d.Shift) + (d.Discrete && parts[1] == "sf" ? 1 : 0);
        if (shift != 0) { Emit(K.Num, Math.Abs(shift).ToString()); Emit(K.Op, shift > 0 ? "+" : "-"); }
        Emit(K.Fn, rpn);
        if (ppf && d.Shift != 0) { Emit(K.Num, Math.Abs(d.Shift).ToString()); Emit(K.Op, d.Shift < 0 ? "+" : "-"); }
        string t = NewTemp();
        Emit(K.Sto, t);
        return new Name(t, l, true);
    }

    /// <summary>
    /// c47.call("ft→m", x) : instruction C47 par son nom écrit en texte (symboles compris), puis ses arguments.
    /// Le nom n'est pas vérifié ici : l'encodeur de DM42 Editor signale une instruction inconnue.
    /// </summary>
    static (string Name, List<Expr> Args) Direct(Call c)
    {
        if (c.Args.Count == 0 || c.Args[0] is not Str s || s.Value.Trim() == "")
            throw new CompileError("c47.call() attend le nom de l'instruction en texte : c47.call(\"ft→m\", x)", c.Line);
        return (s.Value.Trim(), c.Args.Skip(1).ToList());
    }

    /// <summary>Arguments dans l'ordre de la pile de la C47 : inversés pour les fonctions marquées Swap.</summary>
    List<Expr> C47Args(Call c) =>
        C47Functions.Find(c.Func, c.Args.Count)?.Swap == true ? Enumerable.Reverse(c.Args).ToList() : c.Args;

    /// <summary>
    /// statistics.mean(v), median, stdev, pstdev : CLΣ, chaque élément de la liste par Σ+, puis l'instruction C47 ;
    /// le résultat est rangé dans une variable intermédiaire (calculé avant l'expression qui l'utilise).
    /// </summary>
    Expr GenStatistics(Call c)
    {
        var f = C47Functions.FindStatistics(c.Func)!;
        Arity(c, 1);
        if (c.Args[0] is not (Name or ListLit))
            throw new CompileError($"{c.Func}() attend une liste ou une variable liste : {c.Func}(v) ou {c.Func}([1, 2, 3])", c.Line);
        int l = c.Line;
        Emit(K.Cmd, "CLΣ");
        string x = NewTemp();
        var body = new List<Stmt> { new ExprStmt(new Call("c47:Σ+", new() { new Name(x, l, true) }, l), l) };
        GenForEach(new ForEach(x, c.Args[0], body, new(), l));
        Emit(K.Fn, f.Rpn);
        string t = NewTemp();
        Emit(K.Sto, t);
        return new Name(t, l, true);
    }

    /// <summary>Nom de variable entre guillemets dans une commande (INDEX "A"), registre local C47 tel quel (INDEX .01).</summary>
    static string Ref(string name) => Instr.IsLocalReg(name) ? name : $"\"{name}\"";

    /// <summary>Indicateur local libre pour une condition composée ; les conditions imbriquées prennent les suivants.</summary>
    int AllocFlag()
    {
        if (flagDepth > 31) throw new CompileError("Condition trop imbriquée : 32 indicateurs locaux au plus", curLine);
        flagsUsed = true;
        return flagDepth++;
    }

    static string Flag(int f) => "." + f.ToString("00");

    /// <summary>Émet le calcul de la condition, terminé par un seul test vrai quand elle est vraie (IF ou WHILE suit).</summary>
    void CondTest(Expr c)
    {
        switch (c)
        {
            case Cmp cmp: CmpTest(cmp, true); return;
            case Not { A: Cmp cmp }: CmpTest(cmp, false); return;
            case Not { A: Call nf } when !IsUser(nf.Func) && Hp42.FlagTests.TryGetValue(nf.Func, out var nft) && nft.Negated != "":
                Emit(K.Test, $"{nft.Negated} {ImmArg(nf, Imm.Flag):00}");
                return;
            case Call tc when !IsUser(tc.Func) && Hp42.TypeTests.TryGetValue(tc.Func, out var trpn):
            {
                Arity(tc, 1);
                var arg = Fit(Prepare(tc.Args[0], true), 4);
                CheckDepth(arg, 0);
                Gen(arg);
                Emit(K.Test, trpn);
                return;
            }
            case Call pc when pc.Func == "isprime" && !IsUser(pc.Func):
            {
                // sympy.isprime(n) → PRIME?
                Arity(pc, 1);
                var arg = Fit(Prepare(pc.Args[0], true), 4);
                CheckDepth(arg, 0);
                Gen(arg);
                Emit(K.Test, "PRIME?");
                return;
            }
            case Call fc when !IsUser(fc.Func) && Hp42.FlagTests.TryGetValue(fc.Func, out var ft):
                Emit(K.Test, $"{ft.Rpn} {ImmArg(fc, Imm.Flag):00}");
                return;
            case BoolOp or Not:
            {
                int f = AllocFlag();
                BoolInto(c, f);
                Emit(K.Test, $"FS? {Flag(f)}");
                flagDepth--;
                return;
            }
            default:
            {
                // valeur quelconque : vraie si non nulle, comme en Python
                var p = Fit(Prepare(c, true), 4);
                CheckDepth(p, 0);
                Gen(p);
                Emit(K.Test, "X≠0?");
                return;
            }
        }
    }

    /// <summary>
    /// Range la valeur de la condition dans l'indicateur local f. « a and b » n'évalue b que si a est vrai,
    /// « a or b » que si a est faux, comme en Python.
    /// </summary>
    void BoolInto(Expr e, int f)
    {
        switch (e)
        {
            case BoolOp b:
                BoolInto(b.A, f);
                Emit(K.Test, $"{(b.Op == "and" ? "FS?" : "FC?")} {Flag(f)}");
                Emit(K.Struct, "IF");
                BoolInto(b.B, f);
                Emit(K.Struct, "ENDIF");
                return;
            case Not n when !DirectNot(n):
            {
                // not d'un test sans inverse (isMAT, not (a or b)…) : valeur calculée dans g, puis inversée dans f
                int g = AllocFlag();
                BoolInto(n.A, g);
                Emit(K.Cmd, $"CF {Flag(f)}");
                Emit(K.Test, $"FC? {Flag(g)}");
                Emit(K.Cmd, $"SF {Flag(f)}");
                flagDepth--;
                return;
            }
            default:
                // test simple : vrai, il exécute le SF qui suit ; faux, il le saute (règle HP hors IF / WHILE)
                Emit(K.Cmd, $"CF {Flag(f)}");
                CondTest(e);
                Emit(K.Cmd, $"SF {Flag(f)}");
                return;
        }
    }

    /// <summary>not qu'un seul test inversé exprime : comparaison (x≥? devient x<?) ou test d'indicateur avec son contraire (FS? / FC?).</summary>
    bool DirectNot(Not n) => n.A is Cmp
        || n.A is Call c && !IsUser(c.Func) && Hp42.FlagTests.TryGetValue(c.Func, out var ft) && ft.Negated != "";

    void GenIfC47(If s)
    {
        if (Parser.ConstValue(s.Cond) is double k)
        {
            // condition constante : seul le bloc retenu est produit
            GenBlock(k != 0 ? s.Body : s.Else);
            return;
        }
        CondTest(s.Cond);
        Emit(K.Struct, "IF");
        GenBlock(s.Body);
        if (s.Else.Count > 0)
        {
            curLine = s.Line;
            Emit(K.Struct, "ELSE");
            GenBlock(s.Else);
        }
        curLine = s.Line;
        Emit(K.Struct, "ENDIF");
    }

    /// <summary>
    /// while c: … else: … → DO, test, WHILE, corps, ENDDO, puis le else. break saute après le else, continue juste avant
    /// ENDDO (qui reprend au test). Un GTO hors d'un DO est permis : DO ne garde rien en mémoire.
    /// </summary>
    void GenWhileC47(While w)
    {
        int exit = NewLabel(), cont = NewLabel();
        Emit(K.Struct, "DO");
        CondTest(w.Cond);
        Emit(K.Struct, "WHILE");
        loops.Push((exit, cont));
        GenBlock(w.Body);
        loops.Pop();
        curLine = w.Line;
        Emit(K.LblL, n: cont);
        Emit(K.Struct, "ENDDO");
        GenBlock(w.Else);
        Emit(K.LblL, n: exit);
    }

    /// <summary>
    /// for v in range(a, b, pas) : FORᵀᴼᴾ (compare avant le premier tour, comme range qui peut être vide) avec la fin
    /// b - 1 (ou b + 1 en descendant), range excluant sa borne. FORᵀᴼᴾ laisse le compteur un pas après la fin, Python
    /// garde la dernière valeur : le pas est retiré après NEXT. Un break (sortir d'un FOR par GTO garde son emplacement
    /// occupé), une affectation de v dans la boucle ou un pas non entier passent par DO … ENDDO.
    /// </summary>
    void GenForC47(For f)
    {
        if (HasBreak(f.Body) || AssignedIn(f.Body, f.Var) || f.Step != Math.Floor(f.Step))
        {
            GenForDo(f);
            return;
        }
        string v = Var(f.Var);
        var start = Fit(Prepare(f.Start, false), 4);
        CheckDepth(start, 0);
        Gen(start);
        // fin incluse : range(a, n + 1) finit à n, range(a, 10) à 9, sinon b - 1 (b + 1 en descendant) calculé
        int dir = f.Step > 0 ? 1 : -1;
        Expr end = f.Stop;
        bool adjust = false;
        if (Parser.ConstValue(f.Stop) is double cs)
        {
            double ev = cs - dir;
            end = ev < 0 ? new Unary("-", new Num(-ev, null, f.Line), f.Line) : new Num(ev, null, f.Line);
        }
        else if (f.Stop is Bin { Op: "+" or "-" } sb && Parser.ConstValue(sb.B) is double k && (sb.Op == "+" ? k : -k) == dir)
            end = sb.A;
        else adjust = true;
        var stop = Fit(Prepare(end, false), 3);
        CheckDepth(stop, 1);
        Gen(stop);
        maxStack = Math.Max(maxStack, 3);
        if (adjust)
        {
            Emit(K.Num, "1");
            Emit(K.Op, dir > 0 ? "-" : "+");
        }
        Emit(K.Num, FmtNum(f.Step, null));
        Emit(K.ForTop, v);
        int exit = NewLabel(), cont = NewLabel();
        loops.Push((exit, cont));
        GenBlock(f.Body);
        loops.Pop();
        curLine = f.Line;
        Emit(K.LblL, n: cont);
        Emit(K.Next, v);
        Emit(K.Num, FmtNum(f.Step, null));
        Emit(K.StoOp, v, 1);
        GenBlock(f.Else);
        Emit(K.LblL, n: exit);
    }

    /// <summary>for avec break : compteur incrémenté à la main dans un DO … ENDDO, comme le while.</summary>
    void GenForDo(For f)
    {
        GenValue(f.Start);
        Emit(K.Sto, Var(f.Var));
        Expr stop = f.Stop;
        bool simple = Parser.ConstValue(stop) != null || (stop is Name sn && !AssignedIn(f.Body, sn.Id) && sn.Id != f.Var);
        if (!simple)
        {
            GenValue(stop);
            string t = NewTemp();
            Emit(K.Sto, t);
            stop = new Name(t, f.Line, true);
        }
        int exit = NewLabel(), cont = NewLabel();
        Emit(K.Struct, "DO");
        CmpTest(new Cmp(f.Step > 0 ? "<" : ">", new Name(f.Var, f.Line), stop, f.Line), true);
        Emit(K.Struct, "WHILE");
        loops.Push((exit, cont));
        GenBlock(f.Body);
        loops.Pop();
        curLine = f.Line;
        Emit(K.LblL, n: cont);
        Emit(K.Num, FmtNum(f.Step, null));
        Emit(K.StoOp, Var(f.Var), 0);
        Emit(K.Struct, "ENDDO");
        // fin normale : la variable garde la dernière valeur parcourue
        Emit(K.Num, FmtNum(f.Step, null));
        Emit(K.StoOp, Var(f.Var), 1);
        GenBlock(f.Else);
        Emit(K.LblL, n: exit);
    }

    /// <summary>break qui sort de cette boucle (pas d'une boucle imbriquée).</summary>
    static bool HasBreak(List<Stmt> body) => body.Any(s => s switch
    {
        Break => true,
        If i => HasBreak(i.Body) || HasBreak(i.Else),
        _ => false
    });
}
