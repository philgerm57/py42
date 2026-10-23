using System.Globalization;

namespace Py42.Core;

/// <summary>Analyseur descendant récursif pour le sous-ensemble de Python accepté.</summary>
public sealed class Parser
{
    readonly List<Tok> toks;
    int p;
    /// <summary>Modules importés et leurs alias (import numpy as np).</summary>
    readonly HashSet<string> modules = new() { "math", "cmath", "numpy", "np" };
    static readonly HashSet<string> KnownModules = new() { "math", "cmath", "numpy" };

    Parser(List<Tok> toks) { this.toks = toks; }

    public static List<Stmt> Parse(string src) => new Parser(Lexer.Tokenize(src)).Program();

    Tok Cur => toks[p];
    Tok Next() => toks[p++];
    bool Is(T k, string? v = null) => Cur.Kind == k && (v == null || Cur.V == v);
    bool IsOp(string v) => Is(T.Op, v);
    bool IsKw(string v) => Is(T.Kw, v);

    Tok Expect(T k, string? v, string message)
    {
        if (!Is(k, v)) throw new CompileError(message + Found(), Cur.Line);
        return Next();
    }

    string Found() => Cur.Kind switch
    {
        T.NL => " (fin de ligne trouvée)",
        T.EOF => " (fin du fichier trouvée)",
        T.Indent => " (indentation trouvée)",
        T.Dedent => " (fin de bloc trouvée)",
        _ => $" (« {Cur.V} » trouvé)"
    };

    List<Stmt> Program()
    {
        var body = new List<Stmt>();
        while (!Is(T.EOF))
        {
            if (Is(T.NL)) { Next(); continue; }
            body.AddRange(Statement());
        }
        return body;
    }

    List<Stmt> Block()
    {
        Expect(T.Op, ":", "« : » attendu");
        if (Is(T.NL))
        {
            Next();
            Expect(T.Indent, null, "Bloc indenté attendu");
            var b = new List<Stmt>();
            while (!Is(T.Dedent) && !Is(T.EOF))
            {
                if (Is(T.NL)) { Next(); continue; }
                b.AddRange(Statement());
            }
            if (Is(T.Dedent)) Next();
            return b;
        }
        return SimpleStatements();
    }

    List<Stmt> Statement()
    {
        var t = Cur;
        if (t.Kind == T.Indent) throw new CompileError("Indentation inattendue", t.Line);
        if (t.Kind == T.Kw)
        {
            switch (t.V)
            {
                case "def": return new() { FuncDef() };
                case "if": return new() { IfStmt() };
                case "while":
                {
                    Next();
                    var cond = Expression();
                    var body = Block();
                    var orelse = new List<Stmt>();
                    if (IsKw("else")) { Next(); orelse = Block(); }
                    return new() { new While(cond, body, orelse, t.Line) };
                }
                case "for": return new() { ForStmt() };
                case "elif":
                case "else":
                    throw new CompileError($"« {t.V} » sans « if » correspondant", t.Line);
                case "class": case "try": case "except": case "finally": case "with": case "lambda":
                case "yield": case "del": case "nonlocal": case "raise":
                    throw new CompileError($"« {t.V} » n'est pas pris en charge", t.Line);
            }
        }
        return SimpleStatements();
    }

    List<Stmt> SimpleStatements()
    {
        var output = new List<Stmt>();
        while (true)
        {
            var s = Simple();
            if (s != null) output.Add(s);
            if (IsOp(";")) { Next(); if (Is(T.NL)) break; continue; }
            break;
        }
        if (!Is(T.EOF)) Expect(T.NL, null, "Fin d'instruction attendue");
        return output;
    }

    static readonly string[] AugOps = { "+=", "-=", "*=", "/=", "**=", "%=", "//=", "@=" };

