using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Garage.Data.Json;

namespace Garage.Data
{
    /// <summary>Content categories and their on-disk layout.</summary>
    public static class ContentCategories
    {
        /// <summary>Directory based categories: one entity per file.</summary>
        public static readonly string[] Directories = { "engines", "templates", "cars", "calibrations" };

        /// <summary>Collection files: file name → (array property, schema).</summary>
        public static readonly IReadOnlyDictionary<string, string> CollectionKeys = new Dictionary<string, string>
        {
            { "dtc", "codes" },
            { "failure_modes", "modes" },
            { "parts", "parts" },
            { "customers", "customers" },
            { "scenarios", "scenarios" },
            { "jobs", "jobs" },
            { "upgrades", "upgrades" },
            { "tutorials", "tutorials" },
        };

        /// <summary>Schema file for a category.</summary>
        public static string SchemaFor(string category)
        {
            switch (category)
            {
                case "engines": return "engine.schema.json";
                case "templates": return "component_template.schema.json";
                case "cars": return "car.schema.json";
                case "calibrations": return "ecu_map.schema.json";
                case "dtc": return "dtc.schema.json";
                case "failure_modes": return "failure_mode.schema.json";
                case "parts": return "part.schema.json";
                case "customers": return "customer.schema.json";
                case "scenarios": return "scenario.schema.json";
                case "jobs": return "job.schema.json";
                case "upgrades": return "upgrade.schema.json";
                case "tutorials": return "tutorial.schema.json";
                default: return "";
            }
        }
    }

    /// <summary>Result of loading content: entities per category plus validation errors.</summary>
    public sealed class LoadReport
    {
        /// <summary>Validation and parse errors.</summary>
        public List<ValidationError> Errors { get; } = new List<ValidationError>();

        /// <summary>Informational messages (overrides applied, mods loaded).</summary>
        public List<string> Messages { get; } = new List<string>();

        /// <summary>Files read.</summary>
        public int FilesRead { get; set; }

        /// <summary>True when there are no errors.</summary>
        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// Loads JSON content in layers: base first, then mods (alphabetical, or by "priority" in mod.json).
    /// A later layer adds new ids or overrides existing ones; "$patch": true merges into the existing entity,
    /// "$remove": true deletes it. Every file is validated against data/schemas.
    /// </summary>
    public sealed class ContentLoader
    {
        private readonly string _schemaDir;
        private readonly Dictionary<string, JsonValue> _schemas = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
        private readonly IJsonParser _parser;

        /// <summary>Creates a loader using the schemas in schemaDir.</summary>
        public ContentLoader(string schemaDir, IJsonParser? parser = null)
        {
            _schemaDir = schemaDir;
            _parser = parser ?? new DefaultJsonParser();
        }

