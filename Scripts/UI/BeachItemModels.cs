using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The hand and the slot portrait share the beach model, including its per-instance paint.
// Copies contain rendering components only, so they cannot be picked up or hit by a ray.
public sealed class BeachItemModels : MonoBehaviour
{
    readonly Dictionary<BeachSlot,GameObject> models=new();
    readonly Dictionary<BeachSlot,Sprite> portraits=new();
    // Each template's longest side on the beach, in metres.
    readonly Dictionary<BeachSlot,float> sizes=new();
    readonly List<Mesh> lineMeshes=new();
    Camera studio;
    GameObject Template(BeachSlot slot,Transform source)
    {
        if (models.TryGetValue(slot,out var cached)) return cached;
        if (!source) return null;
        var holder=new GameObject(slot.Label);
        holder.transform.SetParent(transform,false);
        var shape=new GameObject("모델").transform;
        shape.SetParent(holder.transform,false);
        Bounds bounds=default;
        bool started=false;
        foreach (var original in source.GetComponentsInChildren<Renderer>(true))
        {
            if (!original.enabled || original is SpriteRenderer) continue;
            Mesh mesh=null;
            if (original.TryGetComponent<MeshFilter>(out var filter)) mesh=filter.sharedMesh;
            else if (original is LineRenderer line)
            {
                mesh=new Mesh();
                line.BakeMesh(mesh,Camera.main,false);
                lineMeshes.Add(mesh);
            }
            if (!mesh) continue;
            var piece=new GameObject(original.name).transform;
            piece.SetParent(shape,false);
            piece.localPosition=Quaternion.Inverse(source.rotation)*(original.transform.position-source.position);
            piece.localRotation=Quaternion.Inverse(source.rotation)*original.transform.rotation;
            piece.localScale=original.transform.lossyScale;
            piece.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=piece.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials=original.sharedMaterials;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            var paint=new MaterialPropertyBlock();
            original.GetPropertyBlock(paint);
            renderer.SetPropertyBlock(paint);
            if (!started) { bounds=renderer.bounds; started=true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        float longest=Mathf.Max(.001f,bounds.size.x,bounds.size.y,bounds.size.z);
        sizes[slot]=longest;
        float scale=1f/longest;
        shape.localPosition=(holder.transform.position-bounds.center)*scale;
        shape.localScale=Vector3.one*scale;
        holder.SetActive(false);
        models.Add(slot,holder);
        return holder;
    }

    public GameObject InHand(BeachSlot slot,Transform source,Transform parent)
    {
        var model=Template(slot,source);
        if (!model) return null;
        var copy=Instantiate(model,parent,false);
        copy.transform.localPosition=new Vector3(.02f,.018f,.105f);
        copy.transform.localRotation=Quaternion.Euler(-40,30,12);
        copy.transform.localScale=Vector3.one*.18f;
        copy.SetActive(true);
        return copy;
    }

    // A copy at its size on the beach (no longer than `longest`), for another player's hand. It casts a shadow.
    public GameObject Carried(BeachSlot slot,Transform source,Transform parent,float longest)
    {
        var model=Template(slot,source);
        if (!model) return null;
        var copy=Instantiate(model,parent,false);
        copy.transform.localPosition=Vector3.zero;
        copy.transform.localRotation=Quaternion.identity;
        copy.transform.localScale=Vector3.one*Mathf.Min(longest,sizes[slot]);
        foreach (var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
            renderer.shadowCastingMode=ShadowCastingMode.On;
        copy.SetActive(true);
        return copy;
    }

    public Sprite Portrait(BeachSlot slot,Transform source)
    {
        if (portraits.TryGetValue(slot,out var cached)) return cached;
        if (!source || SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) return null;
        var model=Template(slot,source);
        if (!studio)
        {
            studio=new GameObject("하단바 모델 카메라").AddComponent<Camera>();
            studio.transform.SetParent(transform,false);
            studio.enabled=false;
            studio.orthographic=true;
            studio.clearFlags=CameraClearFlags.SolidColor;
            studio.backgroundColor=new Color(.09f,.15f,.17f,1);
            studio.nearClipPlane=.05f;
            studio.farClipPlane=6;
            studio.allowHDR=false;
        }
        Vector3 center=new Vector3(5000,5000,5000);
        model.transform.position=center;
        model.SetActive(true);
        studio.transform.position=center+new Vector3(1,1.4f,-2.8f);
        studio.transform.LookAt(center);
        studio.orthographicSize=.67f;
        var target=RenderTexture.GetTemporary(96,96,24,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;
        try
        {
            studio.targetTexture=target;
            studio.Render();
            RenderTexture.active=target;
            var image=new Texture2D(96,96,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,96,96),0,0);
            image.Apply(false,true);
            var sprite=Sprite.Create(image,new Rect(0,0,96,96),Vector2.one*.5f);
            portraits.Add(slot,sprite);
            return sprite;
        }
        finally
        {
            model.SetActive(false);
            model.transform.localPosition=Vector3.zero;
            studio.targetTexture=null;
            RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    void OnDestroy()
    {
        foreach (var sprite in portraits.Values) { Destroy(sprite.texture); Destroy(sprite); }
        foreach (var mesh in lineMeshes) Destroy(mesh);
    }
}
