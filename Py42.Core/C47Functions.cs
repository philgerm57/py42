namespace Py42.Core;

/// <summary>
/// Cible C47 / R47 : fonctions Python (math, scipy.special, statistics, sympy, scipy.constants) et l'instruction C47
/// qui les calcule. Les arguments sont empilés dans l'ordre (le dernier dans X), comme pour la HP-42S.
/// Domaine = catégorie de l'aide C47 (C47.Core.Items.Categories) : l'aide de la cible C47 les range ainsi.
/// Chaque entrée est vérifiée sur le simulateur C47 contre une valeur de référence (Python ou valeur publiée).
/// </summary>
public static class C47Functions
{
    /// <param name="Swap">la C47 attend les deux arguments dans l'ordre inverse de Python (vérifié sur le simulateur)</param>
    /// <param name="Angle">un argument est un angle : RAD en tête du programme, Python travaillant en radians</param>
    public sealed record Fn(string Python, int Args, string Rpn, string Domain, string Fr, string En, bool Swap = false, bool Angle = false);

    const string Math = "Fonctions mathématiques", Trig = "Trigonométrie et angles", Special = "Fonctions spéciales",
                 Stats = "Statistiques et probabilités", Ints = "Entiers, bases et bits", Consts = "Constantes";

    public static readonly Fn[] All =
    {
        // math
        new("sqrt", 1, "√x", Math, "racine carrée", "square root"),
        new("cbrt", 1, "∛x", Math, "racine cubique", "cube root"),
        new("exp", 1, "ⅇˣ", Math, "exponentielle", "exponential"),
        new("exp2", 1, "2ˣ", Math, "2 puissance x", "2 to the power x"),
        new("expm1", 1, "ⅇˣ-1", Math, "ⅇˣ - 1, précis près de 0", "ⅇˣ - 1, accurate near 0"),
        new("log", 1, "LN", Math, "logarithme népérien", "natural logarithm"),
        new("log", 2, "LOGₓy", Math, "log(x, b) : logarithme de x en base b", "log(x, b): logarithm of x to base b"),
        new("log2", 1, "LB", Math, "logarithme en base 2", "base-2 logarithm"),
        new("log10", 1, "LOG", Math, "logarithme décimal", "common logarithm"),
        new("log1p", 1, "LN(1+x)", Math, "ln(1 + x), précis près de 0", "ln(1 + x), accurate near 0"),
        new("pow", 2, "yˣ", Math, "x puissance y", "x to the power y"),
        new("fabs", 1, "|x|", Math, "valeur absolue", "absolute value"),
        new("floor", 1, "floor", Math, "entier inférieur", "floor"),
        new("ceil", 1, "ceil", Math, "entier supérieur", "ceiling"),
        new("trunc", 1, "IP", Math, "partie entière (vers zéro)", "integer part (toward zero)"),
        new("fmod", 2, "RMD", Math, "fmod(x, y) : reste de la division tronquée (signe de x)", "fmod(x, y): remainder of truncated division (sign of x)"),
        new("sign", 1, "sign", Math, "numpy.sign : signe (-1, 0 ou 1)", "numpy.sign: sign (-1, 0 or 1)"),
        // trigonométrie
        new("atan2", 2, "ATAN2", Trig, "atan2(y, x) : angle de (x, y)", "atan2(y, x): angle of (x, y)"),
        new("sinh", 1, "sinh", Trig, "sinus hyperbolique", "hyperbolic sine"),
        new("cosh", 1, "cosh", Trig, "cosinus hyperbolique", "hyperbolic cosine"),
        new("tanh", 1, "tanh", Trig, "tangente hyperbolique", "hyperbolic tangent"),
        new("asinh", 1, "arsinh", Trig, "argument sinus hyperbolique", "inverse hyperbolic sine"),
        new("acosh", 1, "arcosh", Trig, "argument cosinus hyperbolique", "inverse hyperbolic cosine"),
        new("atanh", 1, "artanh", Trig, "argument tangente hyperbolique", "inverse hyperbolic tangent"),
        new("sinc", 1, "sincπ", Trig, "numpy.sinc : sin(πx) / (πx)", "numpy.sinc: sin(πx) / (πx)"),
        // entiers
        new("factorial", 1, "x!", Ints, "factorielle (exacte en entier long)", "factorial (exact as long integer)"),
        new("gcd", 2, "GCD", Ints, "plus grand commun diviseur", "greatest common divisor"),
        new("lcm", 2, "LCM", Ints, "plus petit commun multiple", "least common multiple"),
        new("comb", 2, "COMB", Ints, "combinaisons de k parmi n", "combinations of k out of n"),
        new("perm", 2, "PERM", Ints, "arrangements de k parmi n", "permutations of k out of n"),
        new("nextprime", 1, "NEXTP", Ints, "sympy.nextprime : nombre premier suivant", "sympy.nextprime: next prime"),
        new("fibonacci", 1, "FIB", Ints, "sympy.fibonacci : n-ième nombre de Fibonacci", "sympy.fibonacci: n-th Fibonacci number"),
        // fonctions spéciales (math, scipy.special)
        new("gamma", 1, "Γ(x)", Special, "fonction gamma", "gamma function"),
        new("lgamma", 1, "LNΓ", Special, "logarithme de gamma", "log of the gamma function"),
        new("gammaln", 1, "LNΓ", Special, "scipy : logarithme de gamma", "scipy: log of the gamma function"),
        new("erf", 1, "erf", Special, "fonction d'erreur", "error function"),
        new("erfc", 1, "erfc", Special, "fonction d'erreur complémentaire", "complementary error function"),
        new("beta", 2, "β(x,y)", Special, "fonction bêta d'Euler", "Euler's beta function"),
        new("betaln", 2, "LNβ", Special, "logarithme de la fonction bêta", "log of the beta function"),
        new("zeta", 1, "ζ(x)", Special, "fonction zêta de Riemann", "Riemann zeta function"),
        new("jv", 2, "J_y(x)", Special, "jv(v, x) : Bessel de 1re espèce d'ordre v", "jv(v, x): Bessel function of the 1st kind, order v"),
        new("yv", 2, "Y_y(x)", Special, "yv(v, x) : Bessel de 2e espèce d'ordre v", "yv(v, x): Bessel function of the 2nd kind, order v"),
        new("lambertw", 1, "Wₚ", Special, "W de Lambert, branche principale", "Lambert W, principal branch"),
        new("ellipk", 1, "K(m)", Special, "intégrale elliptique complète de 1re espèce", "complete elliptic integral of the 1st kind"),
        new("ellipe", 1, "E(m)", Special, "intégrale elliptique complète de 2e espèce", "complete elliptic integral of the 2nd kind"),
        new("ellipkinc", 2, "F(ϕ,m)", Special, "ellipkinc(ϕ, m) : intégrale elliptique incomplète de 1re espèce", "ellipkinc(ϕ, m): incomplete elliptic integral of the 1st kind", Swap: true, Angle: true),
        new("ellipeinc", 2, "E(ϕ,m)", Special, "ellipeinc(ϕ, m) : intégrale elliptique incomplète de 2e espèce", "ellipeinc(ϕ, m): incomplete elliptic integral of the 2nd kind", Swap: true, Angle: true),
        new("gammainc", 2, "IΓₚ", Special, "gammainc(a, x) : gamma incomplète régularisée inférieure P", "gammainc(a, x): regularised lower incomplete gamma P", Swap: true),
        new("gammaincc", 2, "IΓ_q", Special, "gammaincc(a, x) : gamma incomplète régularisée supérieure Q", "gammaincc(a, x): regularised upper incomplete gamma Q", Swap: true),
        new("eval_legendre", 2, "Pₙ", Special, "eval_legendre(n, x) : polynôme de Legendre", "eval_legendre(n, x): Legendre polynomial"),
        new("eval_chebyt", 2, "Tₙ", Special, "eval_chebyt(n, x) : Tchebychev de 1re espèce", "eval_chebyt(n, x): Chebyshev, 1st kind"),
        new("eval_chebyu", 2, "Uₙ", Special, "eval_chebyu(n, x) : Tchebychev de 2e espèce", "eval_chebyu(n, x): Chebyshev, 2nd kind"),
        new("eval_hermite", 2, "H_nP", Special, "eval_hermite(n, x) : Hermite (physique)", "eval_hermite(n, x): Hermite (physics)"),
        new("eval_hermitenorm", 2, "Hₙ", Special, "eval_hermitenorm(n, x) : Hermite (probabilités)", "eval_hermitenorm(n, x): Hermite (probability)"),
        new("eval_laguerre", 2, "Lₘ", Special, "eval_laguerre(n, x) : polynôme de Laguerre", "eval_laguerre(n, x): Laguerre polynomial"),
    };

