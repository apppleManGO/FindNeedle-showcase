using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Render the actual selectable models once, then reuse their portraits in the lobby.
public sealed class BeachCharacterPortraits : MonoBehaviour
{
    readonly Dictionary<int,Sprite> portraits=new();
    public Sprite Get(int look)
    {
        if(portraits.TryGetValue(look,out var portrait)) return portrait;
        if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) return null;
        var stage=new GameObject("캐릭터 미리보기");
        stage.transform.position=new Vector3(7000,7000,7000);
        var body=BeachBody.Create(stage.transform,look);
        if(!body) { Destroy(stage); return null; }
        body.Face(180);
        var cameraObject=new GameObject("캐릭터 미리보기 카메라");
        var camera=cameraObject.AddComponent<Camera>();
        camera.enabled=false;
        camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.08f,.18f,.20f,1);
        camera.orthographic=true;
        camera.orthographicSize=.86f;
        camera.nearClipPlane=.05f;
        camera.farClipPlane=6;
        camera.allowHDR=false;
        camera.transform.position=stage.transform.position+new Vector3(0,.76f,-3);
        camera.transform.rotation=Quaternion.identity;
        var target=RenderTexture.GetTemporary(192,320,24,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=target;
            camera.Render();
            RenderTexture.active=target;
            var image=new Texture2D(192,320,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,192,320),0,0);
            image.Apply(false,true);
            portrait=Sprite.Create(image,new Rect(0,0,192,320),Vector2.one*.5f);
            portraits.Add(look,portrait);
            return portrait;
        }
        finally
        {
            camera.targetTexture=null;
            RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(target);
            stage.SetActive(false);
            Destroy(stage);
            Destroy(cameraObject);
        }
    }
    void OnDestroy()
    {
        foreach(var image in portraits.Values) { Destroy(image.texture); Destroy(image); }
    }
}
