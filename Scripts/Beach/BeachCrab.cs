using System.Collections.Generic;
using UnityEngine;

// A crab after the small things the team uncovers and leaves lying on the sand. It cannot be stopped, but it
// can be seen coming and answered: a find left alone for a while gets a few little holes bubbling in the sand
// nearby, then a crab scuttles out sideways, takes it in its claws and drags it back to its burrow. Come
// close and it lets go and runs; let it reach the burrow and it buries the find there, under a mark of
// little holes, where digging brings it back. It never takes the needle or anything large, and never
// touches the sand itself. The host's crab is the real one; guests are told where it is and what it did.
public sealed class BeachCrab : MonoBehaviour
{
    public enum Phase : byte { Hidden, Warning, Coming, Dragging, Fleeing, Hiding }

    // How long an uncovered find may lie on the sand before a crab comes for it.
    public const float Lingers = 20f;
    // Quiet time after a crab has gone, and at the start of a visit.
    public const float Rest = 25f;
    // How long the holes bubble before it comes out: the warning.
    public const float Warning = 3f;
    // Someone this close to it (ignoring height) makes it let go and run.
    public const float Startle = 1.4f;
    // It does not come for a find someone stands this close to.
    public const float Shy = 1.6f;
    // How far below the surface it buries what it took: within one handful of sand.
    public const float Depth = .045f;
    const float Walk = .9f, Drag = .45f, BurrowLeast = 2f, BurrowMost = 3f, Rise = .4f;

    BeachSearch game;
    Transform model, body, clawLeft, clawRight, carried, holes;
    readonly Transform[] legs = new Transform[6];
    static Material shell, burrowShade;
    AudioSource voice;
    AudioClip scratch;
    float rest = Rest, phaseTime, gait;
    BeachFind target, carrying;
    Vector3 burrow, heard;
    float heardYaw;
    // Finds already on the surface when the visit began are part of the beach, not something uncovered.
    HashSet<BeachFind> untouched;
    readonly Dictionary<BeachFind,float> lying = new();
    // Finds this crab buried, each under its little holes until someone digs it out.
    readonly Dictionary<BeachFind,GameObject> buried = new();

    public Phase Now { get; private set; }
    public BeachFind Target => target;
    public FindKind? Carrying => carrying ? carrying.kind : null;
    public BeachFind CarriedFind => carrying;
    // Where it comes out and goes back in.
    public Vector3 Burrow => burrow;
    public IEnumerable<BeachFind> Buried => buried.Keys;

    // What a crab can carry: the small things. Never the needle, a bottle, a bag or a net.
    public static bool Small(FindKind kind) => kind is FindKind.Coin or FindKind.Bracelet or FindKind.Ring or FindKind.Cap
        or FindKind.PullTab or FindKind.FestivalBand or FindKind.Hairpin or FindKind.Toothpick or FindKind.PopsicleStick
        or FindKind.PlasticCap or FindKind.CigaretteButt or FindKind.Straw;

    public void Initialize(BeachSearch beach)
    {
        game = beach;
        model = Build(transform);
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = .85f;
        voice.minDistance = 2;
        voice.maxDistance = 18;
        scratch = Scratch();
        untouched = new HashSet<BeachFind>();
        foreach (var item in game.finds) if (item.gameObject.activeSelf && item.Exposed(game.sand)) untouched.Add(item);
        Hide();
    }

    // For tests and trying it out: no more waiting between crabs, and any find uncovered now will do.
    public void Summon()
    {
        rest = 0;
        foreach (var item in game.finds) if (Eligible(item)) lying[item] = Lingers;
    }

    void Hide()
    {
        Now = Phase.Hidden;
        model.gameObject.SetActive(false);
        target = null;
    }

    void Update()
    {
        if (!game || !game.Started) return;
        if (game.Guest && game.Outbox!=null) Follow(); else Step(Time.deltaTime);
        Animate(Time.deltaTime);
        KeepMarks();
        // While it is about to come out, its holes bubble.
        if (holes) holes.localScale = Now==Phase.Warning ? Vector3.one*(1f+.22f*Mathf.Sin(Time.time*11f)) : Vector3.one;
    }

    // ---- The host's crab ----

    float sendDue;

