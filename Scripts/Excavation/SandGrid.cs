using UnityEngine;

// Heights and dampness of the diggable sand. No scene objects, so the rules can be tested on their own.
public sealed class SandGrid
{
    public const float Repose = .65f;      // tan(33 degrees): the steepest slope dry sand holds
    public const float DryingSeconds = 45f;
    const float Still = .0005f;            // transfers smaller than this end the slide
    const float DampDepth = .04f;          // losing this much height exposes fully damp sand

    public readonly int Columns, Rows;
    public readonly float Step;
    public readonly Vector2 HalfSize;
    public readonly float[] Heights, Damp;

    // Cells touched since the sand last came to rest.
    int minX, maxX, minZ, maxZ;
    public bool Unsettled => minX <= maxX;
    public bool AnyDamp { get; private set; }

    // Cells whose height or dampness changed since the renderer last asked.
    int drawnMinX, drawnMaxX, drawnMinZ, drawnMaxZ;

    // The surface in square tiles of cells, for sending only the worked parts to other players.
    public const int Tile = 16;
    public readonly int TilesAcross, TileCount;
    readonly bool[] unsent;

    public SandGrid(int columns,int rows,float step)
    {
        Columns = columns;
        Rows = rows;
        Step = step;
        HalfSize = new Vector2(columns,rows)*(step*.5f);
        Heights = new float[(columns+1)*(rows+1)];
        Damp = new float[Heights.Length];
        TilesAcross = columns/Tile+1;
        TileCount = TilesAcross*(rows/Tile+1);
        unsent = new bool[TileCount];
        Rest();
        Drawn();
    }

    public int Index(int x,int z) => z*(Columns+1)+x;
    public Vector2 Position(int x,int z) => new Vector2(x*Step-HalfSize.x,z*Step-HalfSize.y);
    bool Interior(int x,int z) => x>0 && z>0 && x<Columns && z<Rows;

    void Rest() { minX = minZ = int.MaxValue; maxX = maxZ = int.MinValue; }
    void Drawn() { drawnMinX = drawnMinZ = int.MaxValue; drawnMaxX = drawnMaxZ = int.MinValue; }

    void Touch(int x,int z)
    {
        if (x<minX) minX = x;
        if (x>maxX) maxX = x;
        if (z<minZ) minZ = z;
        if (z>maxZ) maxZ = z;
        if (x<drawnMinX) drawnMinX = x;
        if (x>drawnMaxX) drawnMaxX = x;
        if (z<drawnMinZ) drawnMinZ = z;
        if (z>drawnMaxZ) drawnMaxZ = z;
        unsent[z/Tile*TilesAcross+x/Tile] = true;
    }

    // True once for each tile changed since it was last asked about.
    public bool TakeUnsent(int tile)
    {
        bool changed = unsent[tile];
        unsent[tile] = false;
        return changed;
    }

    // Overwrites one cell with another player's value for it.
    public void SetCell(int x,int z,float height,float damp)
    {
        int i = Index(x,z);
        if (Heights[i]==height && Damp[i]==damp) return;
        Heights[i] = height;
        Damp[i] = damp;
        if (damp>0) AnyDamp = true;
        Touch(x,z);
    }

    // Hands the renderer the block of cells changed since the last call, then forgets it.
    // Returns false when nothing changed. Drying is not included: it fades the whole beach.
    public bool TakeChanged(out int x0,out int x1,out int z0,out int z1)
    {
        x0 = drawnMinX; x1 = drawnMaxX; z0 = drawnMinZ; z1 = drawnMaxZ;
        bool any = drawnMinX<=drawnMaxX;
        Drawn();
        return any;
    }

    // Replaces the whole surface, as when a guest receives the host's beach. Everything counts as
    // changed and disturbed afterwards, so it is redrawn and any slope still sliding carries on.
    public void Restore(float[] heights,float[] damp)
    {
        System.Array.Copy(heights,Heights,Heights.Length);
        System.Array.Copy(damp,Damp,Damp.Length);
        AnyDamp = false;
        foreach (float d in Damp) if (d>0) { AnyDamp = true; break; }
        minX = minZ = 1; maxX = Columns-1; maxZ = Rows-1;
        drawnMinX = drawnMinZ = 0; drawnMaxX = Columns; drawnMaxZ = Rows;
    }

    public float Total
    {
        get
        {
            double sum = 0;
            foreach (float h in Heights) sum += h;
            return (float)sum;
        }
    }

