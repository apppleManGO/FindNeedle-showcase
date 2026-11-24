using UnityEngine;
using Style = BeachCanvas.Style;

// The emote wheel over the crosshair while the mouse wheel button is held: eight segments of a ring, each
// with a painted icon and its words, the one under the pointer lit, and the choice spelled out in the middle.
public sealed partial class BeachHUD
{
    const int RingPixels = 256;
    Sprite[] ringSegments, ringLit, signIcons, signIconsLit;
    static readonly Style wheelWords = new Style{size=13,weight=FontStyle.Bold,anchor=TextAnchor.MiddleCenter};
    static readonly Style wheelChoice = new Style{size=17,weight=FontStyle.Bold,anchor=TextAnchor.MiddleCenter,wrap=true};
    static readonly Color RingShade = new Color(.075f,.165f,.195f,.86f);
    static readonly Color IconDark = new Color(.2f,.13f,.05f);

    void CreateWheel()
    {
        int count = BeachBody.Wheel.Length;
        ringSegments = new Sprite[count];
        ringLit = new Sprite[count];
        signIcons = new Sprite[count];
        signIconsLit = new Sprite[count];
        for (int i=0; i<count; i++)
        {
            int index = i;
            ringSegments[i] = Painted(p => RingSegment(p,index),RingShade,RingPixels);
            ringLit[i] = Painted(p => RingSegment(p,index),new Color(Gold.r,Gold.g,Gold.b,.96f),RingPixels);
            var shape = SignShape(BeachBody.Wheel[i]);
            signIcons[i] = Painted(shape,Color.white);
            signIconsLit[i] = Painted(shape,IconDark);
        }
    }

    void DrawWheel(float w,float h)
    {
        var wheel = game.Local.Wheel;
        Vector2 centre = new Vector2(w*.5f,h*.5f);
        float outer = BeachEmoteWheel.Outer;
        var ring = new Rect(centre.x-outer,centre.y-outer,outer*2,outer*2);
        int lit = wheel.Hovered;
        for (int i=0; i<ringSegments.Length; i++) ui.Icon(ring,i==lit ? ringLit[i] : ringSegments[i]);
        float across = (BeachEmoteWheel.Inner+outer)*.5f;
        for (int i=0; i<signIcons.Length; i++)
        {
            Vector2 at = centre+BeachEmoteWheel.Direction(i)*across;
            ui.Icon(new Rect(at.x-19,at.y-27,38,38),i==lit ? signIconsLit[i] : signIcons[i]);
            StatusLine(new Rect(at.x-46,at.y+11,92,18),BeachBody.Said(BeachBody.Wheel[i]),i==lit ? IconDark : Color.white,wheelWords);
        }
        string middle = lit<0 ? "놓으면 취소" : BeachBody.Said(BeachBody.Wheel[lit]);
        Panel(new Rect(centre.x-46,centre.y-17,92,34));
        StatusLine(new Rect(centre.x-46,centre.y-17,92,34),middle,lit<0 ? Muted : Gold,wheelChoice);
        // Where the mouse is pointing, so the way to a segment is plain.
        Vector2 dot = centre+wheel.Pointer;
        Fill(new Rect(dot.x-3,dot.y-3,6,6),lit<0 ? Muted : Gold);
    }

    // One eighth of a ring with a small gap either side; `index` counts clockwise from the top.
    static float RingSegment(Vector2 p,int index)
    {
        float half = RingPixels*.5f;
        Vector2 d = p-Vector2.one*half;
        float r = d.magnitude;
        float outer = half-1.5f, inner = half*BeachEmoteWheel.Inner/BeachEmoteWheel.Outer;
        int count = BeachBody.Wheel.Length;
        // Texture y runs up, so clockwise from the top is atan2(x, y).
        float angle = Mathf.Atan2(d.x,d.y)*Mathf.Rad2Deg;
        float off = Mathf.Abs(Mathf.DeltaAngle(angle,index*360f/count))*Mathf.Deg2Rad;
        float side = r*(off-Mathf.PI/count)+2.5f;
        return Mathf.Max(Mathf.Max(inner-r,r-outer),side);
    }

