using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Garage.Data.Json
{
    /// <summary>JSON value kind.</summary>
    public enum JsonKind
    {
        /// <summary>null.</summary>
        Null,
        /// <summary>true/false.</summary>
        Bool,
        /// <summary>Number.</summary>
        Number,
        /// <summary>String.</summary>
        String,
        /// <summary>Array.</summary>
        Array,
        /// <summary>Object.</summary>
        Object,
    }

    /// <summary>Thrown on malformed JSON, with line and column.</summary>
    public sealed class JsonParseException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public JsonParseException(string message, int line, int column)
            : base($"{message} (línea {line}, columna {column})")
        {
            Line = line;
            Column = column;
        }

        /// <summary>1-based line.</summary>
        public int Line { get; }

        /// <summary>1-based column.</summary>
        public int Column { get; }
    }

    /// <summary>
    /// Minimal immutable-ish JSON DOM. Dependency free so it runs unchanged inside Unity
    /// (which ships neither System.Text.Json nor Newtonsoft by default).
    /// </summary>
    public sealed class JsonValue
    {
        private readonly List<KeyValuePair<string, JsonValue>>? _members;
        private readonly Dictionary<string, JsonValue>? _index;
        private readonly List<JsonValue>? _items;

        private JsonValue(JsonKind kind)
        {
            Kind = kind;
            if (kind == JsonKind.Object)
            {
                _members = new List<KeyValuePair<string, JsonValue>>();
                _index = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            }
            else if (kind == JsonKind.Array)
            {
                _items = new List<JsonValue>();
            }
        }

        /// <summary>Kind.</summary>
        public JsonKind Kind { get; }

        /// <summary>Line where the value started (0 if built in code).</summary>
        public int Line { get; private set; }

        /// <summary>Boolean value.</summary>
        public bool BoolValue { get; private set; }

        /// <summary>Numeric value.</summary>
        public double NumberValue { get; private set; }

        /// <summary>String value.</summary>
        public string StringValue { get; private set; } = "";

        /// <summary>The null singleton.</summary>
        public static JsonValue Null { get; } = new JsonValue(JsonKind.Null);

        /// <summary>Creates an object.</summary>
        public static JsonValue NewObject() => new JsonValue(JsonKind.Object);

        /// <summary>Creates an array.</summary>
        public static JsonValue NewArray() => new JsonValue(JsonKind.Array);

        /// <summary>Creates a string.</summary>
        public static JsonValue From(string s) => new JsonValue(JsonKind.String) { StringValue = s ?? "" };

        /// <summary>Creates a number.</summary>
        public static JsonValue From(double d) => new JsonValue(JsonKind.Number) { NumberValue = d };

        /// <summary>Creates a bool.</summary>
        public static JsonValue From(bool b) => new JsonValue(JsonKind.Bool) { BoolValue = b };

        /// <summary>Creates a number array.</summary>
        public static JsonValue From(IEnumerable<double> values)
        {
            JsonValue a = NewArray();
            foreach (double v in values)
            {
                a.Add(From(v));
            }

            return a;
        }

        /// <summary>Creates a string array.</summary>
        public static JsonValue From(IEnumerable<string> values)
        {
            JsonValue a = NewArray();
            foreach (string v in values)
            {
                a.Add(From(v));
            }

            return a;
        }

        /// <summary>Object members in order.</summary>
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members => _members ?? (IReadOnlyList<KeyValuePair<string, JsonValue>>)Array.Empty<KeyValuePair<string, JsonValue>>();

        /// <summary>Array items.</summary>
        public IReadOnlyList<JsonValue> Items => _items ?? (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>();

        /// <summary>Count of members or items.</summary>
        public int Count => Kind == JsonKind.Object ? _members!.Count : Kind == JsonKind.Array ? _items!.Count : 0;

        /// <summary>Is object.</summary>
        public bool IsObject => Kind == JsonKind.Object;

        /// <summary>Is array.</summary>
        public bool IsArray => Kind == JsonKind.Array;

        /// <summary>Member access (returns <see cref="Null"/> when missing).</summary>
        public JsonValue this[string key] => _index != null && _index.TryGetValue(key, out JsonValue v) ? v : Null;

        /// <summary>Item access.</summary>
        public JsonValue this[int i] => _items![i];

        /// <summary>True if the object has the key.</summary>
        public bool Has(string key) => _index != null && _index.ContainsKey(key);

        /// <summary>Sets an object member (replacing).</summary>
        public JsonValue Set(string key, JsonValue value)
        {
            if (_index == null)
            {
                throw new InvalidOperationException("Not an object.");
            }

            if (_index.ContainsKey(key))
            {
                int i = _members!.FindIndex(m => m.Key == key);
                _members[i] = new KeyValuePair<string, JsonValue>(key, value);
            }
            else
            {
                _members!.Add(new KeyValuePair<string, JsonValue>(key, value));
            }

            _index[key] = value;
            return this;
        }

        /// <summary>Sets a string member.</summary>
        public JsonValue Set(string key, string value) => Set(key, From(value));

        /// <summary>Sets a number member.</summary>
        public JsonValue Set(string key, double value) => Set(key, From(value));

        /// <summary>Sets a bool member.</summary>
        public JsonValue Set(string key, bool value) => Set(key, From(value));

        /// <summary>Appends to an array.</summary>
        public JsonValue Add(JsonValue v)
        {
            if (_items == null)
            {
                throw new InvalidOperationException("Not an array.");
            }

            _items.Add(v);
            return this;
        }

        /// <summary>String member or default.</summary>
        public string Str(string key, string fallback = "")
        {
            JsonValue v = this[key];
            return v.Kind == JsonKind.String ? v.StringValue : fallback;
        }

        /// <summary>Number member or default.</summary>
        public double Num(string key, double fallback = 0)
        {
            JsonValue v = this[key];
            return v.Kind == JsonKind.Number ? v.NumberValue : fallback;
        }

        /// <summary>Integer member or default.</summary>
        public int Int(string key, int fallback = 0)
        {
            JsonValue v = this[key];
            return v.Kind == JsonKind.Number ? (int)Math.Round(v.NumberValue) : fallback;
        }

        /// <summary>Bool member or default.</summary>
        public bool Bool(string key, bool fallback = false)
        {
            JsonValue v = this[key];
            return v.Kind == JsonKind.Bool ? v.BoolValue : fallback;
        }

        /// <summary>Converts a numeric array to double[].</summary>
        public double[] ToDoubleArray()
        {
            var r = new double[Count];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = _items![i].NumberValue;
            }

            return r;
        }

        /// <summary>Converts an array of numeric arrays to a matrix [rows, cols].</summary>
        public double[,] ToMatrix()
        {
            int rows = Count;
            int cols = rows > 0 ? _items![0].Count : 0;
            var m = new double[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                JsonValue row = _items![r];
                if (row.Count != cols)
                {
                    throw new FormatException($"Matrix row {r} has {row.Count} values, expected {cols}.");
                }

                for (int c = 0; c < cols; c++)
                {
                    m[r, c] = row[c].NumberValue;
                }
            }

            return m;
        }

        /// <summary>Converts to string list.</summary>
        public List<string> ToStringList()
        {
            var l = new List<string>();
            foreach (JsonValue v in Items)
            {
                l.Add(v.StringValue);
            }

            return l;
        }

        /// <summary>Deep clone.</summary>
        public JsonValue Clone()
        {
            switch (Kind)
            {
                case JsonKind.Object:
                    JsonValue o = NewObject();
                    o.Line = Line;
                    foreach (KeyValuePair<string, JsonValue> m in _members!)
                    {
                        o.Set(m.Key, m.Value.Clone());
                    }

                    return o;
                case JsonKind.Array:
                    JsonValue a = NewArray();
                    a.Line = Line;
                    foreach (JsonValue v in _items!)
                    {
                        a.Add(v.Clone());
                    }

                    return a;
                case JsonKind.Null:
                    return Null;
                default:
                    return new JsonValue(Kind) { BoolValue = BoolValue, NumberValue = NumberValue, StringValue = StringValue, Line = Line };
            }
        }

        /// <summary>Serialises to JSON text.</summary>
        public string ToJson(bool indented = true)
        {
            var sb = new StringBuilder();
            JsonWriter.Write(this, sb, indented, 0);
            return sb.ToString();
        }

        /// <inheritdoc />
        public override string ToString() => ToJson(false);

        /// <summary>Parses JSON text.</summary>
        public static JsonValue Parse(string text) => new JsonParser(text).ParseDocument();

        internal static JsonValue Scalar(JsonKind kind, int line) => new JsonValue(kind) { Line = line };

        internal static JsonValue Container(JsonKind kind, int line) => new JsonValue(kind) { Line = line };

        internal void SetScalar(bool b, double d, string s)
        {
            BoolValue = b;
            NumberValue = d;
            StringValue = s;
        }
    }

    /// <summary>Abstraction to swap the JSON implementation if ever needed.</summary>
    public interface IJsonParser
    {
        /// <summary>Parses text.</summary>
        JsonValue Parse(string text);
    }

    /// <summary>Default parser adapter.</summary>
    public sealed class DefaultJsonParser : IJsonParser
    {
        /// <inheritdoc />
        public JsonValue Parse(string text) => JsonValue.Parse(text);
    }

    /// <summary>Recursive descent RFC 8259 parser (also accepts // comments for hand-edited mod files).</summary>
    internal sealed class JsonParser
    {
        private readonly string _s;
        private int _p;
        private int _line = 1;
        private int _lineStart;

        public JsonParser(string text)
        {
            _s = text ?? "";
            if (_s.Length > 0 && _s[0] == '﻿')
            {
                _p = 1;
            }
        }

        private JsonParseException Error(string msg) => new JsonParseException(msg, _line, _p - _lineStart + 1);

        public JsonValue ParseDocument()
        {
            SkipWs();
            JsonValue v = ParseValue(0);
            SkipWs();
            if (_p < _s.Length)
            {
                throw Error("Contenido inesperado tras el valor JSON");
            }

            return v;
        }

        private void SkipWs()
        {
            while (_p < _s.Length)
            {
                char c = _s[_p];
                if (c == '\n')
                {
                    _line++;
                    _p++;
                    _lineStart = _p;
                }
                else if (c == ' ' || c == '\t' || c == '\r')
                {
                    _p++;
                }
                else if (c == '/' && _p + 1 < _s.Length && _s[_p + 1] == '/')
                {
                    while (_p < _s.Length && _s[_p] != '\n')
                    {
                        _p++;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private JsonValue ParseValue(int depth)
        {
            if (depth > 200)
            {
                throw Error("Anidamiento demasiado profundo");
            }

            if (_p >= _s.Length)
            {
                throw Error("Fin de texto inesperado");
            }

            char c = _s[_p];
            switch (c)
            {
                case '{': return ParseObject(depth);
                case '[': return ParseArray(depth);
                case '"':
                    {
                        int line = _line;
                        JsonValue v = JsonValue.Scalar(JsonKind.String, line);
                        v.SetScalar(false, 0, ParseString());
                        return v;
                    }

                case 't': Expect("true"); return Bool(true);
                case 'f': Expect("false"); return Bool(false);
                case 'n': Expect("null"); return JsonValue.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return ParseNumber();
                    }

                    throw Error($"Carácter inesperado '{c}'");
            }
        }

        private JsonValue Bool(bool b)
        {
            JsonValue v = JsonValue.Scalar(JsonKind.Bool, _line);
            v.SetScalar(b, 0, "");
            return v;
        }

        private void Expect(string word)
        {
            if (string.CompareOrdinal(_s, _p, word, 0, word.Length) != 0)
            {
                throw Error($"Se esperaba '{word}'");
            }

            _p += word.Length;
        }

        private JsonValue ParseObject(int depth)
        {
            JsonValue o = JsonValue.Container(JsonKind.Object, _line);
            _p++;
            SkipWs();
            if (_p < _s.Length && _s[_p] == '}')
            {
                _p++;
                return o;
            }

            while (true)
            {
                SkipWs();
                if (_p >= _s.Length || _s[_p] != '"')
                {
                    throw Error("Se esperaba un nombre de propiedad entre comillas");
                }

                string key = ParseString();
                SkipWs();
                if (_p >= _s.Length || _s[_p] != ':')
                {
                    throw Error("Se esperaba ':'");
                }

                _p++;
                SkipWs();
                if (o.Has(key))
                {
                    throw Error($"Propiedad duplicada '{key}'");
                }

                o.Set(key, ParseValue(depth + 1));
                SkipWs();
                if (_p < _s.Length && _s[_p] == ',')
                {
                    _p++;
                    continue;
                }

                if (_p < _s.Length && _s[_p] == '}')
                {
                    _p++;
                    return o;
                }

                throw Error("Se esperaba ',' o '}'");
            }
        }

        private JsonValue ParseArray(int depth)
        {
            JsonValue a = JsonValue.Container(JsonKind.Array, _line);
            _p++;
            SkipWs();
            if (_p < _s.Length && _s[_p] == ']')
            {
                _p++;
                return a;
            }

            while (true)
            {
                SkipWs();
                a.Add(ParseValue(depth + 1));
                SkipWs();
                if (_p < _s.Length && _s[_p] == ',')
                {
                    _p++;
                    continue;
                }

                if (_p < _s.Length && _s[_p] == ']')
                {
                    _p++;
                    return a;
                }

                throw Error("Se esperaba ',' o ']'");
            }
        }

        private string ParseString()
        {
            _p++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_p >= _s.Length)
                {
                    throw Error("Cadena sin cerrar");
                }

                char c = _s[_p++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c == '\\')
                {
                    if (_p >= _s.Length)
                    {
                        throw Error("Escape incompleto");
                    }

                    char e = _s[_p++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_p + 4 > _s.Length)
                            {
                                throw Error("Escape \\u incompleto");
                            }

                            sb.Append((char)int.Parse(_s.Substring(_p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _p += 4;
                            break;
                        default: throw Error($"Escape inválido '\\{e}'");
                    }
                }
                else if (c == '\n')
                {
                    throw Error("Salto de línea dentro de una cadena");
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        private JsonValue ParseNumber()
        {
            int start = _p;
            if (_s[_p] == '-')
            {
                _p++;
            }

            while (_p < _s.Length && (char.IsDigit(_s[_p]) || _s[_p] == '.' || _s[_p] == 'e' || _s[_p] == 'E' || _s[_p] == '+' || _s[_p] == '-'))
            {
                _p++;
            }

            string t = _s.Substring(start, _p - start);
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                throw Error($"Número inválido '{t}'");
            }

            JsonValue v = JsonValue.Scalar(JsonKind.Number, _line);
            v.SetScalar(false, d, "");
            return v;
        }
    }

    /// <summary>JSON serializer (culture invariant).</summary>
    public static class JsonWriter
    {
        /// <summary>Writes a value.</summary>
        public static void Write(JsonValue v, StringBuilder sb, bool indented, int depth)
        {
            switch (v.Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(v.BoolValue ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(FormatNumber(v.NumberValue)); break;
                case JsonKind.String: WriteString(v.StringValue, sb); break;
                case JsonKind.Array:
                    {
                        bool simple = true;
                        foreach (JsonValue i in v.Items)
                        {
                            if (i.Kind == JsonKind.Object || i.Kind == JsonKind.Array)
                            {
                                simple = false;
                            }
                        }

                        sb.Append('[');
                        for (int i = 0; i < v.Count; i++)
                        {
                            if (i > 0)
                            {
                                sb.Append(simple || !indented ? ", " : ",");
                            }

                            if (indented && !simple)
                            {
                                NewLine(sb, depth + 1);
                            }

                            Write(v[i], sb, indented, depth + 1);
                        }

                        if (indented && !simple && v.Count > 0)
                        {
                            NewLine(sb, depth);
                        }

                        sb.Append(']');
                        break;
                    }

                case JsonKind.Object:
                    {
                        sb.Append('{');
                        int i = 0;
                        foreach (KeyValuePair<string, JsonValue> m in v.Members)
                        {
                            if (i++ > 0)
                            {
                                sb.Append(',');
                            }

                            if (indented)
                            {
                                NewLine(sb, depth + 1);
                            }

                            WriteString(m.Key, sb);
                            sb.Append(indented ? ": " : ":");
                            Write(m.Value, sb, indented, depth + 1);
                        }

                        if (indented && v.Count > 0)
                        {
                            NewLine(sb, depth);
                        }

                        sb.Append('}');
                        break;
                    }
            }
        }

        private static void NewLine(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        /// <summary>Formats a number like JSON (round-trippable, invariant).</summary>
        public static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                return "null";
            }

            if (Math.Abs(d - Math.Round(d)) < 1e-12 && Math.Abs(d) < 1e15)
            {
                return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
            }

            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void WriteString(string s, StringBuilder sb)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }
    }
}
