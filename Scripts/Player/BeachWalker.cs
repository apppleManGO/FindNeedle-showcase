using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public sealed class BeachWalker : MonoBehaviour
{
    public const int MaxHealth = 100;
    [SerializeField, Range(0, MaxHealth)] float health = MaxHealth;
    public int Health => Mathf.CeilToInt(health);
    public float Heat { get; private set; }
    const double GrowthPerSecond = 0.06931471805599453; // ln(2) / 10: damage rate doubles every ten seconds.
    double overheatedSeconds;
    public float HeatDamageRate { get; private set; }
    public bool InShade { get; private set; }

    public void HitByBall() => health = Mathf.Max(0f,health-10f);

    // Back on their feet where they fell, cooled down. Used in co-op, where the beach cannot restart for one player.
    public void Revive()
    {
        health = MaxHealth;
        Heat = 0;
        overheatedSeconds = 0;
        HeatDamageRate = 0;
        Present(false);
    }

    public void UpdateExposure(bool shaded, float seconds)
    {
        InShade = shaded;
        if (shaded)
        {
            overheatedSeconds = 0;
            HeatDamageRate = 0;
            Heat = Mathf.MoveTowards(Heat,0f,seconds/10f);
            health = Mathf.Min(MaxHealth,health + seconds*8f);
            return;
        }

        const float timeToOverheat = 60f;
        float safeSeconds = (1f - Heat) * timeToOverheat;
        Heat = Mathf.Min(1f,Heat + seconds/timeToOverheat);
        float exposedSeconds = Mathf.Max(0f,seconds-safeSeconds);
        if (Heat < 1f) return;

        const double initialRate = 1;
        double startRate = initialRate * System.Math.Exp(GrowthPerSecond * overheatedSeconds);
        overheatedSeconds += exposedSeconds;
        double endRate = initialRate * System.Math.Exp(GrowthPerSecond * overheatedSeconds);
        // Integrate just the overheated part of this frame.
        double damage = (endRate - startRate) / GrowthPerSecond;
        health = Mathf.Max(0f, health - (float)damage);
        HeatDamageRate = (float)endRate;
    }

    public Camera view;
    public Transform leftLeg, rightLeg, rightArm;
    public GameObject trowel, detector;
    public Transform stall;
    Transform leftArm;
    float yaw, pitch = 28f, swingStart = -1;
    bool inspecting;
    public bool CuppedHands { get; set; }
    public bool HoldingBall { get; set; }
    readonly SkinnedMeshRenderer[] hands = new SkinnedMeshRenderer[2];
    readonly GameObject[] sleeves = new GameObject[2];
    // The player's whole figure. In first person only its shadow shows;
    // the title and clear screens step back and show it.
    BeachBody body;
    List<Renderer> bodyParts => body.Parts;
    readonly List<GameObject> firstPerson = new();

    // Heat is never spelled out on screen: the mender's skin slowly turns red instead.
    static readonly Color Sunburn = new Color(.95f,.17f,.11f);
    readonly List<(Renderer part,int slot,Color natural)> skin = new();
    MaterialPropertyBlock paint;
    float shownFlush = -1;
    // 0 while cool, 1 when overheated. Barely visible for the first quarter of the way.
    public float Flush => Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.2f,1f,Heat));

    void CollectSkin()
    {
        paint = new MaterialPropertyBlock();
        foreach (var hand in hands)
            if (hand && hand.sharedMaterial) skin.Add((hand,0,hand.sharedMaterial.color));
        if (!body) return;
        foreach (var part in bodyParts)
        {
            var materials = part.sharedMaterials;
            for (int slot=0; slot<materials.Length; slot++)
                if (materials[slot] && (body.Swimmer || materials[slot].name.ToLowerInvariant().Contains("skin")))
                    skin.Add((part,slot,materials[slot].color));
        }
    }

    // Which beachgoer this player is. Co-op passes the player's id so everyone sees the same figure.
    public void Wear(int look)
    {
        if (look==worn || !body) return;
        bool shown = Presenting;
        if (shown) Present(false);
        body.gameObject.SetActive(false);
        Destroy(body.gameObject);
        body = null;
        CreateBody(look);
        skin.Clear();
        CollectSkin();
        shownFlush = -1;
        if (shown) Present(true);
    }
    int worn;

    void Redden()
    {
        if (paint==null) return;
        float flush = Flush;
        // Once overheated the red throbs, the only hint that things have turned dangerous.
        if (Heat>=1f) flush = .86f+.14f*Mathf.Sin(Time.time*5f);
        else flush *= .86f;
        if (Mathf.Abs(flush-shownFlush)<.004f) return;
        shownFlush = flush;
        foreach (var (part,slot,natural) in skin)
        {
            if (!part) continue;
            paint.SetColor("_BaseColor",Color.Lerp(natural,Sunburn,flush));
            part.SetPropertyBlock(paint,slot);
        }
    }

    // Collapsing from the heat: step back and show the mender, red all over, going down.
    public void Fall()
    {
        if (!body) return;
        Present(true);
        body.Play(BeachBody.Act.Fall);
        // High enough that the whole fall plays out above the panel at the bottom of the screen.
        Vector3 eye = transform.position-transform.forward*2.6f+Vector3.up*1.3f;
        Vector3 ground = transform.position-transform.forward*.45f;
        view.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(ground-eye));
    }
    public bool Presenting { get; private set; }
    public BeachBin[] Bins { get; set; }
    public BeachBody Body => body;
    public Transform HoldPoint => hands[1] ? hands[1].transform : rightArm;
    public bool HoldingFind { get; set; }
    public GameObject Rake { get; set; }
    public Transform Blade => trowel.transform;
    public bool Captured => Cursor.lockState == CursorLockMode.Locked;

    public void InitializeView()
    {
        leftArm = detector.transform.parent;
        var blocks = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (!r.transform.IsChildOf(rightArm) && !r.transform.IsChildOf(leftArm))
            {
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                blocks.Add(r);
            }
        // With the model in place the block figure no longer needs to cast the shadow.
        if (CreateBody(BeachNet.SelectedLook)) foreach (var r in blocks) r.enabled = false;
        foreach (Transform arm in new[]{leftArm,rightArm})
            arm.SetParent(view.transform,false);
        hands[0] = CreateHand(leftArm,"Left");
        hands[1] = CreateHand(rightArm,"Right");
        FitTool(trowel, "진열 모종삽", new Vector3(0,.09f,-.14f), Quaternion.Euler(90,0,0));
        FitTool(detector, "진열 탐지기", new Vector3(-.01f,.45f,-.14f), Quaternion.identity);
        CollectSkin();
        detector.transform.localRotation = Quaternion.Euler(88f,-12f,0);
        view.fieldOfView = 68;
        view.nearClipPlane = .025f;
        Follow();
    }

    // Display roots already normalize the imported meshes and carry their textured URP materials.
    void FitTool(GameObject socket,string displayName,Vector3 handle,Quaternion rotation)
    {
        var display = stall.Find(displayName);
        if (!display) return;
        foreach (var proxy in socket.GetComponentsInChildren<MeshRenderer>(true))
            proxy.enabled = false;
        var model = Instantiate(display,socket.transform,false);
        model.localRotation = rotation;
        model.localPosition = -(rotation * handle);
        model.name = displayName + " (장착)";
    }

    bool CreateBody(int look=0)
    {
        worn = look;
        body = BeachBody.Create(transform,look);
        if (!body) return false;
        foreach (var part in bodyParts)
            part.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        return true;
    }

    // Steps the camera back to show the mender, or returns to the first-person view.
    public void Present(bool show,bool cheer=false)
    {
        if (!body || show==Presenting) return;
        Presenting = show;
        foreach (var part in bodyParts)
            part.shadowCastingMode = show ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
        if (!show)
        {
            foreach (var held in firstPerson) if (held) held.SetActive(true);
            firstPerson.Clear();
            body.Face(0);
            body.Play(BeachBody.Act.Idle);
            Follow();
            return;
        }
        foreach (Transform held in view.transform)
            if (held.gameObject.activeSelf) { held.gameObject.SetActive(false); firstPerson.Add(held.gameObject); }
        // Turn to face the camera so the sea stays behind the figure.
        body.Face(180);
        Vector3 eye = transform.position-transform.forward*1.9f+Vector3.up*.80f;
        view.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(transform.position+Vector3.up*.62f-eye));
        body.Play(cheer ? BeachBody.Act.Cheer : BeachBody.Act.Idle);
    }

    // Keep the beach backdrop, leaving the right side for the needle preview.
    public void FrameMainMenu(float centerX)
    {
        if (!Presenting) return;
        foreach (var part in bodyParts) part.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        const float distance = 2.1f;
        float halfHeight = distance*Mathf.Tan(view.fieldOfView*.5f*Mathf.Deg2Rad);
        float offset = (Mathf.Clamp(centerX,.5f,.8f)-.5f)*2f*halfHeight*view.aspect;
        Vector3 eye = transform.position-transform.forward*distance-transform.right*offset+Vector3.up*.85f;
        view.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(transform.forward));
    }

    // Fits the mender's head and chest into the top `free` share of the screen, above the menu panel.
    public void FramePortrait(float free)
    {
        if (!Presenting) return;
        foreach (var part in bodyParts) part.shadowCastingMode = ShadowCastingMode.On;
        free = Mathf.Clamp(free,.2f,.8f);
        const float crown = 1.47f;
        float span = Mathf.Lerp(.34f,.66f,Mathf.InverseLerp(.3f,.5f,free));
        float slope = Mathf.Tan(view.fieldOfView*.5f*Mathf.Deg2Rad);
        float distance = span/(2f*slope*Mathf.Max(.1f,free-.06f));
        float height = crown-.46f*2f*distance*slope;
        Vector3 eye = transform.position-transform.forward*distance+Vector3.up*height;
        view.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(transform.forward));
    }

    SkinnedMeshRenderer CreateHand(Transform arm,string side)
    {
        var mesh = Resources.Load<Mesh>("BeachHands/"+side);
        var shape = arm.Find("형태");
        if (!mesh || !shape) return null;
        var original = shape.GetComponent<Renderer>();
        var hand = new GameObject(side+" hand").AddComponent<SkinnedMeshRenderer>();
        hand.transform.SetParent(view.transform,false);
        hand.sharedMesh = mesh;
        hand.sharedMaterial = original.sharedMaterial;
        hand.updateWhenOffscreen = true;
        var sleeve = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        sleeve.name = "작업복 소매";
        var collider = sleeve.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        sleeve.transform.SetParent(hand.transform,false);
        sleeve.transform.localPosition = new Vector3(0,0,-.70f);
        sleeve.transform.localRotation = Quaternion.Euler(90,0,0);
        sleeve.transform.localScale = new Vector3(.151f,.44f,.151f);
        var shirt = transform.Find("작업 셔츠");
        // Swimsuit figures have bare arms.
        bool bare = (body && body.Swimmer) || !shirt;
        sleeve.GetComponent<Renderer>().sharedMaterial = bare ? original.sharedMaterial : shirt.GetComponent<Renderer>().sharedMaterial;
        sleeves[arm==rightArm ? 1 : 0] = sleeve;
        sleeve.SetActive(false);
        original.enabled = false;
        return hand;
    }

    void PoseHand(int side,float stroke)
    {
        var hand = hands[side < 0 ? 0 : 1];
        if (!hand) return;
        sleeves[side<0 ? 0 : 1].SetActive(false);
        bool tool = side > 0 ? (trowel.activeSelf || (Rake && Rake.activeSelf)) : detector.activeSelf;
        float curl = CuppedHands && !HoldingBall ? 100 : 0;
        hand.SetBlendShapeWeight(0,curl);
        hand.SetBlendShapeWeight(1,HoldingBall ? 100 : 0);
        hand.SetBlendShapeWeight(2,(tool || (side>0 && HoldingFind)) && !CuppedHands ? 100 : 0);
        if (CuppedHands)
        {
            hand.transform.localPosition = HoldingBall ? new Vector3(side*.147f,-.21f,.39f) :
                new Vector3(side*.094f,-.162f,.413f);
            hand.transform.localRotation = Quaternion.Euler(3,-side*(HoldingBall ? 8 : 23),side*(HoldingBall ? 65 : 32));
        }
        else if (side>0 && HoldingFind)
        {
            hand.transform.localPosition=new Vector3(.22f,-.12f,.42f);
            hand.transform.localRotation=Quaternion.Euler(-16,-25,76);
        }
        else if (tool)
        {
            var grip = side > 0 ? trowel.transform : detector.transform;
            hand.transform.position = grip.position - view.transform.forward*.065f - view.transform.up*.028f;
            hand.transform.localRotation = Quaternion.Euler(-10,-side*38,side*79);
        }
        else
        {
            hand.transform.localPosition = new Vector3(side*(.19f+.035f*stroke),-.205f-.025f*stroke,.325f+.105f*stroke);
            hand.transform.localRotation = Quaternion.Euler(8+20*stroke,-side*13,side*153);
        }
    }

    public bool CanReachSand(Vector3 contact)
    {
        Vector3 shoulder = view.transform.position - Vector3.up*.32f;
        return (contact-shoulder).sqrMagnitude <= 1f;
    }

    public void ReachWithHand(int side,Vector3 contact,Vector3 across,float amount)
    {
        var hand = hands[side < 0 ? 0 : 1];
        Vector3 forward = Vector3.Cross(across,Vector3.up);
        Quaternion rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(40,-side*23,side*166);
        // Mesh origin is the wrist. Put the fingertips on the surface.
        if (hand) sleeves[side<0 ? 0 : 1].SetActive(amount>.01f);
        Transform limb = hand ? hand.transform : (side<0 ? leftArm : rightArm);
        Quaternion pose = hand ? rotation : rotation * Quaternion.Euler(-90,0,0);
        Vector3 wrist = contact - rotation*Vector3.forward*(hand ? .174f : .35f);
        limb.position = Vector3.Lerp(limb.position,wrist,amount);
        limb.rotation = Quaternion.Slerp(limb.rotation,pose,amount);
    }

    public void Capture(bool capture)
    {
        Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !capture;
    }

    public void Move(SandPatch sand)
    {
        var keys = Keyboard.current;
        if (keys == null || Mouse.current == null) return;
        // While the emote wheel is open the mouse moves its pointer, not the view.
        var delta = LookHeld ? Vector2.zero : Mouse.current.delta.ReadValue();
        yaw += delta.x*.14f;
        pitch = Mathf.Clamp(pitch-delta.y*.14f,-72,83);
        transform.rotation = Quaternion.Euler(0,yaw,0);
        Vector2 input = new Vector2((keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0),
            (keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0));
        inspecting = keys.leftShiftKey.isPressed;
        float moveSpeed = (inspecting ? .7f : 2.6f)*Burden;
        Vector3 next = transform.position + transform.rotation * new Vector3(input.x,0,input.y).normalized * (moveSpeed*Time.deltaTime);
        Vector2 bounded = sand.ClampToBeach(new Vector2(next.x,next.z),.22f);
        next.x=bounded.x;
        next.z=bounded.y;
        Vector3 counterLocal=stall.InverseTransformPoint(next);
        if (Mathf.Abs(counterLocal.x)<1.15f && Mathf.Abs(counterLocal.z)<.77f)
            next = transform.position;
        foreach (var bin in Bins)
            if (Mathf.Abs(next.x-bin.transform.position.x)<.44f && Mathf.Abs(next.z-bin.transform.position.z)<.43f) next=transform.position;
        next.y = sand.Height(new Vector2(next.x,next.z));
        transform.position = next;
        if (body) body.Play(input.sqrMagnitude>0 ? BeachBody.Act.Walk : BeachBody.Act.Idle);
        Follow();
    }

    public bool LookHeld { get; set; }
    public void Swing() => swingStart = Time.time;
    // Walking speed as a share of normal, lowered while carrying a full sack.
    public float Burden { get; set; } = 1f;
    public bool Swinging => Time.time-swingStart < .4f;

    public void Follow()
    {
        view.transform.SetPositionAndRotation(transform.position + Vector3.up*(inspecting?.76f:1.16f),Quaternion.Euler(pitch,yaw,0));
        Redden();
        float stroke = Mathf.Sin(Mathf.PI*Mathf.Clamp01((Time.time-swingStart)/.35f));
        Pose(rightArm,1,stroke);
        Pose(leftArm,-1,stroke);
        PoseHand(1,stroke);
        PoseHand(-1,stroke);
    }

    void Pose(Transform arm,int side,float stroke)
    {
        if (CuppedHands)
        {
            arm.localPosition = new Vector3(side*.20f,-.20f,.24f);
            arm.localRotation = Quaternion.Euler(-100,0,-side*24);
            return;
        }
        arm.localPosition = new Vector3(side*(.24f+.035f*stroke),-.22f,.26f+.05f*stroke);
        arm.localRotation = Quaternion.Euler(-100f-15f*stroke,0,-side*12f);
    }
}
