using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Draws the HUD with uGUI while letting BeachHUD describe each frame as a list of rectangles,
// top-left origin, in the same 800 x 480 based units the layout was written in.
public sealed class BeachCanvas
{
    public struct Style
    {
        public int size;
        public FontStyle weight;
        public TextAnchor anchor;
        public bool wrap;
    }

    sealed class Node
    {
        public GameObject root;
        public RectTransform rect;
        public Image image;
        public Text text;
        public Button button;
        public bool clicked;
        public string caption;
    }

    public readonly Canvas Canvas;
    public Vector2 Size { get; private set; }
    public event System.Action Clicked;
    readonly CanvasScaler scaler;
    readonly Font font;
    readonly List<Node> nodes = new();
    readonly Sprite panel, button, pressed, disabled;
    public readonly Sprite Star;
    int used;

    static readonly Color PanelTint = new Color(.075f,.165f,.195f,.97f);
    static readonly Color ButtonInk = new Color(.27f,.17f,.06f);
    static readonly Color DisabledInk = new Color(.42f,.44f,.46f);

    public BeachCanvas(Transform parent,Font font)
    {
        this.font = font;
        var holder = new GameObject("HUD 캔버스");
        holder.transform.SetParent(parent,false);
        Canvas = holder.AddComponent<Canvas>();
        Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Canvas.sortingOrder = 10;
        scaler = holder.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        holder.AddComponent<GraphicRaycaster>();
        panel = Resources.Load<Sprite>("BeachUI/panel");
        button = Resources.Load<Sprite>("BeachUI/button");
        pressed = Resources.Load<Sprite>("BeachUI/button_pressed");
        disabled = Resources.Load<Sprite>("BeachUI/button_disabled");
        Star = Resources.Load<Sprite>("BeachUI/star");
        if (!EventSystem.current)
        {
            var events = new GameObject("HUD 입력");
            events.transform.SetParent(parent,false);
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }

    // Call once a frame before drawing. Returns the drawable size in layout units.
    public Vector2 Begin()
    {
        used = 0;
        Rect pixels = Canvas.pixelRect;
        float scale = Mathf.Clamp(Mathf.Min(pixels.width/800f,pixels.height/480f),.45f,1.6f);
        scaler.scaleFactor = scale;
        Size = new Vector2(pixels.width/scale,pixels.height/scale);
        return Size;
    }

    public void End()
    {
        for (int i=used; i<nodes.Count; i++)
            if (nodes[i].root.activeSelf) nodes[i].root.SetActive(false);
    }

    Node Next(Rect area)
    {
        if (used==nodes.Count) nodes.Add(Create());
        var node = nodes[used++];
        if (!node.root.activeSelf) node.root.SetActive(true);
        node.rect.anchoredPosition = new Vector2(area.x,-area.y);
        node.rect.sizeDelta = area.size;
        return node;
    }

    Node Create()
    {
        var node = new Node();
        node.root = new GameObject("요소",typeof(RectTransform));
        node.rect = (RectTransform)node.root.transform;
        node.rect.SetParent(Canvas.transform,false);
        node.rect.anchorMin = node.rect.anchorMax = node.rect.pivot = new Vector2(0,1);
        node.image = node.root.AddComponent<Image>();
        node.button = node.root.AddComponent<Button>();
        node.button.transition = Selectable.Transition.SpriteSwap;
        node.button.navigation = new Navigation { mode = Navigation.Mode.None };
        node.button.onClick.AddListener(() => { node.clicked = true; Clicked?.Invoke(); });
        var label = new GameObject("글자",typeof(RectTransform));
        var labelRect = (RectTransform)label.transform;
        labelRect.SetParent(node.rect,false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        node.text = label.AddComponent<Text>();
        node.text.font = font;
        node.text.raycastTarget = false;
        node.text.supportRichText = false;
        node.text.verticalOverflow = VerticalWrapMode.Overflow;
        return node;
    }

    void Picture(Node node,Sprite sprite,Color tint,bool sliced)
    {
        node.image.enabled = true;
        node.image.sprite = sprite;
        node.image.type = sliced && sprite ? Image.Type.Sliced : Image.Type.Simple;
        node.image.color = tint;
        node.image.preserveAspect = !sliced && sprite;
    }

    void Plain(Node node)
    {
        node.button.enabled = false;
        node.image.raycastTarget = false;
        node.clicked = false;
        node.caption = null;
    }

    void Write(Node node,string words,Style style,Color ink)
    {
        node.text.enabled = true;
        node.text.text = words;
        node.text.fontSize = style.size;
        node.text.fontStyle = style.weight;
        node.text.alignment = style.anchor;
        node.text.horizontalOverflow = style.wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        node.text.color = ink;
        var rect = (RectTransform)node.text.transform;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public void Panel(Rect area)
    {
        var node = Next(area);
        Plain(node);
        Picture(node,panel,PanelTint,true);
        node.text.enabled = false;
    }

    public void Fill(Rect area,Color colour)
    {
        var node = Next(area);
        Plain(node);
        Picture(node,null,colour,false);
        node.text.enabled = false;
    }

    public void Icon(Rect area,Sprite sprite)
    {
        var node = Next(area);
        Plain(node);
        Picture(node,sprite,Color.white,false);
        node.text.enabled = false;
    }

    public void Label(Rect area,string words,Style style,Color ink)
    {
        var node = Next(area);
        Plain(node);
        node.image.enabled = false;
        Write(node,words,style,ink);
    }

    // True on the frame after the player clicked it.
    public bool Button(Rect area,string words,Style style,bool enabled=true,Color? tint=null)
    {
        var node = Next(area);
        bool clicked = node.clicked && enabled && node.caption==words;
        node.clicked = false;
        node.caption = words;
        Picture(node,enabled ? button : disabled,enabled ? tint ?? Color.white : Color.white,true);
        node.image.raycastTarget = enabled;
        node.button.enabled = enabled;
        var swap = node.button.spriteState;
        swap.pressedSprite = pressed;
        node.button.spriteState = swap;
        Write(node,words,style,enabled ? ButtonInk : DisabledInk);
        // Sit the caption on the face of the button, above its lower lip.
        ((RectTransform)node.text.transform).offsetMin = new Vector2(0,area.height*.12f);
        return clicked;
    }
}
