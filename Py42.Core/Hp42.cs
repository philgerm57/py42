namespace Py42.Core;

/// <summary>Argument immédiat (écrit dans l'instruction, pas sur la pile).</summary>
public enum Imm
{
    None,
    Digits, // FIX/SCI/ENG : 0 à 11 → « FIX 02 », « FIX 10 »
    Tone,   // TONE : 0 à 9 → « TONE 5 »
    Flag,   // SF/CF/ΣREG : 0 à 99 → « SF 21 »
    Size,   // SIZE : 1 à 9999 → « SIZE 0025 »
    Reg     // RCL/STO : registre numéroté 0 à 99 → « RCL 12 »
}

/// <summary>
/// Commande HP-42S appelable depuis Python, ex. SIN(x), FIX(2), BEEP(), PIXEL(ligne, colonne).
/// Args : nombre de valeurs prises sur la pile, dans l'ordre (la dernière va dans X).
/// Results : nombre de valeurs utiles laissées sur la pile (0, 1, ou 2 : a, b = MEAN()).
/// </summary>
public sealed record Hp42Cmd(string Rpn, int Args, int Results, Imm Imm, string Help)
{
    public bool Returns => Results > 0;
}

/// <summary>
/// Table des commandes HP-42S, reprise de la liste de pyrpn (https://pyrpn.atug.com/cmds) avec les mêmes
/// noms et alias. Les noms sont insensibles à la casse ; les noms Python en minuscules de math (sin, sqrt...)
/// restent prioritaires et travaillent en radians.
/// </summary>
public static class Hp42
{
    static Hp42Cmd S(string rpn, string help) => new(rpn, 0, 0, Imm.None, help);
    static Hp42Cmd I(string rpn, Imm imm, string help) => new(rpn, 0, 0, imm, help);
    static Hp42Cmd P(string rpn, int args, string help) => new(rpn, args, 0, Imm.None, help);
    static Hp42Cmd F(string rpn, int args, string help) => new(rpn, args, 1, Imm.None, help);
    static Hp42Cmd F2(string rpn, int args, string help) => new(rpn, args, 2, Imm.None, help);

