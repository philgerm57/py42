namespace Py42.Core;

/// <summary>Programmes d'exemple proposés dans l'application.</summary>
public static class Examples
{
    public sealed record Example(string Title, string Source, string Entry, string Stack, string Inputs);

    public static readonly List<Example> All = new()
    {
        new("Trinôme du second degré", """
# Racines de a·x² + b·x + c
from math import sqrt

def delta(a, b, c):
    return b**2 - 4*a*c

a = float(input("a ?"))
b = float(input("b ?"))
c = float(input("c ?"))
d = delta(a, b, c)
if d < 0:
    print("PAS DE RACINE")
elif d == 0:
    x = -b / (2*a)
    print("X=", x)
else:
    r = sqrt(d)
    x1 = (-b - r) / (2*a)
    x2 = (-b + r) / (2*a)
    print("X1=", x1)
    print("X2=", x2)
""", "MAIN", "", "1 -3 2"),

        new("PGCD (Euclide)", """
# Plus grand commun diviseur
def pgcd(a, b):
    while b != 0:
        a, b = b, a % b
    return a

def ppcm(a, b):
    return a * b / pgcd(a, b)
""", "PPCM", "12 18", ""),

        new("Factorielle et somme", """
# Boucles for avec range()
def fact(n):
    f = 1
    for i in range(2, n + 1):
        f *= i
    return f

def somme(n):
    s = 0
    for k in range(1, n + 1):
        s += 1 / k**2
    return s
""", "FACT", "10", ""),

        new("Racine par Newton", """
# Méthode de Newton pour la racine carrée
def racine(x):
    if x <= 0:
        return 0
    g = x / 2
    while abs(g*g - x) > 1e-10 * x:
        g = (g + x / g) / 2
    return g
""", "RACINE", "2", ""),

        new("Nombres premiers", """
# Affiche les nombres premiers jusqu'à n
def premier(n):
    if n < 2:
        return 0
    d = 2
    while d * d <= n:
        if n % d == 0:
            return 0
        d += 1
    return 1

n = int(input("N ?"))
for k in range(2, n + 1):
    if premier(k):
        print("PREMIER", k)
""", "MAIN", "", "30"),

        new("Triangle (trigonométrie)", """
# Résolution d'un triangle : deux côtés et l'angle compris
import math

def troisieme(a, b, angle):
    g = math.radians(angle)
    return math.sqrt(a*a + b*b - 2*a*b*math.cos(g))

a = float(input("A ?"))
b = float(input("B ?"))
g = float(input("ANGLE ?"))
c = troisieme(a, b, g)
alpha = math.degrees(math.asin(a * math.sin(math.radians(g)) / c))
print("C=", c)
print("ALPHA=", alpha)
""", "MAIN", "", "3 4 90"),
        new("Menu DM42 (varmenu + menu)", """
# Saisie par menu de variables, choix par menu de touches
def operation(a, b):
    choix = menu("ADD", "SUB", "MUL", "DIV")
    if choix == 1:
        return a + b
    elif choix == 2:
        return a - b
    elif choix == 3:
        return a * b
    elif choix == 4:
        assert b != 0, "DIVISION PAR 0"
        return a / b
    return 0

varmenu(x, y)          # tapez une valeur puis la touche X ou Y, puis R/S
FIX(2)
r = operation(x, y)
print("RESULTAT", r)
BEEP()
""", "MAIN", "", "12 5 3"),

        new("Drapeaux et commandes HP42S", """
# Commandes HP42S appelées comme des fonctions
def hms_total(debut, fin):
    return HMSSUB(fin, debut), TOHR(HMSSUB(fin, debut))

SF(1)
if FS(1):
    print("MODE EXPERT")
CF(1)
duree, heures = hms_total(8.3, 17.15)
print("DUREE", duree)
print("HEURES", heures)
p = prompt("PRIX HT ?")
print("TTC", p * 1.2)
TONE(5)
""", "MAIN", "", "100"),
        new("Matrices (système linéaire)", """
# Système A·x = b, déterminant, inverse et tranches
import numpy as np

A = np.array([[4, -2, 1], [-2, 4, -2], [1, -2, 4]])
b = [11, -16, 17]
x = np.linalg.solve(A, b)
print("X", x[0], x[1], x[2])
print("DET", np.linalg.det(A))

# Matrice remplie dans une boucle, produit et sous-matrice
def hilbert(n):
    h = np.zeros((n, n))
    for i in range(n):
        for j in range(n):
            h[i, j] = 1 / (i + j + 1)
    return h

H = hilbert(3)
P = H @ np.linalg.inv(H)
coin = H[0:2, 0:2]
print("TRACE", P[0, 0] + P[1, 1] + P[2, 2])
print(coin)
""", "MAIN", "", ""),

        new("Nombres complexes (circuit RLC)", """
# Impédance d'un circuit RLC série à la fréquence f
import cmath

def impedance(r, l, c, f):
    w = 2 * cmath.pi * f
    return r + 1j * (w * l - 1 / (w * c))

f = float(input("F (HZ) ?"))
z = impedance(100, 0.5, 10e-6, f)
print("Z", z)
print("MODULE", abs(z))
print("PHASE", cmath.phase(z) * 180 / cmath.pi)
i = 230 / z
print("I", abs(i))
""", "MAIN", "", "50"),

        new("Fonctions HP42S (trigo, statistiques)", """
# Commandes HP42S en majuscules (liste pyrpn.atug.com/cmds)
DEG()
myvar = 30
print("sine of", myvar, "is", SIN(myvar))
print("COS 60 =", COS(60), "5! =", Factorial(5))

# Régression linéaire : StatPlus(y, x)
CLStat()
for k in range(1, 6):
    StatPlus(2 * k + 1, k)
my, mx = MEAN()
print("MOY X", mx, "MOY Y", my)
print("PENTE", SLOPE(), "ORIG", YINT())
print("Y(10) =", FCSTY(10))
""", "MAIN", "", ""),
    };
}
