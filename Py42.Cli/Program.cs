using System.Globalization;
using System.Text;
using Py42.Core;

// py42 programme.py [-o sortie.txt] [--sans-numeros] [--sans-rad] [--minuscules] [--nom MAIN]
//                   [--raw sortie.raw] [--run LABEL] [--pile "1 2"] [--entrees "3 4"] [--exemples]
Console.OutputEncoding = Encoding.UTF8;
var opts = new CompileOptions();
string? input = null, output = null, rawOut = null, run = null, pile = "", entrees = "";
bool numbers = true, examples = false, dumpVars = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-o": output = args[++i]; break;
        case "--raw": rawOut = args[++i]; break;
        case "--sans-numeros": numbers = false; break;
        case "--sans-rad": opts.Radians = false; break;
        case "--minuscules": opts.UpperCase = false; break;
        case "--nom": opts.ProgramName = args[++i]; break;
        case "--run": run = args[++i]; break;
        case "--pile": pile = args[++i]; break;
        case "--entrees": entrees = args[++i]; break;
        case "--exemples": examples = true; break;
        case "--vars": dumpVars = true; break;
        default: input = args[i]; break;
    }
}

static double[] Nums(string s) => s.Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries)
    .Select(v => double.Parse(v.Replace(',', '.'), CultureInfo.InvariantCulture)).ToArray();

int Process(string title, string src, string? entry, string stack, string inputs)
{
    try
    {
        var res = Compiler.Compile(src, opts);
        var text = res.Listing(numbers);
        if (output != null) File.WriteAllText(output, text, new UTF8Encoding(false));
        else Console.Write(text);
        foreach (var w in res.Warnings) Console.Error.WriteLine("Avertissement : " + w);
        if (rawOut != null)
        {
            // fichier .raw pour la DM42 / Free42 (encodeur dm42dll)
            var bytes = Py42.Raw.RawExport.Encode(res, out var rawErrors);
            if (bytes == null)
            {
                foreach (var er in rawErrors.Distinct()) Console.Error.WriteLine("Erreur d'encodage : " + er);
                return 1;
            }
            File.WriteAllBytes(rawOut, bytes);
            Console.Error.WriteLine($"{rawOut} : {bytes.Length} octets");
        }
        Console.Error.WriteLine($"{res.Code.Count} pas, {res.LocalLabels} labels locaux, pile max {res.MaxStack}, variables : {string.Join(" ", res.Variables)}");
        if (entry != null)
        {
            var sim = Simulator.Run(res.Code, entry, Nums(stack), Nums(inputs));
            Console.Error.WriteLine($"--- Simulation {title} ({entry}) ---");
            foreach (var d in sim.Display) Console.Error.WriteLine("  " + d);
            if (sim.Error != null) Console.Error.WriteLine($"  ERREUR au pas {sim.ErrorPc + 1} : {sim.Error}");
            Console.Error.WriteLine($"  X={Simulator.Fmt(sim.X)}  Y={Simulator.Fmt(sim.Y)}  ({sim.Steps} pas exécutés)");
            if (dumpVars)
            {
                static string J(Val v)
                {
                    static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);
                    static string C(System.Numerics.Complex c) => c.Imaginary == 0 ? N(c.Real) : $"[{N(c.Real)},{N(c.Imaginary)}]";
                    if (v.IsReal) return N(v.Re);
                    if (v.IsCx) return $"[{N(v.Re)},{N(v.Im)}]";
                    var rows = new List<string>();
                    for (int i = 0; i < v.Rows; i++)
                        rows.Add("[" + string.Join(",", Enumerable.Range(0, v.Cols).Select(j => C(v.M![i, j]))) + "]");
                    return "{\"m\":[" + string.Join(",", rows) + "]}";
                }
                Console.WriteLine("VARS " + "{" + string.Join(",", sim.Vars.Select(kv => $"\"{kv.Key}\":{J(kv.Value)}")) + "}");
                if (sim.Error != null) Console.WriteLine("SIMERR " + sim.Error);
            }
        }
        return 0;
    }
    catch (CompileError e)
    {
        Console.Error.WriteLine(e.Line > 0 ? $"Erreur ligne {e.Line} : {e.Message}" : $"Erreur : {e.Message}");
        return 1;
    }
}

if (examples)
{
    int rc = 0;
    foreach (var ex in Examples.All) { Console.WriteLine($"===== {ex.Title} ====="); rc |= Process(ex.Title, ex.Source, ex.Entry, ex.Stack, ex.Inputs); }
    return rc;
}
if (input == null)
{
    Console.Error.WriteLine("Usage : py42 programme.py [-o sortie.txt] [--raw sortie.raw] [--sans-numeros] [--sans-rad] [--minuscules] [--nom MAIN] [--run LABEL --pile \"1 2\" --entrees \"3 4\"]");
    return 2;
}
return Process(input, File.ReadAllText(input), run, pile, entrees);
