namespace Py42.Core;

/// <summary>
/// Exemples propres à la cible C47 / R47 : récursivité (registres locaux LocR), entiers longs exacts, structures
/// IF / DO / FOR avec conditions composées, textes de plus de 6 caractères. Ils ne se compilent pas tous pour la HP-42S.
/// </summary>
public static class ExamplesC47
{
    /// <summary>Exemples C47 dans la langue choisie.</summary>
    public static List<Examples.Example> All => Tr.English ? English : French;

    static readonly List<Examples.Example> French = new()
    {
        new("Factorielle récursive (entiers longs)", """
# C47 : chaque appel a ses propres registres locaux (LocR),
# la récursivité est donc permise ; les entiers longs sont exacts.
def fact(n):
    if n <= 1:
        return 1
    return n * fact(n - 1)

n = int(input("n ?"))
print("N! =", fact(n))
""", "MAIN", "", "25"),

        new("Fibonacci récursif et itératif", """
# Même suite, deux méthodes : récursive (C47 seulement)
# et itérative avec une boucle for (FORᵀᴼᴾ … NEXT).
def fibrec(n):
    if n < 2:
        return n
    return fibrec(n - 1) + fibrec(n - 2)

def fibit(n):
    a = 0
    b = 1
    for i in range(n):
        a, b = b, a + b
    return a

print("REC", fibrec(15))
print("ITER", fibit(80))
""", "MAIN", "", ""),

        new("Tours de Hanoï", """
# Nombre de déplacements pour n disques, calculé
# par récursivité double : 2^n - 1.
def hanoi(n, a, b, c):
    if n == 0:
        return 0
    m = hanoi(n - 1, a, c, b) + 1
    return m + hanoi(n - 1, c, b, a)

n = int(input("disques ?"))
print("COUPS", hanoi(n, 1, 2, 3))
""", "MAIN", "", "10"),

        new("Années bissextiles (and, or)", """
# Conditions composées : calculées dans un indicateur
# local, avec le court-circuit de Python.
def bissex(a):
    if (a % 4 == 0 and a % 100 != 0) or a % 400 == 0:
        return 1
    return 0

n = 0
for a in range(1900, 2101):
    n = n + bissex(a)
print("BISSEXTILES", n)
""", "MAIN", "", ""),

        new("Collatz et nombre premier (while, break)", """
# while → DO … WHILE … ENDDO ; un for avec break
# passe aussi par DO … ENDDO.
def collatz(n):
    pas = 0
    while n != 1:
        if n % 2 == 0:
            n = n // 2
        else:
            n = 3 * n + 1
        pas += 1
    return pas

def premier(n):
    d = 2
    while d * d <= n:
        if n % d == 0:
            return 0
        d += 1
    return 1

record = 0
kmax = 0
for k in range(1, 30):
    p = collatz(k)
    if p > record:
        record = p
        kmax = k
print("COLLATZ", kmax, record)

trouve = 0
for k in range(1001, 1100):
    if premier(k):
        trouve = k
        break
print("PREMIER", trouve)
""", "MAIN", "", ""),

        new("Textes longs", """
# C47 : un texte est une valeur comme une autre,
# sans la limite de 6 caractères de la HP-42S.
nom = "CALCULATRICE C47"
print(nom)
salut = "BONJOUR"
print(salut, "DE PYTHON")
""", "MAIN", "", ""),
    };

