using System.Collections.Generic;
using UnityEngine;

// A cleanup sack. Finds picked up go into it, a hand at a time; it sits on the sand where it was set down,
// or is carried in both hands, slower the fuller it gets, to the stall to sell and to the bins to sort.
// The team starts each beach with one and can buy more. What is in a sack belongs to the whole team.
public sealed class BeachSack : MonoBehaviour
{
    public const int Capacity = 12, Price = 3, MostSacks = 4;
    // How far away a sack can be reached with E.
    public const float Reach = 2.2f;
    // A sack set down this close to the stall counts as brought to it.
    public const float AtStall = 3.2f;

    readonly Dictionary<FindKind,int> contents = new();
    public IReadOnlyDictionary<FindKind,int> Contents => contents;
    public int Count { get; private set; }
    public bool Full => Count>=Capacity;
    // Everything in it at the stall's prices.
    public int Value { get; private set; }
    // Player number of whoever carries it, -1 while it sits on the sand.
    public int Carrier { get; private set; } = -1;
    public bool Carried => Carrier>=0;
    public int Index { get; private set; }
    public Transform Model => body;
    // Walking speed while carrying it: full speed empty, 60% full.
    public float Burden => Mathf.Lerp(1f,.6f,Count/(float)Capacity);

    BeachSearch beach;
    Transform body;
    Collider hit;
    static Mesh shape;
    static Material cloth;

    public static BeachSack Create(BeachSearch beach,int index,Vector3 position)
    {
        var sack = new GameObject("수거 자루 "+(index+1)).AddComponent<BeachSack>();
        sack.transform.SetParent(beach.transform,false);
        sack.transform.position = position;
        sack.beach = beach;
        sack.Index = index;
        sack.Build();
        beach.sand.SurfaceChanged += sack.Settle;
        return sack;
    }

    void Build()
    {
        body = new GameObject("자루 모양").transform;
        body.SetParent(transform,false);
        // Gunny Sack by Oliver Herklotz (CC BY 3.0, see Wildlife-Credits.txt); the turned sack if it is missing.
        var imported = Resources.Load<GameObject>("CleanupSack/GunnySack");
        var filter = imported ? imported.GetComponentInChildren<MeshFilter>() : null;
        body.gameObject.AddComponent<MeshFilter>().sharedMesh = filter ? filter.sharedMesh : shape ??= Lathe();
        body.gameObject.AddComponent<MeshRenderer>().sharedMaterial = filter ? burlap ??= Burlap() : cloth ??= Cloth();
        var box = gameObject.AddComponent<CapsuleCollider>();
        box.center = Vector3.up*.27f;
        box.radius = .28f;
        box.height = .56f;
        hit = box;
        Swell();
    }

