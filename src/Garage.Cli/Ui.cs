using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Garage.Cli;

/// <summary>Console I/O with support for scripted input (one answer per line) and no-color mode.</summary>
public sealed class Ui
{
    private readonly Queue<string>? _script;
    private readonly TextReader _in;
    private readonly TextWriter _out;

    public Ui(TextReader input, TextWriter output, IEnumerable<string>? script = null)
    {
        _in = input;
        _out = output;
        if (script != null)
        {
            _script = new Queue<string>(script);
        }
    }

    /// <summary>True when the input is a script or redirected (no live cursor tricks).</summary>
    public bool Scripted => _script != null || Console.IsInputRedirected;

    public bool Echo { get; set; } = true;

    public void Line(string s = "") => _out.WriteLine(s);

    public void Title(string s)
    {
        _out.WriteLine();
        _out.WriteLine("══ " + s + " " + new string('═', Math.Max(3, 70 - s.Length)));
    }

    public void Info(string s) => _out.WriteLine("  " + s);

    public string Ask(string prompt)
    {
        _out.Write(prompt + " > ");
        string? line;
        if (_script != null)
        {
            line = _script.Count > 0 ? _script.Dequeue() : "0";
            if (Echo)
            {
                _out.WriteLine(line);
            }
        }
        else
        {
            line = _in.ReadLine();
            if (line == null)
            {
                line = "0";
                _out.WriteLine();
            }
            else if (Console.IsInputRedirected && Echo)
            {
                _out.WriteLine(line);
            }
        }

        return line.Trim();
    }

    public int Menu(string title, IReadOnlyList<string> options, bool zeroIsBack = true)
    {
        Title(title);
        for (int i = 0; i < options.Count; i++)
        {
            _out.WriteLine($"  {i + 1,2}. {options[i]}");
        }

        _out.WriteLine(zeroIsBack ? "   0. Volver" : "   0. Salir");
        while (true)
        {
            string a = Ask("Opción");
            if (int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0 && n <= options.Count)
            {
                return n;
            }

            _out.WriteLine("  Opción no válida.");
            if (_script != null && _script.Count == 0)
            {
                return 0;
            }
        }
    }

    public double AskNumber(string prompt, double fallback)
    {
        string a = Ask(prompt);
        return double.TryParse(a.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
    }

    public bool Confirm(string prompt)
    {
        string a = Ask(prompt + " (s/n)").ToLowerInvariant();
        return a == "s" || a == "si" || a == "sí" || a == "y";
    }
}
