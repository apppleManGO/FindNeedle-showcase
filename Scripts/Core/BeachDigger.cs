using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// One player's side of the search: what they hold, aim at and are told. The beach itself lives in
// BeachSearch. This class reads the sand freely but changes the beach only through BeachSearch methods.
public sealed class BeachDigger : MonoBehaviour
{
    BeachSearch game;
    BeachWalker player;
    BeachInventory inventory;
    LineRenderer digRing;
    SandPatch sand;

    public bool Collapsed => game.Started && !game.Won && player.Health == 0;
    bool Playing => game.Started && !game.Won && !Collapsed;
    public bool HoldingBall => game.Volleyball && game.Volleyball.Held;
    // What the other players see in this player's hands, and whether they are mid-stroke.
    public BeachSlot InHand => CarriedSack ? new BeachSlot(BeachGear.Sack) : HoldingBall ? new BeachSlot(BeachGear.Ball) :
        game.Bucket && game.Bucket.Held ? new BeachSlot(BeachGear.Bucket) :
        inventory.Hand is FindKind held ? new BeachSlot(held) : Equipped;
    // A find picked up stays in the hand until it goes into a sack or a bin; the tools wait meanwhile.
    public bool HoldingFind => inventory.Hand.HasValue;
    public BeachSack CarriedSack => game.CarriedSack;
    public bool CanLiftSack => Playing && !Loaded && !HoldingFind && !HoldingBall && !sweeping && !(game.Bucket && game.Bucket.Held) && !CarriedSack;
    public bool Working => sweeping || player.Swinging;
    public bool CanTakeBall => Playing && !HoldingBall && !Loaded && !sweeping && !HoldingFind && !CarriedSack && !(game.Bucket && game.Bucket.Held);
    public BeachItemModels Models => models;
    public float ShadeDistance { get; private set; }
    public bool NearShop => Vector3.Distance(player.transform.position,game.stall.position)<2.25f;
    public string Message { get; private set; }
    public string Signal { get; private set; }
    public float ScanWait => Mathf.Max(0,nextScan-Time.time);
    public bool Loaded => load > 0;
    const float HandRadius = .28f, HandDepth = .078f;
    const float TrowelRadius = .45f, TrowelDepth = .14f;
    public int Selected { get; private set; }
    public BeachSlot Equipped => inventory.Slot(Selected);
    public bool UsingTrowel => Equipped.Shovel;
    public bool UsingRake => Equipped.Gear==BeachGear.Rake;
    bool Bare => Equipped.Empty || Equipped.Gear==BeachGear.Hands;
    BeachSlot displayed;
    GameObject heldItem;
    BeachItemModels models;
    readonly Dictionary<FindKind,Transform> findModels=new();
    const string HandFull = "손에 든 것을 먼저 자루나 수거함에 넣자";
    GameObject rake;
    float nextRake;
    Vector2 scoopCenter, sweepCenter, sweepAcross;
    bool sweeping;
    public bool BeyondHandReach { get; private set; }
    float sweepTime;
    int pushedSteps;
    const float Approach = .16f, Spread = .27f, Return = .16f;
    const int SweepSteps = 5;

    public float Radius => Loaded ? (UsingTrowel ? TrowelRadius : HandRadius) * BeachSearch.SpreadFactor :
        UsingTrowel ? TrowelRadius : UsingRake ? .49f : .30f;
    public string TargetName => target ? target.Label : "";
    float nextScan, messageEnd, load;
    BeachFind target;
    readonly List<BeachFind> contents = new();
    GameObject sandOnBlade, sandInHands, magnet;

    // Shown every frame while the bucket is in hand or in view, so they are written once.
    static readonly string[] fillPrompts = InteractionPrompts("양동이 {0}/5 · 우클릭  모래 담기");
    static readonly string[] liftPrompts = InteractionPrompts("E  양동이 들기 · {0}/5");
    static string[] InteractionPrompts(string pattern)
    {
        var prompts = new string[SandcastleBucket.Capacity+1];
        for (int scoops=0; scoops<prompts.Length; scoops++) prompts[scoops] = string.Format(pattern,scoops);
        return prompts;
    }

    static readonly Vector3 Handful = new Vector3(.18f,.083f,.14f);
    const float Stride = .62f, LeakFloor = .55f;
    Vector3 lastStep;
    int stepSide = 1;
    float carried;
    bool leakNoticed;

    public string InteractionPrompt { get; private set; }
    public bool BallInReach { get; private set; }
    public bool ShopOpen { get; private set; }