    static readonly List<Examples.Example> English = new()
    {
        new("Recursive factorial (long integers)", """
# C47: each call has its own local registers (LocR),
# so recursion is allowed; long integers are exact.
def fact(n):
    if n <= 1:
        return 1
    return n * fact(n - 1)

n = int(input("n ?"))
print("N! =", fact(n))
""", "MAIN", "", "25"),

        new("Recursive and iterative Fibonacci", """
# Same sequence, two methods: recursive (C47 only)
# and iterative with a for loop (FORᵀᴼᴾ … NEXT).
def fibrec(n):
    if n < 2:
        return n
    return fibrec(n - 1) + fibrec(n - 2)

def fibit(n):
    a = 0
    b = 1
    for i in range(n):
        a, b = b, a + b
    return a

print("REC", fibrec(15))
print("ITER", fibit(80))
""", "MAIN", "", ""),

        new("Towers of Hanoi", """
# Number of moves for n disks, computed by
# double recursion: 2^n - 1.
def hanoi(n, a, b, c):
    if n == 0:
        return 0
    m = hanoi(n - 1, a, c, b) + 1
    return m + hanoi(n - 1, c, b, a)

n = int(input("disks ?"))
print("MOVES", hanoi(n, 1, 2, 3))
""", "MAIN", "", "10"),

        new("Leap years (and, or)", """
# Compound conditions: computed in a local flag,
# with Python's short-circuit evaluation.
def leap(y):
    if (y % 4 == 0 and y % 100 != 0) or y % 400 == 0:
        return 1
    return 0

n = 0
for y in range(1900, 2101):
    n = n + leap(y)
print("LEAP YEARS", n)
""", "MAIN", "", ""),

        new("Collatz and prime number (while, break)", """
# while → DO … WHILE … ENDDO; a for loop with
# break also uses DO … ENDDO.
def collatz(n):
    steps = 0
    while n != 1:
        if n % 2 == 0:
            n = n // 2
        else:
            n = 3 * n + 1
        steps += 1
    return steps

def prime(n):
    d = 2
    while d * d <= n:
        if n % d == 0:
            return 0
        d += 1
    return 1

best = 0
arg = 0
for k in range(1, 30):
    p = collatz(k)
    if p > best:
        best = p
        arg = k
print("COLLATZ", arg, best)

found = 0
for k in range(1001, 1100):
    if prime(k):
        found = k
        break
print("PRIME", found)
""", "MAIN", "", ""),

        new("Long texts", """
# C47: a text is a value like any other,
# without the HP-42S 6-character limit.
name = "CALCULATOR C47"
print(name)
hello = "HELLO"
print(hello, "FROM PYTHON")
""", "MAIN", "", ""),
    };

    /// <summary>
    /// Programmes de la distribution C47 (dossier PROGRAMS du simulateur) réécrits en Python : mêmes calculs et mêmes
    /// résultats, le nom et l'auteur du programme d'origine en tête. Les jeux, graphiques et menus ne sont pas repris.
    /// </summary>
    public static List<Examples.Example> Programs => Tr.English ? ProgramsEn : ProgramsFr;

