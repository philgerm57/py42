#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DM42;
using DM42.decoder;
using DM42.encoder;

namespace Py42.Raw
{
    /// <summary>
    /// Listing RPN → fichier .raw (Free42 / DM42), avec l'encodeur de dm42dll : le même que
    /// « Encode and save RAW » de DM42editor. Fichier partagé par DM42editor, Py42 et py42 (ligne de commande).
    /// </summary>
    public static class RawExport
    {
        /// <summary>Encode un listing (format Free42, avec ou sans numéros de ligne). Renvoie null en cas d'erreur.</summary>
        public static byte[] Encode(string listing, out List<string> errors)
        {
            errors = new List<string>();
            Encoder42.initialize();
            var doc = listing.Replace("\r", "").Split('\n').Select(l => Encoder42.FormatRpnLine(l)).ToList();
            var parser = new RpnParser();
            parser.SetDocument(doc);
            parser.parse();
            if (parser.programs.Count == 0) { errors.Add("Aucun programme dans le listing"); return null; }

            foreach (var prog in parser.programs)
                foreach (var e in prog.getErrors()) errors.Add(e.ToString(false));
            if (errors.Count > 0) return null;

            var bytes = new List<byte>();
            foreach (var prog in parser.programs)
                foreach (RawLine line in prog.rawLines)
                {
                    Encoder42.toRaw(line, "free42");
                    if (line.hasError()) { errors.Add(line.error.ToString(false)); continue; }
                    if (string.IsNullOrEmpty(line.raw)) continue;
                    foreach (var hex in line.raw.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b)) bytes.Add(b);
                        else errors.Add($"Octet invalide « {hex} »");
                    }
                }
            return errors.Count > 0 ? null : bytes.ToArray();
        }

        /// <summary>Encode le résultat d'une conversion Python → RPN.</summary>
        public static byte[] Encode(Py42.Core.CompileResult result, out List<string> errors)
            => Encode(result.Listing(true, header: true), out errors);
    }
}
