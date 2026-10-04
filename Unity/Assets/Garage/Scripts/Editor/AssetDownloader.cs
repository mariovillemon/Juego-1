using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Garage.Data.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Garage.Unity.EditorTools
{
    /// <summary>
    /// Garage/Assets/Download Free Assets: downloads CC0 PBR textures and HDRIs from the public Poly Haven API,
    /// imports them correctly (normal maps as normal maps, sRGB only on albedo, packed HDRP mask map
    /// R=metallic G=AO B=detail mask A=smoothness) and updates the workshop materials. Credits are written to
    /// docs/ASSET_CREDITS.md. Without network it fails with a clear message and changes nothing.
    /// </summary>
    public static class AssetDownloader
    {
        private const string Api = "https://api.polyhaven.com";
        private const string Dest = "Assets/Garage/Downloaded";
        private const string Resolution = "2k";

        /// <summary>Workshop material → Poly Haven category used to pick a texture set.</summary>
        private static readonly (string Material, string Category)[] Targets =
        {
            ("Concrete_Floor", "concrete"),
            ("Painted_Block_Wall", "plaster-concrete"),
            ("Painted_Steel", "metal"),
            ("Bare_Steel", "metal"),
            ("Lift_Paint_Red", "metal"),
            ("Rubber", "floor"),
            ("Bench_Top_Wood", "wood"),
            ("Asphalt", "road"),
        };

        [MenuItem("Garage/Assets/Download Free Assets", priority = 20)]
        public static void DownloadMenu()
        {
            try
            {
                Download();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Descarga de assets", "No se pudieron descargar los assets (¿sin conexión?).\n\n" + e.Message + "\n\nNo se ha modificado nada.", "Aceptar");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static byte[] Get(string url, string what)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("User-Agent", "GarageSim-UnityEditor (open source workshop simulator)");
                UnityWebRequestAsyncOperation op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Poly Haven", what, req.downloadProgress))
                    {
                        req.Abort();
                        throw new OperationCanceledException("Cancelado por el usuario");
                    }

                    System.Threading.Thread.Sleep(20);
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    throw new IOException($"{url}: {req.error}");
                }

                return req.downloadHandler.data;
            }
        }

        private static JsonValue GetJson(string url, string what) => JsonValue.Parse(Encoding.UTF8.GetString(Get(url, what)));

        /// <summary>Picks the most downloaded asset of a category (deterministic).</summary>
        private static string Pick(string type, string category, ISet<string> used)
        {
            JsonValue list = GetJson($"{Api}/assets?t={type}&c={category}", "Listando " + category);
            return list.Members.OrderByDescending(m => m.Value.Num("download_count")).Select(m => m.Key).FirstOrDefault(id => !used.Contains(id))
                ?? list.Members.Select(m => m.Key).FirstOrDefault();
        }

        private static string FileUrl(JsonValue files, string map, string format)
        {
            JsonValue res = files[map][Resolution];
            if (res.Kind != JsonKind.Object)
            {
                res = files[map]["1k"];
            }

            return res[format].Str("url");
        }

        private static void Download()
        {
            // Connectivity check first: no partial changes when offline.
            GetJson($"{Api}/types", "Comprobando conexión");
            Directory.CreateDirectory(Path.Combine(EditorUtil.ProjectRoot, Dest));
            var credits = new List<string>();
            var used = new HashSet<string>();
            int i = 0;
            foreach (var (materialName, category) in Targets)
            {
                EditorUtility.DisplayProgressBar("Poly Haven", materialName, (float)i++ / Targets.Length);
                string id = Pick("textures", category, used);
                if (id == null)
                {
                    continue;
                }

                used.Add(id);
                JsonValue files = GetJson($"{Api}/files/{id}", id);
                string folder = $"{Dest}/{id}";
                Directory.CreateDirectory(Path.Combine(EditorUtil.ProjectRoot, folder));
                string albedo = Save(folder, "albedo.jpg", FileUrl(files, "Diffuse", "jpg"));
                string normal = Save(folder, "normal.jpg", FileUrl(files, "nor_gl", "jpg"));
                byte[] rough = Get(FileUrl(files, "Rough", "jpg"), id + " roughness");
                string aoUrl = files.Has("AO") ? FileUrl(files, "AO", "jpg") : null;
                byte[] ao = aoUrl != null ? Get(aoUrl, id + " AO") : null;
                string metalUrl = files.Has("Metal") ? FileUrl(files, "Metal", "jpg") : null;
                byte[] metal = metalUrl != null ? Get(metalUrl, id + " metal") : null;
                string mask = PackMaskMap(folder, rough, ao, metal);
                AssetDatabase.Refresh();
                Configure(albedo, TextureImporterType.Default, true);
                Configure(normal, TextureImporterType.NormalMap, false);
                Configure(mask, TextureImporterType.Default, false);
                ApplyToMaterial(materialName, albedo, normal, mask);
                JsonValue info = GetJson($"{Api}/info/{id}", id + " info");
                string authors = string.Join(", ", info["authors"].Members.Select(m => m.Key));
                credits.Add($"| {materialName} | {info.Str("name", id)} (`{id}`) | {authors} | CC0 1.0 | https://polyhaven.com/a/{id} |");
            }

            string hdri = Pick("hdris", "indoor", used);
            if (hdri != null)
            {
                JsonValue files = GetJson($"{Api}/files/{hdri}", hdri);
                string path = Save($"{Dest}/{hdri}", "sky.hdr", FileUrl(files, "hdri", "hdr"));
                AssetDatabase.Refresh();
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                if (imp != null)
                {
                    imp.textureShape = TextureImporterShape.TextureCube;
                    imp.SaveAndReimport();
                }

                credits.Add($"| HDRI de referencia | `{hdri}` | Poly Haven | CC0 1.0 | https://polyhaven.com/a/{hdri} |");
            }

            DownloadModels(credits, used);
            WriteCredits(credits);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Descarga de assets", $"Descargados {credits.Count} recursos CC0 de Poly Haven.\nCréditos en docs/ASSET_CREDITS.md.", "Aceptar");
        }

        /// <summary>Poly Haven model categories used to dress the workshop (props on shelves and benches).</summary>
        private static readonly string[] ModelCategories = { "tools", "industrial", "containers", "furniture" };

        /// <summary>Folder with downloaded CC0 models; Build Workshop Scene places them on the shelving.</summary>
        public const string ModelsDir = Dest + "/Models";

        /// <summary>Downloads a few CC0 models (FBX 1k with their textures) for set dressing.</summary>
        private static void DownloadModels(List<string> credits, ISet<string> used)
        {
            int n = 0;
            foreach (string category in ModelCategories)
            {
                JsonValue list;
                try
                {
                    list = GetJson($"{Api}/assets?t=models&c={category}", "Modelos " + category);
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (string id in list.Members.OrderByDescending(m => m.Value.Num("download_count")).Select(m => m.Key).Where(i => !used.Contains(i)).Take(2))
                {
                    JsonValue fbx = GetJson($"{Api}/files/{id}", id)["fbx"]["1k"]["fbx"];
                    if (fbx.Kind != JsonKind.Object || fbx.Str("url").Length == 0)
                    {
                        continue;
                    }

                    string folder = $"{ModelsDir}/{id}";
                    Save(folder, id + ".fbx", fbx.Str("url"));
                    foreach (KeyValuePair<string, JsonValue> inc in fbx["include"].Members)
                    {
                        Save(folder, inc.Key, inc.Value.Str("url"));
                    }

                    used.Add(id);
                    JsonValue info = GetJson($"{Api}/info/{id}", id + " info");
                    string authors = string.Join(", ", info["authors"].Members.Select(m => m.Key));
                    credits.Add($"| Atrezo del taller ({category}) | {info.Str("name", id)} (`{id}`) | {authors} | CC0 1.0 | https://polyhaven.com/a/{id} |");
                    n++;
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[Garage] {n} modelos CC0 descargados en {ModelsDir}. Vuelve a ejecutar Build Workshop Scene para colocarlos.");
        }

        private static string Save(string folder, string file, string url)
        {
            string path = $"{folder}/{file}";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(EditorUtil.ProjectRoot, path)));
            File.WriteAllBytes(Path.Combine(EditorUtil.ProjectRoot, path), Get(url, file));
            return path;
        }

        /// <summary>HDRP mask map: R metallic, G ambient occlusion, B detail mask, A smoothness (1 − roughness).</summary>
        private static string PackMaskMap(string folder, byte[] rough, byte[] ao, byte[] metal)
        {
            var r = new Texture2D(2, 2);
            r.LoadImage(rough);
            Texture2D a = null;
            if (ao != null)
            {
                a = new Texture2D(2, 2);
                a.LoadImage(ao);
            }

            Texture2D m = null;
            if (metal != null)
            {
                m = new Texture2D(2, 2);
                m.LoadImage(metal);
            }

            int w = r.width;
            int h = r.height;
            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            Color[] rp = r.GetPixels();
            Color[] ap = a != null && a.width == w && a.height == h ? a.GetPixels() : null;
            Color[] mp = m != null && m.width == w && m.height == h ? m.GetPixels() : null;
            var px = new Color[rp.Length];
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = new Color(mp != null ? mp[i].r : 0f, ap != null ? ap[i].r : 1f, 1f, 1f - rp[i].r);
            }

            outTex.SetPixels(px);
            string path = $"{folder}/mask.png";
            File.WriteAllBytes(Path.Combine(EditorUtil.ProjectRoot, path), outTex.EncodeToPNG());
            return path;
        }

        private static void Configure(string path, TextureImporterType type, bool srgb)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null)
            {
                return;
            }

            imp.textureType = type;
            imp.sRGBTexture = srgb;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
        }

        private static void ApplyToMaterial(string materialName, string albedo, string normal, string mask)
        {
            string matPath = $"{EditorUtil.MaterialsDir}/{materialName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                return; // run "Build Workshop Scene" first
            }

            mat.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
            mat.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            mat.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(mask));
            mat.SetFloat("_NormalScale", 1f);
            mat.SetTextureScale("_BaseColorMap", new Vector2(4, 4));
            Color tint = mat.GetColor("_BaseColor");
            mat.SetColor("_BaseColor", Color.Lerp(Color.white, tint, 0.35f));
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_MASKMAP");
            EditorUtil.ValidateHdrpMaterial(mat);
            EditorUtility.SetDirty(mat);
        }

        private static void WriteCredits(List<string> lines)
        {
            string path = Path.Combine(EditorUtil.RepoRoot, "docs", "ASSET_CREDITS.md");
            if (!File.Exists(path))
            {
                return;
            }

            string text = File.ReadAllText(path);
            const string marker = "<!-- DOWNLOADED-ASSETS -->";
            int idx = text.IndexOf(marker, StringComparison.Ordinal);
            string head = idx >= 0 ? text.Substring(0, idx + marker.Length) : text + "\n" + marker;
            var sb = new StringBuilder(head);
            sb.AppendLine();
            sb.AppendLine($"Última descarga: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine("| Uso | Recurso | Autor(es) | Licencia | Origen |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (string l in lines)
            {
                sb.AppendLine(l);
            }

            File.WriteAllText(path, sb.ToString());
        }
    }
}
