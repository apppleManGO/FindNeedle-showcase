using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum BeachTheme { Neighborhood, Tournament, Festival }

// The beach everyone shares: the sand, what is buried in it, the parasols, the clock and the result.
// What one player holds, aims at and is told lives in BeachDigger. A digger changes the beach only
// through the public methods below, so in a co-op build these are the calls that go to the host.
public sealed partial class BeachSearch : MonoBehaviour
{
    // The player in the scene, and that player's bag and aiming ring. BeachDigger takes them over at Start.
    public BeachWalker player;
    public SandPatch sand;
    public BeachInventory inventory;
    public BeachFind[] finds;
    public LineRenderer digRing;
    public Transform stall;
    public GameObject spectatorArea;
    public Transform fishingBoat;
    Vector3 boatMooring;
    public BeachDigger Local { get; private set; }
    const string UnlockKey = "FindNeedle.ThemeChoice";
    static BeachTheme? nextVisit;
    public BeachTheme Theme { get; private set; }
    public bool ThemesUnlocked => PlayerPrefs.GetInt(UnlockKey,0)==1;
    public static string ThemeName(BeachTheme theme) => theme switch {
        BeachTheme.Neighborhood => "동네 해수욕장",
        BeachTheme.Tournament => "비치발리볼 대회장",
        _ => "축제 다음 날 해변"
    };
    public bool Started { get; private set; }
    // True once Start has buried everything, so the beach can be handed to a guest.
    public bool Initialized { get; private set; }
    public bool Won { get; private set; }
    readonly List<Transform> parasols = new();
    public const float SpreadFactor = 1.65f;
    public const float MoundHeight = .015f;
    BeachFind buriedPourNeedle;
    public AudioClip handSandSound, shovelSandSound;
    AudioSource speaker;
    AudioClip beep, chime, fanfare;

    // Visit summary shown on the clear screen.
    public float Elapsed { get; private set; }
    public int Picked { get; private set; }
    public int TotalFinds { get; private set; }
    public int Earned { get; private set; }
    public bool NewRecord { get; private set; }
    readonly Dictionary<FindKind,int> cleaned = new();
    public IReadOnlyDictionary<FindKind,int> Cleaned => cleaned;
    public static float BestTime(BeachTheme theme) => PlayerPrefs.GetFloat("FindNeedle.Best."+theme,0f);
    public static string Clock(float seconds) => $"{(int)seconds/60:00}:{(int)seconds%60:00}";

    // Sand feel: footsteps, sand leaking from cupped hands, sliding slopes.
    AudioSource grainSpeaker;
    AudioClip stepSound, pourSound, trickleSound;

    Transform portableParasol;
    public bool ParasolPitched => portableParasol;

    public SandcastleBucket Bucket { get; private set; }
    public BeachTurtle Turtle { get; private set; }
    public BeachVolleyball Volleyball { get; private set; }

    public void PlayBallWarning() => speaker.PlayOneShot(beep,.75f);
    public void Beep() => speaker.PlayOneShot(beep);
    public void Chime(float volume) => speaker.PlayOneShot(chime,volume);
    // Word from the beach to the player it concerns. With one player that is always the local one.
    public void Notice(string text) => Local.Notice(text);