    Stmt? Simple()
    {
        var t = Cur;
        int line = t.Line;
        if (t.Kind == T.Kw)
        {
            switch (t.V)
            {
                case "pass": Next(); return null;
                case "break": Next(); return new Break(line);
                case "continue": Next(); return new Continue(line);
                case "return":
                {
                    Next();
                    var values = new List<Expr>();
                    if (!Is(T.NL) && !Is(T.EOF) && !IsOp(";")) values = ExpressionList();
                    if (values is [TupleLit rt]) values = rt.Items;   // return (a, b) comme return a, b
                    if (values.Count > 4) throw new CompileError("return renvoie au plus 4 valeurs (pile X, Y, Z, T)", line);
                    return new Return(values, line);
                }
                case "assert":
                {
                    Next();
                    var cond = Expression();
                    string? msg = null;
                    if (IsOp(","))
                    {
                        Next();
                        if (!Is(T.Str)) throw new CompileError("Le message d'assert doit être une chaîne de caractères", line);
                        msg = Next().V;
                    }
                    return new Assert(cond, msg, line);
                }
                case "import":
                case "from":
                {
                    var words = new List<string>();
                    while (!Is(T.NL) && !Is(T.EOF) && !IsOp(";")) words.Add(Next().V);
                    var found = words.Where(KnownModules.Contains).ToList();
                    if (found.Count == 0)
                        throw new CompileError("Modules disponibles : math, cmath (complexes) et numpy (matrices)", line);
                    int asAt = words.IndexOf("as");
                    if (asAt >= 0 && asAt + 1 < words.Count) modules.Add(words[asAt + 1]);
                    return new Import(string.Join(",", found), line);
                }
                case "global":
                {
                    Next();
                    var names = new List<string> { Expect(T.Name, null, "Nom de variable attendu").V };
                    while (IsOp(",")) { Next(); names.Add(Expect(T.Name, null, "Nom de variable attendu").V); }
                    return new Global(names, line);
                }
            }
        }

        var first = ExpressionList();
        if (IsOp("="))
        {
            Next();
            var values = ExpressionList();
            // (a, b) = (1, 2) s'écrit comme a, b = 1, 2
            if (first is [TupleLit ft]) first = ft.Items;
            if (values is [TupleLit vt]) values = vt.Items;
            if (IsOp("=")) throw new CompileError("Les affectations en chaîne (a = b = 0) ne sont pas prises en charge", line);
            if (first.Any(e => e is Index))
            {
                if (first.Count != 1 || values.Count != 1)
                    throw new CompileError("Un élément de matrice s'affecte seul : m[i, j] = valeur", line);
                return new IndexAssign((Index)first[0], values[0], line);
            }
            var targets = new List<string>();
            foreach (var e in first)
            {
                if (e is not Name n) throw new CompileError("Seules des variables simples peuvent recevoir une valeur", line);
                targets.Add(n.Id);
            }
            bool multiReturn = values.Count == 1 && values[0] is Call && targets.Count > 1; // a, b = f(...)
            if (targets.Count != values.Count && !multiReturn)
                throw new CompileError($"{targets.Count} variable(s) mais {values.Count} valeur(s)", line);
            if (targets.Count > 4)
                throw new CompileError("Au plus 4 affectations simultanées (la pile a 4 niveaux)", line);
            return new Assign(targets, values, line);
        }
        if (Cur.Kind == T.Op && AugOps.Contains(Cur.V))
        {
            string op = Next().V[..^1];
            if (first.Count == 1 && first[0] is Index ix)
                return new IndexAssign(ix, new Bin(op, ix, Expression(), line), line);
            if (first.Count != 1 || first[0] is not Name n)
                throw new CompileError("Affectation combinée sur une seule variable uniquement", line);
            return new AugAssign(n.Id, op, Expression(), line);
        }
        if (first.Count != 1) throw new CompileError("Liste de valeurs inattendue", line);
        return new ExprStmt(first[0], line);
    }

    List<Expr> ExpressionList()
    {
        var list = new List<Expr> { Expression() };
        while (IsOp(",")) { Next(); list.Add(Expression()); }
        return list;
    }

    Def FuncDef()
    {
        int line = Next().Line;
        string name = Expect(T.Name, null, "Nom de fonction attendu").V;
        Expect(T.Op, "(", "« ( » attendu");
        var ps = new List<string>();
        if (!IsOp(")"))
        {
            ps.Add(Expect(T.Name, null, "Nom de paramètre attendu").V);
            while (IsOp(",")) { Next(); ps.Add(Expect(T.Name, null, "Nom de paramètre attendu").V); }
        }
        Expect(T.Op, ")", "« ) » attendu (les valeurs par défaut ne sont pas prises en charge)");
        if (ps.Count > 4) throw new CompileError("Une fonction prend au plus 4 paramètres (pile X, Y, Z, T)", line);
        if (ps.Distinct().Count() != ps.Count) throw new CompileError("Paramètre en double", line);
        return new Def(name, ps, Block(), line);
    }