    static readonly List<Examples.Example> ProgramsFr = new()
    {
        new("BMI : indice de masse corporelle", """
# BMI (Barry Mead) : indice de masse corporelle.
# L'original prend le poids en livres et la taille
# en pouces : IMC = 703 × poids / taille².
def imc(poids, taille):
    return 703 * poids / taille ** 2

p = float(input("POIDS LB ?"))
t = float(input("TAILLE IN ?"))
print("IMC", round(imc(p, t), 1))
# même calcul en unités métriques : kg / m²
print("METRIQUE", round(p * 0.45359237 / (t * 0.0254) ** 2, 1))
""", "MAIN", "", "154 68"),

        new("FactIt : factorielle itérative", """
# FactIt (Warren Young) : factorielle itérative.
# Un argument négatif est refusé avec un message,
# comme FINISH 'Invalid argument!' de l'original.
# (FactRec, la version récursive de Walter Bonin,
# est l'exemple « Factorielle récursive ».)
def factit(x):
    n = int(x)
    if n < 0:
        print("ARGUMENT INVALIDE")
        return 0
    f = 1
    while n > 0:
        f = f * n
        n -= 1
    return f

print("5! =", factit(5))
print("0! =", factit(0))
print("10! =", factit(10))
print("25! =", factit(25))
factit(-3)
""", "MAIN", "", ""),

        new("NQueens : problème des n reines", """
# NQueens (Calcman54) : première solution du problème
# des n reines par retour arrière. q[i] est la colonne
# de la reine de la ligne i ; la solution est affichée
# comme un nombre, un chiffre par ligne (1 = 1re colonne).
import numpy as np

n = int(input("N (4 A 9) ?"))
q = np.zeros((n, 1))
essais = 0
i = 0
q[0] = -1
while i < n:
    q[i] += 1
    if q[i] >= n:
        i -= 1            # plus de colonne libre : retour arrière
    else:
        essais += 1
        ok = 1
        for j in range(i):
            d = q[i] - q[j]
            if d == 0 or abs(d) == i - j:
                ok = 0
                break
        if ok == 1:
            i += 1
            if i < n:
                q[i] = -1

sol = 0
for k in range(n):
    sol = sol * 10 + q[k] + 1
print("SOLUTION", int(sol))
print("ESSAIS", essais)
""", "MAIN", "", "8"),

        new("PAYCALC : salaire net écossais", """
# PAYCALC (nickapos) : salaire net en Écosse, 2026.
# Tranches d'impôt et cotisations NI par min et max ;
# ANNUEL = 1 donne des montants par an, 0 par mois.
import math

def tranche(ti, bas, haut, taux):
    return max(min(ti, haut) - bas, 0) * taux

rev = float(input("REVENU ?"))
ann = int(input("ANNUEL 1/0 ?"))
div = 12 - 11 * ann
# abattement réduit de 1 pour 2 au-dessus de 100000
pa = max(12570 - max(rev - 100000, 0) / 2, 0)
code = int(math.ceil(pa) / 10)
ti = max(rev - pa, 0)
impot = tranche(ti, 0, 3967, 0.19) + tranche(ti, 3967, 16956, 0.2)
impot += tranche(ti, 16956, 31092, 0.21) + tranche(ti, 31092, 62430, 0.42)
impot += tranche(ti, 62430, 125140, 0.45) + max(ti - 125140, 0) * 0.48
ni = tranche(rev, 12570, 50270, 0.08) + max(rev - 50270, 0) * 0.02
print("NET", round((rev - impot - ni) / div, 2))
print("IMPOT", round(impot / div, 2))
print("NI", round(ni / div, 2))
print("CODE", code)
""", "MAIN", "", "50000 1"),

        new("TRI∡ : triangle côté-côté-côté", """
# TRI∡ (HP-42S Programming Examples, p. 58) : le cas
# côté-côté-côté du solveur de triangles. Angles par
# la formule du demi-angle, en degrés ; aire et périmètre.
import math

s1 = float(input("S1 ?"))
s2 = float(input("S2 ?"))
s3 = float(input("S3 ?"))
p = (s1 + s2 + s3) / 2
a3 = 2 * math.degrees(math.acos(math.sqrt(p * (p - s2) / (s1 * s3))))
a2 = 2 * math.degrees(math.acos(math.sqrt(p * (p - s1) / (s2 * s3))))
a1 = 180 - a2 - a3
aire = s1 * s3 * math.sin(math.radians(a3)) / 2
print("A1", a1)
print("A2", a2)
print("A3", a3)
print("AIRE", aire)
print("PERIMETRE", 2 * p)
""", "MAIN", "", "3 4 5"),

        new("sums : 1/n² dans les deux sens", """
# sums (Jaco Mostert, AN0005) : somme de 1/n² pour
# n = 1 à 10000, en montant puis en descendant. Sur les
# 34 chiffres de la C47, l'ordre change les dernières
# décimales ; la somme infinie vaut π²/6.
import math

fin = 10000
monte = 0
for n in range(1, fin + 1):
    monte += 1 / (n * n)
descend = 0
for n in range(fin, 0, -1):
    descend += 1 / (n * n)
print("MONTE", monte)
print("DESCEND", descend)
print("ECART", monte - descend)
print("RESTE", math.pi ** 2 / 6 - descend)
""", "MAIN", "", ""),

        new("FreeF : temps de chute libre", """
# FreeF (Walter Bonin, manuel du WP43) : temps de chute
# d'un objet lancé vers le haut depuis h0. L'original
# résout h0 + v0·t - g/2·t² = h par SOLVE ; ici par la
# formule du second degré, g venant de scipy.constants.
import math
import scipy.constants

h = float(input("HAUTEUR ?"))
h0 = float(input("H0 ?"))
v0 = float(input("V0 ?"))
g = scipy.constants.g
t = (v0 + math.sqrt(v0 ** 2 + 2 * g * (h0 - h))) / g
print("TEMPS", t)
print("CONTROLE", h0 + v0 * t - g / 2 * t ** 2 - h)
""", "MAIN", "", "0 50 15"),

        new("IBessI : intégrale de cos(x·sin t)", """
# IBessI (HP-34C, manuel du WP43) : l'intégrande
# cos(x·sin t). L'original est intégré par ∫ de la
# calculatrice ; ici par la méthode de Simpson, comparée
# à π·J0(x) donné par scipy.special.jv.
import math
import scipy.special

def f(x, t):
    return math.cos(x * math.sin(t))

x = float(input("X ?"))
n = 20
h = math.pi / n
s = f(x, 0) + f(x, math.pi)
for k in range(1, n):
    if k % 2 == 1:
        s += 4 * f(x, k * h)
    else:
        s += 2 * f(x, k * h)
print("SIMPSON", s * h / 3)
print("PI J0(X)", math.pi * scipy.special.jv(0, x))
""", "MAIN", "", "1"),

        new("OpAmp : aides pour amplis op", """
# OpAmp (Warren Young) : aides pour amplificateurs
# opérationnels : résistances en parallèle, gain,
# pont diviseur et bruit Johnson (k de Boltzmann).
import math
import scipy.constants

def par(r1, r2):
    return r1 * r2 / (r1 + r2)

def gain(rf, rg, noninv):
    g = rf / rg
    if noninv == 1:
        g += 1
    return g

def rdiv(ri, rg, v):
    return v * rg / (rg + ri)

def bruit(r, tk, b1, b0):
    return math.sqrt(4 * scipy.constants.k * tk * (b1 - b0) * r)

print("2 || 2 =", par(2, 2))
print("3 || 6 =", par(3, 6))
print("GAIN NON INV", gain(100000, 10000, 1))
print("GAIN INV", gain(100000, 10000, 0))
print("DIVISEUR", rdiv(10000, 4700, 5))
print("BRUIT V", bruit(10000, 298, 20000, 20))
""", "MAIN", "", ""),

        new("ECA : automate cellulaire (bit à bit)", """
# ECA (programme de test C47) : automate cellulaire
# élémentaire. Une génération est un mot de 16 bits ;
# chaque cellule prend le bit de la règle numéroté par
# (gauche, centre, droite). Opérateurs bit à bit.
# La règle 90 dessine le triangle de Sierpiński.
def generer(ligne, regle):
    m = 65535
    g = ((ligne >> 1) | (ligne << 15)) & m
    d = ((ligne << 1) | (ligne >> 15)) & m
    res = 0
    for i in range(16):
        v = (((g >> i) & 1) << 2) | (((ligne >> i) & 1) << 1) | ((d >> i) & 1)
        res = res | (((regle >> v) & 1) << i)
    return res

regle = int(input("REGLE ?"))
ligne = 256
for k in range(8):
    print(k, ligne)
    ligne = generer(ligne, regle)
""", "MAIN", "", "90"),

        new("Bairsto : racines d'un polynôme", """
# Bairsto (Enrico Bellincioni) : racines d'un polynôme
# par la méthode de Bairstow. On extrait des facteurs
# x² + r·x + s jusqu'à ce qu'il reste le degré 2 ou 1.
# Exemple : x⁵ - 6x⁴ + 12x³ - 12x² + 11x - 6,
# soit (x - 1)(x - 2)(x - 3)(x² + 1).
import math
import numpy as np

def racines(r, s):
    # racines de x² + r·x + s
    d = r * r / 4 - s
    if d >= 0:
        print("RACINE", -r / 2 + math.sqrt(d))
        print("RACINE", -r / 2 - math.sqrt(d))
    else:
        print("RE", -r / 2)
        print("IM +-", math.sqrt(-d))

n = int(input("DEGRE ?"))
a = np.zeros((n + 1, 1))
b = np.zeros((n + 1, 1))
c = np.zeros((n + 1, 1))
for k in range(n + 1):
    v = float(input("COEF ?"))
    a[k] = v
for k in range(1, n + 1):
    a[k] = a[k] / a[0]      # polynôme unitaire
a[0] = 1
while n > 2:
    r = 1
    s = 1
    for it in range(60):
        b[0] = 1
        b[1] = a[1] - r
        c[0] = 1
        c[1] = b[1] - r
        for k in range(2, n + 1):
            b[k] = a[k] - r * b[k - 1] - s * b[k - 2]
            c[k] = b[k] - r * c[k - 1] - s * c[k - 2]
        det = c[n - 2] * c[n - 2] - c[n - 3] * c[n - 1]
        dr = (b[n - 1] * c[n - 2] - b[n] * c[n - 3]) / det
        ds = (b[n] * c[n - 2] - b[n - 1] * c[n - 1]) / det
        r += dr
        s += ds
        if abs(dr) + abs(ds) < 1e-25:
            break
    racines(r, s)
    # le quotient devient le nouveau polynôme
    n -= 2
    for k in range(n + 1):
        a[k] = b[k]
if n == 2:
    racines(a[1], a[2])
else:
    print("RACINE", -a[1])
""", "MAIN", "", "5 1 -6 12 -12 11 -6"),

        new("STRUCT : structures IF, WHILE, FOR", """
# STRUCT (Jaco Mostert, Didier Lachieze, AN0007) :
# quelques exemples de la note sur IF, WHILE et FOR,
# écrits en Python et traduits dans ces structures.
x = 12
moities = 0
while x > 1:                 # HALF : DO … WHILE … ENDDO
    x = x / 2
    moities += 1
print("HALF", moities, x)

compte = 0
for i in range(3):           # WW : boucles imbriquées
    for j in range(2):
        compte += 1
print("WW", compte)

s1 = 5                       # TRI3 : IF … ELSE … ENDIF
s2 = 5
s3 = 8
if s1 == s2 and s2 == s3:
    print("EQUILATERAL")
elif s1 == s2 or s2 == s3 or s1 == s3:
    print("ISOCELE")
else:
    print("QUELCONQUE")

for t in range(0, 16, 5):    # DISPL : x = x0 + v·t
    print("T", t, "X", 10 + 20 * t)

somme = 0
for k in range(1, 101):      # YS : FORᵀᴼᴾ … NEXT
    somme += k
print("YS", somme)
""", "MAIN", "", ""),
    };

