using UnityEngine;

// Imported scenery meshes share a bottom-centre pivot and the coast colour palette.
public static class CoastProp
{
    public static Transform Place(string model,string label,Transform parent,Vector3 position,Vector3 size,float yaw=0)
    {
        var source = Resources.Load<GameObject>("Coast/"+model);
        var prop = new GameObject(label).transform;
        prop.SetParent(parent,false);
        prop.localPosition = position;
        prop.localRotation = Quaternion.Euler(0,yaw,0);
        prop.localScale = size;
        prop.gameObject.AddComponent<MeshFilter>().sharedMesh = source.GetComponentInChildren<MeshFilter>().sharedMesh;
        prop.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("Coast/CoastFinish");
        return prop;
    }
}