    public void Initialize(BeachSearch beach,BeachWalker walker,BeachInventory bag,LineRenderer ring)
    {
        game = beach;
        player = walker;
        inventory = bag;
        digRing = ring;
        sand = beach.sand;
        player.InitializeView();
        magnet = Instantiate(Resources.Load<GameObject>("BeachProps/SorterMagnet"),player.Blade,false);
        magnet.name = "선별 자석";
        magnet.transform.localPosition = new Vector3(.065f,-.24f,.085f);
        magnet.GetComponentInChildren<MeshRenderer>().sharedMaterial = Resources.Load<Material>("BeachProps/MagnetFinish");
        player.Capture(false);
        models = game.gameObject.AddComponent<BeachItemModels>();
        foreach (var item in game.finds) findModels.TryAdd(item.kind,item.transform);
        rake = Instantiate(Resources.Load<GameObject>("BeachProps/HandRake"),player.rightArm,false);
        rake.name = "모래 갈퀴";
        rake.GetComponentInChildren<MeshRenderer>().sharedMaterial = Resources.Load<Material>("BeachProps/RakeFinish");
        rake.transform.localPosition = player.Blade.localPosition;
        rake.transform.rotation = player.view.transform.rotation;
        player.Rake = rake;
        SyncTools();
        digRing.enabled=false;
        sandOnBlade = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sandOnBlade.name="한 삽 분량의 모래";
        Destroy(sandOnBlade.GetComponent<Collider>());
        sandOnBlade.transform.SetParent(player.Blade,false);
        sandOnBlade.transform.localPosition=new Vector3(0,-.25f,-.046f);
        sandOnBlade.transform.localScale=new Vector3(.13f,.16f,.08f);
        sandOnBlade.GetComponent<Renderer>().sharedMaterial=sand.GetComponent<Renderer>().sharedMaterial;
        sandOnBlade.SetActive(false);
        sandInHands = Instantiate(sandOnBlade,player.view.transform);
        var handCollider = sandInHands.GetComponent<Collider>();
        if (handCollider) Destroy(handCollider);
        sandInHands.name = "두 손에 쥔 모래";
        sandInHands.transform.localPosition = new Vector3(0,-.13f,.53f);
        sandInHands.transform.localRotation = Quaternion.identity;
        sandInHands.transform.localScale = Handful;
        lastStep=player.transform.position;
    }

    // The visit begins: leave the title pose and take control.
    public void Enter(){ player.Present(false); player.Capture(true); }

    // The needle is found: stop aiming and let go of the controls while the moment plays.
    public void Freeze()
    {
        digRing.enabled=false;
        target=null;
        player.Capture(false);
    }

    // The needle is found: let go of the controls and pose for the result screen.
    public void ShowResult()
    {
        digRing.enabled=false;
        player.Capture(false);
        player.Present(true,true);
    }

    // One call per stride: a footprint, and a little sand lost from cupped hands.
    void TrackSteps()
    {
        Vector3 here = player.transform.position;
        Vector3 stride = here-lastStep;
        stride.y = 0;
        if (stride.sqrMagnitude < Stride*Stride) return;
        lastStep = here;
        stepSide = -stepSide;
        Vector2 forward = new Vector2(stride.x,stride.z).normalized;
        Vector2 feet = new Vector2(here.x,here.z);
        Vector2 foot = feet+new Vector2(forward.y,-forward.x)*(.09f*stepSide);
        game.Footstep(foot,forward);
        if (Loaded && !UsingTrowel && carried>0 && load>carried*LeakFloor)
        {
            float spill = Mathf.Min(carried*.075f,load-carried*LeakFloor);
            Vector2 drop = sand.ClampToBeach(feet+forward*.32f,.2f);
            game.Leak(sandInHands.transform.position,drop,spill);
            load -= spill;
            float kept = Mathf.InverseLerp(LeakFloor,1f,load/carried);
            sandInHands.transform.localScale = Handful*Mathf.Lerp(.72f,1f,kept);
            if (!leakNoticed)
            {
                leakNoticed = true;
                Notice("손가락 사이로 모래가 샌다 · 멀리 옮길 땐 삽이나 양동이");
            }
        }
        sand.Refresh();
    }

    public Transform ModelFor(BeachSlot slot)
    {
        if (slot.Find is FindKind kind)
        {
            if (kind==FindKind.FishingNet && game.Turtle) return game.Turtle.NetModel;
            return findModels.TryGetValue(kind,out var model) ? model : null;
        }
        return slot.Gear switch {
            BeachGear.Trowel => game.stall.Find("진열 모종삽") ?? player.trowel.transform,
            BeachGear.Detector => game.stall.Find("진열 탐지기") ?? player.detector.transform,
            BeachGear.Magnet => magnet.transform, BeachGear.Rake => rake.transform,
            BeachGear.Parasol => game.ParasolModel, BeachGear.Bucket=>game.Bucket.Model,
            BeachGear.Ball=>game.Volleyball.Model, BeachGear.Sack=>game.Sacks.Count>0 ? game.Sacks[0].Model : null, _ => null };
    }

    public Sprite SlotIcon(BeachSlot slot) => models.Portrait(slot,ModelFor(slot));