    /// <summary>statistics.mean(liste)… : les éléments passent par Σ+ (CLΣ d'abord), puis l'instruction donne le résultat dans X.</summary>
    public static readonly Fn[] Statistics =
    {
        new("mean", 1, "x̄", Stats, "moyenne", "mean"),
        new("fmean", 1, "x̄", Stats, "moyenne", "mean"),
        new("median", 1, "x_MEDN", Stats, "médiane", "median"),
        new("stdev", 1, "s", Stats, "écart type de l'échantillon", "sample standard deviation"),
        new("pstdev", 1, "σ", Stats, "écart type de la population", "population standard deviation"),
    };

    /// <summary>scipy.constants.nom (sans parenthèses) : constante du catalogue de la C47.</summary>
    public static readonly Fn[] Constants =
    {
        new("c", 0, "c", Consts, "vitesse de la lumière", "speed of light"),
        new("h", 0, "ℎ", Consts, "constante de Planck", "Planck constant"),
        new("hbar", 0, "ℏ", Consts, "constante de Planck réduite", "reduced Planck constant"),
        new("k", 0, "k_B", Consts, "constante de Boltzmann", "Boltzmann constant"),
        new("N_A", 0, "N_A", Consts, "nombre d'Avogadro", "Avogadro constant"),
        new("G", 0, "G", Consts, "constante de gravitation", "gravitational constant"),
        new("g", 0, "g⊕", Consts, "pesanteur standard", "standard gravity"),
        new("e", 0, "e", Consts, "charge élémentaire", "elementary charge"),
        new("m_e", 0, "mₑ", Consts, "masse de l'électron", "electron mass"),
        new("m_p", 0, "mₚ", Consts, "masse du proton", "proton mass"),
        new("m_n", 0, "mₙ", Consts, "masse du neutron", "neutron mass"),
        new("u", 0, "mᵤ", Consts, "unité de masse atomique", "atomic mass unit"),
        new("epsilon_0", 0, "ε₀", Consts, "permittivité du vide", "vacuum permittivity"),
        new("mu_0", 0, "μ₀", Consts, "perméabilité du vide", "vacuum permeability"),
        new("R", 0, "R", Consts, "constante des gaz parfaits", "molar gas constant"),
        new("sigma", 0, "σ_B", Consts, "constante de Stefan-Boltzmann", "Stefan-Boltzmann constant"),
        new("Rydberg", 0, "R_∞", Consts, "constante de Rydberg", "Rydberg constant"),
        new("alpha", 0, "α_F", Consts, "constante de structure fine", "fine-structure constant"),
        new("golden", 0, "ϕ", Consts, "nombre d'or", "golden ratio"),
        new("atm", 0, "p₀", Consts, "atmosphère normale (Pa)", "standard atmosphere (Pa)"),
        new("zero_Celsius", 0, "T₀", Consts, "zéro Celsius en kelvins", "zero Celsius in kelvins"),
    };