    static readonly List<Examples.Example> ProgramsEn = new()
    {
        new("BMI: body mass index", """
# BMI (Barry Mead): body mass index.
# The original takes the weight in pounds and the
# height in inches: BMI = 703 × weight / height².
def bmi(weight, height):
    return 703 * weight / height ** 2

w = float(input("WEIGHT LB ?"))
h = float(input("HEIGHT IN ?"))
print("BMI", round(bmi(w, h), 1))
# same calculation in metric units: kg / m²
print("METRIC", round(w * 0.45359237 / (h * 0.0254) ** 2, 1))
""", "MAIN", "", "154 68"),

        new("FactIt: iterative factorial", """
# FactIt (Warren Young): iterative factorial.
# A negative argument is rejected with a message,
# like FINISH 'Invalid argument!' in the original.
# (FactRec, Walter Bonin's recursive version, is
# the "Recursive factorial" example.)
def factit(x):
    n = int(x)
    if n < 0:
        print("INVALID ARGUMENT")
        return 0
    f = 1
    while n > 0:
        f = f * n
        n -= 1
    return f

print("5! =", factit(5))
print("0! =", factit(0))
print("10! =", factit(10))
print("25! =", factit(25))
factit(-3)
""", "MAIN", "", ""),

        new("NQueens: n-queens problem", """
# NQueens (Calcman54): first solution of the n-queens
# problem by backtracking. q[i] is the column of the
# queen on row i; the solution is shown as a number,
# one digit per row (1 = first column).
import numpy as np

n = int(input("N (4 TO 9) ?"))
q = np.zeros((n, 1))
tries = 0
i = 0
q[0] = -1
while i < n:
    q[i] += 1
    if q[i] >= n:
        i -= 1            # no free column left: backtrack
    else:
        tries += 1
        ok = 1
        for j in range(i):
            d = q[i] - q[j]
            if d == 0 or abs(d) == i - j:
                ok = 0
                break
        if ok == 1:
            i += 1
            if i < n:
                q[i] = -1

sol = 0
for k in range(n):
    sol = sol * 10 + q[k] + 1
print("SOLUTION", int(sol))
print("TRIES", tries)
""", "MAIN", "", "8"),

        new("PAYCALC: Scottish take-home pay", """
# PAYCALC (nickapos): Scottish take-home pay, 2026.
# Tax bands and NI contributions with min and max;
# ANNUAL = 1 gives yearly amounts, 0 monthly ones.
import math

def band(ti, low, high, rate):
    return max(min(ti, high) - low, 0) * rate

inc = float(input("INCOME ?"))
ann = int(input("ANNUAL 1/0 ?"))
div = 12 - 11 * ann
# allowance reduced by 1 for every 2 above 100000
pa = max(12570 - max(inc - 100000, 0) / 2, 0)
code = int(math.ceil(pa) / 10)
ti = max(inc - pa, 0)
tax = band(ti, 0, 3967, 0.19) + band(ti, 3967, 16956, 0.2)
tax += band(ti, 16956, 31092, 0.21) + band(ti, 31092, 62430, 0.42)
tax += band(ti, 62430, 125140, 0.45) + max(ti - 125140, 0) * 0.48
ni = band(inc, 12570, 50270, 0.08) + max(inc - 50270, 0) * 0.02
print("NET", round((inc - tax - ni) / div, 2))
print("TAX", round(tax / div, 2))
print("NI", round(ni / div, 2))
print("CODE", code)
""", "MAIN", "", "50000 1"),

        new("TRI∡: side-side-side triangle", """
# TRI∡ (HP-42S Programming Examples, p. 58): the
# side-side-side case of the triangle solver. Angles by
# the half-angle formula, in degrees; area and perimeter.
import math

s1 = float(input("S1 ?"))
s2 = float(input("S2 ?"))
s3 = float(input("S3 ?"))
p = (s1 + s2 + s3) / 2
a3 = 2 * math.degrees(math.acos(math.sqrt(p * (p - s2) / (s1 * s3))))
a2 = 2 * math.degrees(math.acos(math.sqrt(p * (p - s1) / (s2 * s3))))
a1 = 180 - a2 - a3
area = s1 * s3 * math.sin(math.radians(a3)) / 2
print("A1", a1)
print("A2", a2)
print("A3", a3)
print("AREA", area)
print("PERIMETER", 2 * p)
""", "MAIN", "", "3 4 5"),

        new("sums: 1/n² both ways", """
# sums (Jaco Mostert, AN0005): sum of 1/n² for
# n = 1 to 10000, upwards then downwards. With the C47's
# 34 digits, the order changes the last decimals;
# the infinite sum is π²/6.
import math

last = 10000
up = 0
for n in range(1, last + 1):
    up += 1 / (n * n)
down = 0
for n in range(last, 0, -1):
    down += 1 / (n * n)
print("UP", up)
print("DOWN", down)
print("GAP", up - down)
print("REST", math.pi ** 2 / 6 - down)
""", "MAIN", "", ""),

        new("FreeF: free-fall time", """
# FreeF (Walter Bonin, WP43 manual): fall time of an
# object thrown upwards from h0. The original solves
# h0 + v0·t - g/2·t² = h with SOLVE; here by the
# quadratic formula, with g from scipy.constants.
import math
import scipy.constants

h = float(input("HEIGHT ?"))
h0 = float(input("H0 ?"))
v0 = float(input("V0 ?"))
g = scipy.constants.g
t = (v0 + math.sqrt(v0 ** 2 + 2 * g * (h0 - h))) / g
print("TIME", t)
print("CHECK", h0 + v0 * t - g / 2 * t ** 2 - h)
""", "MAIN", "", "0 50 15"),

        new("IBessI: integral of cos(x·sin t)", """
# IBessI (HP-34C, WP43 manual): the integrand
# cos(x·sin t). The original is integrated with the
# calculator's ∫; here with Simpson's rule, compared
# with π·J0(x) from scipy.special.jv.
import math
import scipy.special

def f(x, t):
    return math.cos(x * math.sin(t))

x = float(input("X ?"))
n = 20
h = math.pi / n
s = f(x, 0) + f(x, math.pi)
for k in range(1, n):
    if k % 2 == 1:
        s += 4 * f(x, k * h)
    else:
        s += 2 * f(x, k * h)
print("SIMPSON", s * h / 3)
print("PI J0(X)", math.pi * scipy.special.jv(0, x))
""", "MAIN", "", "1"),

        new("OpAmp: op-amp helpers", """
# OpAmp (Warren Young): op-amp helpers: parallel
# resistors, gain, voltage divider and Johnson noise
# (Boltzmann's k).
import math
import scipy.constants

def par(r1, r2):
    return r1 * r2 / (r1 + r2)

def gain(rf, rg, noninv):
    g = rf / rg
    if noninv == 1:
        g += 1
    return g

def rdiv(ri, rg, v):
    return v * rg / (rg + ri)

def noise(r, tk, b1, b0):
    return math.sqrt(4 * scipy.constants.k * tk * (b1 - b0) * r)

print("2 || 2 =", par(2, 2))
print("3 || 6 =", par(3, 6))
print("GAIN NON INV", gain(100000, 10000, 1))
print("GAIN INV", gain(100000, 10000, 0))
print("DIVIDER", rdiv(10000, 4700, 5))
print("NOISE V", noise(10000, 298, 20000, 20))
""", "MAIN", "", ""),

        new("ECA: cellular automaton (bitwise)", """
# ECA (C47 test program): elementary cellular
# automaton. A generation is a 16-bit word; each cell
# takes the bit of the rule numbered by (left, centre,
# right). Bitwise operators.
# Rule 90 draws the Sierpiński triangle.
def nextgen(row, rule):
    m = 65535
    l = ((row >> 1) | (row << 15)) & m
    r = ((row << 1) | (row >> 15)) & m
    res = 0
    for i in range(16):
        v = (((l >> i) & 1) << 2) | (((row >> i) & 1) << 1) | ((r >> i) & 1)
        res = res | (((rule >> v) & 1) << i)
    return res

rule = int(input("RULE ?"))
row = 256
for k in range(8):
    print(k, row)
    row = nextgen(row, rule)
""", "MAIN", "", "90"),

        new("Bairsto: roots of a polynomial", """
# Bairsto (Enrico Bellincioni): roots of a polynomial
# by Bairstow's method. Quadratic factors x² + r·x + s
# are taken out until degree 2 or 1 remains.
# Example: x⁵ - 6x⁴ + 12x³ - 12x² + 11x - 6,
# that is (x - 1)(x - 2)(x - 3)(x² + 1).
import math
import numpy as np

def roots(r, s):
    # roots of x² + r·x + s
    d = r * r / 4 - s
    if d >= 0:
        print("ROOT", -r / 2 + math.sqrt(d))
        print("ROOT", -r / 2 - math.sqrt(d))
    else:
        print("RE", -r / 2)
        print("IM +-", math.sqrt(-d))

n = int(input("DEGREE ?"))
a = np.zeros((n + 1, 1))
b = np.zeros((n + 1, 1))
c = np.zeros((n + 1, 1))
for k in range(n + 1):
    v = float(input("COEF ?"))
    a[k] = v
for k in range(1, n + 1):
    a[k] = a[k] / a[0]      # monic polynomial
a[0] = 1
while n > 2:
    r = 1
    s = 1
    for it in range(60):
        b[0] = 1
        b[1] = a[1] - r
        c[0] = 1
        c[1] = b[1] - r
        for k in range(2, n + 1):
            b[k] = a[k] - r * b[k - 1] - s * b[k - 2]
            c[k] = b[k] - r * c[k - 1] - s * c[k - 2]
        det = c[n - 2] * c[n - 2] - c[n - 3] * c[n - 1]
        dr = (b[n - 1] * c[n - 2] - b[n] * c[n - 3]) / det
        ds = (b[n] * c[n - 2] - b[n - 1] * c[n - 1]) / det
        r += dr
        s += ds
        if abs(dr) + abs(ds) < 1e-25:
            break
    roots(r, s)
    # the quotient becomes the new polynomial
    n -= 2
    for k in range(n + 1):
        a[k] = b[k]
if n == 2:
    roots(a[1], a[2])
else:
    print("ROOT", -a[1])
""", "MAIN", "", "5 1 -6 12 -12 11 -6"),

        new("STRUCT: IF, WHILE, FOR structures", """
# STRUCT (Jaco Mostert, Didier Lachieze, AN0007):
# some examples from the note on IF, WHILE and FOR,
# written in Python and compiled into those structures.
x = 12
halves = 0
while x > 1:                 # HALF: DO … WHILE … ENDDO
    x = x / 2
    halves += 1
print("HALF", halves, x)

count = 0
for i in range(3):           # WW: nested loops
    for j in range(2):
        count += 1
print("WW", count)

s1 = 5                       # TRI3: IF … ELSE … ENDIF
s2 = 5
s3 = 8
if s1 == s2 and s2 == s3:
    print("EQUILATERAL")
elif s1 == s2 or s2 == s3 or s1 == s3:
    print("ISOSCELES")
else:
    print("SCALENE")

for t in range(0, 16, 5):    # DISPL: x = x0 + v·t
    print("T", t, "X", 10 + 20 * t)

total = 0
for k in range(1, 101):      # YS: FORᵀᴼᴾ … NEXT
    total += k
print("YS", total)
""", "MAIN", "", ""),
    };
}