    If IfStmt()
    {
        int line = Next().Line; // if ou elif
        var cond = Expression();
        var body = Block();
        var orelse = new List<Stmt>();
        if (IsKw("elif")) orelse.Add(IfStmt());
        else if (IsKw("else")) { Next(); orelse = Block(); }
        return new If(cond, body, orelse, line);
    }

    Stmt ForStmt()
    {
        int line = Next().Line;
        string v = Expect(T.Name, null, "Variable de boucle attendue").V;

        // for x, n in m : parcours des lignes d'une matrice (liste de couples), une variable par colonne
        if (IsOp(","))
        {
            var names = new List<string> { v };
            while (IsOp(",")) { Next(); names.Add(Expect(T.Name, null, "Variable de boucle attendue").V); }
            if (names.Count > 4) throw new CompileError("Au plus 4 variables (la pile a 4 niveaux)", line);
            Expect(T.Kw, "in", "« in » attendu");
            var m = Expression();
            if (m is not Name mn)
                throw new CompileError($"for {string.Join(", ", names)} in … : donnez d'abord un nom à la liste (m = [(…), (…)]) puis for {string.Join(", ", names)} in m:", line);
            var rowBody = Block();
            var els = new List<Stmt>();
            if (IsKw("else")) { Next(); els = Block(); }
            // for r in range(rows(m)): x, n = m[r, 0], m[r, 1] ; puis le corps
            string r = "r_" + line;
            var values = names.Select((_, k) => (Expr)new Index(mn, new List<Expr> { new Name(r, line), new Num(k, k.ToString(), line) }, line)).ToList();
            var loopBody = new List<Stmt> { new Assign(names, values, line) };
            loopBody.AddRange(rowBody);
            return new For(r, new Num(0, "0", line), new Call("rows", new List<Expr> { mn }, line), 1, loopBody, els, line);
        }

        Expect(T.Kw, "in", "« in » attendu");
        if (!(Cur.Kind == T.Name && Cur.V == "range" && p + 1 < toks.Count && toks[p + 1].Kind == T.Op && toks[p + 1].V == "("))
        {
            // for x in v : parcours des éléments d'un vecteur / d'une matrice
            var seq = Expression();
            if (seq is TupleLit st) seq = new ListLit(st.Items, st.Line);   // for x in (1, 2, 3)
            var fbody = Block();
            var felse = new List<Stmt>();
            if (IsKw("else")) { Next(); felse = Block(); }
            return new ForEach(v, seq, fbody, felse, line);
        }
        Next();
        Expect(T.Op, "(", "« ( » attendu");
        var args = new List<Expr> { Expression() };
        while (IsOp(",")) { Next(); args.Add(Expression()); }
        Expect(T.Op, ")", "« ) » attendu");
        if (args.Count > 3) throw new CompileError("range() accepte au plus 3 arguments", line);
        var body = Block();
        var orelse = new List<Stmt>();
        if (IsKw("else")) { Next(); orelse = Block(); }
        Expr start = args.Count == 1 ? new Num(0, "0", line) : args[0];
        Expr stop = args.Count == 1 ? args[0] : args[1];
        double step = 1;
        if (args.Count == 3)
        {
            var s = ConstValue(args[2]);
            if (s == null || s == 0) throw new CompileError("Le pas de range() doit être une constante non nulle", line);
            step = s.Value;
        }
        return new For(v, start, stop, step, body, orelse, line);
    }

    public static double? ConstValue(Expr e) => e switch
    {
        Num n => n.Value,
        Unary { Op: "-", A: Num n } => -n.Value,
        Unary { Op: "+", A: Num n } => n.Value,
        _ => null
    };

    // ---------- Expressions (priorités Python) ----------
    Expr Expression()
    {
        if (IsKw("lambda")) throw new CompileError("lambda n'est pas pris en charge", Cur.Line);
        var e = OrExpr();
        if (IsKw("if")) throw new CompileError("L'expression conditionnelle « a if c else b » n'est pas prise en charge", Cur.Line);
        return e;
    }

    Expr OrExpr()
    {
        var l = AndExpr();
        while (IsKw("or")) { int line = Next().Line; l = new BoolOp("or", l, AndExpr(), line); }
        return l;
    }

    Expr AndExpr()
    {
        var l = NotExpr();
        while (IsKw("and")) { int line = Next().Line; l = new BoolOp("and", l, NotExpr(), line); }
        return l;
    }

