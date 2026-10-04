using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Garage.Unity.UI
{
    /// <summary>Tracks open modal panels: while any is open the cursor is free and the player cannot move.</summary>
    public static class UiState
    {
        private static readonly HashSet<UiPanel> Open = new HashSet<UiPanel>();

        /// <summary>Any modal panel open.</summary>
        public static bool AnyOpen => Open.Count > 0;

        internal static void Set(UiPanel p, bool open)
        {
            if (open)
            {
                Open.Add(p);
            }
            else
            {
                Open.Remove(p);
            }

            Cursor.lockState = AnyOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = AnyOpen;
        }

        /// <summary>Closes every panel (Esc).</summary>
        public static bool CloseTop()
        {
            UiPanel top = null;
            foreach (UiPanel p in Open)
            {
                if (top == null || p.Order > top.Order)
                {
                    top = p;
                }
            }

            if (top == null)
            {
                return false;
            }

            top.Close();
            return true;
        }

        /// <summary>Resets (scene change).</summary>
        public static void Reset() => Open.Clear();
    }

    /// <summary>
    /// A modal window built in code: dark panel with a header (title + close button) and a body.
    /// Subclasses implement <see cref="Build"/> once and <see cref="Refresh"/> whenever state changes.
    /// </summary>
    public abstract class UiPanel
    {
        private static int _order;

        protected GameUI Ui { get; private set; }

        protected SimulationRunner Runner => Ui.Runner;

        /// <summary>Root object.</summary>
        public GameObject Root { get; private set; }

        /// <summary>Body (vertical layout).</summary>
        protected RectTransform Body { get; private set; }

        /// <summary>Title label.</summary>
        protected TextMeshProUGUI TitleLabel { get; private set; }

        /// <summary>Stacking order (last opened on top).</summary>
        public int Order { get; private set; }

        /// <summary>Visible.</summary>
        public bool IsOpen => Root != null && Root.activeSelf;

        /// <summary>Window title.</summary>
        protected abstract string Title { get; }

        /// <summary>Window size as a fraction of the screen.</summary>
        protected virtual Vector2 SizeFraction => new Vector2(0.7f, 0.8f);

        /// <summary>Creates the window hidden.</summary>
        public void Create(GameUI ui, Transform canvas)
        {
            Ui = ui;
            Image backdrop = UiKit.Panel(canvas, GetType().Name, UiTheme.Backdrop);
            UiKit.Fill(backdrop.rectTransform);
            Root = backdrop.gameObject;
            Vector2 f = SizeFraction;
            Image window = UiKit.Panel(backdrop.transform, "Window", UiTheme.Panel);
            UiKit.Anchor(window.rectTransform, new Vector2((1 - f.x) / 2, (1 - f.y) / 2), new Vector2((1 + f.x) / 2, (1 + f.y) / 2));
            Image header = UiKit.Panel(window.transform, "Header", UiTheme.Header);
            UiKit.Anchor(header.rectTransform, new Vector2(0, 1), new Vector2(1, 1));
            header.rectTransform.offsetMin = new Vector2(0, -48);
            header.rectTransform.offsetMax = Vector2.zero;
            TitleLabel = UiKit.Label(header.transform, Title, UiTheme.FontTitle);
            UiKit.Fill(TitleLabel.rectTransform, 10);
            Button close = UiKit.Button(header.transform, "X", Close, UiTheme.ButtonDanger, 44);
            RectTransform cr = (RectTransform)close.transform;
            cr.anchorMin = new Vector2(1, 0);
            cr.anchorMax = new Vector2(1, 1);
            cr.offsetMin = new Vector2(-52, 6);
            cr.offsetMax = new Vector2(-8, -6);
            Body = UiKit.Column(window.transform, "Body", 8, false);
            UiKit.Anchor(Body, Vector2.zero, Vector2.one);
            Body.offsetMin = new Vector2(UiTheme.Padding, UiTheme.Padding);
            Body.offsetMax = new Vector2(-UiTheme.Padding, -48 - UiTheme.Padding);
            Build();
            Root.SetActive(false);
        }

        /// <summary>Builds the static content.</summary>
        protected abstract void Build();

        /// <summary>Updates dynamic content (called on open and on game events while open).</summary>
        public virtual void Refresh()
        {
        }

        /// <summary>Per-frame update while open.</summary>
        public virtual void Tick()
        {
        }

        /// <summary>Shows the window.</summary>
        public virtual void Open()
        {
            if (Root == null)
            {
                return;
            }

            Order = ++_order;
            Root.SetActive(true);
            Root.transform.SetAsLastSibling();
            UiState.Set(this, true);
            Refresh();
        }

        /// <summary>Hides the window.</summary>
        public virtual void Close()
        {
            if (Root == null)
            {
                return;
            }

            Root.SetActive(false);
            UiState.Set(this, false);
        }

        /// <summary>Opens or closes.</summary>
        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>Shows a result line in the feed (green ok / red error).</summary>
        protected void Report(Garage.Game.CommandResult r)
        {
            Ui.Toast(r.Message, r.Ok);
        }
    }
}
