using UnityEngine;

// A seagull after the shiny things in the team's sacks. It cannot be stopped, but it can be seen coming and
// answered: it cries and circles over a sack nobody is minding, its shadow crossing the sand, and gives up
// if someone comes near. If it gets away with something, run after it or hit it with the volleyball and it
// lets go; left alone, it settles at the far side of the beach and drops it there. Nothing is lost for good.
// The host's gull is the real one; guests are told where it is and what it did.
public sealed class BeachGull : MonoBehaviour
{
    public enum Phase : byte { Away, Circling, Diving, Fleeing, Perched, Leaving }

    // When it comes: not in the first FirstVisit seconds, then every Between seconds or so.
    public const float FirstVisit = 75f, BetweenLeast = 50f, BetweenMost = 100f;
    // A sack is minded while someone stands this close to it.
    public const float Minded = 3f;
    // How long it circles before diving: the warning.
    public const float Warning = 3.5f;
    // Coming this close (on the ground, ignoring height) makes it drop what it carries.
    public const float Startle = 2.2f;
    const float Speed = 2.4f, CircleRadius = 1.6f, PerchWait = 2.5f;

    BeachSearch game;
    // `model` holds both figures: `flying` with wings that flap, `standing` with a head that turns.
    Transform model, flying, standing, wingLeft, wingRight, head, standingBody, carried;
    float wingSideLeft = 1, wingSideRight = -1;
    static Material plumage;
    AudioSource voice;
    AudioClip cry;
    float wait = FirstVisit, phaseTime, flap;
    BeachSack target;
    Vector3 perch, leaveTo, heard;
    float heardYaw;

    public Phase Now { get; private set; }
    // What it holds in its beak, if anything.
    public FindKind? Carrying { get; private set; }
    public BeachSack Target => target;
    // Where it will settle if nobody follows.
    public Vector3 Perch => perch;

    public void Initialize(BeachSearch beach)
    {
        game = beach;
        model = Build(transform);
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = .75f;
        voice.minDistance = 4;
        voice.maxDistance = 30;
        cry = Cry();
        Hide();
    }

    // For tests and trying it out: the next visit begins now.
    public void Summon() => wait = 0;

    void Hide()
    {
        Now = Phase.Away;
        model.gameObject.SetActive(false);
        transform.position = Offshore(Vector3.zero);
    }

    // High over the water beyond the northern edge.
    Vector3 Offshore(Vector3 near) => new Vector3(near.x,7f,game.sand.HalfSize.y+9f);

    void Update()
    {
        if (!game || !game.Started) return;
        if (game.Guest && game.Outbox!=null) { Follow(); Animate(Time.deltaTime); return; }
        Step(Time.deltaTime);
        Animate(Time.deltaTime);
    }

    // ---- The host's gull ----

    float sendDue;

    public void Step(float dt)
    {
        if (game.Won && Now!=Phase.Away && Now!=Phase.Leaving) Leave();
        phaseTime += dt;
        switch (Now)
        {
            case Phase.Away:
                if (game.Won) return;
                wait -= dt;
                if (wait>0) return;
                wait = Random.Range(BetweenLeast,BetweenMost);
                target = Unminded();
                if (!target) return;
                Begin(Phase.Circling);
                model.gameObject.SetActive(true);
                transform.position = Offshore(target.transform.position);
                Cry(1f);
                game.Notice("끼룩! 갈매기다 · 자루를 지키자");
                break;
            case Phase.Circling:
            {
                if (!Valid(target) || MindedBySomeone(target)) { GiveUp(); break; }
                // Circle above the sack, lower each turn.
                float turn = phaseTime*2.2f;
                float height = Mathf.Lerp(4.6f,2.8f,Mathf.Clamp01(phaseTime/Warning));
                Vector3 centre = target.transform.position;
                Vector3 want = centre+new Vector3(Mathf.Cos(turn),0,Mathf.Sin(turn))*CircleRadius+Vector3.up*height;
                MoveTowards(want,dt,phaseTime<1.5f ? Speed*3f : Speed*1.6f);
                if (phaseTime>=Warning) Begin(Phase.Diving);
                break;
            }
            case Phase.Diving:
            {
                if (!Valid(target) || MindedBySomeone(target)) { GiveUp(); break; }
                Vector3 mouth = target.transform.position+Vector3.up*.6f;
                MoveTowards(mouth,dt,Speed*2.5f);
                if (Vector3.Distance(transform.position,mouth)>.15f) break;
                var taken = target.TakeShiniest();
                if (!taken.HasValue) { GiveUp(); break; }
                Carrying = taken;
                ShowCarried();
                game.GullTook(target,taken.Value);
                perch = FarSide(target.transform.position);
                Begin(Phase.Fleeing);
                Cry(1f);
                game.Notice($"갈매기가 {BeachFind.NameWithObject(taken.Value)} 물고 달아난다! 쫓아가자");
                break;
            }
            case Phase.Fleeing:
            {
                if (Chased()) { LetGo(); break; }
                Vector3 above = perch+Vector3.up*2.6f;
                Vector3 sway = new Vector3(Mathf.Sin(phaseTime*1.7f),0,Mathf.Cos(phaseTime*1.3f))*.6f;
                bool there = new Vector2(transform.position.x-perch.x,transform.position.z-perch.z).magnitude<.4f;
                if (!there) MoveTowards(above+sway,dt,Speed);
                else
                {
                    MoveTowards(perch+Vector3.up*.05f,dt,Speed*.8f);
                    if (transform.position.y-perch.y<.12f)
                    {
                        // Feet on the sand, wings folded, what it carries now in the standing figure's beak.
                        transform.position = perch;
                        Begin(Phase.Perched);
                        ShowCarried();
                    }
                }
                break;
            }
            case Phase.Perched:
                if (Chased() || phaseTime>=PerchWait) LetGo();
                break;
            case Phase.Leaving:
                MoveTowards(leaveTo,dt,Speed*2f);
                if (Vector3.Distance(transform.position,leaveTo)<.3f) Hide();
                break;
        }
        sendDue -= dt;
        if (game.Outbox!=null && !game.Guest && Now!=Phase.Away && sendDue<=0)
        {
            sendDue = .1f;
            game.SendGull(transform.position,transform.eulerAngles.y,Now,Carrying);
        }
    }