    void SyncTools()
    {
        if (heldItem) { heldItem.SetActive(false); Destroy(heldItem); heldItem=null; }
        displayed=Equipped;
        displayedHand=inventory.Hand;
        var shown = inventory.Hand is FindKind held ? new BeachSlot(held) : Equipped;
        if (shown.Find.HasValue || shown.Gear==BeachGear.Parasol)
            heldItem=models.InHand(shown,ModelFor(shown),player.HoldPoint);
        player.HoldingFind=heldItem;
        bool tools = !HoldingFind;
        rake.SetActive(tools && UsingRake);
        player.trowel.SetActive(tools && UsingTrowel);
        player.detector.SetActive(tools && Equipped.Gear==BeachGear.Detector);
        magnet.SetActive(tools && Equipped.Gear==BeachGear.Magnet);
    }
    FindKind? displayedHand;

    public void Notice(string text){Message=text;messageEnd=Time.time+2.8f;}

    // The ball is now in this player's hands.
    public void TookBall()
    {
        SelectSlot(0);
        game.Volleyball.Catch();
        player.HoldingBall = true;
        player.CuppedHands = true;
        rake.SetActive(false);
        player.trowel.SetActive(false);
        player.detector.SetActive(false);
        BallInReach = false;
    }

    public void OpenShop()
    {
        if (!Playing || !NearShop || HoldingBall || game.Bucket.Held) return;
        CancelSweep();
        ShopOpen = true;
        target = null;
        digRing.enabled = false;
        Message = "";
        player.Capture(false);
    }

    public void CloseShop()
    {
        ShopOpen = false;
        player.Capture(Playing);
    }

