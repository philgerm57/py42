using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Win32;
using Py42.Core;

namespace Py42.Studio;

public sealed record Msg(string Text, int Line);

public partial class MainWindow : Window
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly LineHighlighter errorMark = new(Color.FromArgb(80, 214, 64, 40));
    readonly LineHighlighter pyMark = new(Color.FromArgb(70, 224, 137, 47));
    readonly LineHighlighter rpnMark = new(Color.FromArgb(110, 224, 137, 47));
    CompileResult? last;
    string? currentFile;
    string preferredEntry = "MAIN";
    bool ready, syncing;
    const int H = 1; // ligne « 00 { Prgm } » en tête du listing

    public MainWindow()
    {
        InitializeComponent();

        PyEditor.SyntaxHighlighting = LoadXshd("Python.xshd");
        RpnEditor.SyntaxHighlighting = LoadXshd("Rpn.xshd");
        PyEditor.Options.ConvertTabsToSpaces = true;
        PyEditor.Options.IndentationSize = 4;
        PyEditor.Options.EnableHyperlinks = false;
        PyEditor.Options.EnableEmailHyperlinks = false;
        PyEditor.TextArea.IndentationStrategy = new PythonIndentation();
        PyEditor.TextArea.TextView.BackgroundRenderers.Add(errorMark);
        PyEditor.TextArea.TextView.BackgroundRenderers.Add(pyMark);
        RpnEditor.Options.EnableHyperlinks = false;
        RpnEditor.TextArea.TextView.BackgroundRenderers.Add(rpnMark);

        PyEditor.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        PyEditor.TextArea.Caret.PositionChanged += (_, _) => SyncFromPython();
        RpnEditor.TextArea.Caret.PositionChanged += (_, _) => SyncFromRpn();
        timer.Tick += (_, _) => { timer.Stop(); Recompile(); };

        foreach (var ex in Examples.All) ExampleBox.Items.Add(ex.Title);
        ready = true;
        ExampleBox.SelectedIndex = 0;
    }

    // ------------------------------------------------------------------
    //  Conversion
    // ------------------------------------------------------------------
    void Recompile()
    {
        if (!ready) return;
        var opts = new CompileOptions
        {
            ProgramName = ProgName.Text,
            Radians = RadBox.IsChecked == true,
            UpperCase = UpperBox.IsChecked == true
        };
        Messages.Items.Clear();
        errorMark.Lines.Clear();
        try
        {
            last = Compiler.Compile(PyEditor.Text, opts);
            RpnEditor.Text = last.Listing(NumBox.IsChecked == true);
            RpnEditor.Opacity = 1;
            foreach (var w in last.Warnings) Messages.Items.Add(new Msg("⚠  " + w, LineIn(w)));
            if (last.Warnings.Count == 0) Messages.Items.Add(new Msg("✓  Conversion réussie", 0));
            Status.Text = $"{last.Code.Count} pas   ·   {last.LocalLabels}/99 labels locaux   ·   pile max {last.MaxStack}/4   ·   " +
                          $"{last.Variables.Count} variable(s) : {string.Join(" ", last.Variables)}";

            string? keep = EntryBox.SelectedItem as string ?? preferredEntry;
            EntryBox.Items.Clear();
            foreach (var e in last.Entries) EntryBox.Items.Add(e);
            EntryBox.SelectedItem = last.Entries.Contains(keep) ? keep : last.Entries.FirstOrDefault();
        }
        catch (CompileError e)
        {
            last = null;
            Messages.Items.Add(new Msg(e.Line > 0 ? $"✖  Ligne {e.Line} : {e.Message}" : "✖  " + e.Message, e.Line));
            if (e.Line > 0) errorMark.Lines.Add(e.Line);
            RpnEditor.Opacity = 0.4;
            Status.Text = "Erreur : le listing grisé est celui de la dernière conversion réussie.";
        }
        PyEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        SyncFromPython();
    }

    static int LineIn(string text)
    {
        var m = Regex.Match(text, @"Ligne (\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    // ------------------------------------------------------------------
    //  Correspondance Python ↔ RPN
    // ------------------------------------------------------------------
    void SyncFromPython()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            pyMark.Lines.Clear();
            rpnMark.Lines.Clear();
            if (last != null && !RpnEditor.IsKeyboardFocusWithin)
            {
                int src = PyEditor.TextArea.Caret.Line;
                int first = -1;
                for (int i = 0; i < last.Code.Count; i++)
                {
                    if (last.Code[i].Line != src) continue;
                    rpnMark.Lines.Add(i + 1 + H);
                    if (first < 0) first = i + 1 + H;
                }
                if (first > 0 && first <= RpnEditor.Document.LineCount) RpnEditor.ScrollToLine(first);
            }
            Refresh();
        }
        finally { syncing = false; }
    }

    void SyncFromRpn()
    {
        if (syncing || last == null || !RpnEditor.IsKeyboardFocusWithin) return;
        syncing = true;
        try
        {
            int i = RpnEditor.TextArea.Caret.Line - 1 - H;
            pyMark.Lines.Clear();
            rpnMark.Lines.Clear();
            if (i >= 0 && i < last.Code.Count && last.Code[i].Line > 0)
            {
                int src = last.Code[i].Line;
                pyMark.Lines.Add(src);
                for (int j = 0; j < last.Code.Count; j++)
                    if (last.Code[j].Line == src) rpnMark.Lines.Add(j + 1 + H);
                if (src <= PyEditor.Document.LineCount) PyEditor.ScrollToLine(src);
            }
            Refresh();
        }
        finally { syncing = false; }
    }

    void Refresh()
    {
        PyEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        RpnEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    void Message_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (Messages.SelectedItem is not Msg m || m.Line <= 0 || m.Line > PyEditor.Document.LineCount) return;
        PyEditor.ScrollToLine(m.Line);
        PyEditor.TextArea.Caret.Location = new TextLocation(m.Line, 1);
        PyEditor.Focus();
    }

    // ------------------------------------------------------------------
    //  Simulation
    // ------------------------------------------------------------------
    static double[] Nums(string s) => s
        .Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(v => double.Parse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture))
        .ToArray();

    void Run_Click(object sender, RoutedEventArgs e) => RunSimulation();

    void RunSimulation()
    {
        if (last == null) { SimOut.Text = "Corrigez d'abord les erreurs signalées dans les messages."; return; }
        if (EntryBox.SelectedItem is not string entry) { SimOut.Text = "Aucun label à lancer."; return; }
        double[] stack, inputs;
        try { stack = Nums(StackBox.Text); inputs = Nums(InputsBox.Text); }
        catch (FormatException) { SimOut.Text = "Valeur invalide : séparez les nombres par des espaces ou des points-virgules."; return; }

        var r = Simulator.Run(last.Code, entry, stack, inputs);
        var sb = new StringBuilder();
        foreach (var d in r.Display) sb.AppendLine(d);
        if (r.Error != null)
        {
            sb.AppendLine().AppendLine($"ERREUR au pas {r.ErrorPc + 1} : {r.Error}");
            if (r.ErrorPc >= 0 && r.ErrorPc < last.Code.Count)
            {
                rpnMark.Lines.Clear();
                rpnMark.Lines.Add(r.ErrorPc + 1 + H);
                RpnEditor.ScrollToLine(r.ErrorPc + 1 + H);
                int src = last.Code[r.ErrorPc].Line;
                pyMark.Lines.Clear();
                if (src > 0) { pyMark.Lines.Add(src); PyEditor.ScrollToLine(src); }
                Refresh();
            }
        }
        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine($"T: {Simulator.Fmt(r.T)}");
        sb.AppendLine($"Z: {Simulator.Fmt(r.Z)}");
        sb.AppendLine($"Y: {Simulator.Fmt(r.Y)}");
        sb.AppendLine($"X: {Simulator.Fmt(r.X)}");
        sb.AppendLine();
        sb.Append($"{r.Steps} pas exécutés");
        if (r.MaxCallDepth > 0) sb.Append($", {r.MaxCallDepth} niveau(x) d'appel XEQ");
        if (r.MaxCallDepth > 8) sb.Append(" (attention : la HP-42S d'origine n'a que 8 niveaux de retour, la DM42 davantage)");
        SimOut.Text = sb.ToString();
    }

    // ------------------------------------------------------------------
    //  Fichiers et presse-papiers
    // ------------------------------------------------------------------
    void Example_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || ExampleBox.SelectedIndex < 0) return;
        var ex = Examples.All[ExampleBox.SelectedIndex];
        preferredEntry = ex.Entry;
        EntryBox.Items.Clear();
        PyEditor.Text = ex.Source.TrimStart('\n');
        StackBox.Text = ex.Stack;
        InputsBox.Text = ex.Inputs;
        SimOut.Text = "";
        currentFile = null;
        UpdateTitle();
        timer.Stop();
        Recompile();
        if (last != null && last.Entries.Contains(ex.Entry)) EntryBox.SelectedItem = ex.Entry;
    }

    void Option_Changed(object sender, RoutedEventArgs e) { if (ready) Recompile(); }

    void Open_Click(object sender, RoutedEventArgs e) => OpenFile();

    void OpenFile()
    {
        var dlg = new OpenFileDialog { Filter = "Python (*.py)|*.py|Tous les fichiers (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            PyEditor.Text = File.ReadAllText(dlg.FileName);
            currentFile = dlg.FileName;
            ExampleBox.SelectionChanged -= Example_Changed;
            ExampleBox.SelectedIndex = -1;
            ExampleBox.SelectionChanged += Example_Changed;
            UpdateTitle();
            timer.Stop();
            Recompile();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ouverture impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void SavePy_Click(object sender, RoutedEventArgs e) => SavePython();

    void SavePython()
    {
        if (currentFile == null)
        {
            var dlg = new SaveFileDialog { Filter = "Python (*.py)|*.py", FileName = "programme.py" };
            if (dlg.ShowDialog(this) != true) return;
            currentFile = dlg.FileName;
        }
        try
        {
            File.WriteAllText(currentFile, PyEditor.Text, new UTF8Encoding(false));
            Status.Text = $"Enregistré : {currentFile}";
            UpdateTitle();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Enregistrement impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        if (last == null) { Status.Text = "Rien à exporter : corrigez d'abord les erreurs."; return; }
        string name = string.IsNullOrWhiteSpace(ProgName.Text) ? "MAIN" : ProgName.Text.Trim();
        if (currentFile != null) name = Path.GetFileNameWithoutExtension(currentFile);
        var dlg = new SaveFileDialog { Filter = "Listing texte (*.txt)|*.txt", FileName = name + ".txt" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, last.Listing(NumBox.IsChecked == true), new UTF8Encoding(false));
            Status.Text = $"Listing exporté : {dlg.FileName}";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    string? lastRawDir;

    void Raw_Click(object sender, RoutedEventArgs e) => SaveRaw();

    /// <summary>Fichier .raw direct, avec l'encodeur de dm42dll (le même que DM42editor).</summary>
    void SaveRaw()
    {
        if (last == null) { Status.Text = "Rien à encoder : corrigez d'abord les erreurs."; return; }
        var bytes = Py42.Raw.RawExport.Encode(last, out var errors);
        if (bytes == null)
        {
            MessageBox.Show(this, "Le listing n'a pas pu être encodé :\n\n" + string.Join("\n", errors.Distinct().Take(15)),
                            "Générer .raw", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string name = currentFile != null ? Path.GetFileNameWithoutExtension(currentFile)
                    : (string.IsNullOrWhiteSpace(ProgName.Text) ? "MAIN" : ProgName.Text.Trim());
        var dlg = new SaveFileDialog
        {
            Title = "Enregistrer le programme pour la DM42 / Free42",
            Filter = "Programme HP-42S (*.raw)|*.raw",
            DefaultExt = ".raw",
            FileName = name + ".raw",
            InitialDirectory = lastRawDir ?? (currentFile != null ? Path.GetDirectoryName(currentFile)
                             : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, bytes);
            lastRawDir = Path.GetDirectoryName(dlg.FileName);
            Status.Text = $"{Path.GetFileName(dlg.FileName)} : {bytes.Length} octets, {last.Code.Count} pas. DM42 : copiez-le sur son disque USB puis Load Program ; Free42 : File → Import Programs.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Écriture impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (last == null) { Status.Text = "Rien à copier : corrigez d'abord les erreurs."; return; }
        try
        {
            Clipboard.SetText(last.Listing(NumBox.IsChecked == true));
            Status.Text = "Listing copié. Dans Free42 : Edit → Paste Program, puis File → Export Programs pour obtenir le .raw de la DM42.";
        }
        catch (Exception ex) { Status.Text = "Presse-papiers indisponible : " + ex.Message; }
    }

    void UpdateTitle() =>
        Title = currentFile == null ? "Py42 — Python vers RPN HP42S / DM42" : $"Py42 — {Path.GetFileName(currentFile)}";

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.F5) { RunSimulation(); e.Handled = true; }
        else if (e.Key == Key.F8) { SaveRaw(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { SavePython(); e.Handled = true; }
        else if (ctrl && e.Key == Key.O) { OpenFile(); e.Handled = true; }
    }

    static IHighlightingDefinition LoadXshd(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                      ?? throw new InvalidOperationException("Ressource manquante : " + name);
        using var r = XmlReader.Create(s);
        return HighlightingLoader.Load(r, HighlightingManager.Instance);
    }
}

/// <summary>Surligne des lignes entières dans un éditeur AvalonEdit.</summary>
sealed class LineHighlighter : IBackgroundRenderer
{
    readonly Brush brush;
    public HashSet<int> Lines { get; } = new();

    public LineHighlighter(Color c)
    {
        brush = new SolidColorBrush(c);
        brush.Freeze();
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Lines.Count == 0 || !textView.VisualLinesValid) return;
        foreach (var vl in textView.VisualLines)
        {
            if (!Lines.Contains(vl.FirstDocumentLine.LineNumber)) continue;
            double y = vl.VisualTop - textView.VerticalOffset;
            drawingContext.DrawRectangle(brush, null, new Rect(0, y, Math.Max(textView.ActualWidth, 1), vl.Height));
        }
    }
}

/// <summary>Indentation à la Python : +4 après « : », −4 après return / break / continue / pass.</summary>
sealed class PythonIndentation : DefaultIndentationStrategy
{
    static readonly Regex Dedent = new(@"^\s*(return\b|break\b|continue\b|pass\b)");

    public override void IndentLine(TextDocument document, DocumentLine line)
    {
        base.IndentLine(document, line);
        var prev = line.PreviousLine;
        if (prev == null) return;
        string t = document.GetText(prev);
        int hash = t.IndexOf('#');
        if (hash >= 0) t = t[..hash];
        t = t.TrimEnd();
        if (t.EndsWith(':'))
        {
            document.Insert(line.Offset, "    ");
        }
        else if (Dedent.IsMatch(t))
        {
            int n = 0;
            while (n < 4 && line.Offset + n < document.TextLength && document.GetCharAt(line.Offset + n) == ' ') n++;
            if (n > 0) document.Remove(line.Offset, n);
        }
    }
}