    void Start()
    {
        bool startVisit = nextVisit.HasValue;
        Theme = nextVisit ?? BeachTheme.Neighborhood;
        nextVisit = null;
        Vector2 dimensions = Theme switch {
            BeachTheme.Tournament => new Vector2(12.5f,10f),
            BeachTheme.Festival => new Vector2(10f,11.25f),
            _ => new Vector2(10f,10f)
        };
        sand.Initialize(dimensions);
        // The service wall follows the western edge when the tournament beach widens.
        stall.position += Vector3.left*(sand.HalfSize.x-5f);
        Vector3 coastScale = new Vector3(dimensions.x/10f,1,dimensions.y/10f);
        foreach (Transform prop in stall.parent)
        {
            if (prop==stall) continue;
            prop.position=Vector3.Scale(prop.position,coastScale);
            if (prop.name.EndsWith("쪽 해변") || prop.name=="북쪽 물가" || prop.name=="경계 로프" || prop.name=="바다")
                prop.localScale=Vector3.Scale(prop.localScale,coastScale);
            if (prop.name != "수선한 파라솔") continue;
            Vector3 p = prop.position;
            p.x = Mathf.Clamp(p.x,-3.6f*coastScale.x,3.6f*coastScale.x);
            prop.position = p;
            parasols.Add(prop);
        }
        if (spectatorArea) spectatorArea.SetActive(Theme == BeachTheme.Tournament);
        if (fishingBoat) boatMooring = fishingBoat.localPosition;
        inventory.StartVisit(Theme);
        inventory.Funds = () => Gold;
        inventory.Spend = Pay;
        PlaceFirstSack();
        ReleaseGull();
        player.Bins = new[] { BeachBin.Create(stall,WasteBin.Metal), BeachBin.Create(stall,WasteBin.Bottle), BeachBin.Create(stall,WasteBin.General) };
        DressTheme();
        BuryExtras();
        ScatterFinds();
        ReleaseCrab();
        TotalFinds = finds.Length-1;
        Local = player.gameObject.AddComponent<BeachDigger>();
        Local.Initialize(this,player,inventory,digRing);
        Moment = gameObject.AddComponent<NeedleMoment>();
        Moment.Initialize(this);
        speaker=gameObject.AddComponent<AudioSource>();
        grainSpeaker=gameObject.AddComponent<AudioSource>();
        stepSound=SandAudio.Footstep();
        pourSound=SandAudio.Pour();
        trickleSound=SandAudio.Trickle();
        sand.SurfaceChanged += SettleFinds;
        sand.Sliding += OnSlide;
        beep=AudioClip.Create("탐지 펄스",3200,1,32000,false);
        var samples=new float[3200];
        for(int i=0;i<samples.Length;i++)
            samples[i]=.14f*Mathf.Sin(i*2*Mathf.PI*780/32000)*Mathf.Sin(Mathf.PI*i/samples.Length);
        beep.SetData(samples,0);
        chime=Tone("줍기 알림",.09f,880,1320);
        fanfare=Tone("클리어 알림",.15f,660,880,1100,1320);
        if (Theme==BeachTheme.Tournament)
        {
            var court = new GameObject("해변 배구 코트");
            court.transform.SetParent(transform,false);
            Volleyball = court.AddComponent<BeachVolleyball>();
            Volleyball.Initialize(this);
        }
        Bucket = new GameObject("모래성 놀이").AddComponent<SandcastleBucket>();
        Bucket.transform.SetParent(transform,false);
        Bucket.Initialize(this);
        if (Theme==BeachTheme.Festival)
        {
            Turtle = new GameObject("물가에 걸린 폐어망").AddComponent<BeachTurtle>();
            Turtle.transform.SetParent(transform,false);
            Turtle.Initialize(this);
            TotalFinds++;
        }
        Initialized = true;
        // A guest waits here for the host's beach to arrive before starting.
        bool wait = BeachNet.Instance && BeachNet.Instance.Attach(this,startVisit);
        if (wait) player.Present(true);
        else if (startVisit) Begin();
        else player.Present(true);
    }

    readonly Dictionary<int,LineRenderer> pings = new();

    public void Ping(Vector2 spot)
    {
        ShowPing(MyId,spot);
        if (Outbox==null) return;
        Pack(Op.Ping).Write(spot);
        Send();
    }

    // A sign to the team: this player's figure makes it, and everyone else sees it over their head.
    public void Sign(BeachBody.Gesture sign)
    {
        if (player.Body) player.Body.Sign(sign);
        if (Outbox==null) return;
        Pack(Op.Gesture).Write((byte)sign);
        Send();
    }

    void ShowSign(int owner,BeachBody.Gesture sign)
    {
        if (!BeachBody.Known(sign)) return;
        var team = Team?.Invoke();
        if (team!=null && team.TryGetValue(owner,out var figure) && figure && figure.Body) figure.Body.Sign(sign);
    }

    void ShowPing(int owner,Vector2 spot)
    {
        if (!sand.Contains(spot,0)) return;
        if (pings.TryGetValue(owner,out var previous)) Destroy(previous.gameObject);
        var ring = new GameObject("수색 위치 핑").AddComponent<LineRenderer>();
        ring.transform.SetParent(transform,false);
        ring.sharedMaterial = digRing.sharedMaterial;
        var tint = new MaterialPropertyBlock();
        tint.SetColor("_BaseColor",Color.HSVToRGB((.48f+owner*.37f)%1f,.8f,1f));
        ring.SetPropertyBlock(tint);
        ring.loop = true;
        ring.positionCount = 24;
        ring.widthMultiplier = .036f;
        pings[owner] = ring;
        StartCoroutine(DisplayPing(owner,spot,ring));
    }