    public void Step(float dt)
    {
        if (game.Won && (Now==Phase.Warning || Now==Phase.Coming || Now==Phase.Dragging))
        {
            if (carrying) LetGo(); else Flee();
        }
        phaseTime += dt;
        Watch(dt);
        switch (Now)
        {
            case Phase.Hidden:
                if (game.Won) return;
                rest -= dt;
                if (rest>0) return;
                target = Forgotten();
                if (!target) return;
                burrow = BurrowFor(target);
                transform.position = burrow;
                Begin(Phase.Warning);
                ShowHoles(burrow);
                Sound(.8f);
                game.Notice("모래에 작은 구멍이 뽀글뽀글 · 주울 걸 두고 왔나?");
                break;
            case Phase.Warning:
                if (!Eligible(target) || NearSomeone(target.transform.position,Shy)) { GiveUp(); break; }
                if (phaseTime<Warning) break;
                Begin(Phase.Coming);
                model.gameObject.SetActive(true);
                Sound(.6f);
                break;
            case Phase.Coming:
            {
                if (!Eligible(target) || NearSomeone(target.transform.position,Shy) || Startled()) { Flee(); break; }
                if (phaseTime<Rise) break;
                if (!Scuttle(target.transform.position,dt,Walk)) break;
                carrying = target;
                target.gameObject.SetActive(false);
                lying.Remove(target);
                ShowCarried();
                game.CrabTook(carrying);
                Begin(Phase.Dragging);
                Sound(.7f);
                game.Notice($"게가 {BeachFind.NameWithObject(carrying.kind)} 끌고 간다! 다가가면 놓친다");
                break;
            }
            case Phase.Dragging:
                if (Startled()) { LetGo(); break; }
                if (!Scuttle(burrow,dt,Drag)) break;
                BuryCarried();
                break;
            case Phase.Fleeing:
                if (Scuttle(burrow,dt,Walk*1.7f)) Begin(Phase.Hiding);
                break;
            case Phase.Hiding:
                if (phaseTime<Rise) break;
                if (holes && !HoldsAFind(holes)) Destroy(holes.gameObject);
                holes = null;
                rest = Rest;
                Hide();
                Send();
                break;
        }
        sendDue -= dt;
        if (game.Outbox!=null && !game.Guest && Now!=Phase.Hidden && sendDue<=0) Send();
    }

    void Send()
    {
        sendDue = .1f;
        if (game.Outbox!=null && !game.Guest) game.SendCrab(transform.position,transform.eulerAngles.y,Now,carrying);
    }

    void Begin(Phase next)
    {
        Now = next;
        phaseTime = 0;
        Send();
    }

    // Nobody came, but the find was picked up or someone stood by it: the holes go quiet.
    void GiveUp()
    {
        if (holes) Destroy(holes.gameObject);
        holes = null;
        rest = Rest*.5f;
        Hide();
        Send();
    }

    void Flee()
    {
        target = null;
        Begin(Phase.Fleeing);
    }

    // Startled: drops what it carries where it stands, and runs for its burrow.
    void LetGo()
    {
        if (carrying)
        {
            Vector2 spot = game.sand.ClampToBeach(new Vector2(transform.position.x,transform.position.z),.1f);
            Vector3 at = game.Surface(spot)+Vector3.up*.004f;
            game.CrabDropped(carrying,at);
            lying[carrying] = 0;
            carrying = null;
            ShowCarried();
            Sound(.9f);
        }
        rest = Rest;
        Flee();
    }

    void BuryCarried()
    {
        var item = carrying;
        carrying = null;
        ShowCarried();
        Vector3 at = new Vector3(burrow.x,game.sand.Height(new Vector2(burrow.x,burrow.z))-item.topOffset-Depth,burrow.z);
        game.CrabBuried(item,at);
        rest = Rest;
        Begin(Phase.Hiding);
    }

    // Lying uncovered on the sand, small, and not part of the beach as it was first laid out.
    bool Eligible(BeachFind item) => item && item.gameObject.activeSelf && Small(item.kind) &&
                                     !untouched.Contains(item) && item.Exposed(game.sand);

    // How long each uncovered find has been left lying.
    void Watch(float dt)
    {
        foreach (var item in game.finds)
        {
            // Once picked up or covered again, a find from the first layout counts like any other.
            if (untouched.Contains(item) && (!item.gameObject.activeSelf || !item.Exposed(game.sand))) untouched.Remove(item);
            if (item==carrying) continue;
            if (Eligible(item)) { lying.TryGetValue(item,out float t); lying[item] = t+dt; }
            else lying.Remove(item);
        }
    }

