namespace Py42.Core;

// Nœuds internes : accès aux matrices déjà converti en indices HP (à partir de 1).
/// <summary>Élément M[I, J] (I, J à partir de 1).</summary>
sealed record MatElem(Name M, Expr I, Expr J, int Line) : Expr(Line);
/// <summary>Sous-matrice de R lignes × C colonnes à partir de (I, J) — GETM / PUTM.</summary>
sealed record MatSub(Name M, Expr I, Expr J, Expr R, Expr C, int Line) : Expr(Line);

/// <summary>
/// Extensions du compilateur : découpage des expressions trop profondes pour la pile,
/// matrices (INDEX, STOIJ, RCLEL, STOEL, GETM, PUTM...) et nombres complexes (COMPLEX).
/// </summary>
public sealed partial class Compiler
{
    // ======================================================================
    //  Analyse des types : quelles variables sont des matrices
    // ======================================================================
    readonly HashSet<string> matrixNames = new();
    readonly HashSet<string> matrixFuncs = new();   // fonctions qui renvoient une matrice
    bool usesComplex, usesCmath;

    static readonly HashSet<string> MatMakers = new() { "zeros", "matrix", "empty", "array", "asarray", "mat", "inv", "transpose", "solve" };
    static readonly HashSet<string> HpMatMakers = new(StringComparer.OrdinalIgnoreCase) { "NEWMAT", "INVRT", "TRANS", "RSUM", "UVEC", "CROSS" };
    static readonly HashSet<string> CpxFuncs = new(StringComparer.OrdinalIgnoreCase) { "complex", "conjugate", "conj", "phase", "rect", "real", "imag" };

    static IEnumerable<Stmt> AllStmts(List<Stmt> body)
    {
        foreach (var s in body)
        {
            yield return s;
            var subs = s switch
            {
                If i => i.Body.Concat(i.Else).ToList(),
                While w => w.Body.Concat(w.Else).ToList(),
                For f => f.Body.Concat(f.Else).ToList(),
                ForEach f => f.Body.Concat(f.Else).ToList(),
                _ => null
            };
            if (subs != null) foreach (var x in AllStmts(subs)) yield return x;
        }
    }

    static IEnumerable<Expr> SubExprs(Expr e)
    {
        yield return e;
        IEnumerable<Expr> kids = e switch
        {
            Unary u => new[] { u.A },
            Not n => new[] { n.A },
            Bin b => new[] { b.A, b.B },
            Cmp c => new[] { c.A, c.B },
            BoolOp b => new[] { b.A, b.B },
            Call c => c.Args,
            ListLit l => l.Items,
            TupleLit t => t.Items,
            Index ix => ix.Parts.Prepend(ix.Target),
            Slice s => new[] { s.Start, s.Stop }.Where(x => x != null)!,
            Attr a => new[] { a.Target },
            _ => Array.Empty<Expr>()
        };
        foreach (var k in kids) foreach (var x in SubExprs(k)) yield return x;
    }

    static IEnumerable<Expr> StmtExprs(Stmt s) => s switch
    {
        Assign a => a.Values,
        AugAssign a => new[] { a.Value },
        ExprStmt e => new[] { e.E },
        If i => new[] { i.Cond },
        While w => new[] { w.Cond },
        For f => new[] { f.Start, f.Stop },
        ForEach f => new[] { f.Seq },
        Return r => r.Values,
        Assert a => new[] { a.Cond },
        IndexAssign ia => new Expr[] { ia.Target, ia.Value },
        _ => Array.Empty<Expr>()
    };

    bool IsMat(Expr e) => e switch
    {
        ListLit => true,
        MatSub => true,
        Name n => matrixNames.Contains(n.Id),
        Call c when IsUser(c.Func) => matrixFuncs.Contains(c.Func),
        Call c when c.Func.Equals("COMPLEX", StringComparison.OrdinalIgnoreCase) && c.Args.Count == 2 => IsMat(c.Args[0]) || IsMat(c.Args[1]),
        Call c when HpMatMakers.Contains(c.Func) => true,
        Call c => MatMakers.Contains(c.Func),
        Attr { Name: "T" } => true,
        Index ix => ix.Parts.Any(p => p is Slice),
        Bin { Op: "@" } => true,
        Bin b when b.Op is "+" or "-" or "*" or "/" => IsMat(b.A) || IsMat(b.B),
        Unary u => IsMat(u.A),
        _ => false
    };