    // A burlap sack: a bulging body, a cinched neck and a ruffled top, turned about its axis.
    static Mesh Lathe()
    {
        Vector2[] profile = {
            new(0,0), new(.15f,.005f), new(.2f,.06f), new(.215f,.18f), new(.2f,.3f), new(.14f,.38f),
            new(.06f,.43f), new(.045f,.46f), new(.08f,.5f), new(.05f,.52f), new(0,.51f) };
        const int around = 14;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int ring=0; ring<profile.Length; ring++)
        for (int a=0; a<=around; a++)
        {
            float angle = a*Mathf.PI*2/around;
            // A little lumpiness, so it reads as cloth with things inside.
            float lump = ring>0 && ring<6 ? 1f+.06f*Mathf.Sin(angle*3+ring) : 1f;
            vertices.Add(new Vector3(Mathf.Cos(angle)*profile[ring].x*lump,profile[ring].y,Mathf.Sin(angle)*profile[ring].x*lump));
        }
        for (int ring=0; ring<profile.Length-1; ring++)
        for (int a=0; a<around; a++)
        {
            int i = ring*(around+1)+a, j = i+around+1;
            triangles.AddRange(new[]{i,j,i+1, i+1,j,j+1});
        }
        var mesh = new Mesh { name = "수거 자루" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Material burlap;

    static Material Burlap()
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "마대 자루" };
        var palette = Resources.Load<Texture2D>("CleanupSack/GunnySackPalette");
        if (palette)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap",palette);
            material.mainTexture = palette;
        }
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness",.05f);
        return material;
    }

    static Material Cloth()
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "자루 천" };
        var burlap = new Color(.72f,.6f,.4f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor",burlap);
        material.color = burlap;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness",.05f);
        return material;
    }

    // Fuller sacks are fatter.
    void Swell()
    {
        float fill = Count/(float)Capacity;
        body.localScale = new Vector3(.8f+.3f*fill,.85f+.2f*fill,.8f+.3f*fill);
    }

    public bool Is(Collider other) => other && other==hit;

    // ---- Contents ----

    // `force` lets a find in even when full, for a change another player already made.
    public bool Add(FindKind kind,bool force = false)
    {
        if (kind==FindKind.Needle || (Full && !force)) return false;
        contents.TryGetValue(kind,out int count);
        contents[kind] = count+1;
        Count++;
        Value += BeachFind.PriceOf(kind);
        Swell();
        return true;
    }

    bool Remove(FindKind kind,int count)
    {
        if (!contents.TryGetValue(kind,out int held) || held<count) return false;
        if (held==count) contents.Remove(kind); else contents[kind] = held-count;
        Count -= count;
        Value -= BeachFind.PriceOf(kind)*count;
        Swell();
        return true;
    }

    // Everything goes to the stall. Returns the gold.
    public int SellAll()
    {
        int gold = Value;
        contents.Clear();
        Count = Value = 0;
        Swell();
        return gold;
    }

    // The litter that belongs in `bin` goes in. Returns how many.
    public int Sort(WasteBin bin)
    {
        int sorted = 0;
        foreach (var kind in BeachFind.Trash)
            if (BeachFind.BinFor(kind)==bin && contents.TryGetValue(kind,out int count) && Remove(kind,count))
                sorted += count;
        return sorted;
    }

    // One `kind` out, as another player's gull took it.
    public bool TakeOne(FindKind kind) => Remove(kind,1);

    // Takes one of the most valuable things out, for a gull. Null when there is nothing worth taking.
    public FindKind? TakeShiniest()
    {
        FindKind? best = null;
        foreach (var pair in contents)
            if (!BeachFind.IsTrashKind(pair.Key) && (!best.HasValue || BeachFind.PriceOf(pair.Key)>BeachFind.PriceOf(best.Value)))
                best = pair.Key;
        if (best.HasValue) Remove(best.Value,1);
        return best;
    }

    public void Clear()
    {
        contents.Clear();
        Count = Value = 0;
        Swell();
    }

    // ---- Where it is ----

    // Picked up by player `who`. This player's own sack is held before them; anyone else's is drawn by their figure.
    public void Lift(int who)
    {
        Carrier = who;
        hit.enabled = false;
        if (who==beach.MyId)
        {
            body.gameObject.SetActive(true);
            transform.SetParent(beach.player.view.transform,false);
            transform.localPosition = new Vector3(0,-.46f,.6f);
            transform.localRotation = Quaternion.identity;
        }
        else
        {
            // Hidden here and kept at the carrier's feet, so it drops where they stand if they leave.
            transform.SetParent(beach.transform,true);
            body.gameObject.SetActive(false);
        }
    }

    void LateUpdate()
    {
        if (!Carried || Carrier==beach.MyId || beach.Team==null) return;
        if (beach.Team().TryGetValue(Carrier,out var figure) && figure) transform.position = figure.transform.position;
    }

    public void SetDown(Vector3 ground)
    {
        Carrier = -1;
        transform.SetParent(beach.transform,true);
        transform.SetPositionAndRotation(ground,Quaternion.identity);
        body.gameObject.SetActive(true);
        hit.enabled = true;
    }

    public bool CanSetDown(Vector2 spot)
    {
        if (!beach.sand.Contains(spot,.25f)) return false;
        Vector3 ground = beach.Surface(spot);
        return !Physics.CheckSphere(ground+Vector3.up*.3f,.2f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
    }

    // Close enough to the stall to sell from where it stands.
    public bool NearStall => !Carried && Vector3.Distance(transform.position,beach.stall.position)<AtStall;

    void Settle()
    {
        if (Carried || !beach) return;
        Vector3 p = transform.position;
        p.y = beach.sand.Height(new Vector2(p.x,p.z));
        transform.position = p;
    }

    void OnDestroy()
    {
        if (beach && beach.sand) beach.sand.SurfaceChanged -= Settle;
    }
}