    // The find left alone longest, if one has lain long enough with nobody near.
    BeachFind Forgotten()
    {
        BeachFind best = null;
        float longest = Lingers;
        foreach (var pair in lying)
        {
            if (pair.Value<longest || !Eligible(pair.Key) || NearSomeone(pair.Key.transform.position,Shy)) continue;
            best = pair.Key;
            longest = pair.Value;
        }
        return best;
    }

    // A spot a couple of metres from the find, on the beach and as far from everyone as can be found.
    Vector3 BurrowFor(BeachFind item)
    {
        Vector2 from = item.Position;
        Vector2 best = from;
        float bestRoom = -1;
        for (int i=0; i<16; i++)
        {
            float angle = Random.Range(0f,Mathf.PI*2);
            Vector2 spot = from+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*Random.Range(BurrowLeast,BurrowMost);
            if (!game.sand.Contains(spot,.5f)) continue;
            if (Vector2.Distance(spot,new Vector2(game.stall.position.x,game.stall.position.z))<2.5f) continue;
            float room = Nearest(new Vector3(spot.x,0,spot.y));
            if (room>bestRoom) { best = spot; bestRoom = room; }
        }
        if (bestRoom<0) best = game.sand.ClampToBeach(from+Vector2.right*BurrowLeast,.5f);
        return game.Surface(best);
    }

    float Nearest(Vector3 at)
    {
        float nearest = Flat(game.player.transform.position,at);
        if (game.Others!=null) foreach (var (_,feet) in game.Others()) nearest = Mathf.Min(nearest,Flat(feet,at));
        return nearest;
    }

    bool NearSomeone(Vector3 at,float within) => Nearest(at)<within;
    bool Startled() => NearSomeone(transform.position,Startle);

    static float Flat(Vector3 a,Vector3 b) => new Vector2(a.x-b.x,a.z-b.z).magnitude;

