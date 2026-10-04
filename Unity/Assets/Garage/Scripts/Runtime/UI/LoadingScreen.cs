using System.Collections;
using Garage.Unity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Garage.Unity
{
    /// <summary>
    /// Scene transitions: fade to black, asynchronous load with a progress bar and a random tip, and a fade back
    /// in. It survives the scene change (DontDestroyOnLoad) and destroys itself at the end.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        private const float Fade = 0.35f;
        private const int Tips = 5;

        private CanvasGroup _group;
        private Image _bar;
        private static bool _busy;

        /// <summary>Loads a scene behind the loading screen (ignored while another load runs).</summary>
        public static void Load(string scene)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            Time.timeScale = 1f;
            Canvas c = UiKit.Canvas("LoadingScreen", 1000);
            DontDestroyOnLoad(c.gameObject);
            LoadingScreen s = c.gameObject.AddComponent<LoadingScreen>();
            s._group = c.gameObject.AddComponent<CanvasGroup>();
            s._group.alpha = 0f;
            s._group.blocksRaycasts = true;
            Image bg = UiKit.Panel(c.transform, "Black", Color.black);
            UiKit.Fill(bg.rectTransform);
            TextMeshProUGUI title = UiKit.Label(bg.transform, GameOptions.T("load.loading"), UiTheme.FontTitle, UiTheme.Text, TextAlignmentOptions.Center);
            UiKit.Anchor(title.rectTransform, new Vector2(0.2f, 0.5f), new Vector2(0.8f, 0.6f));
            Image track = UiKit.Panel(bg.transform, "Track", UiTheme.Row);
            UiKit.Anchor(track.rectTransform, new Vector2(0.3f, 0.46f), new Vector2(0.7f, 0.475f));
            s._bar = UiKit.Panel(track.transform, "Bar", UiTheme.Accent);
            UiKit.Anchor(s._bar.rectTransform, Vector2.zero, new Vector2(0f, 1f));
            string tip = GameOptions.T("load.tip." + Random.Range(1, Tips + 1));
            TextMeshProUGUI tipLabel = UiKit.Label(bg.transform, tip, UiTheme.FontBody, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiKit.Anchor(tipLabel.rectTransform, new Vector2(0.15f, 0.3f), new Vector2(0.85f, 0.42f));
            s.StartCoroutine(s.Run(scene));
        }

        private IEnumerator Run(string scene)
        {
            yield return FadeTo(1f);
            AsyncOperation op = SceneManager.LoadSceneAsync(scene);
            if (op == null)
            {
                Debug.LogError($"La escena «{scene}» no está en Build Settings.");
                _busy = false;
                Destroy(gameObject);
                yield break;
            }

            while (!op.isDone)
            {
                SetProgress(Mathf.Clamp01(op.progress / 0.9f));
                yield return null;
            }

            SetProgress(1f);
            yield return null; // let the new scene run Start() behind the black screen
            yield return FadeTo(0f);
            _busy = false;
            Destroy(gameObject);
        }

        private void SetProgress(float p) => _bar.rectTransform.anchorMax = new Vector2(p, 1f);

        private IEnumerator FadeTo(float target)
        {
            float start = _group.alpha;
            for (float t = 0; t < Fade; t += Time.unscaledDeltaTime)
            {
                _group.alpha = Mathf.Lerp(start, target, t / Fade);
                yield return null;
            }

            _group.alpha = target;
        }
    }
}