    void Begin(Phase next)
    {
        Now = next;
        phaseTime = 0;
        if (game.Outbox!=null && !game.Guest) game.SendGull(transform.position,transform.eulerAngles.y,Now,Carrying);
    }

    void GiveUp()
    {
        Cry(.7f);
        Leave();
    }

    void Leave()
    {
        leaveTo = Offshore(transform.position);
        target = null;
        Begin(Phase.Leaving);
    }

    // Drops what it carries onto the sand below and flies off.
    void LetGo()
    {
        if (Carrying is FindKind kind)
        {
            Vector2 below = game.sand.ClampToBeach(new Vector2(transform.position.x,transform.position.z),.3f);
            game.GullDropped(kind,below);
        }
        Carrying = null;
        ShowCarried();
        Cry(.8f);
        Leave();
    }

    static bool Valid(BeachSack sack) => sack && !sack.Carried;

    // The sack it goes for: lying on the sand, nobody near, something shiny inside.
    BeachSack Unminded()
    {
        BeachSack best = null;
        foreach (var sack in game.Sacks)
        {
            if (!Valid(sack) || MindedBySomeone(sack) || !HasShiny(sack)) continue;
            if (!best || sack.Value>best.Value) best = sack;
        }
        return best;
    }

    static bool HasShiny(BeachSack sack)
    {
        foreach (var pair in sack.Contents) if (!BeachFind.IsTrashKind(pair.Key)) return true;
        return false;
    }

    bool MindedBySomeone(BeachSack sack)
    {
        Vector3 at = sack.transform.position;
        if (Flat(game.player.transform.position,at)<Minded) return true;
        if (game.Others!=null) foreach (var (_,feet) in game.Others()) if (Flat(feet,at)<Minded) return true;
        return false;
    }

    // Someone ran close underneath, or the volleyball hit it.
    bool Chased()
    {
        Vector3 here = transform.position;
        if (Flat(game.player.transform.position,here)<Startle) return true;
        if (game.Others!=null) foreach (var (_,feet) in game.Others()) if (Flat(feet,here)<Startle) return true;
        var ball = game.Volleyball;
        return ball && ball.Holder<0 && ball.Model && ball.Model.gameObject.activeInHierarchy &&
               Vector3.Distance(ball.Model.position,here)<.6f;
    }

    static float Flat(Vector3 a,Vector3 b) => new Vector2(a.x-b.x,a.z-b.z).magnitude;

    // The far side of the beach from where it stole, so the chase crosses the sand.
    Vector3 FarSide(Vector3 from)
    {
        Vector2 half = game.sand.HalfSize;
        Vector2 spot = new Vector2(-Mathf.Sign(from.x)*(half.x-1f),-Mathf.Sign(from.z)*(half.y-1.2f));
        spot += new Vector2(Random.Range(-1f,1f),Random.Range(-1f,1f));
        spot = game.sand.ClampToBeach(spot,.6f);
        return game.Surface(spot);
    }