    Expr NotExpr()
    {
        if (IsKw("not")) { int line = Next().Line; return new Not(NotExpr(), line); }
        return Comparison();
    }

    static readonly string[] CmpOps = { "<", ">", "<=", ">=", "==", "!=" };

    Expr Comparison()
    {
        var first = Arith();
        Expr? result = null;
        var left = first;
        while (Cur.Kind == T.Op && CmpOps.Contains(Cur.V))
        {
            var t = Next();
            var right = Arith();
            var c = new Cmp(t.V, left, right, t.Line);
            result = result == null ? c : new BoolOp("and", result, c, t.Line); // a < b < c
            left = right;
        }
        // x in (1, 5, 7) → x == 1 or x == 5 or x == 7 ; x not in (…) → x != 1 and x != 5 and x != 7
        bool notIn = IsKw("not") && p + 1 < toks.Count && toks[p + 1].Kind == T.Kw && toks[p + 1].V == "in";
        if (result == null && (IsKw("in") || notIn))
        {
            int line = Cur.Line;
            if (notIn) Next();
            Next();
            var set = Arith();
            var values = set switch { TupleLit t => t.Items, ListLit l => l.Items, _ => null };
            if (values == null || values.Count == 0)
                throw new CompileError("« in » s'emploie avec une liste de valeurs : x in (1, 5, 7)", line);
            Expr test = null;
            foreach (var v in values)
            {
                var c = new Cmp(notIn ? "!=" : "==", first, v, line);
                test = test == null ? c : new BoolOp(notIn ? "and" : "or", test, c, line);
            }
            return test;
        }
        if (IsKw("is") || IsKw("in")) throw new CompileError($"L'opérateur « {Cur.V} » n'est pas pris en charge", Cur.Line);
        return result ?? first;
    }

    Expr Arith()
    {
        var l = Term();
        while (IsOp("+") || IsOp("-")) { var t = Next(); l = new Bin(t.V, l, Term(), t.Line); }
        return l;
    }

    Expr Term()
    {
        var l = UnaryExpr();
        while (IsOp("*") || IsOp("/") || IsOp("//") || IsOp("%") || IsOp("@")) { var t = Next(); l = new Bin(t.V, l, UnaryExpr(), t.Line); }
        return l;
    }

    Expr UnaryExpr()
    {
        if (IsOp("-") || IsOp("+")) { var t = Next(); return new Unary(t.V, UnaryExpr(), t.Line); }
        return Power();
    }

    Expr Power()
    {
        var a = Postfix(Atom());
        if (IsOp("**")) { var t = Next(); return new Bin("**", a, UnaryExpr(), t.Line); }
        return a;
    }

    /// <summary>Indices m[i, j] / m[i][j] / m[a:b, c:d], attributs z.real, m.T, méthodes z.conjugate().</summary>
    Expr Postfix(Expr e)
    {
        while (true)
        {
            if (IsOp("["))
            {
                int line = Next().Line;
                var parts = new List<Expr> { IndexPart() };
                while (IsOp(",")) { Next(); parts.Add(IndexPart()); }
                Expect(T.Op, "]", "« ] » attendu");
                if (parts.Count > 2) throw new CompileError("Deux indices au plus : m[ligne, colonne]", line);
                // m[i][j] → m[i, j]
                if (e is Index { Parts.Count: 1 } prev && prev.Parts[0] is not Slice && parts.Count == 1)
                    e = new Index(prev.Target, new List<Expr> { prev.Parts[0], parts[0] }, line);
                else
                    e = new Index(e, parts, line);
                continue;
            }
            if (IsOp("."))
            {
                int line = Next().Line;
                string attr = Expect(T.Name, null, "Nom attendu après « . »").V;
                if (IsOp("("))
                {
                    Next();
                    var args = new List<Expr> { e };
                    if (!IsOp(")"))
                    {
                        args.Add(Expression());
                        while (IsOp(",")) { Next(); args.Add(Expression()); }
                    }
                    Expect(T.Op, ")", "« ) » attendu");
                    e = new Call(attr, args, line);   // z.conjugate() → conjugate(z), a.dot(b) → dot(a, b)
                }
                else e = new Attr(e, attr, line);
                continue;
            }
            return e;
        }
    }