    System.Collections.IEnumerator DisplayPing(int owner,Vector2 spot,LineRenderer ring)
    {
        for (float age=0; age<5f && !Won && ring && pings.TryGetValue(owner,out var active) && active==ring; age+=Time.deltaTime)
        {
            float radius = .25f + .035f*Mathf.Sin(age*4f);
            for (int i=0;i<ring.positionCount;i++)
            {
                float angle = i*2*Mathf.PI/ring.positionCount;
                Vector2 point = sand.ClampToBeach(spot+radius*new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)),0);
                ring.SetPosition(i,Surface(point)+Vector3.up*.035f);
            }
            yield return null;
        }
        if (!pings.TryGetValue(owner,out var current) || current!=ring) yield break;
        pings.Remove(owner);
        Destroy(ring.gameObject);
    }

    void Update()
    {
        if (fishingBoat)
        {
            // A local, slow oval offshore; its heading follows the tangent at either end.
            float t = Time.timeSinceLevelLoad * .055f;
            fishingBoat.localPosition = boatMooring + new Vector3(7f*Mathf.Sin(t),.035f*Mathf.Sin(t*17),1.2f*Mathf.Cos(t));
            fishingBoat.localRotation = Quaternion.Euler(0,Mathf.Atan2(1.2f*Mathf.Sin(t),7f*Mathf.Cos(t))*Mathf.Rad2Deg,0);
        }
        // In a session the clock belongs to the beach, not to whoever happens to have the controls.
        if (Outbox==null || !Started || Won) return;
        Elapsed += Time.deltaTime;
        if (Guest) return;
        patchSent += Time.deltaTime;
        if (patchSent>=.5f) { patchSent = 0; SendSandPatch(); }
        clockSent += Time.deltaTime;
        if (clockSent<2f) return;
        clockSent = 0;
        Pack(Op.Clock).Write(Elapsed);
        Send();
    }

    static AudioClip Tone(string name,float noteSeconds,params float[] notes)
    {
        const int rate=32000;
        int perNote=Mathf.RoundToInt(noteSeconds*rate);
        var samples=new float[perNote*notes.Length];
        for(int i=0;i<samples.Length;i++)
        {
            int local=i%perNote;
            samples[i]=.16f*Mathf.Sin(local*2*Mathf.PI*notes[i/perNote]/rate)*Mathf.Sin(Mathf.PI*local/perNote);
        }
        var clip=AudioClip.Create(name,samples.Length,1,rate,false);
        clip.SetData(samples,0);
        return clip;
    }

    // The first visit keeps the guided layout. Once the other beaches are open every visit buries things somewhere new.
    void ScatterFinds()
    {
        if (!ThemesUnlocked) return;
        Physics.SyncTransforms();
        var taken = new List<Vector2>();
        foreach (var item in finds)
        {
            Vector2 spot = item.Position;
            for (int attempt=0; attempt<80; attempt++)
            {
                var candidate = new Vector2(Random.Range(-sand.HalfSize.x+.6f,sand.HalfSize.x-.6f),
                    Random.Range(-sand.HalfSize.y+.6f,sand.HalfSize.y-.6f));
                // The tournament needle stays on the court side, where the ball lands.
                if (item.kind==FindKind.Needle && Theme==BeachTheme.Tournament && candidate.x<sand.HalfSize.x-3f) continue;
                if (!FreeSpot(candidate,taken)) continue;
                spot = candidate;
                break;
            }
            taken.Add(spot);
            Vector3 shift = new Vector3(spot.x-item.Position.x,0,spot.y-item.Position.y);
            item.transform.position += shift;
            item.transform.Rotate(0,Random.Range(0f,360f),0,Space.World);
            if (item.traces) item.traces.transform.position += shift;
        }
    }

    bool FreeSpot(Vector2 spot,List<Vector2> taken)
    {
        static Vector2 Flat(Vector3 p) => new Vector2(p.x,p.z);
        if (Vector2.Distance(spot,Flat(player.transform.position))<1f) return false;
        if (Vector2.Distance(spot,Flat(stall.position))<1.7f) return false;
        if (Vector2.Distance(spot,new Vector2(.76f,-2.05f))<.6f) return false; // sandcastle bucket
        foreach (var parasol in parasols)
            if (Vector2.Distance(spot,Flat(parasol.position)+new Vector2(.45f,0))<1.45f) return false; // pole and mat
        foreach (var other in taken)
            if (Vector2.Distance(spot,other)<.7f) return false;
        return !Physics.CheckBox(new Vector3(spot.x,.3f,spot.y),new Vector3(.2f,.28f,.2f));
    }

    // Every sand sound is pitched a little differently so repeats do not sound stamped.
    void SandSound(AudioClip clip,float volume)
    {
        if (!clip) return;
        grainSpeaker.pitch = Random.Range(.88f,1.14f);
        grainSpeaker.PlayOneShot(clip,volume);
    }

    void OnSlide(Vector3 slope) => SandSound(trickleSound,.45f);

    // Sand that slides away from under a find lets it drop to the new surface.
    void SettleFinds()
    {
        foreach (var item in finds)
        {
            if (!item.gameObject.activeSelf) continue;
            Vector3 p = item.transform.position;
            float ground = sand.Height(item.Position);
            if (p.y <= ground+.02f) continue;
            p.y = ground+.004f;
            item.transform.position = p;
        }
        RevealPourNeedle();
    }

    void DressTheme()
    {
        if (Theme==BeachTheme.Neighborhood) return;
        var needle = System.Array.Find(finds,item=>item.kind==FindKind.Needle);
        Vector2 site = Theme==BeachTheme.Tournament ? new Vector2(sand.HalfSize.x-1.15f,-.55f) : new Vector2(-.9f,2.2f);
        Vector2 delta = site-needle.Position;
        Vector3 shift = new Vector3(delta.x,0,delta.y);
        needle.transform.position += shift;
        if (needle.traces) needle.traces.transform.position += shift;
        if (Theme!=BeachTheme.Festival) return;

        Vector2[] caps = {new(-1.35f,1.45f),new(-.56f,2.35f),new(.55f,1.1f),new(-2f,.2f),new(.45f,5.15f)};
        var metal=player.trowel.GetComponentsInChildren<Renderer>(true)[1].sharedMaterial;
        var festivalFinds = new List<BeachFind>(finds);
        foreach (var pos in caps)
        {
            var cap=Instantiate(finds[0],finds[0].transform.parent);
            cap.name="철제 병뚜껑"; cap.kind=FindKind.Cap; cap.traces=null;
            cap.transform.position=new Vector3(pos.x,-.047f,pos.y);
            cap.transform.localScale=new Vector3(.65f,1.4f,.65f);
            cap.GetComponentInChildren<Renderer>().sharedMaterial=metal;
            festivalFinds.Add(cap);
        }
        festivalFinds.Add(FestivalItem(FindKind.PullTab,"PullTab",new Vector3(-.7f,-.034f,.7f),metal,Color.white,.003f));
        festivalFinds.Add(FestivalItem(FindKind.FestivalBand,"FestivalBand",new Vector3(.85f,-.012f,-1.1f),metal,new Color(1f,.25f,.05f),.021f));
        finds=festivalFinds.ToArray();

        var garland=new GameObject("축제 깃발").transform;
        garland.SetParent(stall,false);
        garland.localPosition=new Vector3(0,2.12f,0);
        var line=garland.gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace=false; line.sharedMaterial=digRing.sharedMaterial;
        line.widthMultiplier=.025f; line.positionCount=9;
        for(int i=0;i<9;i++){
            var point=new Vector3((i-4)*.39f,-.20f*Mathf.Sin(i*Mathf.PI/8),0);
            line.SetPosition(i,point);
            var flag=CoastProp.Place("Pennant","축제 깃발",garland,point-Vector3.up*.20f,new Vector3(.23f,.20f,.03f));
            var paint=new MaterialPropertyBlock();
            paint.SetColor("_BaseColor",i%2==0?new Color(1,.70f,.45f):new Color(.60f,.90f,1));
            flag.GetComponent<MeshRenderer>().SetPropertyBlock(paint);
        }
    }
    // More to find on every beach: thin things that pass for the needle until they are uncovered,
    // and litter whose end or rim shows above the sand. Only the hairpin is metal, so the detector stays useful.
    // The marine litter comes last, so the earlier finds of the guided visit stay where they were.
    void BuryExtras()
    {
        var metal = player.trowel.GetComponentsInChildren<Renderer>(true)[1].sharedMaterial;
        var plain = digRing.sharedMaterial;
        var needle = System.Array.Find(finds,item=>item.kind==FindKind.Needle);
        var all = new List<BeachFind>(finds);
        var taken = new List<Vector2>();
        foreach (var item in finds) taken.Add(item.Position);
        Physics.SyncTransforms();
        // The first, guided visit buries them in the same places every time.
        var dice = Random.state;
        if (!ThemesUnlocked) Random.InitState(20261112);

        BeachFind Bury(FindKind kind,float depth,float top,bool shells)
        {
            var root = new GameObject(BeachFind.NameOf(kind)).transform;
            root.SetParent(finds[0].transform.parent,false);
            Vector2 spot = Vector2.zero;
            for (int attempt=0; attempt<200; attempt++)
            {
                spot = new Vector2(Random.Range(-sand.HalfSize.x+.6f,sand.HalfSize.x-.6f),
                    Random.Range(-sand.HalfSize.y+.6f,sand.HalfSize.y-.6f));
                if (FreeSpot(spot,taken)) break;
            }
            taken.Add(spot);
            root.position = new Vector3(spot.x,-depth,spot.y);
            root.rotation = Quaternion.Euler(0,Random.Range(0f,360f),0);
            var find = root.gameObject.AddComponent<BeachFind>();
            find.kind = kind;
            find.topOffset = top;
            if (shells && needle && needle.traces)
            {
                // The same shell cluster that marks the needle, so it gives nothing away.
                find.traces = Instantiate(needle.traces,needle.traces.transform.parent);
                Vector2 shift = spot-needle.Position;
                find.traces.transform.position = needle.traces.transform.position+new Vector3(shift.x,0,shift.y);
                find.traces.SetActive(true);
            }
            all.Add(find);
            return find;
        }

        void Part(BeachFind find,PrimitiveType shape,Vector3 position,Vector3 scale,float tilt,Material material,Color tint)
        {
            var part = GameObject.CreatePrimitive(shape);
            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            part.transform.SetParent(find.transform,false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(tilt,0,0);
            part.transform.localScale = scale;
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            var colour = new MaterialPropertyBlock();
            colour.SetColor("_BaseColor",tint);
            renderer.SetPropertyBlock(colour);
        }

        void BuryLitter(FindKind kind,float depth,float top,bool shells=false)
        {
            var find = Bury(kind,depth,top,shells);
            BeachFind.AttachLitterModel(kind,find.transform);
        }

        var steel = new Color(.20f,.21f,.24f);
        var wood = new Color(.93f,.82f,.60f);
        Color[] lids = {new(.85f,.20f,.20f),new(.20f,.45f,.85f),new(.25f,.70f,.40f)};
        bool festival = Theme==BeachTheme.Festival;

        // Lying flat at the needle's depth under a shell cluster: a thin rod appears, and only then what it is.
        var pin = Bury(FindKind.Hairpin,.055f,.015f,true);
        Part(pin,PrimitiveType.Cube,new Vector3(-.011f,0,0),new Vector3(.012f,.012f,.28f),0,metal,steel);
        Part(pin,PrimitiveType.Cube,new Vector3(.011f,0,0),new Vector3(.012f,.012f,.28f),0,metal,steel);
        Part(pin,PrimitiveType.Cube,new Vector3(0,0,-.14f),new Vector3(.034f,.012f,.012f),0,metal,steel);
        for (int i=0; i<2; i++)
            BuryLitter(FindKind.Toothpick,.050f,.015f,true);

        // One end of a stick or the rim of a lid shows above the sand; a stroke of the hand frees it.
        // The festival beach already has its own litter, so it gets no sticks.
        for (int i=0; i<(festival ? 0 : 3); i++)
            Part(Bury(FindKind.PopsicleStick,.030f,-.005f,false),PrimitiveType.Cube,Vector3.zero,new Vector3(.07f,.012f,.36f),20,plain,wood);
        for (int i=0; i<2; i++)
            Part(Bury(FindKind.PlasticCap,.012f,-.008f,false),PrimitiveType.Cylinder,Vector3.zero,new Vector3(.12f,.022f,.12f),0,plain,lids[i%lids.Length]);

        // What the sea and the visitors leave behind. All of it shows on the surface and none of it is metal:
        // the search keeps passing it, and a stroke of the hand frees each piece.
        var foodMaterial = Resources.Load<Material>("BeachProps/FoodFinish");
        void AddPackaging(BeachFind find,GameObject model)
        {
            var visual = Instantiate(model,find.transform,false);
            visual.GetComponentInChildren<MeshRenderer>().sharedMaterial = foodMaterial;
        }
        var bottleModel = Resources.Load<GameObject>("BeachProps/Bottle");
        var wrapperModel = Resources.Load<GameObject>("BeachProps/Wrapper");
        for (int i=0; i<(festival ? 6 : 4); i++)
            BuryLitter(FindKind.CigaretteButt,.004f,-.006f);
        for (int i=0; i<2; i++)
        {
            var bottle = Bury(FindKind.PlasticBottle,.030f,-.010f,false);
            AddPackaging(bottle,bottleModel);
        }
        for (int i=0; i<2; i++)
            BuryLitter(FindKind.Straw,.010f,-.006f);
        for (int i=0; i<(festival ? 4 : 2); i++)
        {
            var wrapper = Bury(FindKind.SnackWrapper,.004f,-.006f,false);
            AddPackaging(wrapper,wrapperModel);
        }
        for (int i=0; i<3; i++)
            BuryLitter(FindKind.FoamChunk,.012f,-.008f);
        for (int i=0; i<2; i++)
            BuryLitter(FindKind.PlasticBag,.006f,-.008f);
        BuryLitter(FindKind.RopeScrap,.006f,-.008f);

        Random.state = dice;
        finds = all.ToArray();
    }

    BeachFind FestivalItem(FindKind kind,string model,Vector3 position,Material material,Color tint,float top)
    {
        var obj=Instantiate(Resources.Load<GameObject>("Festival/"+model),finds[0].transform.parent);
        obj.name=BeachFind.NameOf(kind);
        obj.transform.position=position;
        foreach(var renderer in obj.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterial=material;
            var color=new MaterialPropertyBlock();
            color.SetColor("_BaseColor",tint);
            renderer.SetPropertyBlock(color);
        }
        var find=obj.AddComponent<BeachFind>();
        find.kind=kind;
        find.topOffset=top;
        return find;
    }

    // ---- What a digger may do to the beach ----

    // The clock runs only while the local player is in control. A co-op host will drive it instead.
    public void Tick(float seconds) { if (Outbox==null) Elapsed += seconds; }

    // One stride: a footprint where the foot lands on sand, and the sound of it.
    public void Footstep(Vector2 foot,Vector2 forward)
    {
        DoFootstep(foot,forward);
        if (Outbox==null) return;
        var w = Pack(Op.Footstep); w.Write(foot); w.Write(forward);
        Send();
    }

    void DoFootstep(Vector2 foot,Vector2 forward)
    {
        if (sand.Contains(foot,.12f))
        {
            sand.Footprint(foot,forward);
            sand.Kick(foot,4,.06f,.45f);
        }
        SandSound(stepSound,.35f);
    }

    // Sand trickling out of cupped hands onto `drop`.
    public void Leak(Vector3 from,Vector2 drop,float spill)
    {
        DoLeak(from,drop,spill);
        if (Outbox==null) return;
        var w = Pack(Op.Leak); w.Write(from); w.Write(drop); w.Write(spill);
        Send();
    }

    void DoLeak(Vector3 from,Vector2 drop,float spill)
    {
        sand.Fall(from,drop,.3f,90f,.05f);
        sand.Pour(drop,spill,.16f);
        SandSound(trickleSound,.5f);
    }

    // One step of a two-handed sweep outwards from `centre`.
    public void SweepStep(Vector2 centre,Vector2 across,float distance,bool first)
    {
        DoSweepStep(centre,across,distance,first);
        if (Outbox==null) return;
        var w = Pack(Op.SweepStep); w.Write(centre); w.Write(across); w.Write(distance); w.Write(first);
        Send();
    }

    void DoSweepStep(Vector2 centre,Vector2 across,float distance,bool first)
    {
        sand.PushWithPalm(centre-across*distance,-across);
        sand.PushWithPalm(centre+across*distance,across);
        sand.Kick(centre-across*(distance+.08f),3,.05f,.55f);
        sand.Kick(centre+across*(distance+.08f),3,.05f,.55f);
        if (first) SandSound(handSandSound,.42f);
        foreach (var item in finds)
            if (item.traces && (Vector2.Distance(item.Position,centre-across*distance)<.17f ||
                Vector2.Distance(item.Position,centre+across*distance)<.17f))
                item.traces.SetActive(false);
    }

    // Call after sweep steps so the surface is redrawn and a re-buried needle can show again.
    public void SurfaceWorked()
    {
        sand.Refresh();
        RevealPourNeedle();
    }

    // Lifts a scoop out of the sand. Finds inside the removed layer go into `into` and leave the beach.
    // Returns how much sand came out; nothing is taken when that is zero.
    public float ScoopSand(Vector2 spot,float radius,float depth,List<BeachFind> into,bool markTrowel,Vector2 forward)
    {
        // Only centres inside the layer removed by this stroke travel with it.
        foreach (var item in finds)
            if (item.gameObject.activeSelf && (spot-item.Position).sqrMagnitude < radius*radius &&
                item.transform.position.y < sand.Height(item.Position))
                into.Add(item);

        float load = sand.Scrape(spot,radius,depth);
        into.RemoveAll(item => sand.Height(item.Position) > item.transform.position.y);
        if (load<=0) { into.Clear(); return 0; }
        if (markTrowel) sand.MarkTrowel(spot,forward);
        foreach (var item in into) Lift(item);
        if (Outbox!=null)
        {
            var w = Pack(Op.Scoop); w.Write(spot); w.Write(radius); w.Write(depth); w.Write(markTrowel); w.Write(forward);
            w.Write((byte)into.Count);
            foreach (var item in into) w.Write((byte)IndexOf(item));
            Send();
        }
        return load;
    }

    // A find leaves the beach inside a scoop.
    void Lift(BeachFind item)
    {
        if (item == buriedPourNeedle)
        {
            SetNeedleVisible(item,true);
            buriedPourNeedle = null;
        }
        item.gameObject.SetActive(false);
    }

    // A find comes back down with poured sand. A needle heaped onto a mound stays hidden until it is uncovered.
    void Land(BeachFind item,Vector3 position,bool covered)
    {
        item.transform.position = position;
        item.gameObject.SetActive(true);
        if (covered)
        {
            buriedPourNeedle = item;
            SetNeedleVisible(item,false);
        }
        else if (item.kind == FindKind.Needle) StartCoroutine(Glint(item));
    }

    // Puts a carried scoop back down at `spot`, with the finds that came up in it. `radius` comes back
    // widened when the sand was spread thin. Returns true when it was heaped onto an existing mound.
    public bool PourSand(Vector2 spot,ref float radius,float load,List<BeachFind> contents,Vector2 scoopCentre,Vector3 source)
    {
        float narrow = radius;
        sand.Fall(source,spot,.38f,260f,radius*.55f);
        SandSound(pourSound,.6f);
        // The starting beach is level at y=0. Sample before adding this load.
        bool onMound = sand.Height(spot) >= MoundHeight;
        if (!onMound) radius *= SpreadFactor;
        sand.Pour(spot,load,radius);
        foreach (var item in contents)
        {
            bool coverNeedle = onMound && item.kind == FindKind.Needle;
            Vector2 place = coverNeedle ? spot : spot + (item.Position - scoopCentre) * (onMound ? 1f : SpreadFactor);
            place=sand.ClampToBeach(place,.10f);
            float offset = coverNeedle ? -item.topOffset-.012f : .004f;
            Land(item,new Vector3(place.x,sand.Height(place)+offset,place.y),coverNeedle);
        }
        if (Outbox!=null)
        {
            var w = Pack(Op.Pour); w.Write(spot); w.Write(narrow); w.Write(radius); w.Write(load); w.Write(source);
            w.Write((byte)contents.Count);
            foreach (var item in contents)
            {
                w.Write((byte)IndexOf(item));
                w.Write(item.transform.position);
                w.Write(item==buriedPourNeedle);
            }
            Send();
        }
        contents.Clear();
        return onMound;
    }

    // Ends a scoop or a pour: redraws the sand and clears the shell clusters the stroke covered.
    public void FinishStroke(Vector2 spot,float radius,bool trowel)
    {
        DoFinishStroke(spot,radius,trowel);
        if (Outbox==null) return;
        var w = Pack(Op.FinishStroke); w.Write(spot); w.Write(radius); w.Write(trowel);
        Send();
    }

    void DoFinishStroke(Vector2 spot,float radius,bool trowel)
    {
        sand.Refresh();
        RevealPourNeedle();
        SandSound(trowel ? shovelSandSound : handSandSound,.42f);
        foreach(var item in finds)
            if(item.traces && Vector2.Distance(spot,item.Position)<radius) item.traces.SetActive(false);
    }

    // Asks for an uncovered find. Alone or as host it is handed over at once through `who.Granted`;
    // a guest asks the host, who settles two hands reaching for the same thing.
    BeachFind pendingTake;
    void FinishTakeRequest(BeachFind item)
    {
        if (pendingTake!=item) return;
        pendingTake=null;
        inventory.Release(item.kind);
    }

    public void RequestTake(BeachFind item,BeachDigger who)
    {
        if(!item.gameObject.activeSelf || !item.Exposed(sand)) return;
        if (!inventory.CanStore(item.kind)) { Notice("손에 든 것을 먼저 자루에 넣자"); return; }
        if (Guest)
        {
            if (pendingTake || !inventory.Reserve(item.kind)) return;
            pendingTake=item;
            Pack(Op.TakeRequest).Write((byte)IndexOf(item));
            Send();
            return;
        }
        item.gameObject.SetActive(false);
        Collected.Add(item);
        if (Outbox!=null)
        {
            var w = Pack(Op.Taken); w.Write((byte)IndexOf(item)); w.Write((byte)MyId);
            Send();
        }
        who.Granted(item);
    }

    // Counts a find someone kept towards the visit summary.
    public void Record(BeachFind item)
    {
        DoRecord(item.kind,.7f);
        if (Outbox==null) return;
        Pack(Op.Record).Write((byte)item.kind);
        Send();
    }

    void DoRecord(FindKind kind,float volume)
    {
        Picked++;
        if (BeachFind.IsTrashKind(kind))
        {
            cleaned.TryGetValue(kind,out int count);
            cleaned[kind]=count+1;
        }
        speaker.PlayOneShot(chime,volume);
    }

    public void HelpTurtle()
    {
        if (!Turtle || Turtle.Current==BeachTurtle.Stage.Collected) return;
        if (Turtle.Current==BeachTurtle.Stage.Released)
        {
            if (!inventory.CanStore(FindKind.FishingNet)) { Notice("폐어망을 들려면 손을 비우자"); return; }
            if (Guest && !inventory.Reserve(FindKind.FishingNet)) return;
        }
        if (Guest)
        {
            Pack(Op.TurtleRequest).Write((byte)Turtle.Current);
            Send();
        }
        else AdvanceTurtle(Turtle.Current,MyId);
    }

    void AdvanceTurtle(BeachTurtle.Stage expected,int actor)
    {
        if (!Turtle || Won || expected!=Turtle.Current || expected==BeachTurtle.Stage.Collected) return;
        var next = expected==BeachTurtle.Stage.Entangled ? BeachTurtle.Stage.Released : BeachTurtle.Stage.Collected;
        ApplyTurtle(next,actor);
        if (Outbox==null) return;
        var writer = Pack(Op.TurtleChanged);
        writer.Write((byte)next);
        writer.Write(actor);
        Send();
    }

    void ApplyTurtle(BeachTurtle.Stage next,int actor)
    {
        if (!Turtle || (int)next!=(int)Turtle.Current+1) return;
        Turtle.SetStage(next,true);
        if (next!=BeachTurtle.Stage.Collected) return;
        DoRecord(FindKind.FishingNet,.7f);
        inventory.Release(FindKind.FishingNet);
        if (actor!=MyId) return;
        inventory.Store(FindKind.FishingNet);
        Notice("폐어망을 수거했다");
    }

    public int Sorted { get; private set; }

    public void AddEarnings(int gold,int sorted = 0)
    {
        Sorted += sorted;
        Earned += gold;
        if (Outbox==null || gold==0) return;
        var w = Pack(Op.Earnings);
        w.Write(gold);
        w.Write(sorted);
        Send();
    }

    // The detector's question: is any metal within 2.8 m in the 90 degrees ahead of `from`?
    public bool MetalAhead(Transform from)
    {
        foreach(var item in finds){
            Vector3 offset=item.transform.position-from.position;offset.y=0;
            if(item.IsMetal && item.gameObject.activeSelf && offset.magnitude<=2.8f && Vector3.Angle(from.forward,offset)<=45f) return true;
        }
        return false;
    }

    // Horizontal distance from `here` to the nearest parasol pole.
    public float NearestParasol(Vector3 here)
    {
        float nearest = float.PositiveInfinity;
        foreach (var parasol in parasols)
        {
            Vector3 delta = here-parasol.position;
            nearest = Mathf.Min(nearest,new Vector2(delta.x,delta.z).magnitude);
        }
        return nearest;
    }

    public Transform ParasolModel => parasols.Count>0 ? parasols[0] : null;
    public bool HasParasols => parasols.Count>0;

    public bool CanPitchParasol(Vector2 spot)
    {
        Vector3 counter = stall.position;
        return sand.Contains(spot,.35f) && Vector2.Distance(spot,new Vector2(counter.x,counter.z))>=1.5f;
    }

    // Plants the portable parasol, or moves it. Returns true when it was already standing somewhere.
    public bool PitchParasol(Vector2 spot)
    {
        bool moved = DoPitchParasol(spot,.5f);
        if (Outbox==null) return moved;
        Pack(Op.Parasol).Write(spot);
        Send();
        return moved;
    }

    bool DoPitchParasol(Vector2 spot,float volume)
    {
        bool moved = portableParasol;
        if (!portableParasol)
        {
            portableParasol = Instantiate(parasols[0],parasols[0].parent);
            portableParasol.name = "휴대용 파라솔";
            // Only the pole and canopy travel; the mat stays with the mended one.
            foreach (Transform piece in portableParasol)
                if (piece.name=="줄무늬 돗자리" || piece.name=="면 줄무늬") Destroy(piece.gameObject);
            parasols.Add(portableParasol);
        }
        portableParasol.position = Surface(spot);
        speaker.PlayOneShot(chime,volume);
        return moved;
    }

    public void Begin(){ Started=true; Local.Enter(); }
    // A guest goes where the host went, whether or not that beach is unlocked on this machine.
    public static void FollowHost(BeachTheme theme)
    {
        nextVisit = theme;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void Visit(BeachTheme theme)
    {
        // Guests follow the host from beach to beach.
        if (Guest) return;
        if (theme!=BeachTheme.Neighborhood && !ThemesUnlocked) return;
        if (BeachNet.Instance) BeachNet.Instance.AnnounceVisit(theme);
        if (!Started && Theme==theme) { Begin(); return; }
        nextVisit=theme;
        SceneManager.LoadScene(gameObject.scene.name);
    }

    public void Win()
    {
        if (Won) return;
        DoWin(MyId);
        if (Outbox==null) return;
        var w = Pack(Op.Win);
        w.Write(Elapsed);
        w.Write((byte)MyId);
        Send();
    }

    // The few seconds after the needle comes up; the result panel waits for them.
    public NeedleMoment Moment { get; private set; }
    // Who picked the needle up.
    public int Finder { get; private set; } = -1;
    public Material RingMaterial => digRing.sharedMaterial;
    public void PlayFanfare() => speaker.PlayOneShot(fanfare,.8f);

    void DoWin(int finder)
    {
        Won=true;
        if (Theme==BeachTheme.Neighborhood && !ThemesUnlocked) PlayerPrefs.SetInt(UnlockKey,1);
        float best=BestTime(Theme);
        NewRecord = best<=0f || Elapsed<best;
        if (NewRecord) PlayerPrefs.SetFloat("FindNeedle.Best."+Theme,Mathf.Max(.01f,Elapsed));
        PlayerPrefs.Save();
        Finder = finder;
        Moment.Begin(finder);
    }

    public BeachFind VisibleFind(Vector2 spot)
    {
        foreach(var item in finds)
            if(item.gameObject.activeSelf && Vector2.Distance(spot,item.Position)<.18f && item.Exposed(sand))
                return item;
        return null;
    }
    public Vector3 Surface(Vector2 spot) => new Vector3(spot.x,sand.Height(spot),spot.y);

    static void SetNeedleVisible(BeachFind needle,bool visible)
    {
        foreach (var piece in needle.GetComponentsInChildren<MeshRenderer>(true))
            piece.forceRenderingOff = !visible;
    }

    void RevealPourNeedle()
    {
        if (!buriedPourNeedle || !buriedPourNeedle.Exposed(sand)) return;
        SetNeedleVisible(buriedPourNeedle,true);
        StartCoroutine(Glint(buriedPourNeedle));
        buriedPourNeedle = null;
    }

    System.Collections.IEnumerator Glint(BeachFind item)
    {
        var flash = new GameObject("바늘 끝 반짝임");
        flash.transform.SetParent(item.transform,false);
        flash.transform.localPosition = new Vector3(0,.018f,.13f);
        var line = flash.AddComponent<LineRenderer>();
        line.sharedMaterial = digRing.sharedMaterial;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 8;
        line.widthMultiplier = .006f;
        for (int i=0; i<8; i++)
        {
            float angle = i*Mathf.PI/4;
            float length = i%2 == 0 ? .047f : .009f;
            line.SetPosition(i,new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0)*length);
        }
        const float duration = .75f;
        for (float elapsed=0; elapsed<duration && item.gameObject.activeSelf && item.Exposed(sand); elapsed+=Time.deltaTime)
        {
            flash.transform.rotation = player.view.transform.rotation;
            flash.transform.localScale = Vector3.one * Mathf.Sin(Mathf.PI*elapsed/duration);
            yield return null;
        }
        Destroy(flash);
    }

    void OnDestroy(){if(BeachNet.Instance)BeachNet.Instance.Detach(this);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;if(beep)Destroy(beep);if(chime)Destroy(chime);if(fanfare)Destroy(fanfare);
        if(stepSound)Destroy(stepSound);if(pourSound)Destroy(pourSound);if(trickleSound)Destroy(trickleSound);
        if(sand){sand.SurfaceChanged-=SettleFinds;sand.Sliding-=OnSlide;}}
}
