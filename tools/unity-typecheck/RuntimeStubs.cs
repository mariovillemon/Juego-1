// Minimal stand-ins for Unity package APIs (uGUI, TextMeshPro, Input System, HDRP, Cinemachine) used ONLY to
// type-check the game's Unity scripts outside the editor. Signatures follow the real packages.
#pragma warning disable CS0067, CS0649, CS0414, CS1591, CS0108, CS0114
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEngine.EventSystems
{
    public class BaseEventData { }
    public class PointerEventData : BaseEventData { }
    public interface IEventSystemHandler { }
    public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData e); }
    public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData e); }
    public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData e); }
    public interface IPointerEnterHandler : IEventSystemHandler { void OnPointerEnter(PointerEventData e); }
    public class UIBehaviour : MonoBehaviour { }
    public class EventSystem : UIBehaviour { public static EventSystem current; public GameObject currentSelectedGameObject => null; }
    public class BaseInputModule : UIBehaviour { }
}

namespace UnityEngine.UI
{
    using UnityEngine.EventSystems;
    public class Graphic : UIBehaviour { public Color color; public bool raycastTarget; public RectTransform rectTransform => null; public virtual Material material { get; set; } }
    public class MaskableGraphic : Graphic { }
    public class Image : MaskableGraphic { public enum Type { Simple, Sliced, Tiled, Filled } public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 } public Type type; public FillMethod fillMethod; public float fillAmount; public Sprite sprite; }
    public class RawImage : MaskableGraphic { public Texture texture; }
    public class Selectable : UIBehaviour { public bool interactable; public Graphic targetGraphic; public ColorBlock colors; }
    public struct ColorBlock { public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor; public float colorMultiplier, fadeDuration; }
    public class Button : Selectable { public class ButtonClickedEvent : UnityEvent { } public ButtonClickedEvent onClick = new ButtonClickedEvent(); }
    public interface ILayoutElement { }
    public class LayoutElement : UIBehaviour, ILayoutElement { public float minWidth, minHeight, preferredWidth, preferredHeight, flexibleWidth, flexibleHeight; public bool ignoreLayout; }
    public abstract class LayoutGroup : UIBehaviour { public RectOffset padding; public TextAnchor childAlignment; }
    public abstract class HorizontalOrVerticalLayoutGroup : LayoutGroup { public float spacing; public bool childForceExpandWidth, childForceExpandHeight, childControlWidth, childControlHeight; }
    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class GridLayoutGroup : LayoutGroup { public enum Constraint { Flexible, FixedColumnCount, FixedRowCount } public Vector2 cellSize, spacing; public Constraint constraint; public int constraintCount; }
    public class ContentSizeFitter : UIBehaviour { public enum FitMode { Unconstrained, MinSize, PreferredSize } public FitMode horizontalFit, verticalFit; }
    public class ScrollRect : UIBehaviour { public RectTransform content, viewport; public bool horizontal, vertical; public float scrollSensitivity; }
    public class RectMask2D : UIBehaviour { }
    public class CanvasScaler : UIBehaviour { public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize } public ScaleMode uiScaleMode; public Vector2 referenceResolution; public float matchWidthOrHeight; }
    public class GraphicRaycaster : UIBehaviour { }
}

namespace TMPro
{
    public enum TextAlignmentOptions { TopLeft, Top, TopRight, Left, Center, Right, MidlineLeft, Midline, MidlineRight, BottomLeft, Bottom, BottomRight }
    public enum TextWrappingModes { NoWrap, Normal, PreserveWhitespace, PreserveWhitespaceNoWrap }
    public class TMP_Text : UnityEngine.UI.MaskableGraphic { public string text; public float fontSize; public TextAlignmentOptions alignment; public TextWrappingModes textWrappingMode; public bool richText; public float outlineWidth; public Color32 outlineColor; }
    public class TextMeshProUGUI : TMP_Text { }
    public class TMP_InputField : UnityEngine.UI.Selectable
    {
        public enum ContentType { Standard, Autocorrected, IntegerNumber, DecimalNumber, Alphanumeric, Name, EmailAddress, Password, Pin, Custom }
        public class OnChangeEvent : UnityEvent<string> { }
        public RectTransform textViewport; public TMP_Text textComponent; public UnityEngine.UI.Graphic placeholder; public ContentType contentType; public string text;
        public OnChangeEvent onValueChanged = new OnChangeEvent();
    }
}

namespace UnityEngine.InputSystem
{
    public enum InputActionType { Value, Button, PassThrough }
    public class InputAction : IDisposable
    {
        public InputAction(string name = null, InputActionType type = InputActionType.Value, string binding = null, string interactions = null, string processors = null, string expectedControlType = null) { }
        public void Enable() { } public void Disable() { } public void Dispose() { }
        public bool WasPressedThisFrame() => false; public bool WasReleasedThisFrame() => false; public bool IsPressed() => false;
        public TValue ReadValue<TValue>() where TValue : struct => default;
        public BindingSyntax AddBinding(string path, string interactions = null, string processors = null, string groups = null) => default;
        public CompositeSyntax AddCompositeBinding(string composite, string interactions = null, string processors = null) => default;
        public struct BindingSyntax { public BindingSyntax WithProcessor(string p) => this; }
        public struct CompositeSyntax { public CompositeSyntax With(string name, string binding, string groups = null, string processors = null) => this; }
    }
    namespace Controls { public class ButtonControl { public bool isPressed; public bool wasPressedThisFrame; } public class KeyControl : ButtonControl { } }
    public class InputDevice { }
    public class Keyboard : InputDevice
    {
        public static Keyboard current;
        public Controls.KeyControl shiftKey, jKey, escapeKey, tabKey, leftAltKey, upArrowKey, downArrowKey, leftArrowKey, rightArrowKey, numpadPlusKey, equalsKey, numpadMinusKey, minusKey, enterKey, spaceKey;
    }
    public class Mouse : InputDevice { public static Mouse current; }
    namespace UI { public class InputSystemUIInputModule : UnityEngine.EventSystems.BaseInputModule { } }
}

namespace UnityEngine.Rendering
{
    public class VolumeParameter<T> { public T value; public bool overrideState; public void Override(T v) { } }
    public class ClampedFloatParameter : VolumeParameter<float> { }
    public class MinFloatParameter : VolumeParameter<float> { }
    public class VolumeComponent : ScriptableObject { public bool active; }
    public class VolumeProfile : ScriptableObject { public List<VolumeComponent> components = new List<VolumeComponent>(); public bool TryGet<T>(out T c) where T : VolumeComponent { c = null; return false; } }
    public class Volume : MonoBehaviour { public bool isGlobal; public VolumeProfile profile, sharedProfile; }
}

namespace UnityEngine.Rendering.HighDefinition
{
    public enum DepthOfFieldMode { Off, UsePhysicalCamera, Manual }
    public class DepthOfFieldModeParameter : VolumeParameter<DepthOfFieldMode> { }
    public class DepthOfField : VolumeComponent
    {
        public DepthOfFieldModeParameter focusMode; public MinFloatParameter focusDistance, nearFocusStart, nearFocusEnd, farFocusStart, farFocusEnd;
    }
    public class HDAdditionalLightData : MonoBehaviour { }
    public class HDAdditionalCameraData : MonoBehaviour { }
    public class DecalProjector : MonoBehaviour { public Material material; public Vector3 size; public Vector3 pivot; public float fadeFactor; public float drawDistance; }
}
