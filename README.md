# Py42 — Python vers RPN HP-42S / DM42

Convertit un Python simplifié en programme RPN pour la HP-42S, la SwissMicros DM42 et l'émulateur Free42.

## Contenu de la solution

| Projet | Rôle |
|---|---|
| `Py42.Core` | Le compilateur (analyse, génération RPN, optimisations) et un simulateur des instructions générées. Bibliothèque .NET 8, sans dépendance. |
| `Py42.Studio` | L'application Windows (WPF) : éditeur Python à coloration syntaxique (AvalonEdit), listing RPN coloré, messages, simulation. |
| `Py42.Cli` | Outil en ligne de commande `py42`, pratique pour convertir des fichiers en lot. |

## Compiler et lancer

Avec Visual Studio 2022 (charge de travail « Développement .NET Desktop ») : ouvrir `Py42.sln`, choisir `Py42.Studio` comme projet de démarrage, puis F5. Le paquet NuGet AvalonEdit est restauré automatiquement.

Avec `build.bat` (SDK .NET 10 ; compile aussi `..\dm42dll` si sa DLL manque) :

```
build.bat              compile la solution
build.bat publish      Py42.exe (_publish\Py42) et py42.exe (_publish\cli) en fichier unique
```

En ligne de commande (SDK .NET 10) :

```
dotnet run --project Py42.Studio
dotnet run --project Py42.Cli -- programme.py -o programme.txt
dotnet run --project Py42.Cli -- --exemples
```

Options de `py42` : `-o fichier.txt`, `--sans-numeros`, `--sans-rad`, `--minuscules`, `--nom MAIN`, `--run LABEL --pile "12 18" --entrees "1 -3 2"`.

## Dans l'application

- La conversion se fait pendant la frappe. Les erreurs surlignent la ligne Python en rouge ; le listing précédent reste affiché en grisé.
- Cliquer une ligne Python surligne les pas RPN correspondants, et inversement.
- **Simulation** : choisir le label à lancer, les valeurs mises sur la pile (la dernière va dans X, comme des arguments) et les réponses aux INPUT, puis R/S ou F5.
- Ctrl+O ouvre un `.py`, Ctrl+S l'enregistre, « Exporter .txt » enregistre le listing.

## Envoyer le programme sur la calculatrice

1. Dans Free42 : *Edit → Paste Program* (après « Copier le RPN »), ou ouvrir le `.txt` et le copier.
2. Dans Free42 : *File → Export Programs…* produit un fichier `.raw`.
3. DM42 : brancher en USB (mode disque), copier le `.raw` dans `/PROGRAMS`, puis sur la calculatrice *SETUP → 0 System → Load Program*.

Sur une vraie HP-42S, saisir le listing à la main (pas de liaison).

## Python accepté