    void Update()
    {
        // The wheel never outlives the play that opened it, or the view would stay held.
        if (Wheel.Open && (game==null || !Playing || ShopOpen || !player.Captured || Keyboard.current==null || Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            Wheel.Cancel();
            player.LookHeld = false;
        }
        if (game==null || !Playing) return;
        // A find can reach the hand from the beach (a rescued net), not only from a press here.
        if (displayedHand!=inventory.Hand && !HoldingBall && !(game.Bucket && game.Bucket.Held) && !CarriedSack) SyncTools();
        var k=Keyboard.current;var mouse=Mouse.current;
        if(k==null || mouse==null) return;
        var Bucket = game.Bucket;
        var Volleyball = game.Volleyball;
        if (ShopOpen)
        {
            if (Time.time > messageEnd) Message = "";
            if (k.escapeKey.wasPressedThisFrame || !NearShop) CloseShop();
            return;
        }
        if(k.escapeKey.wasPressedThisFrame){ CancelSweep(); player.Capture(false);return;}
        if(!player.Captured){
            if(mouse.leftButton.wasPressedThisFrame) player.Capture(true);
            return;
        }
        if (!HoldingBall && !Bucket.Held && !sweeping)
        {
            if (!displayed.Equals(Equipped) || displayedHand!=inventory.Hand) SyncTools();
            float scroll=mouse.scroll.ReadValue().y;
            if (scroll!=0) SelectSlot(Selected+(scroll>0 ? -1 : 1));
        }
        player.Burden = CarriedSack ? CarriedSack.Burden : 1f;
        player.Move(sand);
        game.Tick(Time.deltaTime);
        TrackSteps();
        UpdateHeat(Time.deltaTime);
        if (Collapsed) { Wheel.Cancel(); player.LookHeld = false; return; }
        if(Time.time>messageEnd) Message="";
        Ray ray=player.view.ViewportPointToRay(new Vector3(.5f,.5f,0));
        InteractionPrompt = null;
        BeyondHandReach = false;
        if (Gestures(k,mouse))
        {
            // Choosing a sign: the mouse belongs to the wheel, so nothing is dug or picked up meanwhile.
            target = null;
            digRing.enabled = false;
            BallInReach = false;
            return;
        }
        BallInReach = Volleyball && Volleyball.WithinReach(ray);
        if (HoldingBall || (!Bucket.Held && !sweeping && BallInReach && !Loaded && !HoldingFind && !CarriedSack && k.eKey.wasPressedThisFrame))
        {
            target = null;
            digRing.enabled = false;
            if (!HoldingBall)
            {
                // In co-op a guest asks the host first; the ball arrives through TookBall.
                if (Volleyball.TryCatch()) TookBall();
                BallInReach = false;
            }
            else if (mouse.leftButton.wasPressedThisFrame)
            {
                Volleyball.Throw(ray.direction);
                player.HoldingBall = false;
                player.CuppedHands = false;
                SyncTools();
            }
            return;
        }
        // The ball belongs to the beach. In co-op the host's ball moves on its own, and guests only watch it.
        if (Volleyball && game.Outbox==null) Volleyball.Advance(Time.deltaTime);
        if (Collapsed)
        {
            Faint();
            return;
        }
        if (sweeping)
        {
            AdvanceSweep(Time.deltaTime);
            return;
        }
        bool inside=sand.Aim(ray,out Vector2 spot);
        float reach = inside ? Vector3.Distance(ray.origin,game.Surface(spot))+.005f : 2.7f;
        bool blocked = Physics.Raycast(ray,out var hit,reach);
        if (inside && !blocked && k.gKey.wasPressedThisFrame) game.Ping(spot);
        if (Bucket.Held)
        {
            target=null;digRing.enabled=false;BallInReach=false;
            bool aimed=inside && !blocked;
            digRing.enabled=aimed && (!Bucket.Filled || Bucket.CanPlace(spot));
            if(digRing.enabled) DrawRing(spot,Bucket.Filled ? .17f : HandRadius);
            InteractionPrompt=Bucket.Filled ? "5/5 · 좌클릭  모래탑 찍기" : fillPrompts[Bucket.Scoops];
            if (mouse.leftButton.wasPressedThisFrame && Bucket.Filled)
            {
                if(aimed && Bucket.Mold(spot)) Notice("모래탑을 찍었다");
                else Notice("빈 모래밭을 조준하자");
                return;
            }
            if (mouse.rightButton.wasPressedThisFrame && aimed && !Bucket.Filled)
            {
                ScoopIntoBucket(spot);
                return;
            }
            if (k.eKey.wasPressedThisFrame)
            {
                if(inside && !blocked && Bucket.Place(spot))
                {
                    player.CuppedHands=false;
                    SyncTools();
                    Notice("E로 양동이 들기");
                }
                else Notice("빈 모래밭을 가까이 조준하자");
            }
            return;
        }
        var carried = CarriedSack;
        if (carried)
        {
            target=null; BallInReach=false;
            string filled = $"자루 {carried.Count}/{BeachSack.Capacity}";
            if (blocked && hit.distance<=BeachBin.Reach && hit.collider.TryGetComponent<BeachBin>(out var sortInto))
            {
                digRing.enabled=false;
                InteractionPrompt=$"{sortInto.Label} · E 자루 속 쓰레기 분리배출";
                if (k.eKey.wasPressedThisFrame) SortSack(carried,sortInto);
                return;
            }
            bool aimed=inside && !blocked;
            digRing.enabled=aimed && carried.CanSetDown(spot);
            if (digRing.enabled) DrawRing(spot,.24f);
            InteractionPrompt=NearShop ? filled+" · B 매입소 · E 내려놓기" : filled+" · E 내려놓기";
            if (NearShop && k.bKey.wasPressedThisFrame) { OpenShop(); return; }
            if (k.eKey.wasPressedThisFrame)
            {
                if (aimed && game.SetDownSack(carried,spot)) { player.CuppedHands=false; SyncTools(); }
                else Notice("자루를 내려놓을 빈 모래밭을 조준하자");
            }
            return;
        }
        if (blocked && hit.distance<=BeachBin.Reach && hit.collider.TryGetComponent<BeachBin>(out var bin))
        {
            target=null; digRing.enabled=false; BallInReach=false;
            InteractionPrompt=bin.Label+" · "+(inventory.Hand is FindKind trash && BeachFind.IsTrashKind(trash) ? "E 손에 든 쓰레기 넣기" : "자루를 들고 오면 한 번에 분리배출");
            if (k.eKey.wasPressedThisFrame) Deposit(bin);
            return;
        }
        if (blocked && hit.distance<=BeachSack.Reach && game.SackOf(hit.collider) is BeachSack lying)
        {
            target=null; digRing.enabled=false; BallInReach=false;
            string fullness = $"{lying.Count}/{BeachSack.Capacity}";
            InteractionPrompt = HoldingFind ? (lying.Full ? "자루가 가득 찼다 · "+fullness : "E 자루에 넣기 · "+fullness) :
                Loaded ? "모래를 내려놓으면 자루를 들 수 있다" : "E 자루 들기 · "+fullness;
            if (k.eKey.wasPressedThisFrame) UseSack(lying);
            return;
        }
        if (blocked && (Bucket.IsBucket(hit.collider) || Bucket.IsTower(hit.collider)))
        {
            target=null;digRing.enabled=false;BallInReach=false;
            bool tower=Bucket.IsTower(hit.collider);
            InteractionPrompt=tower ? "E  성탑 허물기" : Loaded ?
                (Bucket.Filled ? "양동이가 가득 찼다" : "E  모래 담기") :
                liftPrompts[Bucket.Scoops];
            if (k.eKey.wasPressedThisFrame) UseBucket(hit.collider);
            return;
        }
        if (game.Turtle && game.Turtle.InReach(ray,blocked ? hit.distance : reach))
        {
            target = null;
            digRing.enabled = false;
            InteractionPrompt = game.Turtle.Current==BeachTurtle.Stage.Entangled ? "E  지느러미의 그물 풀기" : "E  남은 폐어망 수거";
            if (k.eKey.wasPressedThisFrame) game.HelpTurtle();
            return;
        }
        if(blocked) inside=false;
        BeyondHandReach = inside && Bare && !Loaded && !player.CanReachSand(game.Surface(spot));
        target=inside ? game.VisibleFind(spot) : null;
        digRing.enabled=inside;
        if(inside) DrawRing(spot);
        if(k.eKey.wasPressedThisFrame){
            if(BallInReach && Loaded) Notice("모래를 내려놓으면 공을 잡을 수 있다");
            else if(target) { if (HoldingFind) Notice(HandFull); else Take(target); }
            else if(NearShop) { OpenShop(); return; }
        }
        if(game.Won) return;
        if (HoldingFind)
        {
            // The hand is full: the tools wait until the find is put away.
            if(NearShop && k.bKey.wasPressedThisFrame) { OpenShop(); return; }
            InteractionPrompt ??= target ? HandFull : "E 자루나 수거함에 넣기";
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || k.qKey.wasPressedThisFrame || k.fKey.wasPressedThisFrame)
                Notice(HandFull);
            return;
        }
        if(NearShop && k.bKey.wasPressedThisFrame) { OpenShop(); return; }
        if(k.rKey.wasPressedThisFrame) SwitchTool();
        if(k.qKey.wasPressedThisFrame) Scan();
        if(k.fKey.wasPressedThisFrame) SortLoad();
        if((k.tKey.wasPressedThisFrame || mouse.leftButton.wasPressedThisFrame) && Equipped.Gear==BeachGear.Parasol)
        {
            if(inside) PitchParasol(spot); else Notice("파라솔을 꽂을 모래밭을 조준하자");
        }
        if(inside && mouse.leftButton.wasPressedThisFrame) Dig(spot);
        else if(inside && mouse.rightButton.wasPressedThisFrame) HandScoop(spot);
    }

