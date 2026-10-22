namespace Py42.Core;

/// <summary>Erreur de compilation, avec le numéro de ligne Python concerné (0 si global).</summary>
public sealed class CompileError : Exception
{
    public int Line { get; }
    public CompileError(string message, int line) : base(message) { Line = line; }
}

// ---------- Expressions ----------
public abstract record Expr(int Line);
public sealed record Num(double Value, string? Raw, int Line) : Expr(Line);
public sealed record Str(string Value, int Line) : Expr(Line);
/// <summary>Variable. Raw = nom HP42 déjà résolu (variables temporaires).</summary>
public sealed record Name(string Id, int Line, bool Raw = false) : Expr(Line);
public sealed record Const(string Id, int Line) : Expr(Line);           // pi, e
public sealed record Unary(string Op, Expr A, int Line) : Expr(Line);
public sealed record Bin(string Op, Expr A, Expr B, int Line) : Expr(Line);
public sealed record Cmp(string Op, Expr A, Expr B, int Line) : Expr(Line);
public sealed record BoolOp(string Op, Expr A, Expr B, int Line) : Expr(Line); // and / or
public sealed record Not(Expr A, int Line) : Expr(Line);
public sealed record Call(string Func, List<Expr> Args, int Line) : Expr(Line);
/// <summary>Imaginaire pur : 4j.</summary>
public sealed record Imag(double Value, string Raw, int Line) : Expr(Line);
/// <summary>[a, b, c] ou [[1, 2], [3, 4]] : vecteur colonne ou matrice.</summary>
public sealed record ListLit(List<Expr> Items, int Line) : Expr(Line);
/// <summary>(a, b) : seulement comme argument de zeros((l, c)).</summary>
public sealed record TupleLit(List<Expr> Items, int Line) : Expr(Line);
/// <summary>a:b dans un indice (bornes facultatives).</summary>
public sealed record Slice(Expr? Start, Expr? Stop, int Line) : Expr(Line);
/// <summary>m[i, j], v[i], m[a:b, c:d] (indices Python, à partir de 0).</summary>
public sealed record Index(Expr Target, List<Expr> Parts, int Line) : Expr(Line);
/// <summary>z.real, z.imag, m.T</summary>
public sealed record Attr(Expr Target, string Name, int Line) : Expr(Line);

// ---------- Instructions ----------
public abstract record Stmt(int Line);
public sealed record Assign(List<string> Targets, List<Expr> Values, int Line) : Stmt(Line);
public sealed record AugAssign(string Target, string Op, Expr Value, int Line) : Stmt(Line);
public sealed record ExprStmt(Expr E, int Line) : Stmt(Line);
public sealed record If(Expr Cond, List<Stmt> Body, List<Stmt> Else, int Line) : Stmt(Line);
public sealed record While(Expr Cond, List<Stmt> Body, List<Stmt> Else, int Line) : Stmt(Line);
public sealed record For(string Var, Expr Start, Expr Stop, double Step, List<Stmt> Body, List<Stmt> Else, int Line) : Stmt(Line);
/// <summary>return a, b : Values contient plusieurs expressions (a dans Y, b dans X).</summary>
public sealed record Return(List<Expr> Values, int Line) : Stmt(Line);
public sealed record Assert(Expr Cond, string? Message, int Line) : Stmt(Line);
public sealed record Break(int Line) : Stmt(Line);
public sealed record Continue(int Line) : Stmt(Line);
public sealed record Global(List<string> Names, int Line) : Stmt(Line);
public sealed record IndexAssign(Index Target, Expr Value, int Line) : Stmt(Line);
/// <summary>for x in v : parcours des éléments d'un vecteur ou d'une matrice.</summary>
public sealed record ForEach(string Var, Expr Seq, List<Stmt> Body, List<Stmt> Else, int Line) : Stmt(Line);
public sealed record Import(string Module, int Line) : Stmt(Line);
public sealed record Def(string Name, List<string> Params, List<Stmt> Body, int Line) : Stmt(Line);