- Nombres, variables, `+ - * / ** % //`, moins unaire, parenthèses.
- `if / elif / else`, `while` (+ `else`), `for i in range(...)` (+ `else`, pas constant positif ou négatif), `break`, `continue`, `pass`, `assert condition, "message"`.
- Comparaisons `< > <= >= == !=`, chaînées (`0 < x < 10`), `and`, `or`, `not`.
- Commentaires `# …` et blocs `"""` … `"""` (ou `'''`) en début de ligne, docstrings comprises : ignorés, rien dans le RPN.
- Variables déclarées avant `def main():` puis appel final `main()` (ou `if __name__ == "__main__": main()`) : les déclarations vont sous un label à part (`INIT`, ou le nom du programme s'il diffère de MAIN) et `main` commence par `XEQ "INIT"`. Lancer `XEQ "MAIN"` sur la calculatrice suffit donc.
- `def f(a, b, ...)` avec au plus 4 paramètres, `return x` ou `return a, b` (récupéré par `a, b = f(...)`), appels imbriqués, `global`.
- Affectations multiples `a, b = b, a % b` ou `(a, b) = (b, a % b)` (jusqu'à 4), affectations combinées `+= -= *= /= **= %= //=`.
- Tuples : `return (a, b)`, `for v in (1, 2, 3)`, `x in (1, 5, 7)` / `x not in (…)` (→ suite de `==` / `!=`), `print("date", (j, m, a))` → `date (12, 5, 2024)`.
- Liste de couples `m = [(1.0, n1), (1.05, n2), …]` → matrice (une ligne par couple) ; `for x, n in m:` parcourt ses lignes, une variable par colonne.
- `j, m, a = dmy(d)` découpe une date au format JJ.MMAAAA (D.MY de la DM42) : `12.052024` donne 12, 5, 2024.
- Horloge : `DATE()`, `TIME()` (H.MMSS, `toHR(TIME())` en heures décimales), `DDAYS(d1, d2)` (jours de d1 à d2). Le résultat suit le format réglé sur la calculatrice : appelez `DMY()` avant `DATE()` si vous l'utilisez avec `dmy()`. Ce réglage reste actif après le programme. `MDY()` et `YMD()` choisissent les autres formats.
- `x = input("...")`, `x = float(input(...))`, `x = int(input(...))` → `INPUT "X"`.
- `print("texte", x, ...)` → `"texte"`, `ARCL`, `AVIEW` ; `print(x)` → `VIEW "X"`.
- Concaténation : `print("P Vap :" + str(v) + " hPa")` ou `print("N=" + floor(n))` → morceaux à la suite, sans espace ; `floor`, `ceil`, `int` sont ajoutés par `AIP` (sans décimales).
- `x = prompt("PRIX ?")` ou `x = PROMPT("PRIX ?")` → le texte s'affiche (`PROMPT`), on tape la valeur puis R/S. Seul, `PROMPT("PRET")` fait une simple pause. `MENU(...)` et `VARMENU(...)` en majuscules marchent aussi.
- `import math`, `from math import ...` ; fonctions : `sqrt sin cos tan asin acos atan atan2 sinh cosh tanh asinh acosh atanh exp expm1 log log1p log10 abs fabs int trunc floor ceil factorial gamma radians degrees hypot pow max min`, constantes `pi` et `e`.
- `floor(x)` et `ceil(x)` → `x - (x MOD 1)` et `x + (-x MOD 1)` : le MOD de la HP-42S a le signe du diviseur, donc le résultat est juste aussi pour x négatif ; une constante est calculée dès la conversion.
- `round(x, n)` (n entier constant, 0 à 11) → `IP(x·10ⁿ + 0,5·SIGN(x)) ÷ 10ⁿ` : au plus proche, les demis s'éloignent de zéro comme `RND` (Python, lui, arrondit `round(2.5)` à 2) ; le résultat s'affiche avec `n` décimales : `FIX n` est émis à l'affectation `y = round(x, n)` et devant le `print` de `y` ou de `round(x, n)`. Ce mode reste actif ensuite ; un `FIX(n)` écrit dans le programme annule ce réglage pour les variables déjà arrondies. `round(x)` sans nombre de décimales est refusé : écrivez `round(x, 0)`.

## Expressions longues

Les parenthèses peuvent s'imbriquer sans limite. Quand une expression demande plus que les 4 niveaux de la pile (X, Y, Z, T), Py42 calcule d'abord les morceaux les plus profonds dans des variables temporaires `S1`, `S2`… (réutilisées d'une instruction à l'autre) et le signale par un avertissement.

## Listes

Une liste est un vecteur colonne n×1 de la calculatrice.

```python
b = [1, 2, 3]
b.append(4)          # DIM "B" agrandit d'une ligne, puis STOEL
x = b.pop()          # dernier élément, puis DIM réduit d'une ligne
print(b[-1], len(b)) # indices négatifs constants : comptés depuis DIM?
noms = ['a', 'b', 'fred']   # textes de 6 caractères max (ASTO ST X)
assert noms[-1] == 'fred'
```

- `b2 = b` fait une **copie** sur HP42 (pas un alias comme en Python).
- Un code collé avec un décalage commun (bloc indenté) est accepté : le décalage est retiré.

## Matrices

```python
import numpy as np                    # facultatif
A = np.array([[4, -2], [-2, 4]])      # ou A = [[4, -2], [-2, 4]]
b = [2, 4]                            # vecteur colonne 2×1
x = np.linalg.solve(A, b)             # inv(A) @ b
A[0, 1] = 7                           # INDEX "A", STOIJ, STOEL
s = A[0:2, 1:2]                       # sous-matrice : GETM
A[0:1, 0:2] = [[1, 2]]                # PUTM
```

| Python | HP-42S |
|---|---|
| `[[1, 2], [3, 4]]`, `np.array(...)` | `2 2 NEWMAT STO "A" INDEX "A"` puis `STOEL J+` |
| `np.zeros((l, c))`, `zeros(l, c)` | `NEWMAT` |
| `A[i, j]`, `A[i][j]`, `v[i]` (à partir de 0) | `i+1 j+1 INDEX "A" STOIJ R↓ R↓ RCLEL` |
| `A[a:b, c:d]`, `A[1:, :2]` | `GETM` (affectation : `PUTM`) |
| `A @ B`, `A + B`, `2 * A`, `A.T` | `×`, `+`, `×`, `TRANS` |
| `inv(A)`, `det(A)`, `norm(A)`, `solve(A, b)` | `INVRT`, `DET`, `FNRM`, `INVRT` puis `×` |
| `dot(u, v)`, `cross(u, v)` | `DOT`, `CROSS` |
| `rows(A)`, `cols(A)`, `len(v)` | `DIM?` |
| `for x in v:` | boucle cachée sur les éléments, ligne par ligne |

Les fonctions existent aussi sous la forme numpy : `np.linalg.inv`, `np.linalg.det`, `np.linalg.solve`, `np.linalg.norm`, `np.dot`, `np.cross`, `np.transpose`.

Différences avec numpy :
- une tranche `A[0:2, 0:2]` ou `A.T` est une **copie** (en numpy c'est une vue qui suit les changements de `A`) ;
- `A * B` entre deux matrices est refusé : la HP-42S n'a pas de produit élément par élément, utilisez `A @ B` pour le produit matriciel ;
- `for x in A` parcourt tous les éléments (numpy parcourt les lignes) ;
- pas d'indices négatifs ni de pas dans les tranches ; `eye`, `ones` absents (créez avec `zeros` et une boucle) ;
- une fonction qui modifie une matrice reçue en paramètre travaille sur une copie : renvoyez-la (`return m`).

## Nombres complexes

```python
import cmath                          # sqrt(-1), log(-1)... donnent des complexes (CPXRES)
z = 3 + 4j                            # 3 4 COMPLEX
w = complex(1, -2)
print(abs(z), z.real, z.imag, z.conjugate(), cmath.phase(z))
r = cmath.rect(2, 0.5)
```

`+ - * / **`, `sqrt exp log sin cos tan…` fonctionnent sur les complexes. `RECT` est ajouté au début du programme (et `CPXRES` si `cmath` est importé). Une matrice peut contenir des complexes.

## Menus DM42 / HP-42S

```python
varmenu(a, b, c)                  # MVAR "A" ... VARMENU "PROG" STOP EXITALL
choix = menu("ADD", "SUB", "MUL") # CLMENU, "ADD" KEY 1 GTO nn ..., MENU STOP
```

- `varmenu(a, b, c)` : menu des variables. On tape une valeur, on appuie sur la touche de la variable pour la ranger, puis R/S. Les `MVAR` sont placés juste après le label global, comme l'exige la calculatrice. Ces variables gardent leur nom (elles ne sont jamais renommées).
- `choix = menu("A", "B", ...)` : jusqu'à 6 touches. Renvoie 1 à 6 selon la touche, 0 pour EXIT. R/S sans choisir réaffiche le menu.

## Commandes HP42S (jeu de pyrpn)

Les commandes de la liste de pyrpn (https://pyrpn.atug.com/cmds) s'appellent comme des fonctions, avec les mêmes noms et alias, en majuscules ou en minuscules. Les arguments suivent l'ordre de la pile : le dernier va dans X.

```python
myvar = 30
DEG()
print("sine of", myvar, "is", SIN(myvar))   # → sine of 30 is 0.5
```

- **Angles** : `SIN(x)`, `COS(x)`… utilisent le mode d'angle de la calculatrice (`DEG()`, `RAD()`, `GRAD()`). `math.sin(x)` reste en radians : `RAD` est alors ajouté au début du programme.
- **Fonctions** : `SIN COS TAN ASIN ACOS ATAN SINH COSH TANH ASINH ACOSH ATANH LN LOG LN1plus Eto EtoMinus1 EXP10 SQRT Reciprocal ABS IP FP RND SIGN Factorial GAMMA toDEG toRAD toHMS toHR toDEC toOCT PI()`, plus `MOD COMB PERM Percent PercentCH HMSplus HMSminus` (2 arguments).
- **Deux résultats** : `a, b = MEAN()` — `a` reçoit Y et `b` reçoit X. De même `l, c = whatDIM(m)`, `SDEV()` et `SUM()`.
- **Statistiques** : `StatPlus(y, x)`, `StatMinus(y, x)`, `CLStat() LINStat() ALLStat() StatREG(n) StatWhichREG()`, `MEAN SDEV SUM WMEAN CORR SLOPE YINT FCSTX(y) FCSTY(x)`, `LINF() LOGF() EXPF() PWRF() BEST()`.
- **Mode BASE** : `BASEplus BASEminus BASEtimes BASEdivide BASEplusMinus ROTXY`, `BINM() OCTM() DECM() HEXM()`.
- **Matrices** : `NEWMAT(l, c) DET INVRT TRANS FNRM RNRM RSUM UVEC DOT CROSS whatDIM EDIT`.
- **Tests** : `isCPX(x) isMAT(x) isREAL(x) isSTR(x)` dans `if` et `while` ; drapeaux `SF(n) CF(n)`, `if FS(n):`, `FC(n)`, `FSC(n)`, `FCC(n)`.
- **Variables par nom** : `VIEW(x) INPUT(x) ARCL(x) ASTO(x) CLV(x) PRV(x) EDITN(m) SOLVE(x) INTEG(x)`.
- **Programmes par nom** : `PGMSLV("f") PGMINT("f") CLP("f")`.
- **Menus** : `CLMENU()`, puis `alpha("TEXTE")` et `KEYG(n, "f")` / `KEYX(n, "f")` pour chaque touche, puis `MENU()` et `STOP()`. Également `ASSIGN("f", n)`, `EXITALL() CLKEYS() LCLBL()`.
- **ALPHA** : `alpha(...)` construit ALPHA sans l'afficher, `AVIEW(...)` l'affiche, ainsi que `CLA AIP AROT ASHF XTOA ALENG ATOX POSA AON AOFF`.
- **Affichage et modes** : `FIX(n) SCI(n) ENG(n) ALL() RECT() POLAR() CPXRES() REALRES() RDXcomma() RDXperiod() CLLCD() CLD() PSE() DELAY(x) SIZE(n) BEEP() TONE(n)`.
- **Divers** : `GETKEY() RAN() SEED(x) PIXEL(l, c) AGRAPH(l, c)`, impression `PRA PRX PRSTK PRLCD PRUSR ADV PRON PROFF MAN NORM TRACE`.

Le simulateur exécute presque toutes ces commandes. Pour les autres (SOLVE, INTEG, ASTO, AGRAPH, impression…), il affiche une note « non simulé » et continue. Les statistiques y suivent toujours le modèle linéaire.

## Simulation

Le champ INPUT donne les réponses dans l'ordre : `INPUT`, `prompt()`, une valeur par variable de `varmenu()`, le numéro de touche pour `menu()` (0 = EXIT), le code de touche pour `GETKEY()`.

## Correspondances principales

| Python | HP-42S |
|---|---|
| `x = a + b` | `RCL "A"` `RCL+ "B"` `STO "X"` |
| `s += k` | `RCL "K"` `STO+ "S"` |
| `x**2`, `x**0.5`, `x**-1` | `X↑2`, `SQRT`, `1/X` |
| `-x` | `RCL "X"` `+/-` |
| `if a < b:` | test `X>Y?` / `X≤Y?` puis `GTO nn` |
| `def f(a, b):` | `LBL "F"` `STO "B"` `R↓` `STO "A"` |
| `f(1, 2)` | `1` `2` `XEQ "F"` |
| `max(a, b)` | `X<Y?` `X<>Y` `STO ST Y` `R↓` |

## Cible C47 / R47

Avec `CompileOptions.Target = Target.C47` (ou `py42 --c47`), le même Python produit un programme pour la C47 / R47 :

- `if` / `elif` / `else` → `IF` `ELSE` `ENDIF` ; `while` → `DO` test `WHILE` … `ENDDO` ; `for … in range()` → `FORᵀᴼᴾ` … `NEXT`
  (une boucle avec `break` ou qui modifie sa variable passe par `DO` … `ENDDO`). Plus de `GTO` pour ces constructions.
  Les structures sont écrites sans numéro : l'encodeur de DM42 Editor les numérote comme `VALID`.
- `and` / `or` / `not` : la condition est calculée dans un indicateur local (`.00`…), avec le court-circuit de Python.
- Variables locales des fonctions dans les registres locaux (`LocR`, `.00`, `.01`…) : plus de renommage, et la **récursivité est permise**.
- Textes : valeurs poussées dans X, sans la limite de 6 caractères. `print` passe par les instructions de compatibilité
  HP-42S de la C47 (ALPHA = registre K).

Fonctions de la cible C47 (`Py42.Core/C47Functions.cs`, rangées par domaine, reprises dans l'aide C47 de DM42 Editor) :

- `math` et `scipy.special` : `sqrt` → `√x`, `cbrt` → `∛x`, `log(x, b)` → `LOGₓy`, `gamma` → `Γ(x)`, `erf`, `zeta` → `ζ(x)`,
  `jv` / `yv` (Bessel), `lambertw` → `Wₚ`, `ellipk` / `ellipe` / `ellipkinc` / `ellipeinc`, `gammainc` / `gammaincc`,
  polynômes de Legendre, Tchebychev, Hermite, Laguerre (`eval_…`), `gcd`, `lcm`, `comb`, `perm`…
- `sympy` : `nextprime` → `NEXTP`, `fibonacci` → `FIB`, `isprime(n)` dans une condition → `PRIME?`.
- `statistics.mean / median / stdev / pstdev (v)` : `CLΣ`, chaque élément par `Σ+`, puis `x̄`, médiane, `s`, `σ`.
- `scipy.constants.c`, `h`, `k`, `N_A`, `G`, `e`, `m_e`… : constantes du catalogue C47 (sans parenthèses).
- `scipy.stats` (`import scipy.stats as st`, ou `from scipy.stats import norm`) : `norm`, `cauchy`, `logistic`, `expon`,
  `weibull_min`, `t`, `chi2`, `f`, `binom`, `poisson`, `geom`, `nbinom`, avec `.pdf` / `.pmf`, `.cdf`, `.sf`, `.ppf`.
  Les paramètres sont rangés dans les registres lettrés lus par la C47 (`M` `S` pour loc / scale, `N` `P` pour n / p,
  `R` pour λ…), puis l'instruction (`Normlₚ`, `Binom◭`…). Les conventions qui diffèrent de scipy sont compensées :
  `sf` discret (la C47 donne P(X ≥ k)), loi géométrique (la C47 compte les échecs), binomiale négative (1 − p).
  Les 52 combinaisons loi × méthode sont vérifiées sur le simulateur.
- `c47.NOM(...)` (`import c47`) : n'importe quelle instruction C47 par son nom, arguments dans l'ordre de la pile ;
  `c47.call("ft→m", x)` pour un nom qui contient des symboles (conversions, dates, lois…) : toutes les instructions
  de la C47 sont ainsi accessibles. L'aide C47 de DM42 Editor les liste toutes par domaine.
- Opérateurs bit à bit `&` `|` `^` `~` : en entiers courts (`SINT` … `AND` / `OR` / `XOR` / `NOT` … `LINT`), car sur
  des entiers longs ces instructions sont logiques ; `<<` et `>>` passent par 2ⁿ, exacts en entier long.
- `math.fmod` → `RMD`, `numpy.sign` → `sign`.

Les 80 fonctions sont vérifiées sur le simulateur C47 contre Python ou des valeurs publiées (écart relatif < 10⁻⁹).
Quand la C47 attend les arguments dans l'autre ordre que Python (`ellipkinc`, `gammainc`…), la table les inverse.

Six exemples propres à la C47 (`ExamplesC47`, menu « Exemples C47 / R47 » de DM42 Editor) montrent ce que cette cible
ajoute : factorielle récursive en entiers longs exacts (25!), Fibonacci récursif et itératif, tours de Hanoï, années
bissextiles (`and` / `or`), Collatz et recherche de nombre premier (`while`, `break`), textes de plus de 6 caractères.

Douze programmes de la distribution C47 (dossier `PROGRAMS` du simulateur) sont réécrits en Python
(`ExamplesC47.Programs`, sous-menu « Programmes de la distribution C47 ») : BMI, FactIt, NQueens, PAYCALC, TRI∡ (cas
côté-côté-côté), sums, FreeF, IBessI, OpAmp, ECA, Bairsto et STRUCT. Chacun donne au simulateur C47 les résultats de
Python, sur 34 chiffres. Les jeux, graphiques, menus et programmes à manipulation de textes (ROMANv2) ne sont pas repris.

Le listing garde les noms HP-42S des fonctions (`+/-`, `ARCL`…) : DM42 Editor les convertit (`C47.Core/Hp42Converter.cs`),
encode le `.p47` et lance le simulateur C47 (F5 de la fenêtre Python). Le simulateur intégré de Py42 reste celui de la HP-42S.

## Limites (à connaître)

- **Pile de 4 niveaux** : l'ordre d'évaluation est optimisé (méthode de Sethi-Ullman) et ce qui dépasse est calculé à l'avance dans des variables `S1`, `S2`…
- **Variables globales** : toutes les variables HP42 sont globales. Les variables locales d'une fonction qui portent le même nom qu'ailleurs sont renommées automatiquement (`a` dans `delta` devient `A_DEL`). La **récursivité est refusée**.
- **Noms** : 7 caractères maximum (tronqués avec un avertissement). Chaînes de 15 caractères maximum par pas, 14 pour `⊢"..."` (découpage automatique), ALPHA limité à 44 caractères.
- **Angles** : Python calcule en radians ; l'option « RAD automatique » ajoute `RAD` en tête quand le programme fait de la trigonométrie.
- **`//`** devient `÷ IP` (troncature vers zéro) : différent de Python pour les nombres négatifs.
- **99 labels locaux** au plus par programme.
- Pas de chaînes manipulées, f-strings, dictionnaires, classes, exceptions, valeurs par défaut des paramètres. Les listes deviennent des matrices HP (pas de `append`).
- La HP-42S ne distingue pas les majuscules : `b` et `B` sont la même variable (Py42 le signale).
- La HP-42S d'origine a 8 niveaux de retour pour `XEQ` ; la DM42 en a davantage.
- Le simulateur calcule en double précision ; la DM42 calcule en décimal sur 34 chiffres, de petites différences d'arrondi sont possibles.

## Langue

Les messages et l'aide existent en français et en anglais (`Py42.Core/py42_en.txt`). Le français reste la langue par défaut ; `py42 --en` et DM42 Editor activent l'anglais. Py42 Studio reste en français.

## Licence

GNU GPLv3, comme [DM42 Editor](https://github.com/philgerm57/DM42Editor) qui intègre Py42.Core.

HP-42S est une marque de HP Inc. ; DM42 est un produit SwissMicros. Ce projet n'est affilié à aucune de ces sociétés.
