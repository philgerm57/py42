namespace Py42.Core;

public enum K
{
    LblG,       // LBL "NOM"
    LblL,       // LBL 01
    Num,        // 3.14
    Rcl, Sto,   // RCL "X" / STO "X"
    StoOp,      // STO+ "X"
    RclOp,      // RCL× "X"
    Op,         // + - × ÷ Y↑X MOD
    Fn,         // SQRT, SIN, +/- ...
    Const,      // PI
    Test,       // X<Y? ...
    Gto, Xeq, Rtn, End,
    Stk,        // R↓, X<>Y, STO ST Y, →POL
    ViewVar, ViewX,
    AStr,       // "TEXTE"
    AStrAppend, // ⊢"TEXTE" (14 caractères max)
    ArclVar, ArclX, Cla, AView,
    Input, Mode, Stop,
    Cmd,        // commande HP42S libre : texte dans Arg (BEEP, FIX 02, SF 21, PIXEL...)
    KeyGto,     // KEY n GTO nn : Arg = touche (1-9), N = label local
    Mvar,       // MVAR "X"
    VarMenu     // VARMENU "PROG"
}

/// <summary>Une ligne de programme HP42S, avec la ligne Python qui l'a produite.</summary>
public sealed class Instr
{
    public K Kind;
    public string Arg = "";
    public int N;
    public int Line;

    public Instr(K kind, string arg = "", int n = 0, int line = 0) { Kind = kind; Arg = arg; N = n; Line = line; }

    /// <summary>Texte au format des listings Free42 / DM42.</summary>
    /// <summary>Instruction qui saute vers un label local (GTO nn ou KEY n GTO nn).</summary>
    public bool IsJump => Kind is K.Gto or K.KeyGto;

    public override string ToString() => Kind switch
    {
        K.LblG => $"LBL \"{Arg}\"",
        K.LblL => $"LBL {N:00}",
        K.Num => Arg,
        K.Rcl => $"RCL \"{Arg}\"",
        K.Sto => $"STO \"{Arg}\"",
        K.StoOp => $"STO{Op(N)} \"{Arg}\"",
        K.RclOp => $"RCL{Op(N)} \"{Arg}\"",
        K.Op or K.Fn or K.Const or K.Test or K.Stk or K.Mode => Arg,
        K.Gto => $"GTO {N:00}",
        K.Xeq => $"XEQ \"{Arg}\"",
        K.Rtn => "RTN",
        K.End => "END",
        K.ViewVar => $"VIEW \"{Arg}\"",
        K.ViewX => "VIEW ST X",
        K.AStr => $"\"{Arg}\"",
        K.AStrAppend => $"⊢\"{Arg}\"",
        K.ArclVar => $"ARCL \"{Arg}\"",
        K.ArclX => "ARCL ST X",
        K.Cla => "CLA",
        K.AView => "AVIEW",
        K.Input => $"INPUT \"{Arg}\"",
        K.Stop => "STOP",
        K.Cmd => Arg,
        K.KeyGto => $"KEY {Arg} GTO {N:00}",
        K.Mvar => $"MVAR \"{Arg}\"",
        K.VarMenu => $"VARMENU \"{Arg}\"",
        _ => "?"
    };

    // Opérateur des STO+/RCL+ : 0:+ 1:- 2:× 3:÷
    public static readonly string[] Ops = { "+", "-", "×", "÷" };
    static string Op(int n) => Ops[n];
}