    void UseBucket(Collider targetCollider)
    {
        var Bucket = game.Bucket;
        if(Bucket.IsTower(targetCollider))
        {
            Bucket.Crumble(targetCollider);
            return;
        }
        if(Loaded)
        {
            if(!Bucket.Fill(load,contents))
            {
                Notice("들고 있는 모래를 옆에 먼저 내려놓자");
                return;
            }
            load=0;
            contents.Clear();
            SyncCarriedSand();
            Notice($"양동이 {Bucket.Scoops}/5");
            return;
        }
        SelectSlot(0);
        Bucket.Lift();
        player.CuppedHands=true;
        rake.SetActive(false);
        player.trowel.SetActive(false);
        player.detector.SetActive(false);
    }

    void UpdateHeat(float seconds)
    {
        if (!Playing) return;
        float nearest = game.NearestParasol(player.transform.position);
        const float shadeRadius = 1.32f;
        ShadeDistance = Mathf.Max(0f,nearest-shadeRadius);
        player.UpdateExposure(nearest<=shadeRadius,seconds);
        if (Collapsed) Faint();
    }

    void Faint()
    {
        CancelSweep();
        digRing.enabled=false;
        target=null;
        player.Capture(false);
        player.Fall();
    }

    // Bought once per visit; T plants it, or moves it to wherever the search has drifted.
    void PitchParasol(Vector2 spot)
    {
        if (Loaded) { Notice("들고 있는 모래를 먼저 내려놓자"); return; }
        if (!game.HasParasols) return;
        if (!game.CanPitchParasol(spot))
        {
            Notice("여기에는 파라솔을 꽂을 수 없다");
            return;
        }
        bool moved = game.PitchParasol(spot);
        Notice(moved ? "파라솔을 옮겨 꽂았다" : "파라솔을 꽂았다 · 그늘에서 더위를 식히자");
    }

    void Collect(BeachFind item)
    {
        inventory.Store(item);
        game.Record(item);
        SyncTools();
    }

    void DrawRing(Vector2 center,float? overrideRadius=null)
    {
        float radius = overrideRadius ?? (Loaded && sand.Height(center)>=BeachSearch.MoundHeight ? Radius/BeachSearch.SpreadFactor : Radius);
        for(int i=0;i<digRing.positionCount;i++){
            float a=i*2*Mathf.PI/digRing.positionCount;
            Vector2 p=center+radius*new Vector2(Mathf.Cos(a),Mathf.Sin(a));
            digRing.SetPosition(i,new Vector3(p.x,sand.Height(p)+.012f,p.y));
        }
    }

    public void SwitchTool()
    {
        for (int step=1;step<=BeachInventory.SlotCount;step++)
        {
            int i=(Selected+step)%BeachInventory.SlotCount;
            if (inventory.Slot(i).Gear==BeachGear.None) continue;
            SelectSlot(i);
            return;
        }
    }

    public void SelectSlot(int index)
    {
        if (!Playing || HoldingBall || (game.Bucket && game.Bucket.Held) || sweeping) return;
        index=(index+BeachInventory.SlotCount)%BeachInventory.SlotCount;
        if (index==Selected) return;
        if (Loaded && !(UsingTrowel && inventory.Slot(index).Shovel))
        {
            Notice("들고 있는 모래를 먼저 내려놓자");
            return;
        }
        Selected=index;
        Signal=null;
        SyncTools();
    }