        /// <summary>Entities per category, keyed by id, in final (merged) form.</summary>
        public Dictionary<string, Dictionary<string, JsonValue>> Entities { get; } = new Dictionary<string, Dictionary<string, JsonValue>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Source file of each entity (category/id → file).</summary>
        public Dictionary<string, string> Sources { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Loads a schema by file name (cached).</summary>
        public JsonValue? Schema(string fileName)
        {
            if (_schemas.TryGetValue(fileName, out JsonValue s))
            {
                return s;
            }

            string path = Path.Combine(_schemaDir, fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            JsonValue v = _parser.Parse(File.ReadAllText(path));
            _schemas[fileName] = v;
            return v;
        }

        /// <summary>Discovers mod directories with their order.</summary>
        public static List<string> DiscoverMods(string modsDir, LoadReport report)
        {
            var mods = new List<(int Priority, string Name, string Path)>();
            if (!Directory.Exists(modsDir))
            {
                return new List<string>();
            }

            foreach (string dir in Directory.GetDirectories(modsDir))
            {
                int priority = 100;
                string manifest = System.IO.Path.Combine(dir, "mod.json");
                if (File.Exists(manifest))
                {
                    try
                    {
                        JsonValue m = JsonValue.Parse(File.ReadAllText(manifest));
                        priority = m.Int("priority", 100);
                        if (!m.Bool("enabled", true))
                        {
                            report.Messages.Add($"Mod desactivado: {System.IO.Path.GetFileName(dir)}");
                            continue;
                        }
                    }
                    catch (JsonParseException e)
                    {
                        report.Errors.Add(new ValidationError(manifest, "", e.Message, e.Line));
                        continue;
                    }
                }

                mods.Add((priority, System.IO.Path.GetFileName(dir), dir));
            }

            return mods.OrderBy(m => m.Priority).ThenBy(m => m.Name, StringComparer.Ordinal).Select(m => m.Path).ToList();
        }

        /// <summary>Loads base content and then each mod layer.</summary>
        public LoadReport Load(string baseDir, IEnumerable<string>? modDirs = null)
        {
            var report = new LoadReport();
            LoadLayer(baseDir, report, false);
            if (modDirs != null)
            {
                foreach (string m in modDirs)
                {
                    report.Messages.Add($"Cargando mod: {Path.GetFileName(m)}");
                    LoadLayer(m, report, true);
                }
            }

            return report;
        }

        private Dictionary<string, JsonValue> Category(string name)
        {
            if (!Entities.TryGetValue(name, out Dictionary<string, JsonValue> d))
            {
                d = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
                Entities[name] = d;
            }

            return d;
        }

        private void LoadLayer(string dir, LoadReport report, bool isMod)
        {
            if (!Directory.Exists(dir))
            {
                report.Errors.Add(new ValidationError(dir, "", "el directorio de datos no existe", 0));
                return;
            }

            foreach (string category in ContentCategories.Directories)
            {
                string sub = Path.Combine(dir, category);
                if (!Directory.Exists(sub))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(sub, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                {
                    JsonValue? doc = ReadValidated(file, category, report, isMod);
                    if (doc != null)
                    {
                        Merge(category, doc, file, report);
                    }
                }
            }

            foreach (KeyValuePair<string, string> kv in ContentCategories.CollectionKeys)
            {
                string file = Path.Combine(dir, kv.Key + ".json");
                if (!File.Exists(file))
                {
                    continue;
                }

                JsonValue? doc = ReadValidated(file, kv.Key, report, isMod);
                if (doc == null)
                {
                    continue;
                }

                foreach (JsonValue item in doc[kv.Value].Items)
                {
                    Merge(kv.Key, item, file, report);
                }
            }
        }

        private JsonValue? ReadValidated(string file, string category, LoadReport report, bool isMod)
        {
            report.FilesRead++;
            JsonValue doc;
            try
            {
                doc = _parser.Parse(File.ReadAllText(file));
            }
            catch (JsonParseException e)
            {
                report.Errors.Add(new ValidationError(file, "", e.Message, e.Line));
                return null;
            }

            // Patches in mods only need the id: validate full entities only.
            bool patch = doc.Bool("$patch") || doc.Bool("$remove");
            JsonValue? schema = Schema(ContentCategories.SchemaFor(category));
            if (schema != null && !patch)
            {
                var validator = new SchemaValidator(Schema);
                if (ContentCategories.CollectionKeys.ContainsKey(category) && isMod)
                {
                    // Collection files in mods may mix patches; validate non-patch items individually.
                    JsonValue? itemSchema = schema["properties"][ContentCategories.CollectionKeys[category]]["items"];
                    foreach (JsonValue item in doc[ContentCategories.CollectionKeys[category]].Items)
                    {
                        if (!item.Bool("$patch") && !item.Bool("$remove") && itemSchema != null)
                        {
                            report.Errors.AddRange(validator.Validate(item, itemSchema.Kind == JsonKind.Object ? itemSchema : schema, file).Select(e => e).ToList());
                        }
                    }
                }
                else
                {
                    report.Errors.AddRange(validator.Validate(doc, schema, file));
                }
            }

            return doc;
        }

        private void Merge(string category, JsonValue entity, string file, LoadReport report)
        {
            string id = entity.Str("id", entity.Str("code"));
            if (id.Length == 0)
            {
                report.Errors.Add(new ValidationError(file, "", "entidad sin 'id'", entity.Line));
                return;
            }

            Dictionary<string, JsonValue> cat = Category(category);
            string key = category + "/" + id;
            if (entity.Bool("$remove"))
            {
                cat.Remove(id);
                report.Messages.Add($"{key} eliminado por {Path.GetFileName(Path.GetDirectoryName(file) ?? file)}");
                return;
            }

            if (entity.Bool("$patch"))
            {
                if (!cat.TryGetValue(id, out JsonValue existing))
                {
                    report.Errors.Add(new ValidationError(file, "", $"parche para '{key}' que no existe", entity.Line));
                    return;
                }

                cat[id] = DeepMerge(existing, entity);
                report.Messages.Add($"{key} modificado por {file}");
                Sources[key] = file;
                return;
            }

            if (cat.ContainsKey(id))
            {
                report.Messages.Add($"{key} sobrescrito por {file}");
            }

            cat[id] = entity;
            Sources[key] = file;
        }

        /// <summary>Deep merge: objects merged recursively, other values replaced.</summary>
        public static JsonValue DeepMerge(JsonValue target, JsonValue patch)
        {
            JsonValue result = target.Clone();
            foreach (KeyValuePair<string, JsonValue> m in patch.Members)
            {
                if (m.Key.StartsWith("$", StringComparison.Ordinal))
                {
                    continue;
                }

                if (m.Value.IsObject && result[m.Key].IsObject)
                {
                    result.Set(m.Key, DeepMerge(result[m.Key], m.Value));
                }
                else
                {
                    result.Set(m.Key, m.Value.Clone());
                }
            }

            return result;
        }
    }
}