    Expr IndexPart()
    {
        int line = Cur.Line;
        Expr? start = null, stop = null;
        if (!IsOp(":")) start = Expression();
        if (!IsOp(":")) return start!;
        Next();
        if (!IsOp(",") && !IsOp("]") && !IsOp(":")) stop = Expression();
        if (IsOp(":")) throw new CompileError("Le pas dans les tranches (a:b:pas) n'est pas pris en charge", line);
        return new Slice(start, stop, line);
    }

    Expr Atom()
    {
        var t = Cur;
        int line = t.Line;
        switch (t.Kind)
        {
            case T.Num:
                Next();
                if (t.V.EndsWith("j"))
                {
                    string raw = t.V[..^1];
                    return new Imag(double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture), raw, line);
                }
                return new Num(double.Parse(t.V, NumberStyles.Float, CultureInfo.InvariantCulture), t.V, line);
            case T.Str:
                Next();
                var sv = t.V;
                while (Is(T.Str)) sv += Next().V; // concaténation implicite
                return new Str(sv, line);
            case T.Kw when t.V == "True": Next(); return new Num(1, "1", line);
            case T.Kw when t.V == "False": Next(); return new Num(0, "0", line);
            case T.Kw when t.V == "None": throw new CompileError("None n'est pas pris en charge", line);
            case T.Op when t.V == "(":
            {
                Next();
                var e = Expression();
                if (IsOp(","))
                {
                    // (l, c) : seulement pour zeros((l, c))
                    var items = new List<Expr> { e };
                    while (IsOp(",")) { Next(); if (IsOp(")")) break; items.Add(Expression()); }
                    Expect(T.Op, ")", "« ) » attendu");
                    return new TupleLit(items, line);
                }
                Expect(T.Op, ")", "« ) » attendu");
                return e;
            }
            case T.Op when t.V == "[":
            {
                Next();
                var items = new List<Expr>();
                while (!IsOp("]"))
                {
                    items.Add(Expression());
                    if (IsOp(",")) { Next(); continue; }
                    if (!IsOp("]")) throw new CompileError("« , » ou « ] » attendu dans la liste" + Found(), Cur.Line);
                }
                Next();
                if (items.Count == 0) throw new CompileError("Liste vide : créez une matrice avec zeros(lignes, colonnes)", line);
                // liste de tuples [(a, b), (c, d)] : chaque tuple est une ligne de matrice, comme [[a, b], [c, d]]
                items = items.Select(x => x is TupleLit tl ? new ListLit(tl.Items, tl.Line) : x).ToList();
                return new ListLit(items, line);
            }
            case T.Op when t.V == "{":
                throw new CompileError("Les dictionnaires ne sont pas pris en charge", line);
            case T.Name:
            {
                Next();
                string name = t.V;
                if (IsOp(".") && modules.Contains(name))
                {
                    Next();
                    string attr = Expect(T.Name, null, "Nom attendu après « . »").V;
                    if (attr == "linalg" && IsOp("."))   // np.linalg.inv(...)
                    {
                        Next();
                        attr = Expect(T.Name, null, "Nom attendu après « linalg. »").V;
                    }
                    if (!IsOp("("))
                    {
                        if (attr is "pi" or "e") return new Const(attr, line);
                        if (attr == "tau") return new Bin("*", new Num(2, "2", line), new Const("pi", line), line);
                        if (attr == "j") return new Imag(1, "1", line);
                        throw new CompileError($"{name}.{attr} n'est pas disponible", line);
                    }
                    name = attr;
                }
                if (IsOp("("))
                {
                    Next();
                    var args = new List<Expr>();
                    if (!IsOp(")"))
                    {
                        args.Add(Expression());
                        while (IsOp(",")) { Next(); args.Add(Expression()); }
                    }
                    if (IsOp("=")) throw new CompileError("Les arguments nommés (sep=, end=...) ne sont pas pris en charge", line);
                    Expect(T.Op, ")", "« ) » attendu");
                    // zeros((2, 3)) → zeros(2, 3)
                    if (args.Count == 1 && args[0] is TupleLit tl) args = tl.Items;
                    // PROMPT("TEXTE"), MENU("A", "B"), VARMENU(a, b) : mêmes fonctions que prompt(), menu(), varmenu()
                    string lower = name.ToLowerInvariant();
                    if (lower == "prompt" || (lower is "menu" or "varmenu" && args.Count > 0)) name = lower;
                    return new Call(name, args, line);
                }
                return new Name(name, line);
            }
        }
        throw new CompileError("Expression attendue" + Found(), line);
    }
}
