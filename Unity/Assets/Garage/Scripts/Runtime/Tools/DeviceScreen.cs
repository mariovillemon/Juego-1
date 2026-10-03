using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Garage.Unity
{
    /// <summary>
    /// A physical screen in the world: an off-screen camera renders a UI canvas into a RenderTexture that is
    /// shown (emissive) on the device's screen mesh. Subclasses draw text and graphs like real workshop software.
    /// </summary>
    public abstract class DeviceScreen : MonoBehaviour
    {
        [Tooltip("Malla de la pantalla del aparato")] public Renderer screenRenderer;
        public Vector2Int resolution = new Vector2Int(800, 480);
        [Tooltip("Brillo de la pantalla en nits")] public float nits = 250f;
        public Color background = new Color(0.02f, 0.03f, 0.04f);
        public Color foreground = new Color(0.75f, 0.95f, 0.8f);
        public float refreshHz = 4f;

        protected SimulationRunner Runner;
        protected TextMeshProUGUI Text;
        protected RawImage Graph;
        protected Texture2D GraphTexture;
        private RenderTexture _rt;
        private float _timer;
        private static int _slot;

        /// <summary>Content refresh.</summary>
        protected abstract void Refresh();

        protected virtual void Start()
        {
            Runner = FindFirstObjectByType<SimulationRunner>();
            _rt = new RenderTexture(resolution.x, resolution.y, 16) { name = name + "_RT" };
            var camGo = new GameObject(name + "_ScreenCamera");
            camGo.transform.position = new Vector3(0, -1000 - 20 * _slot++, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.cullingMask = 1 << UnityCompat.DeviceUiLayer;
            cam.targetTexture = _rt;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 10f;

            var canvasGo = new GameObject(name + "_Canvas");
            canvasGo.layer = UnityCompat.DeviceUiLayer;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = resolution;

            var graphGo = new GameObject("Graph");
            graphGo.layer = UnityCompat.DeviceUiLayer;
            graphGo.transform.SetParent(canvasGo.transform, false);
            Graph = graphGo.AddComponent<RawImage>();
            var gr = Graph.rectTransform;
            gr.anchorMin = new Vector2(0.02f, 0.02f);
            gr.anchorMax = new Vector2(0.98f, 0.55f);
            gr.offsetMin = gr.offsetMax = Vector2.zero;
            GraphTexture = new Texture2D(512, 200, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            Graph.texture = GraphTexture;
            Graph.enabled = false;

            var textGo = new GameObject("Text");
            textGo.layer = UnityCompat.DeviceUiLayer;
            textGo.transform.SetParent(canvasGo.transform, false);
            Text = textGo.AddComponent<TextMeshProUGUI>();
            Text.fontSize = 18;
            Text.color = foreground;
            Text.enableWordWrapping = false;
            var tr = Text.rectTransform;
            tr.anchorMin = new Vector2(0.02f, 0.02f);
            tr.anchorMax = new Vector2(0.98f, 0.98f);
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            Text.alignment = TextAlignmentOptions.TopLeft;

            if (screenRenderer != null)
            {
                Shader unlit = Shader.Find("HDRP/Unlit");
                var m = new Material(unlit != null ? unlit : Shader.Find("Unlit/Texture")) { name = name + "_Screen" };
                m.SetTexture("_UnlitColorMap", _rt);
                m.SetTexture("_MainTex", _rt);
                m.SetColor("_UnlitColor", Color.white);
                m.SetColor("_EmissiveColor", Color.white * nits / 100f);
                screenRenderer.sharedMaterial = m;
            }
        }

        protected virtual void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= 1f / refreshHz && Runner != null && Runner.Car != null)
            {
                _timer = 0;
                Refresh();
            }
        }

        /// <summary>Clears the graph texture.</summary>
        protected void ClearGraph(Color c)
        {
            var px = GraphTexture.GetPixels32();
            Color32 c32 = c;
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = c32;
            }

            GraphTexture.SetPixels32(px);
            Color32 grid = new Color(c.r + 0.06f, c.g + 0.08f, c.b + 0.06f);
            for (int x = 0; x < GraphTexture.width; x += GraphTexture.width / 10)
            {
                for (int y = 0; y < GraphTexture.height; y++)
                {
                    GraphTexture.SetPixel(x, y, grid);
                }
            }

            for (int y = 0; y < GraphTexture.height; y += GraphTexture.height / 8)
            {
                for (int x = 0; x < GraphTexture.width; x++)
                {
                    GraphTexture.SetPixel(x, y, grid);
                }
            }
        }

        /// <summary>Plots a polyline of normalised values (0..1) into the graph.</summary>
        protected void Plot(double[] values, double min, double max, Color color)
        {
            int w = GraphTexture.width;
            int h = GraphTexture.height;
            int prevY = -1;
            for (int x = 0; x < w; x++)
            {
                int i = values.Length == 0 ? 0 : Mathf.Min(values.Length - 1, x * values.Length / w);
                if (values.Length == 0)
                {
                    break;
                }

                float t = (float)((values[i] - min) / (max - min));
                int y = Mathf.Clamp(Mathf.RoundToInt(t * (h - 1)), 0, h - 1);
                int y0 = prevY < 0 ? y : Mathf.Min(prevY, y);
                int y1 = prevY < 0 ? y : Mathf.Max(prevY, y);
                for (int yy = y0; yy <= y1; yy++)
                {
                    GraphTexture.SetPixel(x, yy, color);
                }

                prevY = y;
            }
        }

        /// <summary>Uploads graph pixels.</summary>
        protected void ApplyGraph()
        {
            GraphTexture.Apply(false);
            Graph.enabled = true;
        }

        protected virtual void OnDestroy()
        {
            if (_rt != null)
            {
                _rt.Release();
            }
        }
    }
}