    static System.Func<Vector2,float> SignShape(BeachBody.Gesture sign) => sign switch
    {
        BeachBody.Gesture.Wave => Hand,
        BeachBody.Gesture.Cheer => Star,
        BeachBody.Gesture.Point => DownArrow,
        BeachBody.Gesture.No => Cross,
        BeachBody.Gesture.Come => Chevrons,
        BeachBody.Gesture.Good => Check,
        BeachBody.Gesture.Thanks => Heart,
        _ => Sun,
    };

    // An open hand: palm, four fingers, thumb.
    static float Hand(Vector2 p)
    {
        float shape = Segment(p,new Vector2(32,19),new Vector2(32,29))-11.5f;
        float[] x = {23.5f,29.8f,36.1f,42.4f}, top = {48,54,53,47};
        for (int i=0; i<4; i++) shape = Mathf.Min(shape,Segment(p,new Vector2(x[i],30),new Vector2(x[i],top[i]))-3.2f);
        return Mathf.Min(shape,Segment(p,new Vector2(21,25),new Vector2(11,36))-3.5f);
    }

    // Five points, the first at the top.
    static float Star(Vector2 p)
    {
        Vector2 d = p-new Vector2(32,30);
        float angle = Mathf.Atan2(d.x,d.y);
        float reach = 11f+16f*Mathf.Pow((Mathf.Cos(5*angle)+1)*.5f,2.2f);
        return (d.magnitude-reach)*.7f;
    }

    // An arrow down onto a spot of sand.
    static float DownArrow(Vector2 p)
    {
        float shape = Segment(p,new Vector2(32,56),new Vector2(32,22))-3.2f;
        shape = Mathf.Min(shape,Segment(p,new Vector2(32,17),new Vector2(20,29))-3.2f);
        shape = Mathf.Min(shape,Segment(p,new Vector2(32,17),new Vector2(44,29))-3.2f);
        Vector2 spot = (p-new Vector2(32,8))/new Vector2(14,3.4f);
        return Mathf.Min(shape,(spot.magnitude-1)*3.4f);
    }

    // Two chevrons pointing down, toward the one looking: come here.
    static float Chevrons(Vector2 p)
    {
        float shape = Mathf.Min(Segment(p,new Vector2(16,48),new Vector2(32,35)),Segment(p,new Vector2(32,35),new Vector2(48,48)));
        shape = Mathf.Min(shape,Mathf.Min(Segment(p,new Vector2(16,31),new Vector2(32,18)),Segment(p,new Vector2(32,18),new Vector2(48,31))));
        return shape-3.6f;
    }

    static float Check(Vector2 p) =>
        Mathf.Min(Segment(p,new Vector2(13,33),new Vector2(26,20)),Segment(p,new Vector2(26,20),new Vector2(51,46)))-4.2f;

    static float Heart(Vector2 p)
    {
        float lobes = Mathf.Min(Vector2.Distance(p,new Vector2(23,40)),Vector2.Distance(p,new Vector2(41,40)))-11.5f;
        return Mathf.Min(lobes,Triangle(p,new Vector2(12.2f,36),new Vector2(51.8f,36),new Vector2(32,11)));
    }

    static float Sun(Vector2 p)
    {
        Vector2 c = new Vector2(32,32);
        float shape = Vector2.Distance(p,c)-10f;
        for (int i=0; i<8; i++)
        {
            float a = i*Mathf.PI/4;
            Vector2 d = new Vector2(Mathf.Sin(a),Mathf.Cos(a));
            shape = Mathf.Min(shape,Segment(p,c+d*16.5f,c+d*25f)-2.6f);
        }
        return shape;
    }

    // Signed distance to a triangle: negative inside.
    static float Triangle(Vector2 p,Vector2 a,Vector2 b,Vector2 c)
    {
        float edge = Mathf.Min(Segment(p,a,b),Mathf.Min(Segment(p,b,c),Segment(p,c,a)));
        static float Side(Vector2 p,Vector2 a,Vector2 b) => (b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);
        float s1 = Side(p,a,b), s2 = Side(p,b,c), s3 = Side(p,c,a);
        bool inside = (s1>=0 && s2>=0 && s3>=0) || (s1<=0 && s2<=0 && s3<=0);
        return inside ? -edge : edge;
    }
}