    public static readonly Dictionary<string, Hp42Cmd> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---- Fonctions d'une valeur (mode d'angle de la calculatrice pour SIN, COS...)
        ["SIN"] = F("SIN", 1, "SIN(x) : sinus (mode d'angle courant)"),
        ["COS"] = F("COS", 1, "COS(x) : cosinus (mode d'angle courant)"),
        ["TAN"] = F("TAN", 1, "TAN(x) : tangente (mode d'angle courant)"),
        ["ASIN"] = F("ASIN", 1, "ASIN(x) : arc sinus"),
        ["ACOS"] = F("ACOS", 1, "ACOS(x) : arc cosinus"),
        ["ATAN"] = F("ATAN", 1, "ATAN(x) : arc tangente"),
        ["SINH"] = F("SINH", 1, "SINH(x) : sinus hyperbolique"),
        ["COSH"] = F("COSH", 1, "COSH(x) : cosinus hyperbolique"),
        ["TANH"] = F("TANH", 1, "TANH(x) : tangente hyperbolique"),
        ["ASINH"] = F("ASINH", 1, "ASINH(x) : argument sinus hyperbolique"),
        ["ACOSH"] = F("ACOSH", 1, "ACOSH(x) : argument cosinus hyperbolique"),
        ["ATANH"] = F("ATANH", 1, "ATANH(x) : argument tangente hyperbolique"),
        ["LN"] = F("LN", 1, "LN(x) : logarithme népérien"),
        ["LOG"] = F("LOG", 1, "LOG(x) : logarithme décimal"),
        ["LN1plus"] = F("LN1+X", 1, "LN1plus(x) : ln(1 + x) précis pour x petit (LN1+X)"),
        ["Eto"] = F("E↑X", 1, "Eto(x) : e puissance x (E↑X)"),
        ["EtoMinus1"] = F("E↑X-1", 1, "EtoMinus1(x) : e^x - 1 précis pour x petit (E↑X-1)"),
        ["CommonExp"] = F("10↑X", 1, "CommonExp(x) : 10 puissance x (10↑X)"),
        ["EXP10"] = F("10↑X", 1, "EXP10(x) : 10 puissance x (10↑X)"),
        ["SQRT"] = F("SQRT", 1, "SQRT(x) : racine carrée"),
        ["Reciprocal"] = F("1/X", 1, "Reciprocal(x) : inverse 1/x (1/X), inverse d'une matrice"),
        ["ABS"] = F("ABS", 1, "ABS(x) : valeur absolue, module d'un complexe"),
        ["IP"] = F("IP", 1, "IP(x) : partie entière"),
        ["FP"] = F("FP", 1, "FP(x) : partie fractionnaire"),
        ["RND"] = F("RND", 1, "RND(x) : arrondi au format d'affichage (FIX n)"),
        ["SIGN"] = F("SIGN", 1, "SIGN(x) : signe (-1, 0 ou 1)"),
        ["GAMMA"] = F("GAMMA", 1, "GAMMA(x) : fonction gamma"),
        ["Factorial"] = F("N!", 1, "Factorial(n) : factorielle (N!)"),
        ["toDEG"] = F("→DEG", 1, "toDEG(x) : radians → degrés (→DEG)"),
        ["toRAD"] = F("→RAD", 1, "toRAD(x) : degrés → radians (→RAD)"),
        ["toHMS"] = F("→HMS", 1, "toHMS(h) : heures décimales → H.MMSS (→HMS)"),
        ["toPOL"] = F2("→POL", 2, "theta, r = toPOL(y, x) : coordonnées polaires (→POL, angle selon DEG/RAD)"),
        ["toREC"] = F2("→REC", 2, "y, x = toREC(theta, r) : coordonnées rectangulaires (→REC)"),
        ["toHR"] = F("→HR", 1, "toHR(hms) : H.MMSS → heures décimales (→HR)"),
        ["toDEC"] = F("→DEC", 1, "toDEC(n) : octal → décimal (→DEC)"),
        ["toOCT"] = F("→OCT", 1, "toOCT(n) : décimal → octal (→OCT)"),

        // ---- Fonctions de deux valeurs (Y, X)
        ["Percent"] = F("%", 2, "Percent(x, p) : x × p / 100 (%)"),
        ["PercentCH"] = F("%CH", 2, "PercentCH(ancien, nouveau) : variation en % (%CH)"),
        ["COMB"] = F("COMB", 2, "COMB(n, k) : combinaisons"),
        ["PERM"] = F("PERM", 2, "PERM(n, k) : arrangements"),
        ["MOD"] = F("MOD", 2, "MOD(y, x) : reste de y / x"),
        ["HMSplus"] = F("HMS+", 2, "HMSplus(a, b) : somme en H.MMSS (HMS+)"),
        ["HMSminus"] = F("HMS-", 2, "HMSminus(a, b) : différence en H.MMSS (HMS-)"),
        ["HMSADD"] = F("HMS+", 2, "HMSADD(a, b) : somme en H.MMSS (HMS+)"),
        ["HMSSUB"] = F("HMS-", 2, "HMSSUB(a, b) : différence en H.MMSS (HMS-)"),
        ["ROTXY"] = F("ROTXY", 2, "ROTXY(y, n) : rotation de bits de y de n positions"),

        // ---- Bases (mode BASE)
        ["BASEplus"] = F("BASE+", 2, "BASEplus(y, x) : addition entière 36 bits (BASE+)"),
        ["BASEminus"] = F("BASE-", 2, "BASEminus(y, x) : soustraction entière 36 bits (BASE-)"),
        ["BASEtimes"] = F("BASE×", 2, "BASEtimes(y, x) : multiplication entière 36 bits (BASE×)"),
        ["BASEdivide"] = F("BASE÷", 2, "BASEdivide(y, x) : division entière 36 bits (BASE÷)"),
        ["BASEplusMinus"] = F("BASE±", 1, "BASEplusMinus(x) : opposé en complément à 2 (BASE±)"),
        ["BINM"] = S("BINM", "BINM() : mode binaire"),
        ["OCTM"] = S("OCTM", "OCTM() : mode octal"),
        ["DECM"] = S("DECM", "DECM() : mode décimal"),
        ["HEXM"] = S("HEXM", "HEXM() : mode hexadécimal"),

        // ---- Matrices
        ["NEWMAT"] = F("NEWMAT", 2, "NEWMAT(lignes, colonnes) : nouvelle matrice nulle"),
        ["RCL"] = new("RCL", 0, 1, Imm.Reg, "RCL(n) : contenu du registre n (0-99), ex. RCL(12) = Σx² après Σ+"),
        ["STO"] = new("STO", 1, 0, Imm.Reg, "STO(x, n) : range x dans le registre n (0-99)"),
        ["COMPLEX"] = F("COMPLEX", 2, "COMPLEX(re, im) : nombre ou matrice complexe ; re, im = COMPLEX(z) les sépare"),
        ["DET"] = F("DET", 1, "DET(m) : déterminant"),
        ["INVRT"] = F("INVRT", 1, "INVRT(m) : inverse"),
        ["TRANS"] = F("TRANS", 1, "TRANS(m) : transposée"),
        ["FNRM"] = F("FNRM", 1, "FNRM(m) : norme de Frobenius"),
        ["RNRM"] = F("RNRM", 1, "RNRM(m) : norme des lignes (max des sommes des |a|)"),
        ["RSUM"] = F("RSUM", 1, "RSUM(m) : sommes des lignes (vecteur colonne)"),
        ["UVEC"] = F("UVEC", 1, "UVEC(v) : vecteur unitaire"),
        ["DOT"] = F("DOT", 2, "DOT(u, v) : produit scalaire"),
        ["CROSS"] = F("CROSS", 2, "CROSS(u, v) : produit vectoriel"),
        ["whatDIM"] = F2("DIM?", 1, "l, c = whatDIM(m) : dimensions (DIM?)"),
        ["EDIT"] = P("EDIT", 1, "EDIT(m) : ouvre l'éditeur de matrice sur m"),

        // ---- Statistiques (Σ+ prend y puis x, comme sur la calculatrice)
        ["StatPlus"] = P("Σ+", 2, "StatPlus(y, x) : ajoute un point (Σ+)"),
        ["StatMinus"] = P("Σ-", 2, "StatMinus(y, x) : retire un point (Σ-)"),
        ["CLStat"] = S("CLΣ", "CLStat() : efface les statistiques (CLΣ)"),
        ["ALLStat"] = S("ALLΣ", "ALLStat() : statistiques complètes (ALLΣ)"),
        ["LINStat"] = S("LINΣ", "LINStat() : statistiques linéaires (LINΣ)"),
        ["StatREG"] = I("ΣREG", Imm.Flag, "StatREG(n) : premier registre statistique (ΣREG)"),
        ["StatWhichREG"] = F("ΣREG?", 0, "StatWhichREG() : premier registre statistique (ΣREG?)"),
        ["MEAN"] = F2("MEAN", 0, "my, mx = MEAN() : moyennes (Y = moyenne des y, X = moyenne des x)"),
        ["SDEV"] = F2("SDEV", 0, "sy, sx = SDEV() : écarts-types"),
        ["SUM"] = F2("SUM", 0, "sy, sx = SUM() : sommes des y et des x"),
        ["WMEAN"] = F("WMEAN", 0, "WMEAN() : moyenne des x pondérée par les y"),
        ["CORR"] = F("CORR", 0, "CORR() : coefficient de corrélation"),
        ["SLOPE"] = F("SLOPE", 0, "SLOPE() : pente du modèle"),
        ["YINT"] = F("YINT", 0, "YINT() : ordonnée à l'origine du modèle"),
        ["FCSTX"] = F("FCSTX", 1, "FCSTX(y) : x prévu pour y"),
        ["FCSTY"] = F("FCSTY", 1, "FCSTY(x) : y prévu pour x"),
        ["LINF"] = S("LINF", "LINF() : modèle linéaire"),
        ["LOGF"] = S("LOGF", "LOGF() : modèle logarithmique"),
        ["EXPF"] = S("EXPF", "EXPF() : modèle exponentiel"),
        ["PWRF"] = S("PWRF", "PWRF() : modèle puissance"),
        ["BEST"] = S("BEST", "BEST() : meilleur modèle"),

        // ---- Affichage et modes
        ["FIX"] = I("FIX", Imm.Digits, "FIX(n) : affichage avec n décimales (0 à 11)"),
        ["SCI"] = I("SCI", Imm.Digits, "SCI(n) : notation scientifique"),
        ["ENG"] = I("ENG", Imm.Digits, "ENG(n) : notation ingénieur"),
        ["ALL"] = S("ALL", "ALL() : affichage de tous les chiffres"),
        ["RAD"] = S("RAD", "RAD() : angles en radians"),
        ["DEG"] = S("DEG", "DEG() : angles en degrés"),
        ["GRAD"] = S("GRAD", "GRAD() : angles en grades"),
        ["RECT"] = S("RECT", "RECT() : complexes en rectangulaire"),
        ["POLAR"] = S("POLAR", "POLAR() : complexes en polaire"),
        ["CPXRES"] = S("CPXRES", "CPXRES() : résultats complexes autorisés"),
        ["REALRES"] = S("REALRES", "REALRES() : résultats réels seulement"),
        ["RDXcomma"] = S("RDX,", "RDXcomma() : virgule décimale (RDX,)"),
        ["RDXperiod"] = S("RDX.", "RDXperiod() : point décimal (RDX.)"),
        ["CLLCD"] = S("CLLCD", "CLLCD() : efface l'écran"),
        ["CLD"] = S("CLD", "CLD() : efface le message affiché"),
        ["PSE"] = S("PSE", "PSE() : pause d'environ 1 seconde"),
        ["DELAY"] = P("DELAY", 1, "DELAY(s) : délai d'impression"),
        ["SIZE"] = I("SIZE", Imm.Size, "SIZE(n) : nombre de registres numérotés"),
        ["ON"] = S("ON", "ON() : calculatrice toujours allumée"),
        ["OFF"] = S("OFF", "OFF() : éteint la calculatrice"),
        ["STOP"] = S("STOP", "STOP() : arrête le programme (R/S pour continuer)"),

        // ---- Sons
        ["BEEP"] = S("BEEP", "BEEP() : bip"),
        ["TONE"] = I("TONE", Imm.Tone, "TONE(n) : note n (0 à 9)"),

        // ---- Drapeaux (FS, FC, FSC, FCC, isFS, isFC sont des tests : if FS(21): ...)
        ["SF"] = I("SF", Imm.Flag, "SF(n) : lève le drapeau n"),
        ["CF"] = I("CF", Imm.Flag, "CF(n) : baisse le drapeau n"),

        // ---- ALPHA, pile, registres
        ["CLA"] = S("CLA", "CLA() : vide ALPHA"),
        ["CLST"] = S("CLST", "CLST() : vide la pile"),
        ["CLX"] = S("CLX", "CLX() : X = 0"),
        ["CLRG"] = S("CLRG", "CLRG() : efface les registres numérotés"),
        ["AON"] = S("AON", "AON() : active le clavier ALPHA"),
        ["AOFF"] = S("AOFF", "AOFF() : désactive le clavier ALPHA"),
        ["ASHF"] = S("ASHF", "ASHF() : décale ALPHA de 6 caractères vers la gauche"),
        ["AROT"] = P("AROT", 1, "AROT(n) : rotation de ALPHA de n caractères"),
        ["AIP"] = P("AIP", 1, "AIP(x) : ajoute la partie entière de x à ALPHA"),
        ["XTOA"] = P("XTOA", 1, "XTOA(code) : ajoute le caractère de code donné à ALPHA"),
        ["ALENG"] = F("ALENG", 0, "ALENG() : longueur de ALPHA"),
        ["ATOX"] = F("ATOX", 0, "ATOX() : retire le 1er caractère de ALPHA et renvoie son code"),
        ["POSA"] = F("POSA", 1, "POSA(code) : position du caractère dans ALPHA (-1 si absent)"),
        ["AVIEW"] = S("AVIEW", "AVIEW() ou AVIEW(\"texte\", x...) : affiche ALPHA"),
        ["PROMPT"] = S("PROMPT", "x = PROMPT(\"texte\") : affiche le texte, attend une valeur (R/S)"),

        // ---- Menus et touches
        ["CLMENU"] = S("CLMENU", "CLMENU() : efface les touches du menu programmable"),
        ["MENU"] = S("MENU", "MENU() : affiche le menu programmable"),
        ["EXITALL"] = S("EXITALL", "EXITALL() : quitte tous les menus"),
        ["CLKEYS"] = S("CLKEYS", "CLKEYS() : efface les affectations du menu CUSTOM"),
        ["KEYASN"] = S("KEYASN", "KEYASN() : menu CUSTOM avec les affectations"),
        ["LCLBL"] = S("LCLBL", "LCLBL() : touches de labels locaux"),

        // ---- Clavier, aléatoire, constantes
        ["GETKEY"] = F("GETKEY", 0, "GETKEY() : attend une touche et renvoie son code"),
        ["RAN"] = F("RAN", 0, "RAN() : nombre aléatoire dans [0, 1["),
        ["SEED"] = P("SEED", 1, "SEED(x) : initialise le générateur aléatoire"),
        ["PI"] = F("PI", 0, "PI() : 3,14159..."),

        // ---- Graphisme (écran 131×16 sur HP-42S, 400×240 sur DM42)
        ["PIXEL"] = P("PIXEL", 2, "PIXEL(ligne, colonne) : allume un point"),
        ["AGRAPH"] = P("AGRAPH", 2, "AGRAPH(ligne, colonne) : dessine ALPHA comme motif de points"),

        // ---- Impression
        ["PRA"] = S("PRA", "PRA() : imprime ALPHA"),
        ["PRX"] = P("PRX", 1, "PRX(x) : imprime x"),
        ["PRSTK"] = S("PRSTK", "PRSTK() : imprime la pile"),
        ["PRZ"] = S("PRΣ", "PRZ() : imprime les registres de statistiques (PRΣ)"),
        ["PRStat"] = S("PRΣ", "PRStat() : imprime les registres de statistiques (PRΣ)"),
        ["PRLCD"] = S("PRLCD", "PRLCD() : imprime l'écran"),
        ["PRUSR"] = S("PRUSR", "PRUSR() : imprime les variables et programmes"),
        ["ADV"] = S("ADV", "ADV() : saut de papier"),
        ["PRON"] = S("PRON", "PRON() : impression activée"),
        ["PROFF"] = S("PROFF", "PROFF() : impression désactivée"),
        ["MAN"] = S("MAN", "MAN() : impression manuelle"),
        ["NORM"] = S("NORM", "NORM() : impression normale"),
        ["TRACE"] = S("TRACE", "TRACE() : impression trace"),
    };

    /// <summary>Tests de drapeaux utilisables dans if / while : FS(21), not FC(3)...</summary>
    public static readonly Dictionary<string, (string Rpn, string Negated)> FlagTests = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FS"] = ("FS?", "FC?"),
        ["FC"] = ("FC?", "FS?"),
        ["isFS"] = ("FS?", "FC?"),
        ["isFC"] = ("FC?", "FS?"),
        ["FSC"] = ("FS?C", ""),   // teste puis baisse le drapeau
        ["FCC"] = ("FC?C", ""),
    };

    /// <summary>Tests de type sur une valeur : if isCPX(z): ...</summary>
    public static readonly Dictionary<string, string> TypeTests = new(StringComparer.OrdinalIgnoreCase)
    {
        ["isCPX"] = "CPX?",
        ["isMAT"] = "MAT?",
        ["isREAL"] = "REAL?",
        ["isSTR"] = "STR?",
    };

    /// <summary>Commandes qui prennent un nom de variable : VIEW(x), INPUT(x), CLV(x)...</summary>
    public static readonly Dictionary<string, (string Rpn, bool Assigns, string Help)> VarCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VIEW"] = ("VIEW", false, "VIEW(x) : affiche la variable x"),
        ["INPUT"] = ("INPUT", true, "INPUT(x) : demande la valeur de x"),
        ["ARCL"] = ("ARCL", false, "ARCL(x) : ajoute la valeur de x à ALPHA"),
        ["ASTO"] = ("ASTO", true, "ASTO(x) : range les 6 premiers caractères de ALPHA dans x"),
        ["CLV"] = ("CLV", false, "CLV(x) : supprime la variable x"),
        ["PRV"] = ("PRV", false, "PRV(x) : imprime la variable x"),
        ["EDITN"] = ("EDITN", false, "EDITN(m) : éditeur de la matrice m"),
        ["SOLVE"] = ("SOLVE", true, "SOLVE(x) : résout pour x (après PGMSLV)"),
        ["INTEG"] = ("INTEG", false, "INTEG(x) : intègre selon x (après PGMINT)"),
    };

    /// <summary>Commandes qui prennent un nom de fonction : PGMSLV("f"), PGMINT("f"), CLP("f").</summary>
    public static readonly Dictionary<string, (string Rpn, string Help)> LabelCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PGMSLV"] = ("PGMSLV", "PGMSLV(\"f\") : choisit la fonction à résoudre (SOLVE)"),
        ["PGMINT"] = ("PGMINT", "PGMINT(\"f\") : choisit la fonction à intégrer (INTEG)"),
        ["CLP"] = ("CLP", "CLP(\"f\") : supprime le programme f"),
    };

    /// <summary>Instructions de la calculatrice traitées comme des fonctions (simulateur : K.Fn).</summary>
    public static readonly HashSet<string> FnOps = new()
    {
        "SIN", "COS", "TAN", "ASIN", "ACOS", "ATAN", "SINH", "COSH", "TANH", "ASINH", "ACOSH", "ATANH",
        "LN", "LOG", "LN1+X", "E↑X", "E↑X-1", "SQRT", "1/X", "ABS", "IP", "N!", "GAMMA", "→DEG", "→RAD", "X↑2", "+/-"
    };

    /// <summary>Recherche d'une commande selon le nombre d'arguments (COMPLEX(z) → 2 résultats).</summary>
    public static bool Lookup(string func, int nargs, out Hp42Cmd cmd)
    {
        if (func == "complex") { cmd = null!; return false; }   // complex() de Python, traité à part
        if (!Commands.TryGetValue(func, out cmd!)) return false;
        if (cmd.Rpn == "COMPLEX" && nargs == 1) cmd = new Hp42Cmd("COMPLEX", 1, 2, Imm.None, cmd.Help);
        return true;
    }

    /// <summary>Texte de l'instruction avec son argument immédiat.</summary>
    public static string WithImm(Hp42Cmd c, int v) => c.Imm switch
    {
        Imm.Digits => $"{c.Rpn} {v:00}",
        Imm.Tone => $"{c.Rpn} {v}",
        Imm.Flag or Imm.Reg => $"{c.Rpn} {v:00}",
        Imm.Size => $"{c.Rpn} {v:0000}",
        _ => c.Rpn
    };

    public static (int Min, int Max) Range(Imm i) => i switch
    {
        Imm.Digits => (0, 11),
        Imm.Tone => (0, 9),
        Imm.Flag or Imm.Reg => (0, 99),
        Imm.Size => (1, 9999),
        _ => (0, 0)
    };

    /// <summary>Toutes les entrées d'aide (commandes, tests, variables, fonctions), pour l'aide et l'auto-complétion.</summary>
    public static IEnumerable<(string Name, string Help)> AllHelp()
    {
        foreach (var kv in Commands) yield return (kv.Key, kv.Value.Help);
        foreach (var kv in VarCommands) yield return (kv.Key, kv.Value.Help);
        foreach (var kv in LabelCommands) yield return (kv.Key, kv.Value.Help);
        foreach (var kv in TypeTests) yield return (kv.Key, $"if {kv.Key}(x): ... test {kv.Value}");
        foreach (var kv in FlagTests) yield return (kv.Key, $"if {kv.Key}(n): ... test {kv.Value.Rpn} sur le drapeau n");
        yield return ("KEYG", "KEYG(touche, \"f\") : touche 1-9 du menu → GTO f");
        yield return ("KEYX", "KEYX(touche, \"f\") : touche 1-9 du menu → XEQ f");
        yield return ("ASSIGN", "ASSIGN(\"f\", touche) : affecte f à une touche du menu CUSTOM (1-18)");
        yield return ("alpha", "alpha(\"texte\", x...) : construit ALPHA sans l'afficher");
    }
}