    /// <summary>Repère les matrices (point fixe) et l'usage des complexes.</summary>
    void InferTypes(List<Stmt> main)
    {
        var bodies = new List<(string? Fn, List<Stmt> Body)> { (null, main) };
        bodies.AddRange(funcs.Values.Select(d => ((string?)d.Name, d.Body)));

        foreach (var (_, body) in bodies)
            foreach (var s in AllStmts(body))
            {
                if (s is Import im && im.Module.Contains("cmath")) { usesCmath = true; usesComplex = true; }
                foreach (var e in StmtExprs(s))
                    foreach (var x in SubExprs(e))
                    {
                        switch (x)
                        {
                            case Imag: usesComplex = true; break;
                            case Attr { Name: "real" or "imag" }: usesComplex = true; break;
                            case Call c when !IsUser(c.Func) && CpxFuncs.Contains(c.Func): usesComplex = true; break;
                        }
                        // utilisations qui imposent une matrice
                        if (x is Index { Target: Name t }) matrixNames.Add(t.Id);
                        if (x is Call { Func: "len" or "rows" or "cols" or "det" or "inv" or "transpose" or "norm" or "dot" or "cross" or "append" or "pop", Args.Count: > 0 } mc
                            && !IsUser(mc.Func) && mc.Args[0] is Name a0) matrixNames.Add(a0.Id);
                    }
                if (s is ForEach { Seq: Name fs }) matrixNames.Add(fs.Id);
            }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (fn, body) in bodies)
                foreach (var s in AllStmts(body))
                {
                    if (s is Assign a)
                    {
                        if (a.Targets.Count == a.Values.Count)
                        {
                            for (int i = 0; i < a.Targets.Count; i++)
                                if (IsMat(a.Values[i]) && matrixNames.Add(a.Targets[i])) changed = true;
                        }
                        // re, im = COMPLEX(matrice complexe) : deux matrices
                        else if (a.Values is [Call { Args.Count: 1 } cc] && cc.Func.Equals("COMPLEX", StringComparison.OrdinalIgnoreCase) && IsMat(cc.Args[0]))
                            foreach (var t in a.Targets) if (matrixNames.Add(t)) changed = true;
                    }
                    if (s is Return r && fn != null && r.Values.Count == 1 && IsMat(r.Values[0]) && matrixFuncs.Add(fn)) changed = true;
                }
        }
    }

    // ======================================================================
    //  Découpage des expressions trop profondes pour la pile X Y Z T
    // ======================================================================
    int spillN;
    readonly List<string> spillNames = new();

    /// <summary>Variable temporaire réutilisée d'une instruction à l'autre (S1, S2...).</summary>
    string SpillTemp()
    {
        if (spillN < spillNames.Count) return spillNames[spillN++];
        string t;
        int k = spillNames.Count + 1;
        do { t = "S" + k++; } while (sourceNames.Contains(t) || varBack.ContainsKey(t));
        varBack[t] = t;
        hpVars.Add(t);
        spillNames.Add(t);
        spillN++;
        if (spillNames.Count == 1)
            warnings.Add($"Ligne {curLine} : expression trop profonde pour la pile à 4 niveaux, découpée automatiquement avec des variables S1, S2...");
        return t;
    }

    /// <summary>Calcule e tout de suite (pile libre) et le range dans une variable temporaire.</summary>
    Name Spill(Expr e)
    {
        var f = Fit(e, 4);
        CheckDepth(f, 0);
        Gen(f);
        string t = SpillTemp();
        Emit(K.Sto, t);
        return new Name(t, e.Line, true);
    }

    /// <summary>
    /// Réécrit e pour qu'il tienne dans « budget » niveaux de pile, en calculant à l'avance
    /// les sous-expressions trop profondes. À appeler quand la pile est libre (avant tout Gen).
    /// </summary>
    Expr Fit(Expr e, int budget)
    {
        if (Need(e) <= budget) return e;
        if (budget <= 1) return Spill(e);
        switch (e)
        {
            case Unary u: return u with { A = Fit(u.A, budget) };
            case Bin b when SqOpt(b) != null || RclOpt(b): return b with { A = Fit(b.A, budget) };
            case Bin b: { var (a, bb) = FitPair(b.A, b.B, budget); return b with { A = a, B = bb }; }
            case Attr a: return a with { Target = Fit(a.Target, budget) };
            case MatElem me: { var (i, j) = FitPair(me.I, me.J, budget); return me with { I = i, J = j }; }
            case MatSub ms:
            {
                var (i, j) = FitPair(ms.I, ms.J, budget);
                var (r, c) = FitPair(ms.R, ms.C, budget);
                return ms with { I = i, J = j, R = r, C = c };
            }
            case Call c when IsUser(c.Func) || (Hp42.Commands.ContainsKey(c.Func) && !Unary1.ContainsKey(c.Func) && !Binary2.Contains(c.Func)):
            {
                if (c.Args.Count > budget) return Spill(e);
                var args = c.Args.Select((x, i) => Fit(x, budget - i)).ToList();
                return c with { Args = args };
            }
            case Call c when c.Args.Count == 1: return c with { Args = new() { Fit(c.Args[0], budget) } };
            case Call c when c.Args.Count == 2:
            {
                var (a, b) = FitPair(c.Args[0], c.Args[1], budget);
                return c with { Args = new() { a, b } };
            }
            default: return Spill(e);
        }
    }

    (Expr, Expr) FitPair(Expr a, Expr b, int budget)
    {
        if (Need(a) >= Need(b)) return (Fit(a, budget), Fit(b, budget - 1));
        return (Fit(a, budget - 1), Fit(b, budget));
    }

    // ======================================================================
    //  Matrices : préparation (indices Python → indices HP)
    // ======================================================================
    static Expr OneBased(Expr i)
    {
        var v = Parser.ConstValue(i);
        if (v != null)
        {
            if (v < 0) throw new CompileError("Les indices négatifs ne sont pas pris en charge", i.Line);
            if (v != Math.Floor(v.Value)) throw new CompileError("Un indice doit être entier", i.Line);
            return new Num(v.Value + 1, null, i.Line);
        }
        return new Bin("+", i, new Num(1, "1", i.Line), i.Line);
    }

    /// <summary>Indice Python → indice HP ; un indice négatif constant compte depuis la fin : b[-1] → DIM? de b.</summary>
    static Expr OneBasedFrom(Expr i, Name m, string dim)
    {
        var v = Parser.ConstValue(i);
        if (v is < 0 && v == Math.Floor(v.Value))
        {
            Expr size = new Call(dim, new() { m }, i.Line);
            return v == -1 ? size : new Bin("-", size, new Num(-v.Value - 1, null, i.Line), i.Line);
        }
        return OneBased(i);
    }

    /// <summary>Retire le décalage commun à toutes les lignes (code collé depuis un bloc indenté).</summary>
    static string Dedent(string src)
    {
        var lines = src.Replace("\r\n", "\n").Split('\n');
        static int Ind(string l) { int n = 0; while (n < l.Length && (l[n] == ' ' || l[n] == '\t')) n++; return n; }
        var code = lines.Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')).ToList();
        if (code.Count == 0) return src;
        int min = code.Min(Ind);
        if (min == 0) return src;
        return string.Join("\n", lines.Select(l =>
            l.Trim().Length == 0 ? "" : Ind(l) >= min ? l[min..] : l.TrimStart()));
    }

    /// <summary>b.append(v) : DIM agrandit la matrice d'une ligne, puis b[-1] = v.</summary>
    void GenAppend(Call c)
    {
        if (c.Args.Count != 2) throw new CompileError("append() prend une seule valeur : liste.append(x)", c.Line);
        if (c.Args[0] is not Name b || IsConstName(b)) throw new CompileError("append() s'applique à une variable liste : b.append(x)", c.Line);
        if (c.Args[1] is ListLit) throw new CompileError("append() ajoute un nombre ou un texte, pas une liste", c.Line);
        GenValue(new Bin("+", new Call("rows", new() { b }, c.Line), new Num(1, "1", c.Line), c.Line));
        Emit(K.Num, "1");
        Emit(K.Cmd, $"DIM \"{Var(b.Id)}\"");
        maxStack = Math.Max(maxStack, 2);
        GenIndexAssign(new IndexAssign(new Index(b, new() { new Num(-1, "-1", c.Line) }, c.Line), c.Args[1], c.Line));
    }

    /// <summary>
    /// j, m, a = dmy(d) : découpe une date JJ.MMAAAA (format D.MY de la DM42).
    /// STO ST Y garde une copie de X dans Y, IP en extrait la partie entière, - laisse la partie fractionnaire.
    /// </summary>
    void GenDmy(Call c, Assign a)
    {
        Arity(c, 1);
        if (a.Targets.Count != 3)
            throw new CompileError($"dmy() renvoie 3 valeurs (jour, mois, année), {a.Targets.Count} variable(s) à remplir", a.Line);
        GenValue(c.Args[0]);
        Emit(K.Stk, "STO ST Y"); Emit(K.Fn, "IP"); Emit(K.Sto, Var(a.Targets[0])); Emit(K.Op, "-");
        Emit(K.Num, "100"); Emit(K.Op, "×");
        Emit(K.Stk, "STO ST Y"); Emit(K.Fn, "IP"); Emit(K.Sto, Var(a.Targets[1])); Emit(K.Op, "-");
        // + 0.5 IP : arrondit l'année (inutile en BCD sur la calculatrice, utile en simulation)
        Emit(K.Num, "10000"); Emit(K.Op, "×"); Emit(K.Num, "0.5"); Emit(K.Op, "+"); Emit(K.Fn, "IP");
        Emit(K.Sto, Var(a.Targets[2]));
        maxStack = Math.Max(maxStack, 2);
    }

    /// <summary>b.pop() : lit b[-1] (si x = b.pop()), puis DIM réduit la matrice d'une ligne.</summary>
    void GenPop(Call c, string? target)
    {
        if (c.Args.Count != 1) throw new CompileError("Seul pop() sans argument (dernier élément) est pris en charge", c.Line);
        if (c.Args[0] is not Name b || IsConstName(b)) throw new CompileError("pop() s'applique à une variable liste : b.pop()", c.Line);
        if (target != null)
        {
            GenValue(new Index(b, new() { new Num(-1, "-1", c.Line) }, c.Line));
            Emit(K.Sto, target);
        }
        GenValue(new Bin("-", new Call("rows", new() { b }, c.Line), new Num(1, "1", c.Line), c.Line));
        Emit(K.Num, "1");
        Emit(K.Cmd, $"DIM \"{Var(b.Id)}\"");
        maxStack = Math.Max(maxStack, 2);
    }

    static Expr Minus(Expr a, Expr b)
    {
        var va = Parser.ConstValue(a);
        var vb = Parser.ConstValue(b);
        if (va != null && vb != null) return new Num(va.Value - vb.Value, null, a.Line);
        if (vb == 0) return a;
        return new Bin("-", a, b, a.Line);
    }

    /// <summary>Cible d'un indice : une variable matrice (sinon calculée dans une variable temporaire).</summary>
    Name MatTarget(Expr target)
    {
        var t = Prepare(target, false);
        if (t is Name n && !IsConstName(n)) return n;
        var f = Fit(t, 4);
        CheckDepth(f, 0);
        Gen(f);
        string tmp = NewTemp();
        Emit(K.Sto, tmp);
        matrixNames.Add(tmp);
        return new Name(tmp, target.Line, true);
    }

    bool Touches(Expr e) => SubExprs(e).Any(x => x is Index or MatElem or MatSub || (x is Call c && (IsUser(c.Func) || MatMakers.Contains(c.Func))));

    /// <summary>m[i, j] → MatElem ; m[a:b, c:d] → MatSub.</summary>
    Expr PrepareIndex(Index ix)
    {
        var m = MatTarget(ix.Target);
        var parts = ix.Parts.Select(p => p is Slice s
            ? new Slice(s.Start == null ? null : Prepare(s.Start, false), s.Stop == null ? null : Prepare(s.Stop, false), s.Line)
            : Prepare(p, false)).ToList();

        if (parts.All(p => p is not Slice))
        {
            var i = OneBasedFrom(parts[0], m, "rows");
            var j = parts.Count > 1 ? OneBasedFrom(parts[1], m, "cols") : new Num(1, "1", ix.Line);
            return new MatElem(m, i, j, ix.Line);
        }

        var starts = new Expr[2];
        var lens = new Expr[2];
        for (int d = 0; d < 2; d++)
        {
            var part = d < parts.Count ? parts[d] : new Slice(null, null, ix.Line);
            if (part is Slice s)
            {
                var start = s.Start ?? new Num(0, "0", ix.Line);
                var stop = s.Stop ?? new Call(d == 0 ? "rows" : "cols", new() { m }, ix.Line);
                starts[d] = OneBased(start);
                lens[d] = Minus(stop, start);
            }
            else
            {
                starts[d] = OneBased(part);
                lens[d] = new Num(1, "1", ix.Line);
            }
        }
        // Les dimensions sont calculées après INDEX : on y évite tout autre accès matrice
        var r = Touches(lens[0]) ? Spill(lens[0]) : lens[0];
        var c = Touches(lens[1]) ? Spill(lens[1]) : lens[1];
        return new MatSub(m, starts[0], starts[1], r, c, ix.Line);
    }

    /// <summary>Liste littérale dans une expression : construite dans une matrice temporaire.</summary>
    Name HoistList(ListLit l)
    {
        string tmp = NewTemp();
        GenListLit(tmp, l);
        matrixNames.Add(tmp);
        return new Name(tmp, l.Line, true);
    }

    // ======================================================================
    //  Matrices et complexes : profondeur de pile et génération
    // ======================================================================
    int NeedExt(Expr e) => e switch
    {
        Imag => 2,
        MatElem me => Pair(Need(me.I), Need(me.J)),
        MatSub ms => Math.Max(Pair(Need(ms.I), Need(ms.J)), Pair(Need(ms.R), Need(ms.C))),
        Attr { Name: "real" or "imag" } a => Math.Max(Need(a.Target), 2),
        Attr { Name: "T" } a => Need(a.Target),
        Attr a => throw new CompileError($"Attribut « .{a.Name} » non pris en charge (disponibles : .real, .imag, .T)", a.Line),
        ListLit or TupleLit => throw new CompileError("Liste ou tuple inattendu ici", e.Line),
        Slice => throw new CompileError("Une tranche a:b ne s'utilise qu'entre crochets", e.Line),
        Index => throw new CompileError("Indice non préparé (erreur interne)", e.Line),
        _ => throw new CompileError("Expression non prise en charge", e.Line)
    };

    /// <summary>Fonctions matrices / complexes. Renvoie -1 si le nom n'en fait pas partie.</summary>
    int NeedMatCall(Call c)
    {
        switch (c.Func)
        {
            case "zeros": case "matrix": case "empty":
                if (c.Args.Count == 1) return Math.Max(Need(c.Args[0]), 2);
                Arity(c, 2);
                return Pair(Need(c.Args[0]), Need(c.Args[1]));
            case "det": case "inv": case "transpose": case "norm":
                Arity(c, 1); return Need(c.Args[0]);
            case "rows": case "cols": case "len": case "real": case "imag": case "conjugate": case "conj": case "phase":
                Arity(c, 1); return Math.Max(Need(c.Args[0]), 2);
            case "dot": case "cross": case "rect":
                Arity(c, 2); return Pair(Need(c.Args[0]), Need(c.Args[1]));
            case "complex":
                if (c.Args.Count == 1) return Math.Max(Need(c.Args[0]), 2);
                Arity(c, 2);
                return Pair(Need(c.Args[0]), Need(c.Args[1]));
            case "ones": case "eye": case "identity":
                throw new CompileError($"{c.Func}() n'a pas d'équivalent direct sur HP42 : créez la matrice avec zeros(l, c) puis remplissez-la dans une boucle", c.Line);
        }
        return -1;
    }

    void GenIJ(Expr i, Expr j)
    {
        if (GenPair(i, j)) Emit(K.Stk, "X<>Y");
    }

    /// <summary>INDEX "M" puis place le pointeur sur (I, J) sans laisser I et J sur la pile.</summary>
    void PointTo(Name m, Expr i, Expr j)
    {
        GenIJ(i, j);
        Emit(K.Cmd, $"INDEX \"{MatName(m)}\"");
        Emit(K.Cmd, "STOIJ");
        Emit(K.Stk, "R↓");
        Emit(K.Stk, "R↓");
    }

    string MatName(Name m) => m.Raw ? m.Id : Var(m.Id);

    bool GenExt(Expr e)
    {
        switch (e)
        {
            case Str st:
                // texte (6 caractères max) : ALPHA puis ASTO ST X
                Need(st);
                Emit(K.Num, "0");
                Emit(K.AStr, st.Value);
                Emit(K.Cmd, "ASTO ST X");
                return true;
            case Imag im:
                Emit(K.Num, "0");
                Emit(K.Num, FmtNum(im.Value, im.Raw));
                Emit(K.Cmd, "COMPLEX");
                return true;
            case MatElem me:
                PointTo(me.M, me.I, me.J);
                Emit(K.Cmd, "RCLEL");
                return true;
            case MatSub ms:
                PointTo(ms.M, ms.I, ms.J);
                if (GenPair(ms.R, ms.C)) Emit(K.Stk, "X<>Y");
                Emit(K.Cmd, "GETM");
                return true;
            case Attr { Name: "real" } a:
                Gen(a.Target); Emit(K.Cmd, "COMPLEX"); Emit(K.Stk, "R↓");
                return true;
            case Attr { Name: "imag" } a:
                Gen(a.Target); Emit(K.Cmd, "COMPLEX"); Emit(K.Stk, "STO ST Y"); Emit(K.Stk, "R↓");
                return true;
            case Attr { Name: "T" } a:
                Gen(a.Target); Emit(K.Cmd, "TRANS");
                return true;
        }
        return false;
    }

    bool GenMatCall(Call c)
    {
        if (NeedMatCall(c) < 0) return false;
        switch (c.Func)
        {
            case "zeros": case "matrix": case "empty":
                if (c.Args.Count == 1) { Gen(c.Args[0]); Emit(K.Num, "1"); }
                else GenIJ(c.Args[0], c.Args[1]);
                Emit(K.Cmd, "NEWMAT");
                break;
            case "det": Gen(c.Args[0]); Emit(K.Cmd, "DET"); break;
            case "inv": Gen(c.Args[0]); Emit(K.Cmd, "INVRT"); break;
            case "transpose": Gen(c.Args[0]); Emit(K.Cmd, "TRANS"); break;
            case "norm": Gen(c.Args[0]); Emit(K.Cmd, "FNRM"); break;
            case "rows": case "len":
                Gen(c.Args[0]); Emit(K.Cmd, "DIM?"); Emit(K.Stk, "R↓");
                break;
            case "cols":
                Gen(c.Args[0]); Emit(K.Cmd, "DIM?"); Emit(K.Stk, "STO ST Y"); Emit(K.Stk, "R↓");
                break;
            case "real":
                Gen(c.Args[0]); Emit(K.Cmd, "COMPLEX"); Emit(K.Stk, "R↓");
                break;
            case "imag":
                Gen(c.Args[0]); Emit(K.Cmd, "COMPLEX"); Emit(K.Stk, "STO ST Y"); Emit(K.Stk, "R↓");
                break;
            case "conjugate": case "conj":
                Gen(c.Args[0]); Emit(K.Cmd, "COMPLEX"); Emit(K.Fn, "+/-"); Emit(K.Cmd, "COMPLEX");
                break;
            case "phase":
                // COMPLEX : Y=re X=im → X<>Y → →POL : X=module Y=angle → R↓
                usesTrig = true;
                Gen(c.Args[0]); Emit(K.Cmd, "COMPLEX"); Emit(K.Stk, "X<>Y"); Emit(K.Stk, "→POL"); Emit(K.Stk, "R↓");
                break;
            case "dot": GenIJ(c.Args[0], c.Args[1]); Emit(K.Cmd, "DOT"); break;
            case "cross": GenIJ(c.Args[0], c.Args[1]); Emit(K.Cmd, "CROSS"); break;
            case "rect":
                // rect(r, phi) : Y=phi X=r → →REC : Y=im X=re → X<>Y → COMPLEX
                usesTrig = true;
                GenIJ(c.Args[1], c.Args[0]);
                Emit(K.Stk, "→REC"); Emit(K.Stk, "X<>Y"); Emit(K.Cmd, "COMPLEX");
                break;
            case "complex":
                if (c.Args.Count == 1) { Gen(c.Args[0]); Emit(K.Num, "0"); }
                else GenIJ(c.Args[0], c.Args[1]);
                Emit(K.Cmd, "COMPLEX");
                break;
        }
        return true;
    }

    /// <summary>3 + 4j avec des constantes → « 3 4 COMPLEX ».</summary>
    static (double Re, double Im, string ReRaw, string ImRaw)? CpxConst(Bin b)
    {
        if (b.Op is not ("+" or "-")) return null;
        double sign = b.Op == "-" ? -1 : 1;
        if (Parser.ConstValue(b.A) is double re && b.B is Imag im)
            return (re, sign * im.Value, FmtNum(re, null), FmtNum(sign * im.Value, null));
        if (b.A is Imag im2 && Parser.ConstValue(b.B) is double re2)
            return (sign * re2, im2.Value, FmtNum(sign * re2, null), FmtNum(im2.Value, null));
        return null;
    }

    // ======================================================================
    //  Commandes HP42S
    // ======================================================================

    /// <summary>SIN, COS... sont des fonctions (K.Fn), MOD un opérateur, PI une constante, le reste une commande.</summary>
    void EmitHp(string rpn)
    {
        if (Hp42.FnOps.Contains(rpn)) Emit(K.Fn, rpn);
        else if (rpn == "MOD") Emit(K.Op, "MOD");
        else if (rpn == "PI") Emit(K.Const, "PI");
        else if (rpn == "STOP") Emit(K.Stop);
        else if (rpn is "→POL" or "→REC") Emit(K.Stk, rpn);
        else Emit(K.Cmd, rpn);
    }

    static bool IsKeyCommand(string f) =>
        f.Equals("KEYG", StringComparison.OrdinalIgnoreCase) || f.Equals("KEYX", StringComparison.OrdinalIgnoreCase)
        || f.Equals("ASSIGN", StringComparison.OrdinalIgnoreCase);

    bool IsHpStatement(Call c) => !IsUser(c.Func) && (
        (c.Func != "input" && Hp42.VarCommands.ContainsKey(c.Func))
        || Hp42.LabelCommands.ContainsKey(c.Func)
        || IsKeyCommand(c.Func));

    /// <summary>Nom passé en argument : variable (x) ou chaîne ("x").</summary>
    static string NameArg(Call c, Expr a, string what) => a switch
    {
        Name n when !n.Raw => n.Id,
        Str s when s.Value.Length > 0 => s.Value,
        _ => throw new CompileError($"{c.Func}() attend un nom de {what} : {c.Func}(x) ou {c.Func}(\"x\")", c.Line)
    };

    int ConstInt(Call c, Expr a, int min, int max, string what)
    {
        var v = Parser.ConstValue(a);
        if (v == null || v != Math.Floor(v.Value) || v < min || v > max)
            throw new CompileError($"{c.Func}() : {what} doit être un entier constant de {min} à {max}", c.Line);
        return (int)v.Value;
    }

    /// <summary>VIEW(x), INPUT(x), CLV(x), PGMSLV("f"), KEYG(1, "f"), ASSIGN("f", 18)...</summary>
    void GenHpStatement(Call c)
    {
        if (Hp42.VarCommands.TryGetValue(c.Func, out var vc))
        {
            Arity(c, 1);
            string v = Var(NameArg(c, c.Args[0], "variable"));
            if (vc.Rpn == "VIEW") Emit(K.ViewVar, v);
            else if (vc.Rpn == "INPUT") Emit(K.Input, v);
            else if (vc.Rpn == "ARCL") Emit(K.ArclVar, v);
            else Emit(K.Cmd, $"{vc.Rpn} \"{v}\"");
            return;
        }
        if (Hp42.LabelCommands.TryGetValue(c.Func, out var lc))
        {
            Arity(c, 1);
            Emit(K.Cmd, $"{lc.Rpn} \"{Label(NameArg(c, c.Args[0], "fonction"))}\"");
            return;
        }
        Arity(c, 2);
        if (c.Func.Equals("ASSIGN", StringComparison.OrdinalIgnoreCase))
        {
            string f = Label(NameArg(c, c.Args[0], "fonction"));
            int key = ConstInt(c, c.Args[1], 1, 18, "la touche");
            Emit(K.Cmd, $"ASSIGN \"{f}\" TO {key:00}");
            return;
        }
        int k = ConstInt(c, c.Args[0], 1, 9, "la touche");
        string lbl = Label(NameArg(c, c.Args[1], "fonction"));
        bool xeq = c.Func.Equals("KEYX", StringComparison.OrdinalIgnoreCase);
        Emit(K.Cmd, $"KEY {k} {(xeq ? "XEQ" : "GTO")} \"{lbl}\"");
    }

    // ======================================================================
    //  Instructions : listes littérales, affectation d'éléments, for x in v
    // ======================================================================
    static bool IsSimple(Expr e) => e switch
    {
        Num or Imag or Const or Name or Str => true,
        Unary u => IsSimple(u.A),
        Bin b => IsSimple(b.A) && IsSimple(b.B),
        _ => false
    };

    /// <summary>m = [[1, 2], [3, 4]] → NEWMAT, INDEX, puis STOEL / J+ pour chaque élément.</summary>
    void GenListLit(string hpName, ListLit l)
    {
        List<List<Expr>> rows;
        if (l.Items.All(x => x is ListLit))
        {
            rows = l.Items.Cast<ListLit>().Select(r => r.Items).ToList();
            if (rows.Any(r => r.Any(x => x is ListLit)))
                throw new CompileError("Trois dimensions ou plus : la HP42 ne gère que des matrices à 2 dimensions", l.Line);
            if (rows.Select(r => r.Count).Distinct().Count() != 1)
                throw new CompileError("Toutes les lignes de la matrice doivent avoir le même nombre d'éléments", l.Line);
        }
        else if (l.Items.Any(x => x is ListLit))
            throw new CompileError("Mélange de nombres et de listes : écrivez [[1, 2], [3, 4]] ou [1, 2, 3]", l.Line);
        else
            rows = l.Items.Select(x => new List<Expr> { x }).ToList();   // vecteur colonne n×1

        int nr = rows.Count, nc = rows[0].Count;
        Emit(K.Num, nr.ToString());
        Emit(K.Num, nc.ToString());
        Emit(K.Cmd, "NEWMAT");
        Emit(K.Sto, hpName);
        Emit(K.Cmd, $"INDEX \"{hpName}\"");
        int total = nr * nc, k = 0;
        for (int i = 0; i < nr; i++)
            for (int j = 0; j < nc; j++, k++)
            {
                var e = rows[i][j];
                if (IsSimple(e))
                {
                    var f = Fit(Prepare(e, true), 4);
                    CheckDepth(f, 0);
                    Gen(f);
                }
                else
                {
                    // élément calculé (appel, autre matrice...) : on repositionne le pointeur après coup
                    var f = Fit(Prepare(e, true), 4);
                    CheckDepth(f, 0);
                    Gen(f);
                    Emit(K.Num, (i + 1).ToString());
                    Emit(K.Num, (j + 1).ToString());
                    Emit(K.Cmd, $"INDEX \"{hpName}\"");
                    Emit(K.Cmd, "STOIJ");
                    Emit(K.Stk, "R↓");
                    Emit(K.Stk, "R↓");
                }
                Emit(K.Cmd, "STOEL");
                if (k < total - 1) Emit(K.Cmd, "J+");
            }
        maxStack = Math.Max(maxStack, 3);
    }

    void GenIndexAssign(IndexAssign ia)
    {
        if (ia.Target.Target is Name tn && fn != null && fn.Params.Contains(tn.Id) && warnedParam.Add(tn.Id))
            warnings.Add($"Ligne {ia.Line} : « {tn.Id} » est un paramètre. Sur HP42 la fonction modifie une copie : renvoyez la matrice (return {tn.Id}).");
        var target = PrepareIndex(ia.Target);
        var v = ia.Value is ListLit vl ? HoistList(vl) : Prepare(ia.Value, true);
        v = Fit(v, 4);
        switch (target)
        {
            case MatElem me:
            {
                var (i, j) = FitPair(me.I, me.J, 3);
                CheckDepth(v, 0);
                maxStack = Math.Max(maxStack, 1 + Pair(Need(i), Need(j)));
                Gen(v);
                PointTo(me.M, i, j);
                Emit(K.Cmd, "STOEL");
                break;
            }
            case MatSub ms:
            {
                var (i, j) = FitPair(ms.I, ms.J, 3);
                CheckDepth(v, 0);
                maxStack = Math.Max(maxStack, 1 + Pair(Need(i), Need(j)));
                Gen(v);
                PointTo(ms.M, i, j);
                Emit(K.Cmd, "PUTM");
                break;
            }
        }
    }
    readonly HashSet<string> warnedParam = new();

    /// <summary>for x in v : boucle sur les éléments (ligne par ligne) avec un compteur caché.</summary>
    void GenForEach(ForEach fe)
    {
        Name seq = fe.Seq is ListLit l ? HoistList(l) : MatTarget(fe.Seq);
        string k = NewTemp(), n = NewTemp(), c = NewTemp();
        // c = colonnes, n = lignes × colonnes
        Emit(K.Rcl, MatName(seq));
        Emit(K.Cmd, "DIM?");
        Emit(K.Sto, c);
        Emit(K.Op, "×");
        Emit(K.Sto, n);
        maxStack = Math.Max(maxStack, 2);
        var kN = new Name(k, fe.Line, true);
        var cN = new Name(c, fe.Line, true);
        var elem = new MatElem(seq,
            new Bin("+", new Call("int", new() { new Bin("/", kN, cN, fe.Line) }, fe.Line), new Num(1, "1", fe.Line), fe.Line),
            new Bin("+", new Bin("%", kN, cN, fe.Line), new Num(1, "1", fe.Line), fe.Line),
            fe.Line);
        var body = new List<Stmt> { new Assign(new() { fe.Var }, new() { elem }, fe.Line) };
        body.AddRange(fe.Body);
        GenFor(new For(k, new Num(0, "0", fe.Line), new Name(n, fe.Line, true), 1, body, fe.Else, fe.Line));
    }
}