    public void Dig(Vector2 spot)
    {
        if (!Playing || !(Bare || UsingTrowel || UsingRake)) return;
        if (UsingRake)
        {
            Vector2 feet = new Vector2(player.transform.position.x,player.transform.position.z);
            float distance = Vector2.Distance(feet,spot);
            if (distance>2.1f || distance<.65f) { Notice("발 앞쪽 모래를 조준하자"); return; }
            if (Time.time<nextRake) return;
            nextRake = Time.time+.45f;
            player.Swing();
            game.Rake(spot,(feet-spot).normalized);
            return;
        }
        if (UsingTrowel) { TransferSand(spot,TrowelRadius,TrowelDepth); return; }
        if (Loaded) { Notice("우클릭으로 두 손의 모래 내려놓기"); return; }
        if (sweeping || ShopOpen || HoldingBall) return;
        if (!player.CanReachSand(game.Surface(spot)))
        {
            Notice("손이 닿지 않는다 · 가까이 가거나 Shift로 숙이기");
            return;
        }
        sweepCenter = spot;
        Vector3 right = player.transform.right;
        sweepAcross = new Vector2(right.x,right.z).normalized;
        sweepTime = 0;
        pushedSteps = 0;
        sweeping = true;
        digRing.enabled = false;
        target = null;
    }

    void CancelSweep()
    {
        if (!sweeping) return;
        sweeping = false;
        player.Follow();
    }

    void AdvanceSweep(float seconds)
    {
        Vector3 contact = game.Surface(sweepCenter);
        if (!player.CanReachSand(contact) || Vector3.Angle(player.view.transform.forward,
            contact-player.view.transform.position)>55f)
        {
            CancelSweep();
            return;
        }

        sweepTime += seconds;
        float progress = Mathf.Clamp01((sweepTime-Approach)/Spread);
        int steps = Mathf.Min(SweepSteps,Mathf.FloorToInt(progress*SweepSteps)+1);
        bool changed = false;
        while (sweepTime>=Approach && pushedSteps<steps)
        {
            float distance = Mathf.Lerp(.035f,.29f,(float)pushedSteps/SweepSteps);
            game.SweepStep(sweepCenter,sweepAcross,distance,pushedSteps==0);
            pushedSteps++;
            changed = true;
        }
        if (changed) game.SurfaceWorked();
        float blend = Mathf.SmoothStep(0,1,sweepTime/Approach) *
            (1-Mathf.SmoothStep(0,1,(sweepTime-Approach-Spread)/Return));
        Vector3 across = new Vector3(sweepAcross.x,0,sweepAcross.y);
        float offset = Mathf.Lerp(.035f,.29f,progress);
        for (int side=-1; side<=1; side+=2)
        {
            Vector3 tip = game.Surface(sweepCenter + side*sweepAcross*offset);
            player.ReachWithHand(side,tip,across,blend);
        }
        if (sweepTime>=Approach+Spread+Return) sweeping=false;
    }

    public void HandScoop(Vector2 spot)
    {
        if (!Playing || !Bare) return;
        TransferSand(spot,HandRadius,HandDepth);
    }

    void TransferSand(Vector2 spot,float radius,float depth)
    {
        player.Swing();
        if (Loaded)
        {
            Vector3 source = UsingTrowel ? sandOnBlade.transform.position : sandInHands.transform.position;
            bool onMound = game.PourSand(spot,ref radius,load,contents,scoopCenter,source);
            load = 0;
            Notice(onMound ? "모래가 포개졌다 · 가운데를 맨손으로 헤쳐보기" : "모래를 얇게 펼쳤다");
        }
        else
        {
            if (!CollectSand(spot,radius,depth,UsingTrowel)) return;
            sand.Kick(spot,UsingTrowel ? 22 : 12,radius*.6f,.9f);
            Notice(UsingTrowel ? "한 삽 떴다 · 좌클릭으로 펼치기" : "두 손에 모래를 쥐었다 · 우클릭으로 펼치기");
        }
        SyncCarriedSand();
        game.FinishStroke(spot,radius,UsingTrowel);
    }

    void ScoopIntoBucket(Vector2 spot)
    {
        if (!CollectSand(spot,HandRadius,HandDepth,false)) return;
        sand.Kick(spot,12,HandRadius*.6f,.9f);
        game.Bucket.Fill(load,contents);
        contents.Clear();
        load=0;
        game.FinishStroke(spot,HandRadius,UsingTrowel);
        Notice(game.Bucket.Filled ? "5/5 · 빈 모래밭을 보고 좌클릭" : $"양동이 {game.Bucket.Scoops}/5");
    }

    bool CollectSand(Vector2 spot,float radius,float depth,bool markTrowel)
    {
        if (Equipped.Empty) SelectSlot(0);
        scoopCenter = spot;
        Vector3 forward = player.transform.forward;
        load = game.ScoopSand(spot,radius,depth,contents,markTrowel,new Vector2(forward.x,forward.z));
        if (!Loaded) return false;
        carried = load;
        sandInHands.transform.localScale = Handful;
        return true;
    }

    void SyncCarriedSand()
    {
        sandOnBlade.SetActive(UsingTrowel && Loaded);
        sandInHands.SetActive(!UsingTrowel && Loaded);
        player.CuppedHands = sandInHands.activeSelf;
    }