    // Sideways, as crabs go, towards `want` on the sand. True on arrival.
    bool Scuttle(Vector3 want,float dt,float speed)
    {
        Vector2 here = new Vector2(transform.position.x,transform.position.z);
        Vector2 there = new Vector2(want.x,want.z);
        Vector2 next = Vector2.MoveTowards(here,there,speed*dt);
        Vector2 step = next-here;
        transform.position = game.Surface(next);
        gait += step.magnitude*38f;
        if (step.sqrMagnitude>1e-8f)
        {
            Vector3 along = new Vector3(step.x,0,step.y).normalized;
            // Facing across the way it goes; whichever side is nearer to how it already faces.
            Vector3 face = Vector3.Cross(along,Vector3.up);
            if (Vector3.Dot(face,transform.forward)<0) face = -face;
            transform.rotation = Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(face),1-Mathf.Exp(-10f*dt));
        }
        return (there-next).sqrMagnitude<.0025f;
    }

    // ---- Burrow marks ----

    void ShowHoles(Vector3 at)
    {
        if (holes && !HoldsAFind(holes)) Destroy(holes.gameObject);
        holes = Holes(at).transform;
    }

    bool HoldsAFind(Transform mark)
    {
        foreach (var pair in buried) if (pair.Value && pair.Value.transform==mark) return true;
        return false;
    }

    // Everyone: a find the crab buried has its holes until it is dug out or picked up.
    public void MarkBuried(BeachFind item)
    {
        if (buried.TryGetValue(item,out var old) && old && (!holes || old.transform!=holes)) Destroy(old);
        Vector3 at = item.transform.position;
        Transform mark = holes && Flat(holes.position,at)<.3f ? holes : Holes(game.Surface(new Vector2(at.x,at.z))).transform;
        buried[item] = mark.gameObject;
        if (mark==holes) holes = null;
    }

    void KeepMarks()
    {
        List<BeachFind> gone = null;
        foreach (var pair in buried)
        {
            var item = pair.Key;
            bool still = item.gameObject.activeSelf && !item.Exposed(game.sand) && pair.Value &&
                         Flat(item.transform.position,pair.Value.transform.position)<.3f;
            if (still) continue;
            (gone ??= new List<BeachFind>()).Add(item);
        }
        if (gone==null) return;
        foreach (var item in gone)
        {
            if (buried[item]) Destroy(buried[item]);
            buried.Remove(item);
        }
    }

    // A few little round holes in the sand. Nothing solid: sacks can still be set down on them.
    GameObject Holes(Vector3 at)
    {
        var root = new GameObject("게 구멍");
        root.transform.SetParent(game.transform,false);
        root.transform.position = at;
        burrowShade ??= Shade();
        var spread = new System.Random(Mathf.RoundToInt(at.x*97+at.z*31));
        for (int i=0; i<4; i++)
        {
            var hole = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(hole.GetComponent<Collider>());
            hole.name = "구멍";
            hole.transform.SetParent(root.transform,false);
            float size = i==0 ? .11f : .045f+.025f*(float)spread.NextDouble();
            float angle = (float)spread.NextDouble()*Mathf.PI*2;
            float away = i==0 ? 0 : .11f+.1f*(float)spread.NextDouble();
            hole.transform.localPosition = new Vector3(Mathf.Cos(angle)*away,.002f,Mathf.Sin(angle)*away);
            hole.transform.localScale = new Vector3(size,.006f,size);
            var paint = hole.GetComponent<MeshRenderer>();
            paint.sharedMaterial = burrowShade;
            paint.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return root;
    }

    public void ForgetMarks()
    {
        foreach (var mark in buried.Values) if (mark) Destroy(mark);
        buried.Clear();
    }

    // ---- A guest's crab ----

    // The host's word about the crab.
    public void Heard(Vector3 position,float yaw,Phase phase,BeachFind holding)
    {
        bool fresh = phase!=Now;
        heard = position;
        heardYaw = yaw;
        if (fresh && phase==Phase.Warning)
        {
            transform.position = position;
            burrow = position;
            ShowHoles(position);
            Sound(.8f);
            game.Notice("모래에 작은 구멍이 뽀글뽀글 · 주울 걸 두고 왔나?");
        }
        if (fresh && phase==Phase.Coming) { transform.position = position; burrow = position; }
        if (fresh && phase==Phase.Hidden && holes && !HoldsAFind(holes)) { Destroy(holes.gameObject); holes = null; }
        if (fresh) phaseTime = 0;
        Now = phase;
        model.gameObject.SetActive(phase!=Phase.Hidden && phase!=Phase.Warning);
        if (holding!=carrying)
        {
            carrying = holding;
            ShowCarried();
        }
    }

    void Follow()
    {
        phaseTime += Time.deltaTime;
        if (Now==Phase.Hidden || Now==Phase.Warning) return;
        Vector3 before = transform.position;
        float ease = 1-Mathf.Exp(-12f*Time.deltaTime);
        Vector3 p = Vector3.Lerp(transform.position,heard,ease);
        transform.position = game.Surface(new Vector2(p.x,p.z));
        transform.rotation = Quaternion.Slerp(transform.rotation,Quaternion.Euler(0,heardYaw,0),ease);
        gait += Flat(before,transform.position)*38f;
    }

    // ---- Looks and sound ----

    void Animate(float dt)
    {
        if (!model.gameObject.activeSelf) return;
        // Up out of the sand when it comes, back down into it when it hides.
        float sunk = Now switch {
            Phase.Coming => 1-Mathf.Clamp01(phaseTime/Rise),
            Phase.Hiding => Mathf.Clamp01(phaseTime/Rise),
            _ => 0 };
        model.localPosition = Vector3.down*(.13f*sunk);
        // Two tripods of legs (left 1 and 3 with right 2, and the other three), lifting and swinging in turn.
        for (int i=0; i<legs.Length; i++)
        {
            if (!legs[i]) continue;
            float side = i<3 ? -1f : 1f;
            float phase = gait+(i%2==0 ? 0 : Mathf.PI);
            float lift = Mathf.Max(0,Mathf.Sin(phase))*28f;
            float swing = Mathf.Cos(phase)*14f;
            legs[i].localRotation = Quaternion.Euler(0,swing*side,lift*side);
        }
        if (body) body.localPosition = Vector3.up*(.004f*Mathf.Abs(Mathf.Sin(gait)));
        // Claws held up with a find between them; otherwise snipping now and then.
        float t = Time.time;
        bool holding = carrying;
        float raise = holding ? -22f : -6f*Mathf.Max(0,Mathf.Sin(t*1.7f));
        float snip = holding ? 6f : 14f*Mathf.Max(0,Mathf.Sin(t*5.3f))*Mathf.Max(0,Mathf.Sin(t*.9f));
        if (clawLeft) clawLeft.localRotation = Quaternion.Euler(raise,-snip,0);
        if (clawRight) clawRight.localRotation = Quaternion.Euler(raise,snip,0);
    }

    void ShowCarried()
    {
        if (carried) { Destroy(carried.gameObject); carried = null; }
        if (!carrying) return;
        var me = game.Local;
        var slot = new BeachSlot(carrying.kind);
        var source = me ? me.ModelFor(slot) : null;
        if (!source || !me.Models) return;
        // Held up between the claws, tipped towards whoever is watching it go.
        var copy = me.Models.Carried(slot,source,body ? body : model,.09f);
        if (!copy) return;
        copy.name = "게가 끄는 "+BeachFind.NameOf(carrying.kind);
        copy.transform.localPosition = new Vector3(0,.06f,.19f);
        copy.transform.localRotation = Quaternion.Euler(-30f,0,0);
        carried = copy.transform;
    }

    void Sound(float volume)
    {
        if (scratch) voice.PlayOneShot(scratch,volume);
    }

    // 'Crab' by jeremy on Poly Pizza (CC BY 3.0, see Wildlife-Credits.txt), cut by the import script into a
    // body with its eyes, two claws hinged at the root and six legs hinged where they meet the shell.
    // Pivots are where the parts were cut, in Unity's frame (the OBJ importer mirrors x).
    static readonly Vector3 ClawPivot = new(-.0566f,.0247f,.0303f);
    static readonly Vector3[] LegPivots = { new(-.0718f,.0166f,.028f), new(-.0726f,.0166f,.0001f), new(-.0618f,.0166f,-.0285f) };

    Transform Build(Transform parent)
    {
        var root = new GameObject("게 모양").transform;
        root.SetParent(parent,false);
        shell ??= Shell();
        body = Part("CrabBody",root,Vector3.zero);
        var hold = body ? body : root;
        clawLeft = Part("CrabClawLeft",hold,ClawPivot);
        clawRight = Part("CrabClawRight",hold,Vector3.Scale(ClawPivot,new Vector3(-1,1,1)));
        for (int i=0; i<3; i++)
        {
            legs[i] = Part("CrabLegLeft"+(i+1),hold,LegPivots[i]);
            legs[i+3] = Part("CrabLegRight"+(i+1),hold,Vector3.Scale(LegPivots[i],new Vector3(-1,1,1)));
        }
        return root;
    }

    // One imported part, placed at its pivot. Null when the model is missing.
    static Transform Part(string name,Transform parent,Vector3 at)
    {
        var source = Resources.Load<GameObject>("Crab/"+name);
        var filter = source ? source.GetComponentInChildren<MeshFilter>() : null;
        if (!filter) return null;
        var part = new GameObject(name).transform;
        part.SetParent(parent,false);
        part.localPosition = at;
        part.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = shell;
        return part;
    }

    static Material Shell()
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "게 껍데기" };
        var palette = Resources.Load<Texture2D>("Crab/CrabPalette");
        if (palette)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap",palette);
            material.mainTexture = palette;
        }
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness",.15f);
        if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections",0f);
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        // Both faces: where a leg turns at the shell, the inside of the cut shows rather than a hole.
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull",(float)UnityEngine.Rendering.CullMode.Off);
        material.doubleSidedGI = true;
        return material;
    }

    static Material Shade()
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { name = "게 구멍 그늘" };
        var dark = new Color(.36f,.28f,.19f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor",dark);
        material.color = dark;
        return material;
    }

    // Scratching in the sand: a few quick dry rasps.
    static AudioClip Scratch()
    {
        const int rate = 22050;
        var samples = new float[(int)(rate*.7f)];
        var noise = new System.Random(11);
        float last = 0;
        for (int i=0; i<samples.Length; i++)
        {
            float t = (float)i/rate;
            float within = (t*9f)%1f;
            float envelope = within<.35f ? Mathf.Sin(within/.35f*Mathf.PI) : 0;
            float n = (float)noise.NextDouble()*2-1;
            // Differencing thins the noise to a dry, high rasp.
            samples[i] = .22f*envelope*(n-last);
            last = n;
        }
        var clip = AudioClip.Create("게 긁는 소리",samples.Length,1,rate,false);
        clip.SetData(samples,0);
        return clip;
    }

    void OnDestroy()
    {
        if (scratch) Destroy(scratch);
    }
}
