using UnityEngine;

// Another player as seen from here: their figure walking to wherever their machine says they are, holding what
// they hold, with sand in their hands or on the blade, and bent over when they dig.
// Runs after the figure has posed its arms, so the held thing follows the hand.
[DefaultExecutionOrder(100)]
public sealed class BeachGhost : MonoBehaviour
{
    BeachBody body;
    public BeachBody Body => body;
    // Set when the needle is found: arms up with the team, whatever they hold.
    public bool Cheering;
    Vector3 target;
    float yaw;
    int worn=-1;
    bool walking, down;
    BeachSlot holding;
    GameObject held, sand;
    Vector3 heldOffset;
    Quaternion heldTilt = Quaternion.identity;
    bool bothHands;

    public void Wear(int look)
    {
        if(worn==look) return;
        worn=look;
        if(body) { body.gameObject.SetActive(false); Destroy(body.gameObject); }
        body=BeachBody.Create(transform,look);
        Pose();
    }

    public static BeachGhost Create(Transform parent,int id,Vector3 position)
    {
        var ghost = new GameObject("함께 찾는 사람 "+id).AddComponent<BeachGhost>();
        ghost.transform.SetParent(parent,false);
        ghost.transform.position = ghost.target = position;
        // The same figure that player wears on their own screen.
        ghost.body = BeachBody.Create(ghost.transform,id);
        return ghost;
    }

    public void MoveTo(Vector3 position,float heading,bool fallen)
    {
        walking = (position-target).sqrMagnitude>.0004f;
        target = position;
        yaw = heading;
        down = fallen;
    }

    bool loaded, digging;

    // What this player holds, whether they carry sand, and whether they are mid-stroke.
    public void Hold(BeachSlot slot,bool sandLoaded,bool working,BeachSearch beach)
    {
        loaded = sandLoaded;
        digging = working;
        if (!slot.Equals(holding))
        {
            holding = slot;
            if (held) { held.SetActive(false); Destroy(held); held = null; }
            Choose(slot,beach);
        }
        if (loaded && !sand)
        {
            sand = Handful(beach);
            sand.transform.SetParent(transform,true);
        }
        if (sand) sand.SetActive(loaded);
        Pose();
    }

    // How each thing sits in the hand: its longest side, where it is held from, and how it is tilted.
    void Choose(BeachSlot slot,BeachSearch beach)
    {
        bothHands = slot.Gear is BeachGear.Ball or BeachGear.Bucket or BeachGear.Sack;
        float longest = .3f;
        heldOffset = Vector3.zero;
        heldTilt = Quaternion.identity;
        switch (slot.Gear)
        {
            case BeachGear.None when slot.Find.HasValue: longest = .45f; heldOffset = new Vector3(0,0,.06f); break;
            case BeachGear.None: case BeachGear.Hands: return;
            case BeachGear.Trowel: longest = .32f; heldOffset = new Vector3(0,-.1f,.08f); heldTilt = Quaternion.Euler(70,0,0); break;
            case BeachGear.Magnet: longest = .26f; heldOffset = new Vector3(0,-.08f,.08f); heldTilt = Quaternion.Euler(70,0,0); break;
            case BeachGear.Rake: longest = .45f; heldOffset = new Vector3(0,-.12f,.1f); heldTilt = Quaternion.Euler(60,0,0); break;
            case BeachGear.Detector: longest = 1.05f; heldOffset = new Vector3(0,-.35f,.25f); heldTilt = Quaternion.Euler(35,0,0); break;
            case BeachGear.Parasol: longest = 1.25f; heldOffset = new Vector3(0,.25f,0); break;
            case BeachGear.Bucket: longest = .34f; heldOffset = new Vector3(0,-.05f,.3f); break;
            case BeachGear.Ball: longest = .28f; heldOffset = new Vector3(0,0,.3f); break;
            case BeachGear.Sack: longest = .55f; heldOffset = new Vector3(0,-.18f,.3f); break;
        }
        var me = beach ? beach.Local : null;
        if (!me || !me.Models) return;
        var source = me.ModelFor(slot);
        if (!source) return;
        held = me.Models.Carried(slot,source,transform,longest);
        if (held) held.name = "든 것 · "+slot.Label;
    }

    static GameObject Handful(BeachSearch beach)
    {
        var lump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        lump.name = "든 모래";
        Destroy(lump.GetComponent<Collider>());
        lump.transform.localScale = new Vector3(.15f,.09f,.13f);
        if (beach && beach.sand) lump.GetComponent<Renderer>().sharedMaterial = beach.sand.GetComponent<Renderer>().sharedMaterial;
        lump.SetActive(false);
        return lump;
    }

    void Pose()
    {
        if (!body) return;
        bool inHand = !holding.Empty && holding.Gear!=BeachGear.Hands;
        body.Carrying = inHand;
        // Sand carried in bare hands is held before the chest, like the ball.
        body.BothHands = bothHands || (loaded && !inHand);
        body.Digging = digging;
    }

    void Update()
    {
        // Positions arrive some fifteen times a second; ease between them.
        float ease = 1-Mathf.Exp(-14f*Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position,target,ease);
        transform.rotation = Quaternion.Slerp(transform.rotation,Quaternion.Euler(0,yaw,0),ease);
        if (body) body.Play(down ? BeachBody.Act.Fall : Cheering ? BeachBody.Act.Cheer : walking ? BeachBody.Act.Walk : BeachBody.Act.Idle);
    }

    void LateUpdate()
    {
        if (Cheering)
        {
            if (held) held.SetActive(false);
            if (sand) sand.SetActive(false);
            return;
        }
        if (!held && !(sand && sand.activeSelf)) return;
        Vector3 grip = Grip();
        Quaternion facing = transform.rotation;
        if (held)
        {
            held.SetActive(!down);
            held.transform.SetPositionAndRotation(grip+facing*heldOffset,facing*heldTilt);
        }
        if (sand && sand.activeSelf)
        {
            // On the blade's tip when a tool carries it, otherwise cupped in both hands.
            bool onBlade = held && !bothHands;
            Vector3 lump = onBlade ? held.transform.position+facing*new Vector3(0,.01f,.05f) : grip+facing*new Vector3(0,.02f,.06f);
            sand.transform.SetPositionAndRotation(lump,facing);
            sand.transform.localScale = onBlade ? new Vector3(.1f,.05f,.09f) : new Vector3(.15f,.09f,.13f);
        }
    }

    // Where the hands are: the right hand, between both hands, or roughly there on a figure without arm bones.
    Vector3 Grip()
    {
        var right = body ? body.RightHand : null;
        if (!right) return transform.position+transform.rotation*new Vector3(.22f,.85f,.28f);
        bool bareHanded = !holding.Find.HasValue && (holding.Gear is BeachGear.None or BeachGear.Hands);
        if (!bothHands && !(loaded && bareHanded)) return right.position;
        var left = body.LeftHand;
        return left ? (right.position+left.position)*.5f : right.position;
    }

    void OnDestroy()
    {
        if (sand) Destroy(sand);
    }
}