    // Removes a bowl of sand and returns how much came out. Nothing is dug below `floor`.
    // Pressing sand down (a footprint) uncovers nothing damp, so it passes `expose: false`.
    public float Scrape(Vector2 center,float radius,float depth,float floor=-.50f,bool expose=true)
    {
        float removed = 0f;
        int span = Mathf.CeilToInt(radius/Step);
        int cx = Mathf.RoundToInt((center.x+HalfSize.x)/Step);
        int cz = Mathf.RoundToInt((center.y+HalfSize.y)/Step);
        for (int z=Mathf.Max(1,cz-span); z<=Mathf.Min(Rows-1,cz+span); z++)
        for (int x=Mathf.Max(1,cx-span); x<=Mathf.Min(Columns-1,cx+span); x++)
        {
            float weight = 1-(Position(x,z)-center).sqrMagnitude/(radius*radius);
            if (weight<=0) continue;
            int i = Index(x,z);
            float before = Heights[i];
            float after = Mathf.Min(before,Mathf.Max(floor,before-depth*weight*weight));
            if (after>=before) continue;
            Heights[i] = after;
            removed += before-after;
            if (expose) Expose(i,before-after);
            Touch(x,z);
        }
        return removed;
    }

    // Spreads `amount` (the unit Scrape returns) as a mound.
    public void Pour(Vector2 center,float amount,float radius)
    {
        if (amount<=0) return;
        int cx = Mathf.RoundToInt((center.x+HalfSize.x)/Step);
        int cz = Mathf.RoundToInt((center.y+HalfSize.y)/Step);
        int span = Mathf.CeilToInt(radius/Step);
        float total = 0;
        for (int pass=0; pass<2; pass++)
        for (int z=Mathf.Max(1,cz-span); z<=Mathf.Min(Rows-1,cz+span); z++)
        for (int x=Mathf.Max(1,cx-span); x<=Mathf.Min(Columns-1,cx+span); x++)
        {
            float w = Mathf.Max(0,1-(Position(x,z)-center).sqrMagnitude/(radius*radius));
            if (pass==0) { total += w*w; continue; }
            if (total<=0 || w<=0) continue;
            int i = Index(x,z);
            Heights[i] += amount*w*w/total;
            // Freshly dug sand lands damp.
            if (.55f*w>Damp[i]) { Damp[i] = .55f*w; AnyDamp = true; }
            Touch(x,z);
        }
    }

    void Expose(int i,float lost)
    {
        Damp[i] = Mathf.Min(1f,Damp[i]+lost/DampDepth);
        AnyDamp = true;
    }

    // One sliding pass over the disturbed cells. Returns true while sand is still moving.
    // `slide` receives the centre of this pass's movement.
    public bool Settle(out Vector2 slide)
    {
        slide = default;
        if (!Unsettled) return false;
        int x0 = Mathf.Max(1,minX-1), x1 = Mathf.Min(Columns-1,maxX+1);
        int z0 = Mathf.Max(1,minZ-1), z1 = Mathf.Min(Rows-1,maxZ+1);
        Rest();
        float straight = Repose*Step, diagonal = straight*1.41421356f;
        for (int z=z0; z<=z1; z++)
        for (int x=x0; x<=x1; x++)
        {
            int i = Index(x,z);
            for (int dz=-1; dz<=1; dz++)
            for (int dx=-1; dx<=1; dx++)
            {
                if ((dx==0 && dz==0) || !Interior(x+dx,z+dz)) continue;
                int j = Index(x+dx,z+dz);
                float excess = Heights[i]-Heights[j]-(dx!=0 && dz!=0 ? diagonal : straight);
                if (excess<=Still) continue;
                float moved = excess*.2f;
                Heights[i] -= moved;
                Heights[j] += moved;
                Expose(i,moved);
                Touch(x,z);
                Touch(x+dx,z+dz);
            }
        }
        if (!Unsettled) return false;
        slide = (Position(minX,minZ)+Position(maxX,maxZ))*.5f;
        return true;
    }

    public bool Settle() => Settle(out _);

    // Returns true when any cell's dampness changed.
    public bool Dry(float seconds)
    {
        if (!AnyDamp) return false;
        float step = seconds/DryingSeconds;
        bool left = false;
        for (int i=0; i<Damp.Length; i++)
        {
            if (Damp[i]<=0) continue;
            Damp[i] = Mathf.Max(0f,Damp[i]-step);
            left |= Damp[i]>0;
        }
        AnyDamp = left;
        return true;
    }

    // Interpolate the same triangle the renderer uses for this point.
    public float Height(Vector2 position)
    {
        float gx = (Mathf.Clamp(position.x,-HalfSize.x,HalfSize.x)+HalfSize.x)/Step;
        float gz = (Mathf.Clamp(position.y,-HalfSize.y,HalfSize.y)+HalfSize.y)/Step;
        int x = Mathf.Clamp(Mathf.FloorToInt(gx),0,Columns-1);
        int z = Mathf.Clamp(Mathf.FloorToInt(gz),0,Rows-1);
        float u = gx-x, v = gz-z;
        float b = Heights[Index(x+1,z)], c = Heights[Index(x,z+1)];
        if (u+v<=1) return (1-u-v)*Heights[Index(x,z)]+u*b+v*c;
        return (u+v-1)*Heights[Index(x+1,z+1)]+(1-v)*b+(1-u)*c;
    }
}
