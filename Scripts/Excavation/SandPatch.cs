using UnityEngine;
using UnityEngine.Rendering;

public sealed class SandPatch : MonoBehaviour
{
    public GameObject preview;
    public Vector2 HalfSize { get; private set; }
    public event System.Action SurfaceChanged;
    public float CellArea => Step*Step;
    int columns, rows;
    const float Step = 10f/160f;
    SandGrid grid;
    Vector3[] points, normals;
    Color32[] tint;
    SandGrains grains;
    float drying, slideSound;
    // Raised while sand is sliding, with a point on the slope.
    public event System.Action<Vector3> Sliding;
    Mesh mesh;
    Material sand;
    public Material surfaceMaterial;
    Texture2D grain;
    SandTracks tracks;

    public Vector2 ClampToBeach(Vector2 p,float inset)
    {
        return new Vector2(Mathf.Clamp(p.x,-HalfSize.x+inset,HalfSize.x-inset),
            Mathf.Clamp(p.y,-HalfSize.y+inset,HalfSize.y-inset));
    }

    public bool Contains(Vector2 p,float inset=0) =>
        Mathf.Abs(p.x)<=HalfSize.x-inset && Mathf.Abs(p.y)<=HalfSize.y-inset;

    public void Initialize(Vector2 metres)
    {
        columns=Mathf.RoundToInt(metres.x/Step);
        rows=Mathf.RoundToInt(metres.y/Step);
        grid = new SandGrid(columns,rows,Step);
        HalfSize = grid.HalfSize;
        points = new Vector3[(columns+1)*(rows+1)];
        normals = new Vector3[points.Length];
        tint = new Color32[points.Length];
        if (preview) preview.SetActive(false);
        var triangles = new int[columns * rows * 6];
        var uv = new Vector2[points.Length];
        int t = 0;
        for (int z = 0; z <= rows; z++)
        for (int x = 0; x <= columns; x++)
        {
            int i = Index(x, z);
            points[i] = new Vector3(x * Step - HalfSize.x, 0, z * Step - HalfSize.y);
            normals[i] = Vector3.up;
            uv[i] = new Vector2((float)x/columns,(float)z/rows);
            if (x == columns || z == rows) continue;
            int next = i + columns + 1;
            triangles[t++] = i; triangles[t++] = next; triangles[t++] = i + 1;
            triangles[t++] = i + 1; triangles[t++] = next; triangles[t++] = next + 1;
        }
        mesh = new Mesh { name = "Diggable sand" };
        if (points.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.MarkDynamic();
        mesh.vertices = points;
        mesh.uv = uv;
        mesh.colors32 = tint;
        mesh.triangles = triangles;
        mesh.normals = normals;
        // Fixed and generous, so a moving surface never has to remeasure itself.
        mesh.bounds = new Bounds(new Vector3(0,.5f,0),new Vector3(2*HalfSize.x,3f,2*HalfSize.y));
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        if (surfaceMaterial)
        {
            sand = new Material(surfaceMaterial);
            sand.mainTextureScale = Vector2.Scale(sand.mainTextureScale,HalfSize/5f);
            mesh.RecalculateTangents();
        }
        else
        {
            sand = new Material(GraphicsSettings.currentRenderPipeline.defaultMaterial);
            sand.color = Color.white;
            grain = new Texture2D(256, 256, TextureFormat.RGB24, true);
            var pixels = new Color32[256 * 256];
            var random = new System.Random(41);
            for (int i = 0; i < pixels.Length; i++)
            {
                float ripple = Mathf.Sin(i / 256 * .19f + Mathf.Sin(i % 256 * .045f));
                float v = (float)random.NextDouble() * .11f + ripple * .023f;
                pixels[i] = new Color(.86f + v, .73f + v, .57f + v);
            }
            grain.SetPixels32(pixels);
            grain.Apply();
            sand.mainTexture = grain;
            sand.mainTextureScale = 13 * HalfSize/5f;
            sand.SetFloat("_Smoothness", .08f);
        }
        gameObject.AddComponent<MeshRenderer>().sharedMaterial = sand;
        tracks = new SandTracks(transform,mesh,HalfSize,Resources.Load<Shader>("SandTracks"));
        grains = new SandGrains(transform,sand);
    }

    void Update()
    {
        if (grid==null) return;
        grains.Advance(Time.deltaTime);
        bool moved = false;
        Vector2 slide = default;
        // Two passes a frame lets a collapse take a visible moment instead of snapping.
        for (int pass=0; pass<2 && grid.Unsettled; pass++)
            if (grid.Settle(out var centre)) { moved = true; slide = centre; }
        drying += Time.deltaTime;
        bool dried = false;
        if (drying>=.25f) { dried = grid.Dry(drying); drying = 0; }
        slideSound -= Time.deltaTime;
        if (moved)
        {
            tracks.Clear(slide,.22f);
            Vector3 slope = new Vector3(slide.x,grid.Height(slide),slide.y);
            grains.Kick(slope,2,.16f,.35f);
            if (slideSound<=0) { slideSound = .35f; Sliding?.Invoke(slope); }
            Refresh();
        }
        // Drying fades the whole beach, which the block redraw above does not cover.
        if (dried) Paint();
    }

    void Paint()
    {
        var damp = grid.Damp;
        for (int i=0; i<tint.Length; i++) tint[i].r = (byte)(damp[i]*255f);
        mesh.SetColors(tint,0,tint.Length,Quiet);
    }

    const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

    // Moves only the block of vertices the sand changed, and relights it with its one-cell rim.
    void Reshape(int x0,int x1,int z0,int z1)
    {
        var heights = grid.Heights;
        var damp = grid.Damp;
        for (int z=z0; z<=z1; z++)
        for (int x=x0; x<=x1; x++)
        {
            int i = Index(x,z);
            points[i].y = heights[i];
            tint[i].r = (byte)(damp[i]*255f);
        }
        x0 = Mathf.Max(0,x0-1); x1 = Mathf.Min(columns,x1+1);
        z0 = Mathf.Max(0,z0-1); z1 = Mathf.Min(rows,z1+1);
        for (int z=z0; z<=z1; z++)
        for (int x=x0; x<=x1; x++)
        {
            int west = Index(Mathf.Max(0,x-1),z), east = Index(Mathf.Min(columns,x+1),z);
            int south = Index(x,Mathf.Max(0,z-1)), north = Index(x,Mathf.Min(rows,z+1));
            float alongX = (heights[east]-heights[west])/((east-west)*Step);
            float alongZ = (heights[north]-heights[south])/((north-south)/(columns+1)*Step);
            normals[Index(x,z)] = new Vector3(-alongX,1,-alongZ).normalized;
        }
        mesh.SetVertices(points,0,points.Length,Quiet);
        mesh.SetNormals(normals,0,normals.Length,Quiet);
        mesh.SetColors(tint,0,tint.Length,Quiet);
    }

    // Grains thrown up from the surface around `spot`.
    public void Kick(Vector2 spot,int count,float radius,float speed) =>
        grains.Kick(new Vector3(spot.x,grid.Height(spot),spot.y),count,radius,speed);

    // Sand falling from `from` onto `spot` for `seconds`.
    public void Fall(Vector3 from,Vector2 spot,float seconds,float rate,float spread) =>
        grains.Fall(from,new Vector3(spot.x,grid.Height(spot),spot.y),seconds,rate,spread);

    // A shallow print. Walking alone never digs below `-1.4 cm`, so it cannot uncover anything buried.
    public void Footprint(Vector2 foot,Vector2 forward)
    {
        grid.Scrape(foot+forward*.05f,.085f,.011f,-.014f,false);
        grid.Scrape(foot-forward*.05f,.085f,.011f,-.014f,false);
        tracks.Foot(foot,forward);
    }

    int Index(int x, int z) => z * (columns + 1) + x;

    public float Scrape(Vector2 center, float radius, float depth,bool clearTracks=true)
    {
        float removed = grid.Scrape(center,radius,depth);
        if (removed>0 && clearTracks) tracks.Clear(center,radius);
        return removed;
    }

    public void PushWithPalm(Vector2 finger, Vector2 outward)
    {
        float removed = Scrape(finger,.17f,.025f,false);
        if (removed <= 0f) return;
        Vector2 mound = finger + outward*.12f;
        mound = ClampToBeach(mound,Step);
        Pour(mound,removed,.16f);
        tracks.Fingers(finger,outward);
    }

    public void MarkTrowel(Vector2 centre,Vector2 forward) => tracks.Blade(centre,forward);

    public void Pour(Vector2 center, float removed, float radius)
    {
        if (removed<=0) return;
        tracks.Clear(center,radius);
        grid.Pour(center,removed,radius);
    }

    public bool Aim(Ray ray, out Vector2 hit)
    {
        hit = default;
        if (ray.direction.y >= 0) return false;
        const float reach = 2.7f;
        float previous = 0;
        for (float t = .04f; t <= reach; t += .04f)
        {
            Vector3 p = ray.GetPoint(t);
            hit = new Vector2(p.x,p.z);
            if (Contains(hit,.09f) && p.y <= Height(hit))
            {
                float low = previous, high = t;
                for (int i=0;i<7;i++)
                {
                    float mid = (low+high)*.5f;
                    Vector3 q = ray.GetPoint(mid);
                    if (q.y > Height(new Vector2(q.x,q.z))) low = mid; else high = mid;
                }
                p = ray.GetPoint(high);
                hit = new Vector2(p.x,p.z);
                return true;
            }
            previous = t;
        }
        return false;
    }

    public void Refresh()
    {
        if (grid.TakeChanged(out int x0,out int x1,out int z0,out int z1)) Reshape(x0,x1,z0,z1);
        tracks.Flush();
        SurfaceChanged?.Invoke();
    }

    public float Height(Vector2 position) => grid.Height(position);

    // The surface as numbers, for handing a whole beach to another player.
    public SandGrid Grid => grid;
    public float[] Heights => grid.Heights;
    public float[] Damp => grid.Damp;

    public void Restore(float[] heights,float[] damp)
    {
        grid.Restore(heights,damp);
        Refresh();
    }

    void OnDestroy()
    {
        tracks?.Dispose();
        Destroy(mesh);
        Destroy(sand);
        Destroy(grain);
    }
}
