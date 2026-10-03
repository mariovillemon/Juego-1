using System.Linq;
using System.Text;
using Garage.Sim.Ecu;
using Garage.Sim.Maps;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Garage.Unity
{
    /// <summary>
    /// Laptop with calibration software: colour-coded table (blue→red by value), cursor editing with arrows and +/-,
    /// a 3D surface of the selected map built as a mesh next to the laptop, and the live operating point.
    /// </summary>
    public sealed class EcuMapEditorView : DeviceScreen
    {
        public string tableId = EcuCalibration.IgnitionAdvance;
        [Tooltip("Objeto donde se dibuja la superficie 3D del mapa")] public MeshFilter surface;
        public float step = 0.5f;

        private int _row;
        private int _col;

        protected override void Start()
        {
            resolution = new Vector2Int(1280, 720);
            base.Start();
            Text.fontSize = 14;
        }

        protected override void Update()
        {
            base.Update();
            Keyboard k = Keyboard.current;
            Map3D map = Runner?.Car?.Ecu.Calibration.Table(tableId);
            if (k == null || map == null || !k.leftAltKey.isPressed)
            {
                return; // Alt held = editing focus
            }

            if (k.upArrowKey.wasPressedThisFrame) _row = Mathf.Max(0, _row - 1);
            if (k.downArrowKey.wasPressedThisFrame) _row = Mathf.Min(map.Y.Count - 1, _row + 1);
            if (k.leftArrowKey.wasPressedThisFrame) _col = Mathf.Max(0, _col - 1);
            if (k.rightArrowKey.wasPressedThisFrame) _col = Mathf.Min(map.X.Count - 1, _col + 1);
            if (k.numpadPlusKey.wasPressedThisFrame || k.equalsKey.wasPressedThisFrame) map[_row, _col] += step;
            if (k.numpadMinusKey.wasPressedThisFrame || k.minusKey.wasPressedThisFrame) map[_row, _col] -= step;
            if (k.tabKey.wasPressedThisFrame)
            {
                var ids = Runner.Car.Ecu.Calibration.Tables.Keys.OrderBy(x => x).ToList();
                tableId = ids[(ids.IndexOf(tableId) + 1) % ids.Count];
            }
        }

        protected override void Refresh()
        {
            Map3D map = Runner.Car.Ecu.Calibration.Table(tableId);
            if (map == null)
            {
                Text.text = "Mapa no disponible";
                return;
            }

            map.Range(out double min, out double max);
            var live = Runner.Car.Ecu.Live;
            double rpm = live.TryGetValue("rpm", out double r) ? r : 0;
            double load = live.TryGetValue("abs_load", out double l) ? l / 100 : 0;
            int liveCol = map.X.Nearest(rpm);
            int liveRow = map.Y.Nearest(load);
            var sb = new StringBuilder();
            sb.AppendLine($"<b>{tableId}</b> [{map.Unit}]   Tab: siguiente mapa · Alt+flechas: celda · Alt +/-: valor");
            sb.Append("        ");
            for (int c = 0; c < map.X.Count; c++)
            {
                sb.Append(map.X[c].ToString("0").PadLeft(6));
            }

            sb.AppendLine();
            for (int row = 0; row < map.Y.Count; row++)
            {
                sb.Append(map.Y[row].ToString("0.##").PadLeft(6)).Append("  ");
                for (int c = 0; c < map.X.Count; c++)
                {
                    float t = (float)((map[row, c] - min) / System.Math.Max(1e-9, max - min));
                    Color col = Color.Lerp(new Color(0.2f, 0.4f, 1f), new Color(1f, 0.25f, 0.2f), t);
                    string hex = ColorUtility.ToHtmlStringRGB(col);
                    string cell = map[row, c].ToString(max > 50 ? "0" : "0.0").PadLeft(6);
                    bool cursor = row == _row && c == _col;
                    bool op = row == liveRow && c == liveCol;
                    sb.Append(cursor ? $"<mark=#FFFFFF40><color=#{hex}>{cell}</color></mark>" : op ? $"<u><color=#{hex}>{cell}</color></u>" : $"<color=#{hex}>{cell}</color>");
                }

                sb.AppendLine();
            }

            sb.AppendLine($"\nPunto de trabajo: {rpm:0} rpm, carga {load:0.00}  (subrayado)   Celda [{_row},{_col}] = {map[_row, _col]:0.###}");
            Text.text = sb.ToString();
            BuildSurface(map, min, max);
        }

        private void BuildSurface(Map3D map, double min, double max)
        {
            if (surface == null)
            {
                return;
            }

            int nx = map.X.Count;
            int ny = map.Y.Count;
            var verts = new Vector3[nx * ny];
            var colors = new Color[nx * ny];
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    float t = (float)((map[y, x] - min) / System.Math.Max(1e-9, max - min));
                    verts[y * nx + x] = new Vector3(x / (float)(nx - 1) - 0.5f, t * 0.35f, y / (float)(ny - 1) - 0.5f);
                    colors[y * nx + x] = Color.Lerp(Color.blue, Color.red, t);
                }
            }

            var tris = new int[(nx - 1) * (ny - 1) * 6];
            int k = 0;
            for (int y = 0; y < ny - 1; y++)
            {
                for (int x = 0; x < nx - 1; x++)
                {
                    int i = y * nx + x;
                    tris[k++] = i; tris[k++] = i + nx; tris[k++] = i + 1;
                    tris[k++] = i + 1; tris[k++] = i + nx; tris[k++] = i + nx + 1;
                }
            }

            Mesh mesh = surface.sharedMesh != null && surface.sharedMesh.name == "MapSurface" ? surface.sharedMesh : new Mesh { name = "MapSurface" };
            mesh.Clear();
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            surface.sharedMesh = mesh;
        }
    }
}
