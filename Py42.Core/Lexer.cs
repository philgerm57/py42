using System.Text;
using System.Text.RegularExpressions;

namespace Py42.Core;

public enum T { Num, Str, Name, Kw, Op, NL, Indent, Dedent, EOF }

public sealed record Tok(T Kind, string V, int Line);

/// <summary>Découpe un source Python en jetons, avec INDENT / DEDENT comme CPython.</summary>
public static class Lexer
{
    public static readonly HashSet<string> Keywords = new()
    {
        "def","if","elif","else","while","for","in","return","break","continue","pass",
        "and","or","not","True","False","None","import","from","global","as","lambda",
        "class","try","except","finally","with","yield","del","is","nonlocal","raise","assert"
    };

    static readonly string[] Ops =
    {
        "**=","//=","**","//","@=","@","==","!=","<=",">=","+=","-=","*=","/=","%=",
        "+","-","*","/","%","<",">","=","(",")",",",":",".","[","]",";","{","}"
    };

    static readonly Regex NumRx = new(@"\G(\d[\d_]*\.[\d_]*|\.\d[\d_]*|\d[\d_]*)([eE][+-]?\d+)?", RegexOptions.Compiled);
    static readonly Regex IdRx = new(@"\G[\p{L}_][\p{L}\p{Nd}_]*", RegexOptions.Compiled);

    /// <summary>
    /// Commentaires multilignes """ ... """ ou ''' ... ''' (docstrings) : remplacés par des espaces,
    /// sauts de ligne conservés pour garder les numéros de ligne. Rien n'est produit dans le RPN.
    /// Ils doivent commencer une ligne (comme une docstring) : x = """...""" n'est pas accepté.
    /// </summary>
    public static string BlankDocstrings(string src)
    {
        src = src.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!src.Contains("\"\"\"") && !src.Contains("'''")) return src;
        var sb = new StringBuilder(src);
        int line = 1, i = 0;
        bool lineStart = true;   // seulement des espaces depuis le début de la ligne
        while (i < src.Length)
        {
            char c = src[i];
            if (c == '\n') { line++; lineStart = true; i++; continue; }
            if (c == ' ' || c == '\t') { i++; continue; }
            if (c == '#') { while (i < src.Length && src[i] != '\n') i++; continue; }
            bool triple = (c == '"' || c == '\'') && i + 2 < src.Length && src[i + 1] == c && src[i + 2] == c;
            if (triple)
            {
                if (!lineStart)
                    throw new CompileError("Les chaînes \"\"\" … \"\"\" ne servent que de commentaires : placez-les en début de ligne", line);
                string q = new string(c, 3);
                int end = src.IndexOf(q, i + 3, StringComparison.Ordinal);
                if (end < 0) throw new CompileError($"Commentaire {q} non terminé", line);
                for (int k = i; k < end + 3; k++)
                    if (src[k] == '\n') line++;
                    else sb[k] = ' ';
                i = end + 3;
                lineStart = false;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                // chaîne ordinaire : on la saute pour ne pas y chercher de """
                int j = i + 1;
                while (j < src.Length && src[j] != c && src[j] != '\n') j += src[j] == '\\' ? 2 : 1;
                i = Math.Min(src.Length, j + 1);
                lineStart = false;
                continue;
            }
            lineStart = false;
            i++;
        }
        return sb.ToString();
    }

    public static List<Tok> Tokenize(string src)
    {
        var lines = BlankDocstrings(src).Split('\n');
        var toks = new List<Tok>();
        var indents = new Stack<int>();
        indents.Push(0);
        int paren = 0;
        bool joined = false; // ligne précédente terminée par « \ »

        for (int ln = 0; ln < lines.Length; ln++)
        {
            string s = lines[ln];
            int L = ln + 1, i = 0;

            if (paren == 0 && !joined)
            {
                int col = 0;
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) { col += s[i] == '\t' ? 4 : 1; i++; }
                if (i >= s.Length || s[i] == '#') continue; // ligne vide ou commentaire
                if (col > indents.Peek()) { indents.Push(col); toks.Add(new(T.Indent, "", L)); }
                else
                {
                    while (col < indents.Peek()) { indents.Pop(); toks.Add(new(T.Dedent, "", L)); }
                    if (col != indents.Peek()) throw new CompileError("Indentation incohérente avec les lignes précédentes", L);
                }
            }
            joined = false;

            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t') { i++; continue; }
                if (c == '#') break;
                if (c == '\\' && s[(i + 1)..].Trim().Length == 0) { joined = true; break; }

                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    var m = NumRx.Match(s, i);
                    string num = m.Value.Replace("_", "");
                    i += m.Length;
                    // 4j : imaginaire pur (le j collé au nombre, comme en Python)
                    if (i < s.Length && (s[i] == 'j' || s[i] == 'J') && (i + 1 >= s.Length || !(char.IsLetterOrDigit(s[i + 1]) || s[i + 1] == '_')))
                    {
                        num += "j";
                        i++;
                    }
                    toks.Add(new(T.Num, num, L));
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    var m = IdRx.Match(s, i);
                    string w = m.Value;
                    i += m.Length;
                    if (i < s.Length && (s[i] == '"' || s[i] == '\'') && w.ToLowerInvariant() is "f" or "rf" or "fr")
                        throw new CompileError("Les f-strings ne sont pas prises en charge : utilisez print(\"texte\", x)", L);
                    if (i < s.Length && (s[i] == '"' || s[i] == '\'') && w.ToLowerInvariant() is "r" or "u")
                        continue; // préfixe de chaîne sans effet ici
                    toks.Add(new(Keywords.Contains(w) ? T.Kw : T.Name, w, L));
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    var sb = new StringBuilder();
                    int j = i + 1;
                    while (j < s.Length && s[j] != c)
                    {
                        if (s[j] == '\\' && j + 1 < s.Length)
                        {
                            char e = s[j + 1];
                            sb.Append(e switch { 'n' => ' ', 't' => ' ', _ => e });
                            j += 2;
                            continue;
                        }
                        sb.Append(s[j]);
                        j++;
                    }
                    if (j >= s.Length) throw new CompileError("Chaîne de caractères non terminée", L);
                    toks.Add(new(T.Str, sb.ToString(), L));
                    i = j + 1;
                    continue;
                }
                string? op = null;
                foreach (var o in Ops)
                    if (string.CompareOrdinal(s, i, o, 0, o.Length) == 0) { op = o; break; }
                if (op == null) throw new CompileError($"Caractère inattendu « {c} »", L);
                if (op is "(" or "[" or "{") paren++;
                if (op is ")" or "]" or "}") paren = Math.Max(0, paren - 1);
                toks.Add(new(T.Op, op, L));
                i += op.Length;
            }

            if (paren == 0 && !joined && toks.Count > 0 && toks[^1].Kind != T.NL && toks[^1].Kind != T.Indent && toks[^1].Kind != T.Dedent)
                toks.Add(new(T.NL, "", L));
        }

        if (paren > 0) throw new CompileError("Parenthèse non fermée en fin de fichier", lines.Length);
        int last = lines.Length;
        if (toks.Count > 0 && toks[^1].Kind != T.NL) toks.Add(new(T.NL, "", last));
        while (indents.Count > 1) { indents.Pop(); toks.Add(new(T.Dedent, "", last)); }
        toks.Add(new(T.EOF, "", last));
        return toks;
    }
}
