using System.Collections.Generic;
using UnityEngine;

// A beachgoer's whole figure: one of the swimsuit characters (Tirgames LowPoly Beach Characters),
// or the Quaternius Worker when that pack is not in the project.
// The pack ships only idle and walk, so cheering and collapsing are posed from code.
public sealed class BeachBody : MonoBehaviour
{
    public enum Act { Idle, Walk, Cheer, Fall }
    // Short signs to the team from the emote wheel (or keys 1-8), laid over standing or walking.
    public enum Gesture : byte { None, Wave, Cheer, Point, No, Come, Good, Thanks, Hot }
    // Clockwise from the top of the wheel, and keys 1-8 in the same order.
    public static readonly Gesture[] Wheel =
        { Gesture.Wave, Gesture.Cheer, Gesture.Point, Gesture.No, Gesture.Come, Gesture.Good, Gesture.Thanks, Gesture.Hot };
    public static bool Known(Gesture sign) => sign>Gesture.None && sign<=Gesture.Hot;
    public const float GestureSeconds = 1.6f;

    static readonly string[] Swimmers =
    {
        "Char_BeachMale01","Char_BeachFemale01","Char_BeachMale02","Char_BeachFemale02","Char_BeachMale03",
        "Char_BeachFemale03","Char_BeachMale04","Char_BeachFemale04","Char_BeachMale05","Char_BeachFemale05",
    };
    public static int LookCount => Swimmers.Length;
    public static string LookName(int look) => "해변 친구 "+(look+1);

    // Crown of the head. The first-person eye sits at 1.16 m.
    const float Height = 1.47f;
    static readonly Dictionary<Material,Material> Converted = new();

    public readonly List<Renderer> Parts = new();
    // How another player's figure holds what they hold: in the right hand, in both hands before the chest,
    // and whether they are digging. Only the swimsuit figures have the arm bones for it.
    public bool Carrying, BothHands, Digging;
    public Transform RightHand => animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
    public Transform LeftHand => animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
    bool leaning;
    // The swimsuit figures share one atlas material, so the whole figure is skin as far as sunburn goes.
    public bool Swimmer { get; private set; }

    Animator animator;
    Animation legacy;
    string idleClip, walkClip, cheerClip, fallClip, playing;
    Act act;
    float actStart;
    Vector3 rest;
    Quaternion facing = Quaternion.identity;
    Gesture gesture;
    float gestureStart;

    // `look` picks the character; players pass their id so everyone sees the same figure for the same person.
    public static BeachBody Create(Transform parent,int look)
    {
        bool swimmer = true;
        var model = Resources.Load<GameObject>("BeachSwimmers/"+Swimmers[Mathf.Abs(look)%Swimmers.Length]);
        if (!model) { swimmer = false; model = Resources.Load<GameObject>("BeachBody/Worker"); }
        if (!model) return null;
        var figure = Instantiate(model,parent);
        figure.name = swimmer ? "해변 사람" : "수선공 몸";
        var body = figure.AddComponent<BeachBody>();
        body.Swimmer = swimmer;
        body.Dress();
        if (body.Parts.Count>0) return body;
        Destroy(figure);
        return null;
    }

