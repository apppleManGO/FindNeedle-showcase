using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Store only worked tiles on the CPU. Upload changed tiles together after each terrain update.
sealed class SandTracks
{
    const int Tile = 64;
    const float Density = 80f;
    readonly Vector2Int size;
    readonly Vector2 half, pixelsPerMetre;
    readonly Dictionary<Vector2Int,byte[]> tiles = new();
    readonly HashSet<Vector2Int> dirty = new();
    readonly Texture2D mask, upload;
    readonly Material material;
    readonly MeshRenderer overlay;

    public SandTracks(Transform parent,Mesh surface,Vector2 halfSize,Shader shader)
    {
        half = halfSize;
        size = Vector2Int.CeilToInt(2*half*Density/Tile)*Tile;
        pixelsPerMetre = new Vector2(size.x/(2*half.x),size.y/(2*half.y));
        mask = new Texture2D(size.x,size.y,TextureFormat.R8,false,true);
        mask.wrapMode = TextureWrapMode.Clamp;
        mask.filterMode = FilterMode.Bilinear;
        mask.LoadRawTextureData(new byte[size.x*size.y]);
        mask.Apply(false,true);
        upload = new Texture2D(Tile,Tile,TextureFormat.R8,false,true);
        material = new Material(shader);
        material.mainTexture = mask;
        var layer = new GameObject("모래 수색 흔적");
        layer.transform.SetParent(parent,false);
        layer.AddComponent<MeshFilter>().sharedMesh = surface;
        overlay = layer.AddComponent<MeshRenderer>();
        overlay.sharedMaterial = material;
        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        // Always drawn: it also carries dampness, wind ripples and glints.
    }

    enum Brush { Erase, Fingers, Blade, Foot }

    public void Clear(Vector2 centre,float radius) => Stamp(centre,Vector2.up,radius,Brush.Erase);
    public void Fingers(Vector2 centre,Vector2 direction) => Stamp(centre,direction,.115f,Brush.Fingers);
    public void Blade(Vector2 centre,Vector2 direction) => Stamp(centre,direction,.28f,Brush.Blade);
    public void Foot(Vector2 centre,Vector2 direction) => Stamp(centre,direction,.16f,Brush.Foot);

    void Stamp(Vector2 centre,Vector2 direction,float radius,Brush brush)
    {
        int x0 = Mathf.Max(0,Mathf.FloorToInt((centre.x-radius+half.x)*pixelsPerMetre.x));
        int z0 = Mathf.Max(0,Mathf.FloorToInt((centre.y-radius+half.y)*pixelsPerMetre.y));
        int x1 = Mathf.Min(size.x-1,Mathf.CeilToInt((centre.x+radius+half.x)*pixelsPerMetre.x));
        int z1 = Mathf.Min(size.y-1,Mathf.CeilToInt((centre.y+radius+half.y)*pixelsPerMetre.y));
        var across = new Vector2(-direction.y,direction.x);
        for (int z=z0;z<=z1;z++)
        for (int x=x0;x<=x1;x++)
        {
            var delta = new Vector2((x+.5f)/pixelsPerMetre.x-half.x,(z+.5f)/pixelsPerMetre.y-half.y)-centre;
            float distance = delta.magnitude;
            if (distance>=radius) continue;
            var key = new Vector2Int(x/Tile,z/Tile);
            tiles.TryGetValue(key,out var tile);
            int index = (z%Tile)*Tile+x%Tile;
            int old = tile==null ? 0 : tile[index];
            float fade = Mathf.Clamp01((radius-distance)/(.20f*radius));
            int next;
            if (brush==Brush.Erase) next = Mathf.RoundToInt(old*(1-fade));
            else
            {
                float u = Vector2.Dot(delta,direction), v = Vector2.Dot(delta,across);
                float strength;
                if (brush==Brush.Fingers)
                {
                    float gap = Mathf.Abs(Mathf.Abs(v)-.047f);
                    float groove = Mathf.Min(Mathf.Abs(v),gap);
                    strength = .83f*Mathf.Clamp01(1-groove/.016f);
                }
                else if (brush==Brush.Foot)
                {
                    // A sole-shaped shadow, darkest at the rim where sand is pressed up.
                    float sole = Mathf.Sqrt(u*u/(.125f*.125f)+v*v/(.052f*.052f));
                    strength = sole<1f ? .42f+.40f*sole*sole : 0;
                }
                else
                {
                    // A curved lip at the end of the cut and a quieter scraped interior.
                    float lip = .14f-2.7f*v*v;
                    strength = Mathf.Abs(v)<.16f && u<lip ?
                        .24f+.73f*Mathf.Clamp01(1-(lip-u)/.036f) : 0;
                }
                next = Mathf.Max(old,Mathf.RoundToInt(255*fade*strength));
            }
            if (old==next) continue;
            if (tile==null) tiles.Add(key,tile=new byte[Tile*Tile]);
            tile[index]=(byte)next;
            dirty.Add(key);
        }
    }

    public void Flush()
    {
        foreach(var key in dirty)
        {
            upload.LoadRawTextureData(tiles[key]);
            upload.Apply(false,false);
            Graphics.CopyTexture(upload,0,0,0,0,Tile,Tile,mask,0,0,key.x*Tile,key.y*Tile);
        }
        dirty.Clear();
    }

    public void Dispose()
    {
        Object.Destroy(material);
        Object.Destroy(mask);
        Object.Destroy(upload);
    }
}
