namespace Py42.Core;

/// <summary>Version anglaise des programmes d'exemple, dans le même ordre que <see cref="Examples"/>.</summary>
static class ExamplesEn
{
    public static readonly List<Examples.Example> All = new()
    {
        new("Quadratic equation", """
# Roots of a·x² + b·x + c
from math import sqrt

def delta(a, b, c):
    return b**2 - 4*a*c

a = float(input("a ?"))
b = float(input("b ?"))
c = float(input("c ?"))
d = delta(a, b, c)
if d < 0:
    print("NO ROOT")
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

        new("GCD (Euclid)", """
# Greatest common divisor
def gcd(a, b):
    while b != 0:
        a, b = b, a % b
    return a

def lcm(a, b):
    return a * b / gcd(a, b)
""", "LCM", "12 18", ""),

        new("Factorial and sum", """
# for loops with range()
def fact(n):
    f = 1
    for i in range(2, n + 1):
        f *= i
    return f

def total(n):
    s = 0
    for k in range(1, n + 1):
        s += 1 / k**2
    return s
""", "FACT", "10", ""),

        new("Newton's root", """
# Newton's method for the square root
def root(x):
    if x <= 0:
        return 0
    g = x / 2
    while abs(g*g - x) > 1e-10 * x:
        g = (g + x / g) / 2
    return g
""", "ROOT", "2", ""),

        new("Prime numbers", """
# Displays the prime numbers up to n
def prime(n):
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
    if prime(k):
        print("PRIME", k)
""", "MAIN", "", "30"),

        new("Triangle (trigonometry)", """
# Solving a triangle: two sides and the included angle
import math

def third(a, b, angle):
    g = math.radians(angle)
    return math.sqrt(a*a + b*b - 2*a*b*math.cos(g))

a = float(input("A ?"))
b = float(input("B ?"))
g = float(input("ANGLE ?"))
c = third(a, b, g)
alpha = math.degrees(math.asin(a * math.sin(math.radians(g)) / c))
print("C=", c)
print("ALPHA=", alpha)
""", "MAIN", "", "3 4 90"),
        new("DM42 menu (varmenu + menu)", """
# Input through a variable menu, choice through a key menu
def operate(a, b):
    choice = menu("ADD", "SUB", "MUL", "DIV")
    if choice == 1:
        return a + b
    elif choice == 2:
        return a - b
    elif choice == 3:
        return a * b
    elif choice == 4:
        assert b != 0, "DIVISION BY 0"
        return a / b
    return 0

varmenu(x, y)          # type a value then the X or Y key, then R/S
FIX(2)
r = operate(x, y)
print("RESULT", r)
BEEP()
""", "MAIN", "", "12 5 3"),

        new("Flags and HP42S commands", """
# HP42S commands called as functions
def hms_total(start, stop):
    return HMSSUB(stop, start), TOHR(HMSSUB(stop, start))

SF(1)
if FS(1):
    print("EXPERT MODE")
CF(1)
span, hours = hms_total(8.3, 17.15)
print("SPAN", span)
print("HOURS", hours)
p = prompt("NET PRICE ?")
print("GROSS", p * 1.2)
TONE(5)
""", "MAIN", "", "100"),
        new("Matrices (linear system)", """
# System A·x = b, determinant, inverse and slices
import numpy as np

A = np.array([[4, -2, 1], [-2, 4, -2], [1, -2, 4]])
b = [11, -16, 17]
x = np.linalg.solve(A, b)
print("X", x[0], x[1], x[2])
print("DET", np.linalg.det(A))

# Matrix filled in a loop, product and submatrix
def hilbert(n):
    h = np.zeros((n, n))
    for i in range(n):
        for j in range(n):
            h[i, j] = 1 / (i + j + 1)
    return h

H = hilbert(3)
P = H @ np.linalg.inv(H)
corner = H[0:2, 0:2]
print("TRACE", P[0, 0] + P[1, 1] + P[2, 2])
print(corner)
""", "MAIN", "", ""),

        new("Complex numbers (RLC circuit)", """
# Impedance of a series RLC circuit at frequency f
import cmath

def impedance(r, l, c, f):
    w = 2 * cmath.pi * f
    return r + 1j * (w * l - 1 / (w * c))

f = float(input("F (HZ) ?"))
z = impedance(100, 0.5, 10e-6, f)
print("Z", z)
print("MODULUS", abs(z))
print("PHASE", cmath.phase(z) * 180 / cmath.pi)
i = 230 / z
print("I", abs(i))
""", "MAIN", "", "50"),

        new("HP42S functions (trig, statistics)", """
# HP42S commands in upper case (list at pyrpn.atug.com/cmds)
DEG()
myvar = 30
print("sine of", myvar, "is", SIN(myvar))
print("COS 60 =", COS(60), "5! =", Factorial(5))

# Linear regression: StatPlus(y, x)
CLStat()
for k in range(1, 6):
    StatPlus(2 * k + 1, k)
my, mx = MEAN()
print("MEAN X", mx, "MEAN Y", my)
print("SLOPE", SLOPE(), "INT", YINT())
print("Y(10) =", FCSTY(10))
""", "MAIN", "", ""),
    };
}
