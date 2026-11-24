using UnityEngine;

// A short length of discarded net leads from the search area to the turtle at the waterline.
public sealed class BeachTurtle : MonoBehaviour
{
    public enum Stage : byte { Entangled, Released, Collected }
    public Stage Current { get; private set; }
    GameObject web;
    public Transform NetModel => web.transform;
    Transform animal;
    BeachSearch beach;
    MaterialPropertyBlock tint;
    Vector3 shore;
    float swimTime;
    readonly Color cord = new Color(.13f,.34f,.31f);

    public void Initialize(BeachSearch game)
    {
        beach = game;
        tint = new MaterialPropertyBlock();
        Vector2 spot = new Vector2(1.9f,game.sand.HalfSize.y-.8f);
        shore = game.Surface(spot);
        transform.position = shore;
        animal = new GameObject("바다거북").transform;
        animal.SetParent(transform,false);
        Color olive = new Color(.37f,.49f,.27f);
        Oval("등딱지",new Vector3(0,.17f,0),new Vector3(.65f,.32f,.88f),new Color(.24f,.34f,.16f));
        Oval("머리",new Vector3(0,.18f,.49f),new Vector3(.22f,.21f,.29f),olive);
        for (int side=-1; side<=1; side+=2)
        {
            Oval("앞지느러미",new Vector3(side*.34f,.08f,.19f),new Vector3(.43f,.07f,.17f),olive).localRotation = Quaternion.Euler(0,-side*32,0);
            Oval("뒷지느러미",new Vector3(side*.23f,.07f,-.3f),new Vector3(.22f,.07f,.18f),olive);
            Oval("눈",new Vector3(side*.089f,.214f,.55f),Vector3.one*.042f,Color.black);
        }
        web = new GameObject("버려진 어망");
        web.transform.SetParent(transform,false);
        BeachFind.AttachLitterModel(FindKind.FishingNet,web.transform);
        // Loose leads follow the terrain from the net into the search area.
        for (int i=0;i<3;i++)
        {
            var points = new Vector3[16];
            for (int j=0;j<points.Length;j++)
            {
                float t=j/15f;
                Vector2 along = spot+new Vector2(-.28f-.95f*t+.18f*Mathf.Sin(8*t)+i*.042f,-.23f-2.5f*t);
                points[j] = game.Surface(along)-shore+Vector3.up*.026f;
            }
            Strand(points);
        }
    }

    Transform Oval(string label,Vector3 position,Vector3 scale,Color color)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        part.name = label;
        part.transform.SetParent(animal,false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Collider>().enabled = false;
        var renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = beach.digRing.sharedMaterial;
        Paint(renderer,color);
        return part.transform;
    }

    void Paint(Renderer renderer,Color color)
    {
        tint.SetColor("_BaseColor",color);
        tint.SetColor("_Color",color);
        renderer.SetPropertyBlock(tint);
    }

    void Strand(params Vector3[] points)
    {
        var line = new GameObject("그물코").AddComponent<LineRenderer>();
        line.transform.SetParent(web.transform,false);
        line.useWorldSpace = false;
        line.positionCount = points.Length;
        line.widthMultiplier = .018f;
        line.sharedMaterial = beach.digRing.sharedMaterial;
        line.startColor = line.endColor = cord;
        Paint(line,cord);
        line.SetPositions(points);
    }

    public bool InReach(Ray ray,float visibleDistance)
    {
        if (Current==Stage.Collected) return false;
        Vector3 delta = shore + Vector3.up*(Current==Stage.Entangled ? .18f : .05f) - ray.origin;
        float depth = Vector3.Dot(ray.direction,delta);
        return depth>0 && depth<=2.3f && depth<=visibleDistance+.12f && (delta-depth*ray.direction).sqrMagnitude<.30f*.30f;
    }

    public void SetStage(Stage stage,bool animate)
    {
        if (Current==stage) return;
        bool releasing = Current==Stage.Entangled && stage==Stage.Released;
        Current = stage;
        web.SetActive(stage!=Stage.Collected);
        web.transform.localScale = stage==Stage.Entangled ? Vector3.one : new Vector3(.85f,.16f,.85f);
        if (stage==Stage.Entangled)
        {
            animal.localPosition = Vector3.zero;
            animal.gameObject.SetActive(true);
        }
        else if (!animate) animal.gameObject.SetActive(false);
        else if (releasing) swimTime = 0;
    }

    void Update()
    {
        if (Current==Stage.Entangled || !animal.gameObject.activeSelf) return;
        swimTime += Time.deltaTime;
        animal.localPosition = new Vector3(.025f*Mathf.Sin(swimTime*5f),-.12f*Mathf.Max(0,swimTime-4.5f),.9f*swimTime);
        if (swimTime>=7f) animal.gameObject.SetActive(false);
    }
}
