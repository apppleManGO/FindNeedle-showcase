using UnityEngine;

public sealed class BeachBin : MonoBehaviour
{
    public WasteBin Kind { get; private set; }
    public string Label => gameObject.name;
    public Vector3 Opening => transform.position+Vector3.up*.78f;
    public const float Reach = 2f;

    public static BeachBin Create(Transform stall,WasteBin kind)
    {
        var root = new GameObject(kind switch {
            WasteBin.Metal => "금속함", WasteBin.Bottle => "페트병함", _ => "일반 수거함" });
        root.transform.SetParent(stall,false);
        root.transform.localPosition = new Vector3(-1.34f-(int)kind*.72f,0,.145f);
        var bin = root.AddComponent<BeachBin>();
        bin.Kind=kind;
        var hit=root.AddComponent<BoxCollider>();
        hit.center=Vector3.up*.4f;
        hit.size=new Vector3(.64f,.8f,.62f);
        var model=CoastProp.Place("Bin","수거함 본체",root.transform,Vector3.zero,new Vector3(.64f,.8f,.62f));
        var paint=new MaterialPropertyBlock();
        paint.SetColor("_BaseColor",kind switch {
            WasteBin.Metal=>new Color(.73f,.86f,1), WasteBin.Bottle=>new Color(.60f,1,.80f), _=>Color.white });
        model.GetComponent<MeshRenderer>().SetPropertyBlock(paint);
        var sign=new GameObject("수거함 표시").AddComponent<SpriteRenderer>();
        sign.transform.SetParent(root.transform,false);
        sign.transform.localPosition=new Vector3(0,.49f,-.313f);
        string icon=kind switch { WasteBin.Metal=>"metal", WasteBin.Bottle=>"bottle", _=>"general" };
        sign.sprite=Resources.Load<Sprite>("BeachUI/bin-"+icon);
        sign.transform.localScale=Vector3.one*.55f;
        return bin;
    }
}