    public void Take(BeachFind item)
    {
        if(!Playing) return;
        game.RequestTake(item,this);
    }

    // The beach hands over a find this player asked for.
    public void Granted(BeachFind item)
    {
        if(item.kind==FindKind.Needle){game.Win();}
        else {Collect(item);Notice(item.Label+" 주움 · 자루에 넣자");}
    }

    // Held open with the mouse wheel button; the HUD draws it.
    public readonly BeachEmoteWheel Wheel = new();
    float signedAt = -1;

    // A sign to the team: from the wheel when its button is let go, or straight from keys 1-8.
    // Signs come a few times a second at most. Returns true while the wheel is open.
    bool Gestures(Keyboard k,Mouse mouse)
    {
        if (mouse.middleButton.wasPressedThisFrame) Wheel.Show();
        if (Wheel.Open)
        {
            if (!mouse.middleButton.isPressed || mouse.leftButton.wasPressedThisFrame) MakeSign(Wheel.Release());
            else Wheel.Move(mouse.delta.ReadValue());
        }
        player.LookHeld = Wheel.Open;
        if (Wheel.Open) return true;
        for (int i=0; i<BeachBody.Wheel.Length; i++)
            if (k[Key.Digit1+i].wasPressedThisFrame) { MakeSign(BeachBody.Wheel[i]); break; }
        return false;
    }

    void MakeSign(BeachBody.Gesture sign)
    {
        if (!BeachBody.Known(sign) || Time.time-signedAt<.3f) return;
        signedAt = Time.time;
        game.Sign(sign);
    }

    // Co-op only: gets up where they fell. Sand and finds they were carrying drop at their feet.
    public void Revive()
    {
        if (!Collapsed) return;
        if (Loaded)
        {
            Vector3 here = player.transform.position;
            Vector2 feet = sand.ClampToBeach(new Vector2(here.x,here.z),.3f);
            float radius = HandRadius;
            game.PourSand(feet,ref radius,load,contents,scoopCenter,here+Vector3.up*.5f);
            load = 0;
            SyncCarriedSand();
            game.FinishStroke(feet,radius,UsingTrowel);
        }
        if (CarriedSack)
        {
            Vector3 here = player.transform.position;
            game.SetDownSack(CarriedSack,sand.ClampToBeach(new Vector2(here.x,here.z),.3f));
            player.CuppedHands=false;
            SyncTools();
        }
        player.Revive();
        player.Capture(true);
    }

    public void Scan()
    {
        if(!Playing || Equipped.Gear!=BeachGear.Detector || Time.time<nextScan) return;
        nextScan=Time.time+3;
        bool found=Loaded ? contents.Exists(item=>item.IsMetal) : game.MetalAhead(player.transform);
        Signal=(Loaded?"삽 위":"전방")+" · "+(found?"금속 반응":"무반응");
        if(found)game.Beep();
    }

    public void SortLoad()
    {
        if(!Playing || Equipped.Gear!=BeachGear.Magnet) return;
        if(!Loaded){Notice("먼저 모종삽으로 모래를 떠야 한다");return;}
        var iron=contents.Find(f=>f.IsIron);
        if(!iron){Notice("삽 위에 자석에 붙는 물건이 없다");return;}
        if (!inventory.CanStore(iron.kind)) { Notice(HandFull); return; }
        contents.Remove(iron);
        if(iron.kind==FindKind.Needle){game.Win();Notice("자석에 바늘이 붙었다");}
        else{Collect(iron);Notice("자석으로 "+BeachFind.WithObject(iron.Label)+" 건졌다");}
    }

    // Everything brought to the stall: the sack carried here, sacks set down beside it, and the find in hand.
    public int SaleValue
    {
        get
        {
            int gold = inventory.Hand is FindKind held ? BeachFind.PriceOf(held) : 0;
            foreach (var sack in game.SacksAtStall) gold += sack.Value;
            return gold;
        }
    }

    public void Sell()
    {
        if (!Playing || !NearShop) return;
        int gold = 0;
        foreach (var sack in new List<BeachSack>(game.SacksAtStall)) gold += game.SellSack(sack);
        if (inventory.Hand is FindKind held && held!=FindKind.Needle)
        {
            inventory.TakeHand();
            int price = BeachFind.PriceOf(held);
            game.AddEarnings(price);
            gold += price;
            SyncTools();
        }
        Notice(gold>0 ? "판매 완료  +"+gold+" G" : "판매할 물건이 없다 · 자루를 매입소로 가져오자");
        if (gold>0) game.Chime(.7f);
    }

    public void BuySack()
    {
        if (!Playing || !NearShop) return;
        if (!game.BuySack()) { Notice(game.Sacks.Count>=BeachSack.MostSacks ? $"자루는 {BeachSack.MostSacks}개까지 쓸 수 있다" : "골드가 부족하다"); return; }
        game.Chime(.7f);
        Notice("수거 자루를 샀다 · 매입소 앞 모래에 놓였다");
    }