    void Dress()
    {
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        GetComponentsInChildren(true,Parts);
        Fit();
        foreach (var part in Parts)
        {
            if (part is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            if (Swimmer) part.sharedMaterials = System.Array.ConvertAll(part.sharedMaterials,ForUrp);
        }
        rest = transform.localPosition;
        animator = GetComponentInChildren<Animator>();
        if (animator)
        {
            animator.applyRootMotion = false;
            // In first person only the shadow shows, and it should still walk.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return;
        }
        legacy = GetComponentInChildren<Animation>();
        if (!legacy) return;
        idleClip = Clip("idle");
        walkClip = Clip("walk");
        cheerClip = Clip("victory","wave","clap","cheer","yes");
        fallClip = Clip("death","fall","defeat");
        foreach (string loop in new[]{idleClip,walkClip,cheerClip})
            if (loop!=null) legacy[loop].wrapMode = WrapMode.Loop;
        if (fallClip!=null) legacy[fallClip].wrapMode = WrapMode.ClampForever;
        PlayLegacy(idleClip);
    }

    // Stands the figure on the parent's origin at a common height, whatever scale the model ships in.
    void Fit()
    {
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        var baked = new Mesh();
        foreach (var part in Parts)
        {
            if (part is not SkinnedMeshRenderer skinned || !skinned.sharedMesh) continue;
            // Renderer bounds are unreliable before the first skinning pass, so measure the posed mesh itself.
            skinned.BakeMesh(baked,true);
            var place = Matrix4x4.TRS(skinned.transform.position,skinned.transform.rotation,Vector3.one);
            foreach (var vertex in baked.vertices)
            {
                float y = place.MultiplyPoint3x4(vertex).y-transform.parent.position.y;
                low = Mathf.Min(low,y);
                high = Mathf.Max(high,y);
            }
        }
        Destroy(baked);
        if (high-low<.01f) return;
        float fit = Height/(high-low);
        transform.localScale *= fit;
        transform.localPosition = Vector3.down*(low*fit);
    }

    // The pack's materials are written for the built-in pipeline.
    static Material ForUrp(Material source)
    {
        if (!source) return source;
        if (Converted.TryGetValue(source,out var done) && done) return done;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader || source.shader==shader) return source;
        var lit = new Material(shader) { name = source.name };
        if (source.HasProperty("_MainTex")) lit.SetTexture("_BaseMap",source.GetTexture("_MainTex"));
        lit.SetColor("_BaseColor",source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
        lit.SetFloat("_Smoothness",.08f);
        Converted[source] = lit;
        return lit;
    }

    // First clip whose name contains one of the words, shortest name first so "Idle" beats "Idle_Gun".
    string Clip(params string[] words)
    {
        string best = null;
        foreach (string word in words)
        {
            foreach (AnimationState state in legacy)
                if (state.name.ToLowerInvariant().Contains(word) && (best==null || state.name.Length<best.Length))
                    best = state.name;
            if (best!=null) break;
        }
        return best;
    }

    void PlayLegacy(string clip)
    {
        if (clip==null || clip==playing) return;
        playing = clip;
        legacy.CrossFade(clip,.18f);
    }

    public void Play(Act next)
    {
        if (next==act) return;
        act = next;
        actStart = Time.time;
        if (legacy)
        {
            PlayLegacy(next switch { Act.Walk => walkClip, Act.Cheer => cheerClip ?? idleClip, Act.Fall => fallClip ?? idleClip, _ => idleClip });
            return;
        }
        if (animator && animator.isActiveAndEnabled)
            animator.CrossFadeInFixedTime(next==Act.Walk ? "Walk" : "Idle",.18f);
        if (next!=Act.Fall && next!=Act.Cheer) Stand();
    }

    // Starts a sign, replacing any still showing.
    public void Sign(Gesture next)
    {
        gesture = next;
        gestureStart = Time.time;
    }

    // The sign being made now, or None once it has run its time.
    public Gesture Showing => gesture!=Gesture.None && Time.time-gestureStart<GestureSeconds ? gesture : Gesture.None;
    public static string Said(Gesture sign) => sign switch
    {
        Gesture.Wave => "안녕!", Gesture.Cheer => "와!", Gesture.Point => "여기!", Gesture.No => "아니",
        Gesture.Come => "이리 와!", Gesture.Good => "좋아!", Gesture.Thanks => "고마워", Gesture.Hot => "덥다…", _ => null,
    };

    // Turns the figure about its feet, e.g. to face the camera on the title screen.
    public void Face(float degrees)
    {
        facing = Quaternion.Euler(0,degrees,0);
        if (act!=Act.Fall || legacy) Stand();
    }

    void Stand()
    {
        transform.localRotation = facing;
        transform.localPosition = rest;
    }

    void LateUpdate()
    {
        if (legacy || !animator) return;
        float t = Time.time-actStart;
        if (act==Act.Fall)
        {
            // Knees give first, then the figure tips onto its back.
            float k = Mathf.Clamp01(t/.75f);
            k = k*k*(3-2*k);
            transform.localRotation = facing*Quaternion.Euler(-88f*k,0,0);
            transform.localPosition = rest+Vector3.up*(.07f*k);
            Lift(HumanBodyBones.LeftUpperArm,-1,Vector3.down,k);
            Lift(HumanBodyBones.RightUpperArm,1,Vector3.down,k);
        }
        else if (act==Act.Idle || act==Act.Walk)
        {
            Hold(t);
            if (!Digging) Gesturing();
        }
        else if (act==Act.Cheer)
        {
            float hop = Mathf.Abs(Mathf.Sin(t*6.5f));
            transform.localRotation = facing;
            transform.localPosition = rest+Vector3.up*(.05f*hop);
            float raise = Mathf.Clamp01(t/.25f);
            Lift(HumanBodyBones.LeftUpperArm,-1,Vector3.up*(1.6f+.5f*hop),raise);
            Lift(HumanBodyBones.RightUpperArm,1,Vector3.up*(1.6f+.5f*hop),raise);
        }
    }

    void Hold(float t)
    {
        if (Digging)
        {
            // Bent over the sand, the right arm working it.
            leaning = true;
            transform.localRotation = facing*Quaternion.Euler(24f,0,0);
            float stroke = .5f+.5f*Mathf.Sin(Time.time*11f);
            Lift(HumanBodyBones.RightUpperArm,1,new Vector3(-.9f,-1f,.5f+.7f*stroke),.9f);
            if (BothHands) Lift(HumanBodyBones.LeftUpperArm,-1,new Vector3(.9f,-1f,.5f+.7f*stroke),.9f);
            return;
        }
        if (leaning) { leaning = false; transform.localRotation = facing; }
        if (BothHands)
        {
            Lift(HumanBodyBones.RightUpperArm,1,new Vector3(-1f,-.5f,1f),.85f);
            Lift(HumanBodyBones.LeftUpperArm,-1,new Vector3(1f,-.5f,1f),.85f);
        }
        else if (Carrying) Lift(HumanBodyBones.RightUpperArm,1,new Vector3(-.6f,-1f,.7f),.7f);
    }

    void Gesturing()
    {
        var sign = Showing;
        if (sign==Gesture.None) return;
        float t = Time.time-gestureStart;
        // Eased in over a fifth of a second and out over the last quarter.
        float k = Mathf.Clamp01(Mathf.Min(t/.2f,(GestureSeconds-t)/.25f));
        k = k*k*(3-2*k);
        switch (sign)
        {
            case Gesture.Wave:
                // The right hand high, swinging side to side.
                Lift(HumanBodyBones.RightUpperArm,1,new Vector3(-.75f+.45f*Mathf.Sin(t*11f),1.7f,.15f),k);
                break;
            case Gesture.Cheer:
            {
                float hop = Mathf.Abs(Mathf.Sin(t*6.5f));
                transform.localPosition = rest+Vector3.up*(.05f*hop*k);
                Lift(HumanBodyBones.LeftUpperArm,-1,Vector3.up*(1.6f+.5f*hop),k);
                Lift(HumanBodyBones.RightUpperArm,1,Vector3.up*(1.6f+.5f*hop),k);
                break;
            }
            case Gesture.Point:
                // Straight ahead and a little down, at the sand in front.
                Lift(HumanBodyBones.RightUpperArm,1,new Vector3(-1f,-.25f,1.6f),k);
                Straighten(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,k);
                break;
            case Gesture.No:
            {
                // Head shaking, hands turned out a little.
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head) head.rotation = Quaternion.AngleAxis(28f*k*Mathf.Sin(t*13f),transform.up)*head.rotation;
                Lift(HumanBodyBones.LeftUpperArm,-1,new Vector3(.35f,-.8f,.45f),k*.8f);
                Lift(HumanBodyBones.RightUpperArm,1,new Vector3(-.35f,-.8f,.45f),k*.8f);
                break;
            }
            case Gesture.Come:
            {
                // The right arm held out, the forearm curling back toward the body again and again.
                float curl = .5f+.5f*Mathf.Sin(t*9f);
                Lift(HumanBodyBones.RightUpperArm,1,new Vector3(-.85f,-.45f,1f),k);
                Forearm(1,Vector3.Lerp(new Vector3(0,.25f,1f),new Vector3(-.1f,1f,.15f),curl),k);
                break;
            }
            case Gesture.Good:
            {
                // A fist raised before the chest and a nod or two.
                Lift(HumanBodyBones.RightUpperArm,1,new Vector3(-1f,-.85f,.55f),k);
                Forearm(1,new Vector3(-.15f,1f,.55f),k);
                Turn(HumanBodyBones.Head,transform.right,16f*k*Mathf.Max(0,Mathf.Sin(t*9f)));
                break;
            }
            case Gesture.Thanks:
                // A small bow from the waist, the head following.
                Turn(HumanBodyBones.Spine,transform.right,32f*k);
                Turn(HumanBodyBones.Head,transform.right,12f*k);
                break;
            case Gesture.Hot:
            {
                // Fanning the face with the right hand, head tipped back a little.
                Lift(HumanBodyBones.RightUpperArm,1,new Vector3(-.65f,-.55f,.45f),k);
                Forearm(1,new Vector3(-.55f+.3f*Mathf.Sin(t*16f),1f,.35f),k);
                Turn(HumanBodyBones.Head,transform.right,-12f*k);
                break;
            }
        }
    }

