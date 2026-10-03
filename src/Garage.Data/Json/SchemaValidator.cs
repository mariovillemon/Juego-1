using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Garage.Data.Json
{
    /// <summary>One validation error with a JSON Pointer path.</summary>
    public sealed class ValidationError
    {
        /// <summary>Creates an error.</summary>
        public ValidationError(string file, string path, string message, int line)
        {
            File = file;
            Path = path;
            Message = message;
            Line = line;
        }

        /// <summary>Source file.</summary>
        public string File { get; }

        /// <summary>JSON Pointer of the failing value.</summary>
        public string Path { get; }

        /// <summary>Message (Spanish).</summary>
        public string Message { get; }

        /// <summary>Line in the source file (0 = unknown).</summary>
        public int Line { get; }

        /// <inheritdoc />
        public override string ToString() => $"{File}{(Line > 0 ? ":" + Line.ToString(CultureInfo.InvariantCulture) : "")} {(Path.Length == 0 ? "/" : Path)}: {Message}";
    }

    /// <summary>
    /// JSON Schema validator supporting the subset used by the game's schemas (draft 2020-12 keywords):
    /// type, properties, required, additionalProperties, items, enum, const, minimum, maximum, exclusiveMinimum,
    /// minLength, maxLength, pattern, minItems, maxItems, uniqueItems, $ref (local "#/$defs/x" and "file.schema.json#/..."),
    /// $defs, oneOf, anyOf, allOf.
    /// </summary>
    public sealed class SchemaValidator
    {
        private readonly Func<string, JsonValue?> _resolveSchema;

        /// <summary>Creates a validator. resolveSchema loads other schema files by name for cross-file $ref.</summary>
        public SchemaValidator(Func<string, JsonValue?> resolveSchema)
        {
            _resolveSchema = resolveSchema;
        }

        /// <summary>Validates a document against a schema.</summary>
        public List<ValidationError> Validate(JsonValue document, JsonValue schema, string fileName)
        {
            var errors = new List<ValidationError>();
            Check(document, schema, schema, "", fileName, errors, 0);
            return errors;
        }

        private JsonValue? ResolveRef(string reference, JsonValue root, out JsonValue newRoot)
        {
            newRoot = root;
            string file = "";
            string pointer = reference;
            int hash = reference.IndexOf('#');
            if (hash > 0)
            {
                file = reference.Substring(0, hash);
                pointer = reference.Substring(hash);
            }
            else if (hash < 0)
            {
                file = reference;
                pointer = "#";
            }

            if (file.Length > 0)
            {
                JsonValue? other = _resolveSchema(file);
                if (other == null)
                {
                    return null;
                }

                newRoot = other;
            }

            JsonValue cur = newRoot;
            if (pointer == "#" || pointer == "#/")
            {
                return cur;
            }

            foreach (string part in pointer.Substring(2).Split('/'))
            {
                string key = part.Replace("~1", "/").Replace("~0", "~");
                cur = cur[key];
                if (cur.Kind == JsonKind.Null)
                {
                    return null;
                }
            }

            return cur;
        }

        private static bool TypeMatches(JsonValue v, string type)
        {
            switch (type)
            {
                case "object": return v.Kind == JsonKind.Object;
                case "array": return v.Kind == JsonKind.Array;
                case "string": return v.Kind == JsonKind.String;
                case "number": return v.Kind == JsonKind.Number;
                case "integer": return v.Kind == JsonKind.Number && Math.Abs(v.NumberValue - Math.Round(v.NumberValue)) < 1e-9;
                case "boolean": return v.Kind == JsonKind.Bool;
                case "null": return v.Kind == JsonKind.Null;
                default: return false;
            }
        }

        private static bool DeepEquals(JsonValue a, JsonValue b) => a.ToJson(false) == b.ToJson(false);

        private void Check(JsonValue v, JsonValue s, JsonValue root, string path, string file, List<ValidationError> errors, int depth)
        {
            if (depth > 64 || s.Kind != JsonKind.Object)
            {
                return;
            }

            void Err(string msg) => errors.Add(new ValidationError(file, path, msg, v.Line));

            if (s.Has("$ref"))
            {
                JsonValue? target = ResolveRef(s.Str("$ref"), root, out JsonValue newRoot);
                if (target == null)
                {
                    Err($"$ref no resuelto: {s.Str("$ref")}");
                }
                else
                {
                    Check(v, target, newRoot, path, file, errors, depth + 1);
                }
            }

            JsonValue type = s["type"];
            if (type.Kind == JsonKind.String && !TypeMatches(v, type.StringValue))
            {
                Err($"tipo esperado '{type.StringValue}', encontrado '{v.Kind.ToString().ToLowerInvariant()}'");
                return;
            }

            if (type.Kind == JsonKind.Array)
            {
                bool any = false;
                foreach (JsonValue t in type.Items)
                {
                    any |= TypeMatches(v, t.StringValue);
                }

                if (!any)
                {
                    Err($"tipo no permitido '{v.Kind.ToString().ToLowerInvariant()}'");
                    return;
                }
            }

            if (s.Has("enum"))
            {
                bool found = false;
                foreach (JsonValue e in s["enum"].Items)
                {
                    found |= DeepEquals(e, v);
                }

                if (!found)
                {
                    var allowed = new List<string>();
                    foreach (JsonValue e in s["enum"].Items)
                    {
                        allowed.Add(e.ToJson(false));
                    }

                    Err($"valor {v.ToJson(false)} no permitido; opciones: {string.Join(", ", allowed)}");
                }
            }

            if (s.Has("const") && !DeepEquals(s["const"], v))
            {
                Err($"debe ser {s["const"].ToJson(false)}");
            }

            if (v.Kind == JsonKind.Number)
            {
                if (s.Has("minimum") && v.NumberValue < s.Num("minimum"))
                {
                    Err($"valor {v.NumberValue.ToString(CultureInfo.InvariantCulture)} menor que el mínimo {s.Num("minimum").ToString(CultureInfo.InvariantCulture)}");
                }

                if (s.Has("maximum") && v.NumberValue > s.Num("maximum"))
                {
                    Err($"valor {v.NumberValue.ToString(CultureInfo.InvariantCulture)} mayor que el máximo {s.Num("maximum").ToString(CultureInfo.InvariantCulture)}");
                }

                if (s.Has("exclusiveMinimum") && v.NumberValue <= s.Num("exclusiveMinimum"))
                {
                    Err($"valor debe ser mayor que {s.Num("exclusiveMinimum").ToString(CultureInfo.InvariantCulture)}");
                }
            }

            if (v.Kind == JsonKind.String)
            {
                if (s.Has("minLength") && v.StringValue.Length < s.Int("minLength"))
                {
                    Err($"texto demasiado corto (mínimo {s.Int("minLength")})");
                }

                if (s.Has("maxLength") && v.StringValue.Length > s.Int("maxLength"))
                {
                    Err($"texto demasiado largo (máximo {s.Int("maxLength")})");
                }

                if (s.Has("pattern") && !Regex.IsMatch(v.StringValue, s.Str("pattern")))
                {
                    Err($"'{v.StringValue}' no cumple el patrón {s.Str("pattern")}");
                }
            }

            if (v.Kind == JsonKind.Array)
            {
                if (s.Has("minItems") && v.Count < s.Int("minItems"))
                {
                    Err($"se requieren al menos {s.Int("minItems")} elementos (hay {v.Count})");
                }

                if (s.Has("maxItems") && v.Count > s.Int("maxItems"))
                {
                    Err($"se permiten como máximo {s.Int("maxItems")} elementos (hay {v.Count})");
                }

                if (s.Bool("uniqueItems"))
                {
                    var seen = new HashSet<string>();
                    foreach (JsonValue i in v.Items)
                    {
                        if (!seen.Add(i.ToJson(false)))
                        {
                            Err($"elemento repetido {i.ToJson(false)}");
                        }
                    }
                }

                if (s.Has("items"))
                {
                    for (int i = 0; i < v.Count; i++)
                    {
                        Check(v[i], s["items"], root, path + "/" + i.ToString(CultureInfo.InvariantCulture), file, errors, depth + 1);
                    }
                }
            }

            if (v.Kind == JsonKind.Object)
            {
                foreach (JsonValue r in s["required"].Items)
                {
                    if (!v.Has(r.StringValue))
                    {
                        Err($"falta la propiedad obligatoria '{r.StringValue}'");
                    }
                }

                JsonValue props = s["properties"];
                JsonValue additional = s["additionalProperties"];
                foreach (KeyValuePair<string, JsonValue> m in v.Members)
                {
                    string childPath = path + "/" + m.Key;
                    if (props.Has(m.Key))
                    {
                        Check(m.Value, props[m.Key], root, childPath, file, errors, depth + 1);
                    }
                    else if (additional.Kind == JsonKind.Bool && !additional.BoolValue)
                    {
                        if (!m.Key.StartsWith("$", StringComparison.Ordinal))
                        {
                            errors.Add(new ValidationError(file, childPath, $"propiedad desconocida '{m.Key}'", m.Value.Line));
                        }
                    }
                    else if (additional.Kind == JsonKind.Object)
                    {
                        Check(m.Value, additional, root, childPath, file, errors, depth + 1);
                    }
                }
            }

            if (s.Has("allOf"))
            {
                foreach (JsonValue sub in s["allOf"].Items)
                {
                    Check(v, sub, root, path, file, errors, depth + 1);
                }
            }

            if (s.Has("anyOf") || s.Has("oneOf"))
            {
                bool one = s.Has("oneOf");
                int matches = 0;
                List<ValidationError>? firstErrors = null;
                foreach (JsonValue sub in (one ? s["oneOf"] : s["anyOf"]).Items)
                {
                    var tmp = new List<ValidationError>();
                    Check(v, sub, root, path, file, tmp, depth + 1);
                    if (tmp.Count == 0)
                    {
                        matches++;
                    }
                    else if (firstErrors == null)
                    {
                        firstErrors = tmp;
                    }
                }

                if (matches == 0)
                {
                    Err((one ? "oneOf" : "anyOf") + ": no encaja con ninguna alternativa" + (firstErrors != null && firstErrors.Count > 0 ? " (" + firstErrors[0].Message + ")" : ""));
                }
                else if (one && matches > 1)
                {
                    Err("oneOf: encaja con varias alternativas");
                }
            }
        }
    }
}
