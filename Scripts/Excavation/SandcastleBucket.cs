using System.Collections.Generic;
using UnityEngine;

public sealed class SandcastleBucket : MonoBehaviour
{
    public bool Held { get; private set; }
    public const int Capacity = 5;
    public int Scoops { get; private set; }
    public bool Filled => Scoops == Capacity;
    float volume;
    GameObject fill;
    Transform bucket;
    public Transform Model => bucket;
    BoxCollider bucketCollider;
    BeachSearch beach;
    readonly List<BeachFind> finds = new();
    readonly Dictionary<Collider,float> towers = new();

    public void Initialize(BeachSearch game)
    {
        beach=game;
        bucket=new GameObject("주황색 모래성 양동이").transform;
        bucket.SetParent(transform,false);
        bucket.position=new Vector3(.76f,0,-2.05f);
        CoastProp.Place("Bucket","모래성 양동이",bucket,Vector3.zero,new Vector3(.34f,.39f,.34f));
        bucketCollider=bucket.gameObject.AddComponent<BoxCollider>();
        bucketCollider.center=Vector3.up*.16f;
        bucketCollider.size=new Vector3(.34f,.32f,.34f);
        var contents=CoastProp.Place("Island","담긴 모래",bucket,Vector3.up*.19f,new Vector3(.28f,.026f,.28f));
        contents.GetComponent<MeshRenderer>().sharedMaterial=game.sand.GetComponent<Renderer>().sharedMaterial;
        fill=contents.gameObject;
        fill.SetActive(false);
        beach.sand.SurfaceChanged += SettleOnSand;
    }

    public bool IsBucket(Collider target) => target && target==bucketCollider;
    public bool IsTower(Collider target) => target && towers.ContainsKey(target);

    public void Lift()
    {
        if (Held) return;
        Held=true;bucketCollider.enabled=false;
        bucket.SetParent(beach.player.view.transform,false);
        bucket.localPosition=new Vector3(0,-.25f,.57f);
        bucket.localRotation=Quaternion.identity;
    }

    public bool Place(Vector2 spot)
    {
        if (!Held || !CanPlace(spot)) return false;
        Vector3 ground=new Vector3(spot.x,beach.sand.Height(spot),spot.y);
        bucket.SetParent(transform,true);bucket.SetPositionAndRotation(ground,Quaternion.identity);
        Held=false;bucketCollider.enabled=true;return true;
    }

    public bool CanPlace(Vector2 spot)
    {
        if (!beach.sand.Contains(spot,.20f)) return false;
        Vector3 ground=new Vector3(spot.x,beach.sand.Height(spot),spot.y);
        return !Physics.CheckBox(ground+Vector3.up*.14f,new Vector3(.17f,.125f,.17f));
    }

    public bool Fill(float amount,List<BeachFind> carried)
    {
        if(Filled || amount<=0) return false;
        finds.AddRange(carried);
        volume+=amount;
        Scoops++;
        fill.SetActive(true);
        float progress=(float)Scoops/Capacity;
        fill.transform.localPosition=Vector3.up*(.025f+.20f*progress);
        float width=.20f+.09f*progress;
        fill.transform.localScale=new Vector3(width,.022f,width);
        return true;
    }

    public bool Mold(Vector2 spot)
    {
        if(!Held || !Filled || !CanPlace(spot)) return false;
        BuildTower(new Vector3(spot.x,beach.sand.Height(spot),spot.y));
        for(int i=0;i<finds.Count;i++){
            float angle=i*2.4f;
            Vector2 point=spot+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*.28f;
            point=beach.sand.ClampToBeach(point,.09f);
            finds[i].transform.position=new Vector3(point.x,beach.sand.Height(point)+.012f,point.y);
            finds[i].gameObject.SetActive(true);
        }
        finds.Clear();
        volume=0;
        Scoops=0;
        fill.SetActive(false);
        return true;
    }

    void BuildTower(Vector3 position)
    {
        var root=new GameObject("양동이 모래탑").transform;
        root.SetParent(transform,false);root.position=position;
        CoastProp.Place("Castle","모래성",root,Vector3.zero,new Vector3(.34f,.25f,.34f));
        var hit=root.gameObject.AddComponent<BoxCollider>();
        hit.center=Vector3.up*.125f;hit.size=new Vector3(.34f,.25f,.34f);
        towers.Add(hit,volume);
    }

    public void Crumble(Collider tower)
    {
        var p=tower.transform.position;
        beach.sand.Pour(new Vector2(p.x,p.z),towers[tower],.34f*tower.transform.localScale.x+.10f);
        beach.sand.Refresh();towers.Remove(tower);tower.enabled=false;Destroy(tower.gameObject);
    }
    void SettleOnSand()
    {
        if(!Held) Settle(bucket);
        foreach(var tower in towers.Keys) Settle(tower.transform);
    }

    void Settle(Transform prop)
    {
        Vector3 p=prop.position;
        p.y=beach.sand.Height(new Vector2(p.x,p.z));
        prop.position=p;
    }

    void OnDestroy()
    {
        if(beach && beach.sand) beach.sand.SurfaceChanged -= SettleOnSand;
    }
}