    // Turns one bone about `axis` by `degrees`, over the clip's pose.
    void Turn(HumanBodyBones bone,Vector3 axis,float degrees)
    {
        var part = animator.GetBoneTransform(bone);
        if (part) part.rotation = Quaternion.AngleAxis(degrees,axis)*part.rotation;
    }

    // Points a forearm along `aim`, in the figure's own frame (x to its right), after the upper arm is placed.
    void Forearm(int side,Vector3 aim,float amount)
    {
        var lower = animator.GetBoneTransform(side<0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = animator.GetBoneTransform(side<0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (!lower || !hand) return;
        var bend = Quaternion.FromToRotation(hand.position-lower.position,transform.rotation*aim);
        lower.rotation = Quaternion.Slerp(Quaternion.identity,bend,amount)*lower.rotation;
    }

    // Unbends the elbow so the forearm carries on along the upper arm.
    void Straighten(HumanBodyBones upperBone,HumanBodyBones lowerBone,HumanBodyBones handBone,float amount)
    {
        var upper = animator.GetBoneTransform(upperBone);
        var lower = animator.GetBoneTransform(lowerBone);
        var hand = animator.GetBoneTransform(handBone);
        if (!upper || !lower || !hand) return;
        var unbend = Quaternion.FromToRotation(hand.position-lower.position,lower.position-upper.position);
        lower.rotation = Quaternion.Slerp(Quaternion.identity,unbend,amount)*lower.rotation;
    }

    // Swings an arm toward `aim` (in the figure's own frame, x pointing out to that side), over whatever the clip posed.
    void Lift(HumanBodyBones bone,int side,Vector3 aim,float amount)
    {
        var upper = animator.GetBoneTransform(bone);
        var lower = animator.GetBoneTransform(side<0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        if (!upper || !lower) return;
        Vector3 now = lower.position-upper.position;
        Vector3 want = transform.rotation*(aim+Vector3.right*side);
        upper.rotation = Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(now,want),amount)*upper.rotation;
    }
}
