using System.Globalization;
using System.Numerics;
using System.Text;

namespace Py42.Core;

/// <summary>Valeur de la pile ou d'une variable : réel, complexe ou matrice (éléments complexes possibles).</summary>
public sealed class Val
{
    public enum Kind { Real, Cx, Mat, Str }
    public Kind K { get; }
    public double Re { get; }
    public double Im { get; }
    public Complex[,]? M { get; }
    /// <summary>Texte (6 caractères max sur HP-42S), ou éléments texte d'une matrice.</summary>
    public string? S { get; }
    public string?[,]? SM { get; private set; }

    Val(Kind k, double re, double im, Complex[,]? m, string? s = null) { K = k; Re = re; Im = im; M = m; S = s; }

    public static Val Text(string s) => new(Kind.Str, 0, 0, null, s.Length > 6 ? s[..6] : s);
    public bool IsStr => K == Kind.Str;
    public string? TextAt(int i, int j) => SM?[i, j];
    public void SetAt(int i, int j, Val v)
    {
        if (v.IsStr) { SM ??= new string?[Rows, Cols]; SM[i, j] = v.S; M![i, j] = 0; }
        else { M![i, j] = v.C; if (SM != null) SM[i, j] = null; }
    }
    public Val GetAt(int i, int j)
    {
        if (SM?[i, j] is { } t) return Text(t);
        var c = M![i, j];
        return c.Imaginary == 0 ? R(c.Real) : Cx(c);
    }
    /// <summary>DIM : redimensionne en gardant les éléments dans l'ordre (ligne par ligne).</summary>
    public Val Resized(int rows, int cols)
    {
        var m = new Complex[rows, cols];
        string?[,]? sm = SM == null ? null : new string?[rows, cols];
        int n = Math.Min(rows * cols, Rows * Cols);
        for (int k = 0; k < n; k++)
        {
            m[k / cols, k % cols] = M![k / Cols, k % Cols];
            if (sm != null) sm[k / cols, k % cols] = SM![k / Cols, k % Cols];
        }
        return new Val(Kind.Mat, 0, 0, m) { SM = sm };
    }

    public static Val R(double v) => new(Kind.Real, v, 0, null);
    public static Val Cx(Complex c) => new(Kind.Cx, c.Real, c.Imaginary, null);
    public static Val Mat(Complex[,] m) => new(Kind.Mat, 0, 0, m);
    /// <summary>Matrice complexe (COMPLEX de deux matrices réelles) : CPX? est vrai.</summary>
    public static Val CxMat(Complex[,] m) => new(Kind.Mat, 0, 0, m) { IsCxMat = true };
    public bool IsCxMat { get; private init; }
    public static readonly Val Zero = R(0);

    public bool IsReal => K == Kind.Real;
    public bool IsCx => K == Kind.Cx;
    public bool IsMat => K == Kind.Mat;
    public Complex C => K == Kind.Mat ? throw new SimError("Invalid Type : une matrice n'est pas un nombre")
        : K == Kind.Str ? throw new SimError($"Invalid Type : « {S} » est un texte, pas un nombre") : new Complex(Re, Im);
    public double Real => K == Kind.Real ? Re
        : throw new SimError(K switch
        {
            Kind.Cx => "Invalid Type : nombre réel attendu (complexe trouvé)",
            Kind.Str => $"Invalid Type : nombre réel attendu (texte « {S} » trouvé)",
            _ => "Invalid Type : nombre réel attendu (matrice trouvée)"
        });
    public int Rows => M!.GetLength(0);
    public int Cols => M!.GetLength(1);

    /// <summary>Copie : sur la HP-42S, STO et RCL copient les matrices.</summary>
    public Val Copy() => K == Kind.Mat ? new Val(Kind.Mat, 0, 0, (Complex[,])M!.Clone()) { SM = (string?[,]?)SM?.Clone(), IsCxMat = IsCxMat } : this;

    /// <summary>Un complexe dont la partie imaginaire est nulle reste complexe sur HP-42S, sauf résultat d'une opération réelle.</summary>
    public static Val Num(Complex c, bool keepCx) => keepCx || c.Imaginary != 0 ? Cx(c) : R(c.Real);
}

sealed class SimError : Exception { public SimError(string m) : base(Tr.T(m)) { } }

public sealed class SimResult
{
    public List<string> Display { get; } = new();
    public Val X = Val.Zero, Y = Val.Zero, Z = Val.Zero, T = Val.Zero;
    public int Steps;
    public string? Error;
    /// <summary>Index de l'instruction en cours au moment de l'erreur (-1 sinon).</summary>
    public int ErrorPc = -1;
    public int MaxCallDepth;
    public Dictionary<string, Val> Vars { get; } = new();
    /// <summary>Registres numérotés (RCL 05, STO IND …, Σ+).</summary>
    public Dictionary<int, Val> Regs { get; } = new();
}

/// <summary>
/// Trace d'une exécution pour le pas à pas : l'état AVANT chaque instruction (pile, ALPHA, variables), puis l'état final.
/// Les saisies étant données à l'avance, l'exécution est déterministe : rejouer la trace équivaut à exécuter pas à pas,
/// et permet aussi de revenir en arrière.
/// </summary>
public sealed class SimTrace
{
    public readonly record struct Step(int Pc, int Depth, Val X, Val Y, Val Z, Val T, string Alpha, int Out, int Snap);
    public sealed record Snapshot(KeyValuePair<string, Val>[] Vars, KeyValuePair<int, Val>[] Regs);

    public List<Step> Steps { get; } = new();
    public List<Snapshot> Snaps { get; } = new();
    /// <summary>Nombre de pas enregistrés au plus ; au-delà, l'exécution continue sans trace.</summary>
    public int Limit { get; init; } = 200_000;
    public bool Truncated { get; internal set; }
}

/// <summary>
/// Petit simulateur des instructions générées, pour vérifier un programme avant
/// de le taper sur la calculatrice. Calcul en double (la DM42 calcule en décimal 34 chiffres).
/// Gère réels, complexes et matrices.
/// </summary>
public static class Simulator
{
    public static string Fmt(double v)
    {
        if (double.IsNaN(v)) return "NaN";
        if (double.IsInfinity(v)) return v > 0 ? "9.99999999999ᴇ499" : "-9.99999999999ᴇ499";
        if (v == 0) return "0";
        if (Math.Abs(v) < 1e-14 && v != 0) v = Math.Round(v, 15);
        string s = v.ToString("G12", CultureInfo.InvariantCulture);
        return s.Replace("E+", "ᴇ").Replace("E-", "ᴇ-");
    }