    /// <summary>
    /// Loi de scipy.stats : les paramètres (après x) vont dans les registres lettrés lus par la C47 (table de wp34s.c),
    /// puis x, puis l'instruction de la méthode. Params : registre par paramètre positionnel de scipy ;
    /// "" = loc, qui doit valoir 0 ; "R/" = rangé en 1/valeur (scale de scipy, taux λ de la C47).
    /// "P~" = rangé en 1 - valeur (binomiale négative : la C47 attend la probabilité d'échec).
    /// Defaults : valeurs des paramètres absents. Pdf / Cdf / Sf / Ppf : pdf ou pmf, cdf, sf (1 - cdf), ppf (quantile).
    /// Discrete : sf de scipy = P(X > k), la C47 donne P(X ≥ k), d'où k + 1.
    /// Shift : décalage de x (géométrique : scipy compte les essais à partir de 1, la C47 les échecs à partir de 0).
    /// Conventions vérifiées sur le simulateur C47.
    /// </summary>
    public sealed record Dist(string Python, string[] Params, double[] Defaults, string Pdf, string Cdf, string Sf, string Ppf, string Fr, string En,
                              bool Discrete = false, int Shift = 0);

    public static readonly Dist[] Distributions =
    {
        new("norm", new[] { "M", "S" }, new[] { 0.0, 1.0 }, "Normlₚ", "Norml◭", "Norml◮", "Norml⁻¹", "loi normale (loc = μ, scale = σ)", "normal distribution (loc = μ, scale = σ)"),
        new("cauchy", new[] { "M", "S" }, new[] { 0.0, 1.0 }, "Cauchₚ", "Cauch◭", "Cauch◮", "Cauch⁻¹", "loi de Cauchy (loc, scale)", "Cauchy distribution (loc, scale)"),
        new("logistic", new[] { "M", "S" }, new[] { 0.0, 1.0 }, "Logisₚ", "Logis◭", "Logis◮", "Logis⁻¹", "loi logistique (loc, scale)", "logistic distribution (loc, scale)"),
        new("expon", new[] { "", "R/" }, new[] { 0.0, 1.0 }, "Exponₚ", "Expon◭", "Expon◮", "Expon⁻¹", "loi exponentielle (loc = 0, scale = 1/λ)", "exponential distribution (loc = 0, scale = 1/λ)"),
        new("weibull_min", new[] { "Q", "", "S" }, new[] { 1.0, 0.0, 1.0 }, "Weiblₚ", "Weibl◭", "Weibl◮", "Weibl⁻¹", "loi de Weibull (c = forme, loc = 0, scale)", "Weibull distribution (c = shape, loc = 0, scale)"),
        new("t", new[] { "M" }, new[] { 1.0 }, "tₚ(x)", "t◭(x)", "t◮(x)", "t⁻¹(p)", "loi de Student (df)", "Student's t distribution (df)"),
        new("chi2", new[] { "M" }, new[] { 1.0 }, "χ²_p(x)", "χ²◭(x)", "χ²◮(x)", "(χ²)⁻¹", "loi du χ² (df)", "chi-squared distribution (df)"),
        new("f", new[] { "M", "N" }, new[] { 1.0, 1.0 }, "Fₚ(x)", "F◭(x)", "F◮(x)", "F⁻¹(p)", "loi F de Fisher (dfn, dfd)", "Fisher's F distribution (dfn, dfd)"),
        new("binom", new[] { "N", "P" }, new[] { 1.0, 0.5 }, "Binomₚ", "Binom◭", "Binom◮", "Binom⁻¹", "loi binomiale (n, p)", "binomial distribution (n, p)", Discrete: true),
        new("poisson", new[] { "R" }, new[] { 1.0 }, "Poissₚ", "Poiss◭", "Poiss◮", "Poiss⁻¹", "loi de Poisson (mu)", "Poisson distribution (mu)", Discrete: true),
        new("geom", new[] { "P" }, new[] { 0.5 }, "Geomₚ", "Geom◭", "Geom◮", "Geom⁻¹", "loi géométrique (p, essais à partir de 1)", "geometric distribution (p, trials from 1)", Discrete: true, Shift: -1),
        new("nbinom", new[] { "N", "P~" }, new[] { 1.0, 0.5 }, "NBinₚ", "NBin◭", "NBin◮", "NBin⁻¹", "loi binomiale négative (n, p)", "negative binomial distribution (n, p)", Discrete: true),
    };

    /// <summary>Loi normale centrée réduite : norm.pdf(x) sans loc ni scale.</summary>
    public static readonly Dist StdNormal = new("norm", Array.Empty<string>(), Array.Empty<double>(), "ϕₚ", "Φ◭", "Φ◮", "Φ⁻¹", "", "");

    public static Dist? FindDistribution(string python) => Distributions.FirstOrDefault(d => d.Python == python);

    /// <summary>Instruction C47 d'une méthode scipy.stats, ou null.</summary>
    public static string? Method(Dist d, string method) => method switch
    {
        "pdf" or "pmf" => d.Pdf, "cdf" => d.Cdf, "sf" => d.Sf, "ppf" => d.Ppf, _ => null
    };

    public static Fn? Find(string python, int args) => All.FirstOrDefault(f => f.Python == python && f.Args == args);
    public static Fn? FindStatistics(string python) => Statistics.FirstOrDefault(f => f.Python == python);
    public static Fn? FindConstant(string python) => Constants.FirstOrDefault(f => f.Python == python);
}
