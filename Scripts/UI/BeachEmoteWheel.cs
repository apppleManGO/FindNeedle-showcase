using UnityEngine;

// The emote wheel: held open with the mouse wheel button, a ring of eight signs around the crosshair.
// While it is open the mouse moves a pointer instead of the view; the sign under the pointer is made when
// the button is let go. Let go near the middle and nothing happens.
public sealed class BeachEmoteWheel
{
    // In HUD layout units: the ring's outer and inner edges, and how far the pointer must go to choose.
    public const float Outer = 150, Inner = 58, Dead = 34;
    // Mouse pixels to layout units.
    const float Sensitivity = .45f;

    public bool Open { get; private set; }
    // From the middle of the wheel, x to the right and y down, like the HUD layout.
    public Vector2 Pointer { get; private set; }

    // The segment under the pointer, clockwise from the top, or -1 near the middle.
    public int Hovered => Open && Pointer.magnitude>=Dead ? Segment(Pointer) : -1;
    public BeachBody.Gesture Choice => Hovered<0 ? BeachBody.Gesture.None : BeachBody.Wheel[Hovered];

    public static int Segment(Vector2 pointer)
    {
        float clockwise = Mathf.Atan2(pointer.x,-pointer.y)*Mathf.Rad2Deg;
        int count = BeachBody.Wheel.Length;
        return ((Mathf.RoundToInt(clockwise/(360f/count))%count)+count)%count;
    }

    // Where segment `index` sits, clockwise from the top, as a direction in layout units (y down).
    public static Vector2 Direction(int index)
    {
        float angle = index*2*Mathf.PI/BeachBody.Wheel.Length;
        return new Vector2(Mathf.Sin(angle),-Mathf.Cos(angle));
    }

    public void Show()
    {
        Open = true;
        Pointer = Vector2.zero;
    }

    // `delta` is the mouse movement in pixels, y up.
    public void Move(Vector2 delta)
    {
        if (!Open) return;
        Pointer = Vector2.ClampMagnitude(Pointer+new Vector2(delta.x,-delta.y)*Sensitivity,Outer*.8f);
    }

    // Closes the wheel and gives the sign chosen, or None.
    public BeachBody.Gesture Release()
    {
        var chosen = Choice;
        Cancel();
        return chosen;
    }

    public void Cancel()
    {
        Open = false;
        Pointer = Vector2.zero;
    }
}