    void MoveTowards(Vector3 want,float dt,float speed)
    {
        Vector3 step = Vector3.MoveTowards(transform.position,want,speed*dt)-transform.position;
        transform.position += step;
        Vector3 flat = new Vector3(step.x,0,step.z);
        if (flat.sqrMagnitude>1e-6f)
            transform.rotation = Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(flat),1-Mathf.Exp(-8f*dt));
    }

    // ---- A guest's gull ----

    // The host's word about the gull.
    public void Heard(Vector3 position,float yaw,Phase phase,FindKind? carrying)
    {
        bool fresh = phase!=Now;
        if (Now==Phase.Away && phase!=Phase.Away) { transform.position = position; model.gameObject.SetActive(true); }
        heard = position;
        heardYaw = yaw;
        Now = phase;
        Carrying = carrying;
        ShowCarried();
        if (phase==Phase.Away) { model.gameObject.SetActive(false); return; }
        if (!fresh) return;
        if (phase==Phase.Circling) { Cry(1f); game.Notice("끼룩! 갈매기다 · 자루를 지키자"); }
        else if (phase==Phase.Fleeing && carrying.HasValue) { Cry(1f); game.Notice($"갈매기가 {BeachFind.NameWithObject(carrying.Value)} 물고 달아난다! 쫓아가자"); }
        else if (phase==Phase.Leaving) Cry(.7f);
    }

    void Follow()
    {
        if (Now==Phase.Away) return;
        float ease = 1-Mathf.Exp(-10f*Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position,heard,ease);
        transform.rotation = Quaternion.Slerp(transform.rotation,Quaternion.Euler(0,heardYaw,0),ease);
        if (Now==Phase.Leaving && Vector3.Distance(transform.position,heard)<.3f && heard.y>6f) model.gameObject.SetActive(false);
    }

    // ---- Looks and sound ----

    void Animate(float dt)
    {
        if (!model.gameObject.activeSelf) return;
        bool perched = Now==Phase.Perched;
        if (flying) flying.gameObject.SetActive(!perched);
        if (standing) standing.gameObject.SetActive(perched);
        flap += dt;
        if (perched) { Idle(); return; }
        // The model's wings are raised a little; 0 is that pose, negative is down.
        float wing = Now switch {
            Phase.Circling => -8f+6f*Mathf.Sin(flap*2.2f),                       // gliding, a lazy beat now and then
            Phase.Diving => -15f+30f*Mathf.Sin(flap*16f),                        // quick strokes into the dive
            Phase.Leaving => -16f+28f*Mathf.Sin(flap*12f),
            _ => -16f+28f*Mathf.Sin(flap*9f) };                                    // carrying something away
        if (wingLeft) wingLeft.localRotation = Quaternion.Euler(0,0,wing*wingSideLeft);
        if (wingRight) wingRight.localRotation = Quaternion.Euler(0,0,wing*wingSideRight);
        flying.localRotation = Quaternion.Euler(Now==Phase.Diving ? 25f : 0,0,Now==Phase.Circling ? -18f : 0);
        // Each downstroke lifts the body a touch.
        flying.localPosition = Vector3.up*(.02f*Mathf.Sin(flap*(Now==Phase.Circling ? 2.2f : 9f)+Mathf.PI));
    }

    // Standing on the sand: breathing, looking about, now and then a peck.
    void Idle()
    {
        if (standingBody) standingBody.localScale = new Vector3(1,1+.025f*Mathf.Sin(flap*3.1f),1);
        if (!head) return;
        float cycle = flap%3.2f;
        float peck = cycle>2.6f ? Mathf.Sin((cycle-2.6f)/.6f*Mathf.PI)*55f : 0;
        float look = 38f*Mathf.Sin(flap*1.3f)*(cycle>2.6f ? .2f : 1f);
        head.localRotation = Quaternion.Euler(peck,look,0);
    }

    void ShowCarried()
    {
        if (carried) { Destroy(carried.gameObject); carried = null; }
        if (!(Carrying is FindKind kind)) return;
        var me = game.Local;
        var slot = new BeachSlot(kind);
        var source = me ? me.ModelFor(slot) : null;
        if (!source || !me.Models) return;
        // Held under the beak of whichever figure is showing; it moves with the head when standing.
        bool perched = Now==Phase.Perched && head;
        var copy = me.Models.Carried(slot,source,perched ? head : flying ? flying : model,.16f);
        if (!copy) return;
        copy.name = "물고 가는 "+BeachFind.NameOf(kind);
        copy.transform.localPosition = perched ? new Vector3(0,.07f,.17f) : new Vector3(0,.0f,.43f);
        carried = copy.transform;
    }

    void Cry(float volume)
    {
        if (cry) voice.PlayOneShot(cry,volume);
    }

    // The Poly by Google gulls (CC BY 3.0, see Wildlife-Credits.txt), cut into parts by the import script:
    // in flight a body and two wings hinged at the shoulder; standing a body, legs and a head on its neck.
    // Pivots are where the parts were cut, in Unity's frame (the OBJ importer mirrors x).
    static readonly Vector3 BodyPivot = new(0,.06f,0), WingPivot = new(.048f,.12f,.048f);
    static readonly Vector3 FeetPivot = new(0,-.0108f,-.024f), NeckPivot = new(0,.48f,.024f);

    Transform Build(Transform parent)
    {
        var root = new GameObject("갈매기 모양").transform;
        root.SetParent(parent,false);
        plumage ??= Plumage();
        flying = new GameObject("나는 갈매기").transform;
        flying.SetParent(root,false);
        // The flying figure is centred on its body; the gull's position is that centre.
        Part("GullFlyingBody",flying,Vector3.zero);
        wingLeft = Part("GullWingLeft",flying,WingPivot-BodyPivot);
        wingRight = Part("GullWingRight",flying,Vector3.Scale(WingPivot,new Vector3(-1,1,1))-BodyPivot);
        if (wingLeft) wingSideLeft = Side(wingLeft);
        if (wingRight) wingSideRight = Side(wingRight);
        standing = new GameObject("서 있는 갈매기").transform;
        standing.SetParent(root,false);
        // Standing, its feet are on the gull's position.
        standingBody = Part("GullStandingBody",standing,Vector3.zero);
        Part("GullLegs",standing,Vector3.zero);
        head = Part("GullHead",standing,NeckPivot-FeetPivot);
        standing.gameObject.SetActive(false);
        return root;
    }

    // +1 for a wing on the +x side, so raising it is a positive turn about the forward axis.
    static float Side(Transform wing)
    {
        var mesh = wing.GetComponent<MeshFilter>();
        return mesh && mesh.sharedMesh && mesh.sharedMesh.bounds.center.x<0 ? -1f : 1f;
    }

    // One imported part, placed at its pivot. Null when the model is missing.
    static Transform Part(string name,Transform parent,Vector3 at)
    {
        var source = Resources.Load<GameObject>("Seagull/"+name);
        var filter = source ? source.GetComponentInChildren<MeshFilter>() : null;
        if (!filter) return null;
        var part = new GameObject(name).transform;
        part.SetParent(parent,false);
        part.localPosition = at;
        part.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = plumage;
        return part;
    }

    static Material Plumage()
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "갈매기 깃털" };
        var palette = Resources.Load<Texture2D>("Seagull/GullPalette");
        if (palette)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap",palette);
            material.mainTexture = palette;
        }
        // Matte, with no environment reflection: at a glancing angle the beach's red parasols showed in the white.
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness",0f);
        if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections",0f);
        if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights",0f);
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        // Both faces: when a wing turns at the shoulder the inside of the cut shows rather than a hole.
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull",(float)UnityEngine.Rendering.CullMode.Off);
        material.doubleSidedGI = true;
        return material;
    }

    // "Kee-ow": a harsh cry falling in pitch, twice.
    static AudioClip Cry()
    {
        const int rate = 22050;
        var samples = new float[(int)(rate*.9f)];
        var noise = new System.Random(5);
        float phase = 0;
        for (int i=0; i<samples.Length; i++)
        {
            float t = (float)i/rate;
            float call = t<.38f ? t/.38f : (t-.45f)/.45f;
            if (t>=.38f && t<.45f) { samples[i] = 0; continue; }
            float pitch = Mathf.Lerp(1750f,900f,Mathf.Clamp01(call))*(1+.03f*Mathf.Sin(t*90f));
            phase += 2*Mathf.PI*pitch/rate;
            float envelope = Mathf.Sin(Mathf.PI*Mathf.Clamp01(call))*(t<.38f ? 1f : .8f);
            float tone = Mathf.Sin(phase)+.5f*Mathf.Sin(2*phase)+.25f*Mathf.Sin(3*phase);
            samples[i] = .09f*envelope*(tone+.35f*((float)noise.NextDouble()*2-1));
        }
        var clip = AudioClip.Create("갈매기 울음",samples.Length,1,rate,false);
        clip.SetData(samples,0);
        return clip;
    }

    void OnDestroy()
    {
        if (cry) Destroy(cry);
    }
}