    // A sack on the sand: put the find in hand into it, or pick it up.
    public void UseSack(BeachSack sack)
    {
        if (HoldingFind)
        {
            var kind = inventory.Hand.Value;
            if (!game.PutInSack(sack,kind)) { Notice($"자루가 가득 찼다 · 매입소로 가져가거나 새 자루를 사자"); return; }
            inventory.TakeHand();
            if (heldItem)
            {
                var tossed = heldItem;
                heldItem = null;
                tossed.transform.SetParent(game.transform,true);
                StartCoroutine(DropInto(tossed,sack.transform.position+Vector3.up*.62f));
            }
            SyncTools();
            player.Swing();
            Notice($"{BeachFind.NameWithObject(kind)} 자루에 넣었다 · {sack.Count}/{BeachSack.Capacity}");
            return;
        }
        if (Loaded) { Notice("들고 있는 모래를 먼저 내려놓자"); return; }
        if (!CanLiftSack) return;
        CancelSweep();
        game.LiftSack(sack);
    }

    // The beach has handed this player a sack: both hands go to it.
    public void LiftedSack()
    {
        SelectSlot(0);
        player.CuppedHands=true;
        rake.SetActive(false);
        player.trowel.SetActive(false);
        player.detector.SetActive(false);
        magnet.SetActive(false);
        Notice("자루를 들었다 · 매입소에서 B로 팔고, 수거함에 E로 쓰레기를 넣는다");
    }

    void SortSack(BeachSack sack,BeachBin bin)
    {
        if (!Playing || Vector3.Distance(player.view.transform.position,bin.Opening)>BeachBin.Reach+.45f) return;
        int count = game.SortSack(sack,bin.Kind);
        if (count==0) { Notice($"자루에 {bin.Label}에 넣을 쓰레기가 없다"); return; }
        player.Swing();
        game.Chime(.7f);
        Notice($"{bin.Label}에 {count}개 배출  +{count*BeachInventory.SortedPrice} G");
    }

    public void BuyRake()
    {
        if (!Playing || !NearShop || Loaded || !inventory.BuyRake()) return;
        SelectSlot(inventory.GearSlot(BeachGear.Rake));
        game.Chime(.7f);
        Notice("모래 갈퀴 장착 · 좌클릭으로 표면 훑기");
    }

    // The find in hand, if it is litter, into the bin it belongs in.
    public void Deposit(BeachBin bin)
    {
        if (!Playing || Loaded || HoldingBall || game.Bucket.Held || ShopOpen || !bin || Vector3.Distance(player.view.transform.position,bin.Opening)>BeachBin.Reach+.45f) return;
        if (!(inventory.Hand is FindKind kind) || !BeachFind.IsTrashKind(kind))
        {
            Notice("손에 든 쓰레기가 없다 · 자루를 들고 오면 한 번에 넣는다");
            return;
        }
        if (BeachFind.BinFor(kind)!=bin.Kind)
        {
            Notice("이 물건에 맞는 수거함을 찾아보자");
            return;
        }
        inventory.TakeHand();
        if (heldItem)
        {
            var dropped=heldItem;
            heldItem=null;
            dropped.transform.SetParent(game.transform,true);
            StartCoroutine(DropInto(dropped,bin.Opening));
        }
        SyncTools();
        player.Swing();
        int gold=BeachInventory.SortedPrice;
        game.AddEarnings(gold,1);
        game.Chime(.7f);
        Notice($"{bin.Label}에 배출  +{gold} G");
    }

    System.Collections.IEnumerator DropInto(GameObject item,Vector3 opening)
    {
        Vector3 start=item.transform.position;
        for(float t=0;t<1;t+=Time.deltaTime/.3f)
        {
            item.transform.position=Vector3.Lerp(start,opening-Vector3.up*.17f,t)+Vector3.up*(.32f*Mathf.Sin(t*Mathf.PI));
            yield return null;
        }
        Destroy(item);
    }

    public void BuyParasol(){
        if(!Playing || !NearShop)return;
        if(!inventory.BuyParasol()){Notice(inventory.HasParasol?"파라솔은 이미 샀다":"잔액이 부족하다");return;}
        SelectSlot(inventory.GearSlot(BeachGear.Parasol));
        game.Chime(.7f);
        Notice("휴대용 파라솔 구매 · 모래밭을 보고 T로 꽂기");
    }

    public void Buy(){
        if(!Playing || !NearShop)return;
        if(Loaded){Notice("모래를 내려놓은 뒤 장비를 장착하자");return;}
        string tool=inventory.NextTool;
        if(!inventory.BuyNext()){Notice(inventory.NextPrice==0?"이 해변의 장비를 모두 갖췄다":"잔액이 부족하다");return;}
        SelectSlot(inventory.GearSlot(inventory.HasMagnet ? BeachGear.Magnet : inventory.HasDetector ? BeachGear.Detector : BeachGear.Trowel));
        game.Chime(.7f);Notice(tool+" 장착");
    }
}