    /// <summary>Nombre selon le mode d'affichage de la calculatrice : FIX, SCI ou ENG, n décimales.</summary>
    static string FmtMode(double v, string mode, int n)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return Fmt(v);
        var inv = CultureInfo.InvariantCulture;
        if (mode == "FIX")
        {
            double a = Math.Abs(v);
            // comme sur la calculatrice : passe en SCI si le nombre ne tient pas ou s'arrondirait à zéro
            if (a < 1e12 && (v == 0 || Math.Round(a, n, MidpointRounding.AwayFromZero) != 0))
            {
                int intDigits = a >= 1 ? (int)Math.Floor(Math.Log10(a)) + 1 : 1;
                int dec = Math.Min(n, 12 - intDigits);   // 12 chiffres au plus
                string t = ((decimal)v).ToString("F" + dec, inv);
                return dec == 0 ? t + "." : t;
            }
            mode = "SCI";
        }
        if (v == 0) return (0.0).ToString("F" + n, inv) + "ᴇ0";
        string e = v.ToString("E" + n, inv);
        int ex = int.Parse(e[(e.IndexOf('E') + 1)..], inv);
        if (mode == "SCI") return $"{e[..e.IndexOf('E')]}ᴇ{ex}";
        int ex3 = (int)Math.Floor(ex / 3.0) * 3;   // ENG : exposant multiple de 3
        double m = double.Parse(e, inv) / Math.Pow(10, ex3);
        return $"{m.ToString("F" + Math.Max(0, n - (ex - ex3)), inv)}ᴇ{ex3}";
    }

    /// <summary>Lit une date selon le format réglé : D (JJ.MMAAAA), M (MM.JJAAAA) ou Y (AAAA.MMJJ).</summary>
    static DateTime ParseDate(double v, char mode)
    {
        decimal d = Math.Round((decimal)Math.Abs(v), 6);
        int ip = (int)d;
        decimal fp = d - ip;
        if (mode == 'Y') { int mm = (int)(fp * 100); return new DateTime(ip, mm, (int)Math.Round((fp * 100 - mm) * 100)); }
        int b = (int)(fp * 100);
        int yy = (int)Math.Round((fp * 100 - b) * 10000);
        return mode == 'D' ? new DateTime(yy, b, ip) : new DateTime(yy, ip, b);
    }

    static string FmtC(Complex c)
    {
        double im = Math.Abs(c.Imaginary) < 1e-13 * Math.Max(1, c.Magnitude) ? 0 : c.Imaginary;
        double re = Math.Abs(c.Real) < 1e-13 * Math.Max(1, c.Magnitude) ? 0 : c.Real;
        return im < 0 ? $"{Fmt(re)} -i{Fmt(-im)}" : $"{Fmt(re)} i{Fmt(im)}";
    }

    /// <summary>Affichage court, comme sur la calculatrice : « 3 i4 », « [ 2×3 Matrix ] ».</summary>
    public static string Fmt(Val v) => v.K switch
    {
        Val.Kind.Real => Fmt(v.Re),
        Val.Kind.Cx => FmtC(v.C),
        Val.Kind.Str => v.S!,
        _ => $"[ {v.Rows}×{v.Cols} Matrix ]"
    };

    /// <summary>Contenu complet (matrices détaillées ligne par ligne).</summary>
    public static string Describe(Val v)
    {
        if (!v.IsMat) return Fmt(v);
        var sb = new StringBuilder("[");
        for (int i = 0; i < v.Rows; i++)
        {
            sb.Append('[');
            for (int j = 0; j < v.Cols; j++)
            {
                if (j > 0) sb.Append("  ");
                var c = v.M![i, j];
                if (v.TextAt(i, j) is { } t) sb.Append('"').Append(t).Append('"');
                else sb.Append(c.Imaginary == 0 ? Fmt(c.Real) : FmtC(c));
            }
            sb.Append(']');
        }
        return sb.Append(']').ToString();
    }

    // nœuds et poids de Gauss-Kronrod 7-15 (QUADPACK) : GkX[j] décroissants, GkW[7] = poids du centre, GW = Gauss 7 points
    static readonly double[] GkX =
    {
        0.991455371120812639206854697526329, 0.949107912342758524526189684047851, 0.864864423359769072789712788640926,
        0.741531185599394439863864773280788, 0.586087235467691130294144845693013, 0.405845151377397166906606412076961,
        0.207784955007898467600689403773245,
    };
    static readonly double[] GkW =
    {
        0.022935322010529224963732008058970, 0.063092092629978553290700663189204, 0.104790010322250183839876322541518,
        0.140653259715525918745189590510238, 0.169004726639267902826583426598550, 0.190350578064785409913256402421014,
        0.204432940075298892414161999234649, 0.209482141084727828012999174891714,
    };
    static readonly double[] GW = { 0.129484966168869693270611432679082, 0.279705391489276667901467771423780, 0.381830050505118944950369775488975, 0.417959183673469387755102040816327 };

    /// <summary>Retour d'une fonction appelée par SOLVE / INTEG : adresse de retour fictive qui arrête l'exécution imbriquée.</summary>
    const int FnReturn = -2;

    public static SimResult Run(List<Instr> code, string entry, IEnumerable<double>? stack = null,
                                IEnumerable<double>? inputs = null, int maxSteps = 100_000_000, SimTrace? trace = null)
    {
        var r = new SimResult();
        var labels = new Dictionary<string, int>();
        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].Kind == K.LblG) labels["\"" + code[i].Arg] = i;
            if (code[i].Kind == K.LblL) labels[code[i].N.ToString()] = i;
            if (code[i].Kind == K.LblG) labels.TryAdd("#" + code[i].Arg, i);   // GTO "NOM" d'un listing RPN
        }
        Val x = Val.Zero, y = Val.Zero, z = Val.Zero, t = Val.Zero;
        void Lift(Val v) { t = z; z = y; y = x; x = v; }
        void Drop2(Val v) { x = v; y = z; z = t; }
        foreach (var v in stack ?? Array.Empty<double>()) Lift(Val.R(v));
        var queue = new Queue<double>(inputs ?? Array.Empty<double>());
        var vars = r.Vars;
        var calls = new Stack<int>();
        string alpha = "";
        bool rad = false, polar = false, cpxres = false;
        var flags = new bool[100];
        int fix = 4;
        char dateMode = 'D';   // format de date du simulateur, JJ.MMAAAA tant que le programme ne le change pas
        string? dispMode = null;   // FIX, SCI, ENG exécuté par le programme ; sinon le mode de la calculatrice n'est pas connu
        string Num(double v) => dispMode == null ? Fmt(v) : FmtMode(v, dispMode, fix);
        string Disp(Val v) => v.K switch
        {
            Val.Kind.Real => Num(v.Re),
            Val.Kind.Cx => v.C.Imaginary < 0 ? $"{Num(v.C.Real)} -i{Num(-v.C.Imaginary)}" : $"{Num(v.C.Real)} i{Num(v.C.Imaginary)}",
            _ => Fmt(v)
        };
        var rng = new Random(42);
        var keys = new Dictionary<int, int>();        // touche -> label local (KEY n GTO nn)
        var keyText = new Dictionary<int, string>();
        bool menuOn = false;
        List<string>? varMenu = null;
        string? idxName = null;                        // matrice indexée (INDEX)
        int ii = 1, jj = 1;
        var keysG = new Dictionary<int, (string Lbl, bool Xeq)>();   // KEY n GTO/XEQ "LABEL"
        double sn = 0, sx = 0, sx2 = 0, sy = 0, sy2 = 0, sxy = 0;  // statistiques (Σ+)
        string model = "LINF"; int sreg = 11;
        var regs = r.Regs;                              // registres numérotés (REGS)
        void SyncStats()
        {
            // modèle LINΣ : Σx, Σx², Σy, Σy², Σxy, n dans les registres ΣREG à ΣREG+5
            double[] v = { sx, sx2, sy, sy2, sxy, sn };
            for (int k = 0; k < 6; k++) regs[sreg + k] = Val.R(v[k]);
        }
        var noted = new HashSet<string>();
        void Note(string m) { if (noted.Add(m)) r.Display.Add(m); }
        if (!labels.TryGetValue("\"" + entry, out int pc)) { r.Error = Tr.T($"Label « {entry} » introuvable"); return r; }

        double ToRad(double a) => rad ? a : a * Math.PI / 180;
        double FromRad(double a) => rad ? a : a * 180 / Math.PI;
        // Nom d'opérande : variable, registre numéroté (RCL 05 : 0 tant qu'il n'a rien reçu, comme sur la calculatrice)
        // ou registre de pile (ST X, ST Y, ST Z, ST T)
        static bool IsReg(string n) => n.Length > 0 && n.All(char.IsDigit);
        static bool IsStack(string n) => n.Length == 4 && n.StartsWith("ST ");
        Val Get(string n)
        {
            if (IsStack(n))
                return n[3] switch
                {
                    'X' => x, 'Y' => y, 'Z' => z, 'T' => t,
                    _ => throw new SimError($"{n} n'est pas simulé (LASTX)")
                };
            if (IsReg(n)) return regs.TryGetValue(int.Parse(n, CultureInfo.InvariantCulture), out var rv) ? rv : Val.Zero;
            return vars.TryGetValue(n, out var v) ? v : throw new SimError($"Nonexistent : la variable « {n} » n'existe pas encore");
        }
        bool Has(string n) => IsStack(n) || IsReg(n) || vars.ContainsKey(n);
        void Set(string n, Val v)
        {
            if (IsStack(n))
                switch (n[3])
                {
                    case 'X': x = v; break;
                    case 'Y': y = v; break;
                    case 'Z': z = v; break;
                    case 'T': t = v; break;
                    default: throw new SimError($"{n} n'est pas simulé (LASTX)");
                }
            else if (IsReg(n)) regs[int.Parse(n, CultureInfo.InvariantCulture)] = v;
            else vars[n] = v;
        }

        // Adressage indirect : « RCL IND 05 », « GTO IND "N" », « FIX IND ST X »… L'opérande contient un numéro
        // (registre, label local, drapeau, nombre de décimales) ou un texte (variable, label global).
        Instr Deref(Instr ins)
        {
            string a = ins.Arg;
            int at = a.StartsWith("IND ") ? 0 : a.IndexOf(" IND ", StringComparison.Ordinal) + 1;
            string pre = a[..at], opnd = a[(at + 4)..].Trim();
            var v = Get(opnd);
            string name;
            int num = 0;
            bool isNum = !v.IsStr;
            if (isNum)
            {
                if (!v.IsReal) throw new SimError($"Invalid Type : l'adresse indirecte {opnd} n'est pas un nombre réel");
                num = (int)Math.Truncate(Math.Abs(v.Re));
                name = num.ToString("00", CultureInfo.InvariantCulture);
            }
            else name = v.S!;
            if (ins.Kind is K.Gto or K.Xeq)
                return isNum ? new Instr(ins.Kind, "", num, ins.Line) : new Instr(ins.Kind, name, 0, ins.Line);
            return new Instr(ins.Kind, pre + name, ins.N, ins.Line);
        }

        // ---- trace du pas à pas : état avant chaque instruction
        void Record(int at)
        {
            if (trace == null) return;
            if (trace.Steps.Count >= trace.Limit) { trace.Truncated = true; return; }
            trace.Steps.Add(new SimTrace.Step(at, calls.Count, x, y, z, t, alpha, r.Display.Count, Snap()));
        }
        int Snap()
        {
            // nouvel instantané seulement si une variable ou un registre a changé (les valeurs sont remplacées, pas modifiées)
            var last = trace!.Snaps.Count > 0 ? trace.Snaps[^1] : null;
            if (last != null && last.Vars.Length == vars.Count && last.Regs.Length == regs.Count)
            {
                bool same = true;
                int k = 0;
                foreach (var kv in vars)
                {
                    if (last.Vars[k].Key != kv.Key || !ReferenceEquals(last.Vars[k].Value, kv.Value)) { same = false; break; }
                    k++;
                }
                k = 0;
                if (same)
                    foreach (var kv in regs.OrderBy(kv => kv.Key))
                    {
                        if (last.Regs[k].Key != kv.Key || !ReferenceEquals(last.Regs[k].Value, kv.Value)) { same = false; break; }
                        k++;
                    }
                if (same) return trace.Snaps.Count - 1;
            }
            trace.Snaps.Add(new SimTrace.Snapshot(vars.ToArray(), regs.OrderBy(kv => kv.Key).ToArray()));
            return trace.Snaps.Count - 1;
        }
        bool noLift = false;   // après ENTER, le nombre tapé remplace X (listings RPN ; Py42 n'émet pas ENTER)
        Val Indexed()
        {
            if (idxName == null) throw new SimError("Nonexistent : aucune matrice indexée (INDEX)");
            var m = Get(idxName);
            if (!m.IsMat) throw new SimError($"Invalid Type : « {idxName} » n'est pas une matrice");
            return m;
        }

        Val Fn(string f, Val a)
        {
            if (f == "+/-") return a.IsMat ? MapM(a, c => -c) : a.IsCx ? Val.Cx(-a.C) : Val.R(-a.Re);
            if (f == "1/X")
            {
                if (a.IsMat) return Val.Mat(Inverse(a.M!));
                if (a.C == Complex.Zero) throw new SimError("Divide by 0 : 1/X de zéro");
                return a.IsCx ? Val.Cx(1 / a.C) : Val.R(1 / a.Re);
            }
            if (a.IsMat) throw new SimError($"Invalid Type : {f} sur une matrice n'est pas simulé");
            if (a.IsCx)
            {
                var c = a.C;
                return f switch
                {
                    "SQRT" => Val.Cx(Complex.Sqrt(c)),
                    "X↑2" => Val.Cx(c * c),
                    "ABS" => Val.R(c.Magnitude),
                    "E↑X" => Val.Cx(Complex.Exp(c)),
                    "LN" => c == Complex.Zero ? throw new SimError("Invalid Data : LN de 0") : Val.Cx(Complex.Log(c)),
                    "LOG" => c == Complex.Zero ? throw new SimError("Invalid Data : LOG de 0") : Val.Cx(Complex.Log10(c)),
                    "SIN" => Val.Cx(Complex.Sin(c)), "COS" => Val.Cx(Complex.Cos(c)), "TAN" => Val.Cx(Complex.Tan(c)),
                    "ASIN" => Val.Cx(Complex.Asin(c)), "ACOS" => Val.Cx(Complex.Acos(c)), "ATAN" => Val.Cx(Complex.Atan(c)),
                    "SINH" => Val.Cx(Complex.Sinh(c)), "COSH" => Val.Cx(Complex.Cosh(c)), "TANH" => Val.Cx(Complex.Tanh(c)),
                    _ => throw new SimError($"Invalid Type : {f} sur un complexe n'est pas simulé")
                };
            }
            double v = a.Re;
            Val CxOr(string err, Func<Complex> cx) => cpxres ? Val.Cx(cx()) : throw new SimError(err);
            return f switch
            {
                "SQRT" => v < 0 ? CxOr("Invalid Data : racine d'un nombre négatif (import cmath pour un résultat complexe)", () => Complex.Sqrt(v)) : Val.R(Math.Sqrt(v)),
                "X↑2" => Val.R(v * v),
                "SIN" => Val.R(Math.Sin(ToRad(v))),
                "COS" => Val.R(Math.Cos(ToRad(v))),
                "TAN" => Val.R(Math.Tan(ToRad(v))),
                "ASIN" => Math.Abs(v) > 1 ? CxOr("Invalid Data : ASIN hors de [-1, 1]", () => Complex.Asin(v)) : Val.R(FromRad(Math.Asin(v))),
                "ACOS" => Math.Abs(v) > 1 ? CxOr("Invalid Data : ACOS hors de [-1, 1]", () => Complex.Acos(v)) : Val.R(FromRad(Math.Acos(v))),
                "ATAN" => Val.R(FromRad(Math.Atan(v))),
                "SINH" => Val.R(Math.Sinh(v)), "COSH" => Val.R(Math.Cosh(v)), "TANH" => Val.R(Math.Tanh(v)),
                "ASINH" => Val.R(Math.Asinh(v)),
                "ACOSH" => v < 1 ? CxOr("Invalid Data : ACOSH < 1", () => Complex.Log(v + Complex.Sqrt(v * v - 1))) : Val.R(Math.Acosh(v)),
                "ATANH" => Math.Abs(v) >= 1 ? throw new SimError("Invalid Data : ATANH hors de ]-1, 1[") : Val.R(Math.Atanh(v)),
                "E↑X" => Val.R(Math.Exp(v)),
                "E↑X-1" => Val.R(Math.Exp(v) - 1),
                "LN" => v <= 0 ? (v < 0 ? CxOr("Invalid Data : LN d'un nombre négatif", () => Complex.Log(v)) : throw new SimError("Invalid Data : LN de 0")) : Val.R(Math.Log(v)),
                "LN1+X" => v <= -1 ? throw new SimError("Invalid Data : LN1+X") : Val.R(Math.Log(1 + v)),
                "LOG" => v <= 0 ? (v < 0 ? CxOr("Invalid Data : LOG d'un nombre négatif", () => Complex.Log10(v)) : throw new SimError("Invalid Data : LOG de 0")) : Val.R(Math.Log10(v)),
                "ABS" => Val.R(Math.Abs(v)),
                "IP" => Val.R(Math.Truncate(v)),
                "N!" => Val.R(Fact(v)),
                "GAMMA" => Val.R(Gamma(v)),
                "→RAD" => Val.R(v * Math.PI / 180),
                "→DEG" => Val.R(v * 180 / Math.PI),
                _ => throw new SimError("Fonction inconnue " + f)
            };
        }

        int errPc = pc;                // instruction en cours (la plus imbriquée), pour situer une erreur
        string? slvPgm = null, intPgm = null;   // PGMSLV, PGMINT
        try
        {
            while (pc < code.Count) Step();
        }
        catch (SimError e)
        {
            r.Error = e.Message;
            r.ErrorPc = errPc;
        }
        Record(-1);   // état final
        (r.X, r.Y, r.Z, r.T) = (x, y, z, t);
        return r;

        void Step()
        {
            {
                if (++r.Steps > maxSteps) throw new SimError($"Arrêt après {maxSteps} pas : boucle infinie ?");
                Record(pc);
                errPc = pc;
                var ins = code[pc++];
                if (ins.Kind is not (K.AStr or K.AStrAppend or K.Num) && (ins.Arg.StartsWith("IND ") || ins.Arg.Contains(" IND ")))
                    ins = Deref(ins);
                bool liftOff = noLift;   // ENTER ne vaut que pour l'instruction qui le suit
                noLift = false;
                switch (ins.Kind)
                {
                    case K.LblG: case K.LblL: break;
                    case K.Num:
                    {
                        var nv = Val.R(double.Parse(ins.Arg.Replace("ᴇ", "E"), NumberStyles.Float, CultureInfo.InvariantCulture));
                        if (liftOff) x = nv; else Lift(nv);
                        break;
                    }
                    case K.Const: Lift(Val.R(Math.PI)); break;
                    case K.Rcl: Lift(Get(ins.Arg).Copy()); break;
                    case K.Sto: Set(ins.Arg, x.Copy()); break;
                    case K.StoOp: Set(ins.Arg, Arith(StoOps[ins.N], Get(ins.Arg), x)); break;
                    case K.RclOp: x = Arith(StoOps[ins.N], x, Get(ins.Arg)); break;
                    case K.Op: Drop2(Arith(ins.Arg, y, x)); break;
                    case K.Fn: x = Fn(ins.Arg, x); break;
                    case K.Test:
                    {
                        bool ok;
                        if (ins.Arg.StartsWith("ISG ") || ins.Arg.StartsWith("DSE "))
                        {
                            // compteur ccccc.fffii : ISG saute quand c > fff, DSE quand c ≤ fff
                            string reg = ins.Arg[4..];
                            double cv = Get(reg).Real;
                            double ac = Math.Abs(cv), frac = Math.Round((ac - Math.Truncate(ac)) * 100000);
                            double cnt = Math.Truncate(cv), fin = Math.Floor(frac / 100), step = frac % 100;
                            if (step == 0) step = 1;
                            bool isg = ins.Arg.StartsWith("ISG");
                            cnt += isg ? step : -step;
                            Set(reg, Val.R(cnt + Math.Sign(cnt == 0 ? 1 : cnt) * frac / 100000));
                            ok = isg ? cnt <= fin : cnt > fin;
                        }
                        else if (ins.Arg.StartsWith("F"))
                        {
                            int f = int.Parse(ins.Arg[^2..]);
                            ok = ins.Arg.StartsWith("FS") ? flags[f] : !flags[f];
                            if (ins.Arg.Contains("?C")) flags[f] = false;
                        }
                        else if (ins.Arg == "CPX?") ok = x.IsCx || (x.IsMat && x.IsCxMat);
                        else if (ins.Arg == "MAT?") ok = x.IsMat;
                        else if (ins.Arg == "REAL?") ok = x.IsReal;
                        else if (ins.Arg == "STR?") ok = x.IsStr;
                        else ok = Test(ins.Arg, x, y);
                        if (!ok) pc++;
                        break;
                    }
                    case K.Gto:
                        if (ins.Arg.Length > 0)
                        {
                            // GTO "NOM" d'un listing RPN
                            if (!labels.TryGetValue("#" + ins.Arg, out int gl)) throw new SimError($"Label « {ins.Arg} » introuvable");
                            pc = gl;
                        }
                        else if (!labels.TryGetValue(ins.N.ToString(), out pc)) throw new SimError($"Label {ins.N:00} introuvable");
                        break;
                    case K.Xeq:
                        // XEQ 05 d'un listing RPN : Arg vide, label local dans N
                        if (!labels.TryGetValue(ins.Arg.Length == 0 ? ins.N.ToString() : "\"" + ins.Arg, out int target))
                            throw new SimError($"Label « {(ins.Arg.Length == 0 ? ins.N.ToString("00") : ins.Arg)} » introuvable");
                        calls.Push(pc);
                        r.MaxCallDepth = Math.Max(r.MaxCallDepth, calls.Count);
                        pc = target;
                        break;
                    case K.Rtn:
                    case K.End:
                        if (calls.Count == 0) { pc = code.Count; break; }
                        pc = calls.Pop();
                        break;
                    case K.Stk:
                        switch (ins.Arg)
                        {
                            case "R↓": { var ox = x; x = y; y = z; z = t; t = ox; break; }
                            case "X<>Y": (x, y) = (y, x); break;
                            case "ENTER": Lift(x.Copy()); noLift = true; break;
                            case "R↑": { var ot = t; t = z; z = y; y = x; x = ot; break; }
                            case "STO ST Y": y = x; break;
                            case "→POL":
                            {
                                double xr = x.Real, yr = y.Real;
                                double rr = Math.Sqrt(xr * xr + yr * yr), th = FromRad(Math.Atan2(yr, xr));
                                x = Val.R(rr); y = Val.R(th);
                                break;
                            }
                            case "→REC":
                            {
                                double rr = x.Real, th = ToRad(y.Real);
                                x = Val.R(rr * Math.Cos(th)); y = Val.R(rr * Math.Sin(th));
                                break;
                            }
                        }
                        break;
                    case K.ViewVar: r.Display.Add($"{ins.Arg}={(Get(ins.Arg).IsMat ? Describe(Get(ins.Arg)) : Disp(Get(ins.Arg)))}"); break;
                    case K.ViewX: r.Display.Add($"X={(x.IsMat ? Describe(x) : Disp(x))}"); break;
                    case K.AStr: alpha = ins.Arg; break;
                    case K.AStrAppend: alpha += ins.Arg; break;
                    case K.ArclVar: alpha += Disp(Get(ins.Arg)); break;
                    case K.ArclX: alpha += Disp(x); break;
                    case K.Cla: alpha = ""; break;
                    case K.AView: r.Display.Add(alpha.Length > 44 ? alpha[^44..] : alpha); break;
                    case K.Input:
                    {
                        Val v = queue.Count > 0 ? Val.R(queue.Dequeue()) : Has(ins.Arg) ? Get(ins.Arg) : Val.Zero;
                        Set(ins.Arg, v);
                        Lift(v);
                        r.Display.Add($"{ins.Arg}?  ← {Fmt(v)}");
                        break;
                    }
                    case K.Mode:
                        switch (ins.Arg)
                        {
                            case "RAD": rad = true; break;
                            case "DEG": rad = false; break;
                            case "RECT": polar = false; break;
                            case "POLAR": polar = true; break;
                            case "CPXRES": cpxres = true; break;
                        }
                        break;
                    case K.Stop:
                        if (menuOn && (keys.Count > 0 || keysG.Count > 0))
                        {
                            string shown = string.Join("", keys.Keys.Concat(keysG.Keys).Where(k => k <= 6).Distinct().OrderBy(k => k)
                                .Select(k => $"[{(keyText.TryGetValue(k, out var kt) ? kt : "?")}]"));
                            if (queue.Count == 0)
                                throw new SimError($"Le programme attend un choix dans le menu {shown} : ajoutez le numéro de la touche (1 à 6, 0 = EXIT) dans INPUT");
                            int k = (int)queue.Dequeue();
                            if (k == 0) k = 9;
                            r.Display.Add($"MENU {shown}  ← {(k == 9 ? "EXIT" : k.ToString())}");
                            if (keysG.TryGetValue(k, out var g))
                            {
                                if (!labels.TryGetValue("\"" + g.Lbl, out int gl)) throw new SimError($"Label « {g.Lbl} » introuvable");
                                if (g.Xeq) { calls.Push(pc); r.MaxCallDepth = Math.Max(r.MaxCallDepth, calls.Count); }
                                pc = gl;
                            }
                            else if (keys.TryGetValue(k, out int keyLbl)) pc = labels[keyLbl.ToString()];
                            else throw new SimError($"Touche {k} sans action dans le menu {shown}");
                        }
                        else if (varMenu != null)
                        {
                            var parts = new List<string>();
                            foreach (var v in varMenu)
                            {
                                if (queue.Count > 0) vars[v] = Val.R(queue.Dequeue());
                                parts.Add(vars.TryGetValue(v, out var val) ? $"{v}={Fmt(val)}" : $"{v}=?");
                            }
                            r.Display.Add($"VARMENU  {string.Join("  ", parts)}  (R/S)");
                        }
                        else r.Display.Add(Tr.T("(STOP — R/S pour continuer)"));
                        break;
                    case K.KeyGto:
                    {
                        int k = int.Parse(ins.Arg);
                        keys[k] = ins.N;
                        keyText[k] = alpha.Length > 6 ? alpha[..6] : alpha;
                        break;
                    }
                    case K.Mvar: break;
                    case K.VarMenu:
                    {
                        varMenu = new List<string>();
                        if (labels.TryGetValue("\"" + ins.Arg, out int at))
                            for (int j = at + 1; j < code.Count && code[j].Kind == K.Mvar; j++) varMenu.Add(code[j].Arg);
                        break;
                    }
                    case K.Cmd:
                    {
                        string[] w = ins.Arg.Split(' ', 2);
                        int arg = w.Length > 1 && int.TryParse(w[1], out var a1) ? a1 : 0;
                        switch (w[0])
                        {
                            case "FIX": case "SCI": case "ENG": fix = arg; dispMode = w[0]; break;
                            case "ALL": fix = 11; dispMode = null; break;
                            case "RAD": rad = true; break;
                            case "DEG": case "GRAD": rad = false; break;
                            case "RECT": polar = false; break;
                            case "POLAR": polar = true; break;
                            case "CPXRES": cpxres = true; break;
                            case "REALRES": cpxres = false; break;
                            case "SF": flags[arg] = true; break;
                            case "CF": flags[arg] = false; break;
                            case "BEEP": r.Display.Add("♪ BEEP"); break;
                            case "DMY": dateMode = 'D'; break;
                            case "MDY": dateMode = 'M'; break;
                            case "YMD": dateMode = 'Y'; break;
                            case "DATE":
                            {
                                var nowT = DateTime.Now;
                                Lift(Val.R(dateMode == 'Y' ? nowT.Year + nowT.Month / 100.0 + nowT.Day / 10000.0
                                    : (dateMode == 'D' ? nowT.Day + nowT.Month / 100.0 : nowT.Month + nowT.Day / 100.0) + nowT.Year / 1000000.0));
                                break;
                            }
                            case "TIME":
                            {
                                var nowT = DateTime.Now;
                                Lift(Val.R(nowT.Hour + nowT.Minute / 100.0 + nowT.Second / 10000.0));
                                break;
                            }
                            case "DDAYS": Drop2(Val.R((ParseDate(x.Real, dateMode) - ParseDate(y.Real, dateMode)).Days)); break;
                            case "TONE": r.Display.Add($"♪ TONE {arg}"); break;
                            case "CLA": alpha = ""; break;
                            case "CLST": x = y = z = t = Val.Zero; break;
                            case "CLX": x = Val.Zero; break;
                            case "CLLCD": r.Display.Add(Tr.T("(écran effacé)")); break;
                            case "PIXEL": break;
                            case "AVIEW": r.Display.Add(alpha); break;
                            case "PROMPT":
                            {
                                // x = prompt("...") : la valeur tapée est rangée juste après (STO) ; sinon simple pause
                                bool wantsValue = pc < code.Count && code[pc].Kind == K.Sto;
                                if (wantsValue && queue.Count > 0) { double v = queue.Dequeue(); Lift(Val.R(v)); r.Display.Add($"{alpha}  ← {Fmt(v)}"); }
                                else if (wantsValue) throw new SimError($"PROMPT « {alpha} » attend une valeur : ajoutez-la dans INPUT");
                                else r.Display.Add($"{alpha}  (PROMPT — R/S)");
                                break;
                            }
                            case "CLMENU": keys.Clear(); keysG.Clear(); keyText.Clear(); break;
                            case "KEY":
                            {
                                // KEY n GTO "LBL" / KEY n XEQ "LBL"
                                var kw = w[1].Split(' ', 3);
                                int kn = int.Parse(kw[0]);
                                keysG[kn] = (kw[2].Trim('"'), kw[1] == "XEQ");
                                keyText[kn] = alpha.Length > 6 ? alpha[..6] : alpha;
                                break;
                            }
                            case "CLV": vars.Remove(w[1].Trim('"')); break;
                            case "ASTO":
                                Set(w[1].Trim('"'), Val.Text(alpha));   // variable, registre (ASTO 01) ou ST X
                                break;
                            case "DIM":
                            {
                                string dn = w[1].Trim('"');
                                int dr = (int)y.Real, dc = (int)x.Real;
                                if (dr < 1 || dc < 1) throw new SimError($"Dimension Error : DIM {dr}×{dc} impossible (une matrice a au moins 1 élément)");
                                vars[dn] = vars.TryGetValue(dn, out var old) && old.IsMat ? old.Resized(dr, dc) : Val.Mat(new Complex[dr, dc]);
                                break;
                            }

                            // ---- statistiques (modèle linéaire)
                            case "Σ+": case "Σ-":
                            {
                                double xv = x.Real, yv = y.Real, sg = w[0] == "Σ+" ? 1 : -1;
                                sn += sg; sx += sg * xv; sx2 += sg * xv * xv; sy += sg * yv; sy2 += sg * yv * yv; sxy += sg * xv * yv;
                                x = Val.R(sn);
                                SyncStats();
                                break;
                            }
                            case "CLΣ": sn = sx = sx2 = sy = sy2 = sxy = 0; SyncStats(); break;
                            case "RCL": Lift(Get(w[1].Trim('"')).Copy()); break;
                            case "STO": Set(w[1].Trim('"'), x.Copy()); break;
                            case "X<>":
                            {
                                string xn = w[1].Trim('"');
                                var ov = Get(xn);
                                Set(xn, x);
                                x = ov;
                                break;
                            }

                            // ---- solveur et intégration
                            case "PGMSLV": slvPgm = w[1].Trim('"'); break;
                            case "PGMINT": intPgm = w[1].Trim('"'); break;
                            case "SOLVE": if (!Solve(w[1].Trim('"'))) pc++; break;   // pas de racine : saute la ligne suivante
                            case "INTEG": Integ(w[1].Trim('"')); break;
                            case "CLRG": regs.Clear(); break;
                            case "LINF": case "LOGF": case "EXPF": case "PWRF": case "BEST":
                                model = w[0];
                                if (model != "LINF") Note($"({model} : le simulateur calcule avec le modèle linéaire)");
                                break;
                            case "MEAN":
                                if (sn == 0) throw new SimError("Stat Math Error : aucun point (Σ+)");
                                Lift(Val.R(sy / sn)); Lift(Val.R(sx / sn));
                                break;
                            case "SUM": Lift(Val.R(sy)); Lift(Val.R(sx)); break;
                            case "SDEV":
                            {
                                if (sn < 2) throw new SimError("Stat Math Error : il faut au moins 2 points");
                                Lift(Val.R(Math.Sqrt((sy2 - sy * sy / sn) / (sn - 1))));
                                Lift(Val.R(Math.Sqrt((sx2 - sx * sx / sn) / (sn - 1))));
                                break;
                            }
                            case "WMEAN":
                                if (sy == 0) throw new SimError("Stat Math Error : somme des poids nulle");
                                Lift(Val.R(sxy / sy));
                                break;
                            case "SLOPE": case "YINT": case "CORR": case "FCSTX": case "FCSTY":
                            {
                                double d = sn * sx2 - sx * sx;
                                if (sn < 2 || d == 0) throw new SimError("Stat Math Error : pas assez de points distincts");
                                double m = (sn * sxy - sx * sy) / d, b0 = (sy - m * sx) / sn;
                                double corr = (sn * sxy - sx * sy) / Math.Sqrt(d * (sn * sy2 - sy * sy));
                                switch (w[0])
                                {
                                    case "SLOPE": Lift(Val.R(m)); break;
                                    case "YINT": Lift(Val.R(b0)); break;
                                    case "CORR": Lift(Val.R(corr)); break;
                                    case "FCSTX": x = Val.R((x.Real - b0) / m); break;
                                    case "FCSTY": x = Val.R(m * x.Real + b0); break;
                                }
                                break;
                            }
                            case "ΣREG?": Lift(Val.R(sreg)); break;
                            case "ΣREG": sreg = int.Parse(w[1]); break;

                            // ---- bases et entiers
                            case "→DEC": x = Val.R(Convert.ToInt64(((long)x.Real).ToString(), 8)); break;
                            case "→OCT": x = Val.R(double.Parse(Convert.ToString((long)x.Real, 8), CultureInfo.InvariantCulture)); break;
                            case "BASE+": Drop2(Val.R((long)y.Real + (long)x.Real)); break;
                            case "BASE-": Drop2(Val.R((long)y.Real - (long)x.Real)); break;
                            case "BASE×": Drop2(Val.R((long)y.Real * (long)x.Real)); break;
                            case "BASE÷":
                                if ((long)x.Real == 0) throw new SimError("Divide by 0 : division par zéro");
                                Drop2(Val.R((long)y.Real / (long)x.Real));
                                break;
                            case "BASE±": x = Val.R(-(long)x.Real); break;
                            case "ROTXY":
                            {
                                ulong v36 = (ulong)((long)y.Real) & 0xFFFFFFFFFUL;
                                int n = ((int)x.Real % 36 + 36) % 36;
                                ulong rot = ((v36 >> n) | (v36 << (36 - n))) & 0xFFFFFFFFFUL;
                                long sv = (rot & 0x800000000UL) != 0 ? (long)rot - (1L << 36) : (long)rot;
                                Drop2(Val.R(sv));
                                break;
                            }

                            // ---- ALPHA
                            case "AIP": alpha += ((long)Math.Truncate(x.Real)).ToString(); break;
                            case "AROT":
                            {
                                if (alpha.Length > 0)
                                {
                                    int n = (((int)x.Real) % alpha.Length + alpha.Length) % alpha.Length;
                                    alpha = alpha[n..] + alpha[..n];
                                }
                                break;
                            }
                            case "ASHF": alpha = alpha.Length > 6 ? alpha[6..] : ""; break;

                            // ---- matrices
                            case "RNRM":
                            {
                                if (!x.IsMat) throw new SimError("Invalid Type : RNRM attend une matrice");
                                double best = 0;
                                for (int i = 0; i < x.Rows; i++) { double s0 = 0; for (int j = 0; j < x.Cols; j++) s0 += x.M![i, j].Magnitude; best = Math.Max(best, s0); }
                                x = Val.R(best);
                                break;
                            }
                            case "RSUM":
                            {
                                if (!x.IsMat) throw new SimError("Invalid Type : RSUM attend une matrice");
                                var rs = new Complex[x.Rows, 1];
                                for (int i = 0; i < x.Rows; i++) for (int j = 0; j < x.Cols; j++) rs[i, 0] += x.M![i, j];
                                x = Val.Mat(rs);
                                break;
                            }
                            case "UVEC":
                            {
                                if (!x.IsMat) throw new SimError("Invalid Type : UVEC attend une matrice");
                                double n2 = 0;
                                foreach (var c in x.M!) n2 += c.Real * c.Real + c.Imaginary * c.Imaginary;
                                if (n2 == 0) throw new SimError("Invalid Data : vecteur nul");
                                double nrm = Math.Sqrt(n2);
                                x = MapM(x, c => c / nrm);
                                break;
                            }

                            // ---- sans effet dans le simulateur
                            case "ADV": case "PRΣ": case "PRA": case "PRX": case "PRSTK": case "PRLCD": case "PRUSR": case "PRV":
                            case "PRON": case "PROFF": case "MAN": case "NORM": case "TRACE": case "DELAY":
                            case "AON": case "AOFF": case "SIZE": case "ON": case "OFF": case "CLKEYS": case "KEYASN": case "LCLBL":
                            case "ALLΣ": case "LINΣ": case "RDX,": case "RDX.": case "BINM": case "OCTM": case "DECM": case "HEXM":
                            case "CLD": case "PSE": case "EDIT": case "EDITN": case "ASSIGN":
                                break;
                            case "MENU": menuOn = true; break;
                            case "EXITALL": menuOn = false; varMenu = null; break;
                            case "GETKEY":
                            {
                                double v = queue.Count > 0 ? queue.Dequeue() : throw new SimError("GETKEY attend une touche : ajoutez son code dans INPUT");
                                Lift(Val.R(v));
                                r.Display.Add($"GETKEY ← {Fmt(v)}");
                                break;
                            }
                            case "RAN": Lift(Val.R(rng.NextDouble())); break;
                            case "SEED": rng = new Random((int)(x.Real * 1e6)); break;
                            case "XTOA": alpha += (char)(int)x.Real; break;
                            case "ALENG": Lift(Val.R(alpha.Length)); break;
                            case "ATOX":
                                Lift(Val.R(alpha.Length > 0 ? alpha[0] : 0));
                                if (alpha.Length > 0) alpha = alpha[1..];
                                break;
                            case "POSA": x = Val.R(alpha.IndexOf((char)(int)x.Real)); break;
                            case "FP": x = Val.R(x.Real - Math.Truncate(x.Real)); break;
                            case "IP": x = Val.R(Math.Truncate(x.Real)); break;
                            case "SIGN": x = Val.R(Math.Sign(x.Real)); break;
                            case "RND": x = Val.R(Math.Round(x.Real, Math.Min(fix, 11), MidpointRounding.AwayFromZero)); break;
                            case "10↑X": x = Val.R(Math.Pow(10, x.Real)); break;
                            case "→DEG": x = Val.R(x.Real * 180 / Math.PI); break;
                            case "→RAD": x = Val.R(x.Real * Math.PI / 180); break;
                            case "→HR": x = Val.R(ToHr(x.Real)); break;
                            case "→HMS": x = Val.R(ToHms(x.Real)); break;
                            case "HMS+": Drop2(Val.R(ToHms(ToHr(y.Real) + ToHr(x.Real)))); break;
                            case "HMS-": Drop2(Val.R(ToHms(ToHr(y.Real) - ToHr(x.Real)))); break;
                            case "COMB": Drop2(Val.R(Fact(y.Real) / (Fact(x.Real) * Fact(y.Real - x.Real)))); break;
                            case "PERM": Drop2(Val.R(Fact(y.Real) / Fact(y.Real - x.Real))); break;
                            case "%": x = Val.R(x.Real * y.Real / 100); break;
                            case "%CH": Drop2(Val.R((x.Real - y.Real) * 100 / y.Real)); break;

                            // ---- complexes
                            case "COMPLEX" when x.IsMat:
                            {
                                bool split = x.IsCxMat || x.M!.Cast<Complex>().Any(c => c.Imaginary != 0);
                                if (split)
                                {
                                    var re = new Complex[x.Rows, x.Cols];
                                    var im = new Complex[x.Rows, x.Cols];
                                    for (int i = 0; i < x.Rows; i++)
                                        for (int j = 0; j < x.Cols; j++)
                                        {
                                            var c = x.M![i, j];
                                            re[i, j] = polar ? c.Magnitude : c.Real;
                                            im[i, j] = polar ? FromRad(c.Phase) : c.Imaginary;
                                        }
                                    // pile : Y = parties réelles, X = parties imaginaires
                                    x = Val.Mat(re);
                                    Lift(Val.Mat(im));
                                }
                                else
                                {
                                    if (!y.IsMat || y.Rows != x.Rows || y.Cols != x.Cols)
                                        throw new SimError("Dimension Error : COMPLEX attend deux matrices réelles de même taille (Y = réel, X = imaginaire)");
                                    var m = new Complex[x.Rows, x.Cols];
                                    for (int i = 0; i < x.Rows; i++)
                                        for (int j = 0; j < x.Cols; j++)
                                        {
                                            double a = y.M![i, j].Real, b = x.M![i, j].Real;
                                            m[i, j] = polar ? Complex.FromPolarCoordinates(a, ToRad(b)) : new Complex(a, b);
                                        }
                                    Drop2(Val.CxMat(m));
                                }
                                break;
                            }
                            case "COMPLEX":
                                if (x.IsCx)
                                {
                                    var c = x.C;
                                    if (polar) { x = Val.R(c.Magnitude); Lift(Val.R(FromRad(c.Phase))); }
                                    else { x = Val.R(c.Real); Lift(Val.R(c.Imaginary)); }
                                }
                                else
                                {
                                    double a = y.Real, b = x.Real;
                                    Drop2(Val.Cx(polar ? Complex.FromPolarCoordinates(a, ToRad(b)) : new Complex(a, b)));
                                }
                                break;

                            // ---- matrices
                            case "NEWMAT":
                            {
                                int nr = (int)y.Real, nc = (int)x.Real;
                                if (nr < 1 || nc < 1) throw new SimError("Invalid Data : dimensions de matrice ≤ 0");
                                Drop2(Val.Mat(new Complex[nr, nc]));
                                break;
                            }
                            case "INDEX":
                            {
                                string n = w[1].Trim('"');
                                if (!Get(n).IsMat) throw new SimError($"Invalid Type : « {n} » n'est pas une matrice");
                                idxName = n; ii = 1; jj = 1;
                                break;
                            }
                            case "STOIJ":
                            {
                                var m = Indexed();
                                int i = (int)y.Real, j = (int)x.Real;
                                if (i < 1 || j < 1 || i > m.Rows || j > m.Cols)
                                    throw new SimError($"Dimension Error : élément ({i}, {j}) hors de la matrice {m.Rows}×{m.Cols} (indice Python {i - 1}, {j - 1})");
                                ii = i; jj = j;
                                break;
                            }
                            case "RCLIJ": Lift(Val.R(ii)); Lift(Val.R(jj)); break;
                            case "RCLEL":
                            {
                                Lift(Indexed().GetAt(ii - 1, jj - 1));
                                break;
                            }
                            case "STOEL":
                                if (x.IsMat) throw new SimError("Invalid Type : on ne range pas une matrice dans un élément");
                                Indexed().SetAt(ii - 1, jj - 1, x);
                                break;
                            case "J+":
                            {
                                var m = Indexed();
                                if (++jj > m.Cols) { jj = 1; if (++ii > m.Rows) ii = 1; }
                                break;
                            }
                            case "I+":
                            {
                                var m = Indexed();
                                if (++ii > m.Rows) { ii = 1; if (++jj > m.Cols) jj = 1; }
                                break;
                            }
                            case "GETM":
                            {
                                var m = Indexed();
                                int nr = (int)y.Real, nc = (int)x.Real;
                                if (nr < 1 || nc < 1 || ii - 1 + nr > m.Rows || jj - 1 + nc > m.Cols)
                                    throw new SimError($"Dimension Error : sous-matrice {nr}×{nc} en ({ii}, {jj}) hors de la matrice {m.Rows}×{m.Cols}");
                                var s = new Complex[nr, nc];
                                for (int i = 0; i < nr; i++) for (int j = 0; j < nc; j++) s[i, j] = m.M![ii - 1 + i, jj - 1 + j];
                                Drop2(Val.Mat(s));
                                break;
                            }
                            case "PUTM":
                            {
                                var m = Indexed();
                                if (!x.IsMat) throw new SimError("Invalid Type : PUTM attend une matrice dans X");
                                if (ii - 1 + x.Rows > m.Rows || jj - 1 + x.Cols > m.Cols)
                                    throw new SimError($"Dimension Error : la matrice {x.Rows}×{x.Cols} ne tient pas en ({ii}, {jj}) dans {m.Rows}×{m.Cols}");
                                for (int i = 0; i < x.Rows; i++) for (int j = 0; j < x.Cols; j++) m.M![ii - 1 + i, jj - 1 + j] = x.M![i, j];
                                break;
                            }
                            case "DIM?":
                            {
                                if (!x.IsMat) throw new SimError("Invalid Type : DIM? attend une matrice");
                                int nr = x.Rows, nc = x.Cols;
                                x = Val.R(nr);
                                Lift(Val.R(nc));
                                break;
                            }
                            case "TRANS":
                            {
                                if (!x.IsMat) throw new SimError("Invalid Type : TRANS attend une matrice");
                                var s = new Complex[x.Cols, x.Rows];
                                for (int i = 0; i < x.Rows; i++) for (int j = 0; j < x.Cols; j++) s[j, i] = x.M![i, j];
                                x = Val.Mat(s);
                                break;
                            }
                            case "INVRT":
                                if (!x.IsMat) throw new SimError("Invalid Type : INVRT attend une matrice");
                                x = Val.Mat(Inverse(x.M!));
                                break;
                            case "DET":
                                if (!x.IsMat) throw new SimError("Invalid Type : DET attend une matrice");
                                x = Val.Num(Det(x.M!), false);
                                break;
                            case "FNRM":
                            {
                                if (!x.IsMat) throw new SimError("Invalid Type : FNRM attend une matrice");
                                double s = 0;
                                foreach (var c in x.M!) s += c.Real * c.Real + c.Imaginary * c.Imaginary;
                                x = Val.R(Math.Sqrt(s));
                                break;
                            }
                            case "DOT":
                            {
                                if (!x.IsMat || !y.IsMat || x.M!.Length != y.M!.Length)
                                    throw new SimError("Dimension Error : DOT attend deux vecteurs de même taille");
                                Complex s = 0;
                                var xa = x.M.Cast<Complex>().ToArray();
                                var ya = y.M!.Cast<Complex>().ToArray();
                                for (int i = 0; i < xa.Length; i++) s += xa[i] * ya[i];
                                Drop2(Val.Num(s, false));
                                break;
                            }
                            case "CROSS":
                            {
                                if (!x.IsMat || !y.IsMat || x.M!.Length != 3 || y.M!.Length != 3)
                                    throw new SimError("Dimension Error : CROSS attend deux vecteurs à 3 éléments");
                                var a = y.M.Cast<Complex>().ToArray();
                                var b = x.M.Cast<Complex>().ToArray();
                                var res = new Complex[y.Rows, y.Cols];
                                var cr = new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
                                int k2 = 0;
                                for (int i = 0; i < y.Rows; i++) for (int j = 0; j < y.Cols; j++) res[i, j] = cr[k2++];
                                Drop2(Val.Mat(res));
                                break;
                            }
                            default:
                                Note($"({w[0]} n'est pas simulé : vérifiez ce passage sur la calculatrice)");
                                break;
                        }
                        break;
                    }
                }
                if (x.IsReal && double.IsInfinity(x.Re)) throw new SimError("Out of Range : dépassement de capacité");
            }
        }

        // ---- SOLVE / INTEG : la fonction de l'utilisateur est exécutée comme un sous-programme (XEQ), jusqu'à son RTN
        double EvalFn(string pgm, string v, double at)
        {
            if (!labels.TryGetValue("\"" + pgm, out int target)) throw new SimError($"Label « {pgm} » introuvable");
            Set(v, Val.R(at));
            x = y = z = t = Val.R(at);
            int savedPc = pc, savedErr = errPc;
            calls.Push(FnReturn);
            r.MaxCallDepth = Math.Max(r.MaxCallDepth, calls.Count);
            pc = target;
            while (pc != FnReturn)
            {
                if (pc < 0 || pc >= code.Count) throw new SimError($"La fonction « {pgm} » se termine sans RTN");
                Step();
            }
            pc = savedPc;
            errPc = savedErr;
            if (!x.IsReal) throw new SimError($"Invalid Type : la fonction « {pgm} » doit laisser un nombre réel dans X");
            return x.Re;
        }

        // SOLVE "V" : estimations dans V et dans X, comme sur la calculatrice. Résultat : X = racine, Y = estimation
        // précédente, Z = f(racine), T = 0 racine, 1 changement de signe, 2 extremum, 3 mauvaises estimations,
        // 4 constante. Rend faux s'il n'y a pas de racine : le programme saute alors la ligne suivante.
        bool Solve(string v)
        {
            string pgm = slvPgm ?? throw new SimError("SOLVE sans PGMSLV : choisissez d'abord la fonction (PGMSLV \"NOM\")");
            double a = Has(v) && Get(v).IsReal ? Get(v).Re : 0, b = x.Real;
            if (a == b) b = a == 0 ? 1e-3 : a * (1 + 1e-3);
            double fa = EvalFn(pgm, v, a), fb = EvalFn(pgm, v, b);
            double fmax = Math.Max(Math.Abs(fa), Math.Abs(fb));
            int resCode;
            double root, prev, froot;
            if (double.IsNaN(fa) || double.IsNaN(fb)) { resCode = 3; root = b; prev = a; froot = fb; }
            else if (fa == 0) { resCode = 0; root = a; prev = b; froot = 0; }
            else if (fb == 0) { resCode = 0; root = b; prev = a; froot = 0; }
            else
            {
                resCode = -1;
                // sans encadrement : méthode de Muller (parabole par les 3 derniers points), qui va vers une racine
                // ou, faute de racine, vers l'extremum ; s'arrête au premier changement de signe
                bool hasP = false;
                double p0 = 0, fp0 = 0;
                double bx = Math.Abs(fa) < Math.Abs(fb) ? a : b, bf = Math.Abs(fa) < Math.Abs(fb) ? fa : fb;   // meilleur point
                for (int it = 0; it < 200 && Math.Sign(fa) == Math.Sign(fb); it++)
                {
                    double c = double.NaN;
                    if (hasP && p0 != a && a != b)
                    {
                        double h1 = a - p0, h2 = b - a, d1 = (fa - fp0) / h1, d2 = (fb - fa) / h2;
                        double qa = (d2 - d1) / (h2 + h1), qb = d2 + qa * h2;
                        if (qa != 0)
                        {
                            double disc = qb * qb - 4 * qa * fb;
                            if (disc >= 0)
                            {
                                double den = qb + (qb < 0 ? -1 : 1) * Math.Sqrt(disc);
                                if (den != 0) c = b - 2 * fb / den;
                            }
                            else c = b - qb / (2 * qa);   // la parabole ne coupe pas l'axe : son sommet
                        }
                    }
                    if (double.IsNaN(c)) c = fb == fa ? b + 2 * (b - a) : b - fb * (b - a) / (fb - fa);
                    double lim = 100 * Math.Abs(b - a) + 1e-300;   // au plus 100 fois l'écart précédent
                    if (Math.Abs(c - b) > lim) c = b + Math.Sign(c - b) * lim;
                    if (double.IsNaN(c) || double.IsInfinity(c)) { resCode = 3; break; }
                    if (Math.Abs(c - b) <= 1e-14 * Math.Max(1, Math.Abs(b)))
                    {
                        resCode = fa == fb ? 4 : 2;   // plus de progrès sans changement de signe : extremum (ou constante)
                        break;
                    }
                    double fc = EvalFn(pgm, v, c);
                    fmax = Math.Max(fmax, Math.Abs(fc));
                    p0 = a; fp0 = fa; hasP = true;
                    a = b; fa = fb; b = c; fb = fc;
                    if (Math.Abs(fc) < Math.Abs(bf)) { bx = c; bf = fc; }
                    if (fc == 0) { resCode = 0; break; }
                }
                if (resCode == 0) { root = b; prev = a; froot = 0; }
                else if (resCode == -1 && Math.Sign(fa) != Math.Sign(fb))
                {
                    // encadrement : méthode de Brent
                    double c = a, fc = fa, d = b - a, e = d;
                    for (int it = 0; it < 300; it++)
                    {
                        if (Math.Sign(fb) == Math.Sign(fc)) { c = a; fc = fa; d = e = b - a; }
                        if (Math.Abs(fc) < Math.Abs(fb)) { a = b; b = c; c = a; fa = fb; fb = fc; fc = fa; }
                        double tol = 2 * 1e-16 * Math.Abs(b) + 1e-300, m = 0.5 * (c - b);
                        if (Math.Abs(m) <= tol || fb == 0) break;
                        if (Math.Abs(e) >= tol && Math.Abs(fa) > Math.Abs(fb))
                        {
                            double s = fb / fa, p, q;
                            if (a == c) { p = 2 * m * s; q = 1 - s; }
                            else
                            {
                                double q0 = fa / fc, r0 = fb / fc;
                                p = s * (2 * m * q0 * (q0 - r0) - (b - a) * (r0 - 1));
                                q = (q0 - 1) * (r0 - 1) * (s - 1);
                            }
                            if (p > 0) q = -q; else p = -p;
                            if (2 * p < Math.Min(3 * m * q - Math.Abs(tol * q), Math.Abs(e * q))) { e = d; d = p / q; }
                            else { d = m; e = m; }
                        }
                        else { d = m; e = m; }
                        a = b; fa = fb;
                        b += Math.Abs(d) > tol ? d : (m > 0 ? tol : -tol);
                        fb = EvalFn(pgm, v, b);
                        fmax = Math.Max(fmax, Math.Abs(fb));
                    }
                    // racine si f s'annule à la précision du calcul, sinon changement de signe (discontinuité)
                    resCode = fb == 0 || Math.Abs(fb) <= 1e-9 * fmax ? 0 : 1;
                    root = b; prev = c; froot = fb;
                }
                else
                {
                    if (resCode == -1) resCode = 2;
                    root = bx; prev = bx == b ? a : b; froot = bf;
                }
            }
            Set(v, Val.R(root));
            (x, y, z, t) = (Val.R(root), Val.R(prev), Val.R(froot), Val.R(resCode));
            if (resCode >= 2)
                r.Display.Add(Tr.T($"(SOLVE {v} : {(resCode switch { 2 => "Extremum", 3 => "Bad Guess(es)", _ => "Constant?" })}, la ligne suivante est sautée)"));
            return resCode <= 1;
        }

        // INTEG "V" : de LLIM à ULIM, précision relative ACC. Résultat : X = intégrale, Y = incertitude.
        // Gauss-Kronrod 7-15 adaptatif (la calculatrice utilise Romberg) ; les bornes ne sont pas évaluées.
        void Integ(string v)
        {
            string pgm = intPgm ?? throw new SimError("INTEG sans PGMINT : choisissez d'abord la fonction (PGMINT \"NOM\")");
            double lo = Get("LLIM").Real, hi = Get("ULIM").Real;
            double acc = Has("ACC") ? Math.Abs(Get("ACC").Real) : 1e-6;
            if (!Has("ACC")) Note(Tr.T("(INTEG : variable ACC absente, précision 1ᴇ-6)"));
            var (sx0, sy0, sz0, st0) = (x, y, z, t);
            double total = 0, err = 0;
            if (lo != hi)
            {
                int evals = 0;
                (double I, double E, double A) GK(double a, double b)
                {
                    double c = (a + b) / 2, h = (b - a) / 2;
                    double fc = EvalFn(pgm, v, c);
                    double k = fc * GkW[7], g = fc * GW[3], ab = Math.Abs(fc) * GkW[7];
                    for (int j = 0; j < 7; j++)
                    {
                        double f1 = EvalFn(pgm, v, c - h * GkX[j]), f2 = EvalFn(pgm, v, c + h * GkX[j]);
                        k += GkW[j] * (f1 + f2);
                        ab += GkW[j] * (Math.Abs(f1) + Math.Abs(f2));
                        if (j % 2 == 1) g += GW[j / 2] * (f1 + f2);
                    }
                    evals += 15;
                    return (k * h, Math.Abs((k - g) * h), ab * Math.Abs(h));
                }
                var whole = GK(lo, hi);
                double scale = Math.Max(Math.Abs(whole.I), 1e-3 * whole.A);
                double tolAll = Math.Max(acc * scale, 1e-15 * whole.A);
                var work = new Stack<(double a, double b, int depth, double I, double E)>();
                work.Push((lo, hi, 0, whole.I, whole.E));
                while (work.Count > 0)
                {
                    var (a, b, depth, I, E) = work.Pop();
                    if (E <= tolAll * Math.Abs((b - a) / (hi - lo)) || depth >= 40 || evals > 60_000)
                    {
                        total += I; err += E;
                        continue;
                    }
                    double mid = (a + b) / 2;
                    var left = GK(a, mid);
                    var right = GK(mid, b);
                    work.Push((mid, b, depth + 1, right.I, right.E));
                    work.Push((a, mid, depth + 1, left.I, left.E));
                }
                if (evals > 60_000) Note(Tr.T("(INTEG : trop d'évaluations, précision non atteinte)"));
            }
            (x, y, z, t) = (sx0, sy0, sz0, st0);
            Lift(Val.R(err));
            Lift(Val.R(total));
        }

        Val Arith(string op, Val a, Val b)   // a op b  (a = Y, b = X)
        {
            if (a.IsMat || b.IsMat) return MatArith(op, a, b);
            bool cx = a.IsCx || b.IsCx;
            if (!cx)
            {
                double u = a.Re, v = b.Re;
                return op switch
                {
                    "+" => Val.R(u + v),
                    "-" => Val.R(u - v),
                    "×" => Val.R(u * v),
                    "÷" => v == 0 ? throw new SimError("Divide by 0 : division par zéro") : Val.R(u / v),
                    "Y↑X" => RealPow(u, v),
                    "MOD" => Val.R(v == 0 ? u : ((u % v) + v) % v),
                    _ => throw new SimError("Opération inconnue " + op)
                };
            }
            Complex p = a.C, q = b.C;
            return op switch
            {
                "+" => Val.Cx(p + q),
                "-" => Val.Cx(p - q),
                "×" => Val.Cx(p * q),
                "÷" => q == Complex.Zero ? throw new SimError("Divide by 0 : division par zéro") : Val.Cx(p / q),
                "Y↑X" => Val.Cx(Complex.Pow(p, q)),
                _ => throw new SimError($"Invalid Type : {op} sur des complexes")
            };
        }

        Val RealPow(double u, double v)
        {
            if (u == 0 && v < 0) throw new SimError("Invalid Data : 0 à une puissance négative");
            if (u < 0 && v != Math.Floor(v))
            {
                if (cpxres) return Val.Cx(Complex.Pow(u, v));
                throw new SimError("Invalid Data : puissance non entière d'un négatif (import cmath pour un résultat complexe)");
            }
            return Val.R(Math.Pow(u, v));
        }
    }

    static readonly string[] StoOps = { "+", "-", "×", "÷" };

    static Val MapM(Val m, Func<Complex, Complex> f)
    {
        var s = new Complex[m.Rows, m.Cols];
        for (int i = 0; i < m.Rows; i++) for (int j = 0; j < m.Cols; j++) s[i, j] = f(m.M![i, j]);
        return Val.Mat(s);
    }

    static Val MatArith(string op, Val a, Val b)
    {
        if (a.IsMat && b.IsMat)
        {
            switch (op)
            {
                case "+": case "-":
                {
                    if (a.Rows != b.Rows || a.Cols != b.Cols)
                        throw new SimError($"Dimension Error : {a.Rows}×{a.Cols} {op} {b.Rows}×{b.Cols}");
                    var s = new Complex[a.Rows, a.Cols];
                    for (int i = 0; i < a.Rows; i++) for (int j = 0; j < a.Cols; j++)
                        s[i, j] = op == "+" ? a.M![i, j] + b.M![i, j] : a.M![i, j] - b.M![i, j];
                    return Val.Mat(s);
                }
                case "×":
                {
                    if (a.Cols != b.Rows) throw new SimError($"Dimension Error : produit {a.Rows}×{a.Cols} × {b.Rows}×{b.Cols}");
                    var s = new Complex[a.Rows, b.Cols];
                    for (int i = 0; i < a.Rows; i++)
                        for (int j = 0; j < b.Cols; j++)
                        {
                            Complex sum = 0;
                            for (int k = 0; k < a.Cols; k++) sum += a.M![i, k] * b.M![k, j];
                            s[i, j] = sum;
                        }
                    return Val.Mat(s);
                }
                case "÷":
                    return MatArith("×", a, Val.Mat(Inverse(b.M!)));
            }
            throw new SimError($"Invalid Type : {op} entre deux matrices");
        }
        if (a.IsMat)
        {
            var q = b.C;
            return op switch
            {
                "+" => MapM(a, c => c + q),
                "-" => MapM(a, c => c - q),
                "×" => MapM(a, c => c * q),
                "÷" => q == Complex.Zero ? throw new SimError("Divide by 0 : division par zéro") : MapM(a, c => c / q),
                _ => throw new SimError($"Invalid Type : {op} avec une matrice")
            };
        }
        var p = a.C;
        return op switch
        {
            "+" => MapM(b, c => p + c),
            "-" => MapM(b, c => p - c),
            "×" => MapM(b, c => p * c),
            "÷" => MatArith("×", Val.Mat(Inverse(b.M!)), Val.Cx(p)),
            _ => throw new SimError($"Invalid Type : {op} avec une matrice")
        };
    }

    static Complex[,] Inverse(Complex[,] m)
    {
        int n = m.GetLength(0);
        if (n != m.GetLength(1)) throw new SimError("Dimension Error : seule une matrice carrée est inversible");
        var a = (Complex[,])m.Clone();
        var inv = new Complex[n, n];
        for (int i = 0; i < n; i++) inv[i, i] = 1;
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++) if (a[r, c].Magnitude > a[piv, c].Magnitude) piv = r;
            if (a[piv, c].Magnitude < 1e-300) throw new SimError("Singular Matrix : matrice non inversible");
            if (piv != c)
                for (int k = 0; k < n; k++)
                {
                    (a[c, k], a[piv, k]) = (a[piv, k], a[c, k]);
                    (inv[c, k], inv[piv, k]) = (inv[piv, k], inv[c, k]);
                }
            var d = a[c, c];
            for (int k = 0; k < n; k++) { a[c, k] /= d; inv[c, k] /= d; }
            for (int r = 0; r < n; r++)
            {
                if (r == c) continue;
                var f = a[r, c];
                if (f == Complex.Zero) continue;
                for (int k = 0; k < n; k++) { a[r, k] -= f * a[c, k]; inv[r, k] -= f * inv[c, k]; }
            }
        }
        return inv;
    }

    static Complex Det(Complex[,] m)
    {
        int n = m.GetLength(0);
        if (n != m.GetLength(1)) throw new SimError("Dimension Error : DET demande une matrice carrée");
        var a = (Complex[,])m.Clone();
        Complex det = 1;
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++) if (a[r, c].Magnitude > a[piv, c].Magnitude) piv = r;
            if (a[piv, c].Magnitude < 1e-300) return 0;
            if (piv != c) { for (int k = 0; k < n; k++) (a[c, k], a[piv, k]) = (a[piv, k], a[c, k]); det = -det; }
            det *= a[c, c];
            for (int r = c + 1; r < n; r++)
            {
                var f = a[r, c] / a[c, c];
                for (int k = c; k < n; k++) a[r, k] -= f * a[c, k];
            }
        }
        return det;
    }

    static double ToHr(double hms)
    {
        double s = Math.Sign(hms), v = Math.Abs(hms);
        double h = Math.Truncate(v), m = Math.Truncate((v - h) * 100 + 1e-9);
        double sec = ((v - h) * 100 - m) * 100;
        return s * (h + m / 60 + sec / 3600);
    }

    static double ToHms(double hr)
    {
        double s = Math.Sign(hr), v = Math.Abs(hr);
        double h = Math.Truncate(v), mm = (v - h) * 60, m = Math.Truncate(mm + 1e-9), sec = (mm - m) * 60;
        return s * (h + m / 100 + sec / 10000);
    }

    static double Fact(double x)
    {
        if (x < 0 || x != Math.Floor(x)) throw new SimError("Invalid Type : N! demande un entier positif");
        double f = 1;
        for (int i = 2; i <= x; i++) f *= i;
        return f;
    }

    static double Gamma(double x)
    {
        if (x <= 0 && x == Math.Floor(x)) throw new SimError("Invalid Data : GAMMA d'un entier ≤ 0");
        if (x < 0.5) return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1 - x));
        x -= 1;
        double[] g = { 0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
        double a = g[0], tt = x + 7.5;
        for (int i = 1; i < 9; i++) a += g[i] / (x + i);
        return Math.Sqrt(2 * Math.PI) * Math.Pow(tt, x + 0.5) * Math.Exp(-tt) * a;
    }

    static bool Test(string t, Val x, Val y)
    {
        switch (t)
        {
            case "X=0?": return !x.IsMat && x.C == Complex.Zero;
            case "X≠0?": return x.IsMat || x.C != Complex.Zero;
            case "X=Y?": return Same(x, y);
            case "X≠Y?": return !Same(x, y);
        }
        double xv = x.Real;
        double yv = t.EndsWith("Y?") ? y.Real : 0;
        return t switch
        {
            "X<0?" => xv < 0, "X>0?" => xv > 0, "X≤0?" => xv <= 0, "X≥0?" => xv >= 0,
            "X<Y?" => xv < yv, "X>Y?" => xv > yv, "X≤Y?" => xv <= yv, "X≥Y?" => xv >= yv,
            _ => throw new SimError("Test inconnu " + t)
        };
    }

    static bool Same(Val a, Val b)
    {
        if (a.IsStr || b.IsStr) return a.IsStr && b.IsStr && a.S == b.S;
        if (a.IsMat != b.IsMat) return false;
        if (!a.IsMat) return a.C == b.C;
        if (a.Rows != b.Rows || a.Cols != b.Cols) return false;
        for (int i = 0; i < a.Rows; i++) for (int j = 0; j < a.Cols; j++) if (a.M![i, j] != b.M![i, j]) return false;
        return true;
    }
}
