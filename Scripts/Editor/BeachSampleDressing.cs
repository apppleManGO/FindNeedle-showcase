using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class BeachSampleDressing
{
    const string Source = "Assets/BeachTestSamples/";
    const string Art = "Assets/FindNeedle/Beach/Art/";
    const string ScenePath = "Assets/Scenes/SampleScene.unity";

    static T Asset<T>(string name) where T : UnityEngine.Object =>
        AssetDatabase.LoadAssetAtPath<T>(Source + name) ??
        throw new InvalidOperationException("Missing beach sample: " + name);

    static Material Surface(string name, string texture)
    {
        var path = Art + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material,path);
        }
        material.mainTexture = Asset<Texture2D>(texture);
        material.SetFloat("_Smoothness",.12f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // Use a separate parent for placement so the imported FBX axis conversion stays intact.
    static Transform Place(string name, string file, Transform parent, Vector3 position, float width, float yaw, Material material)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent,false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>(file),root);
        var renderers = model.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers[0].bounds;
        foreach(var renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
            var materials = renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++) materials[i]=material;
            renderer.sharedMaterials=materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        float factor=width/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
        Vector3 basePoint=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        model.transform.position=root.position+(model.transform.position-basePoint)*factor;
        model.transform.localScale*=factor;
        PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
        root.localPosition=position;
        root.localRotation=Quaternion.Euler(0,yaw,0);
        return root;
    }

    public static void Dress(BeachSearch game)
    {
        var coast=game.stall.parent;
        var palette=Surface("IslandPalette","Kenney/Textures/colormap.png");
        void Shore(string name,string file,float x,float z,float size,float yaw)
        {
            Place(name,"Kenney/"+file+".fbx",coast,new Vector3(x,-.02f,z),size,yaw,palette);
        }
        Shore("물가 야자수","palm-bend",-5.65f,4.7f,4.3f,28);
        Shore("부두 야자수","palm-bend",5.55f,5.2f,3.8f,-105);
        Shore("먼 야자수","palm-bend",6.7f,6.4f,3.3f,70);
        Shore("해안 바위","rocks-sand-a",-4.1f,6.6f,2.2f,25);
        Shore("부두 바위","rocks-sand-a",4.5f,7.2f,1.5f,145);
        Shore("부두 앞 모래톱","patch-sand",2.1f,7.65f,2.8f,15);
        Shore("작은 부두","structure-platform-dock-small",2.3f,7.95f,3.4f,90);
        var sand=Surface("CoastSand","Sand/coast_sand_01_diff_1k.jpg");
        const string normal="Sand/coast_sand_01_nor_gl_1k.jpg";
        var importer=(TextureImporter)AssetImporter.GetAtPath(Source+normal);
        if(importer.textureType!=TextureImporterType.NormalMap){
            importer.textureType=TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        sand.SetTexture("_BumpMap",Asset<Texture2D>(normal));
        sand.EnableKeyword("_NORMALMAP");
        sand.SetFloat("_BumpScale",.18f);
        sand.mainTextureScale=Vector2.one*2.8f;
        game.sand.surfaceMaterial=sand;
        game.sand.preview.GetComponent<Renderer>().sharedMaterial=sand;
        DressShells(game);
        // Put the detailed tools on the counter; the held tools have authored grips and scoop sockets.
        Place("진열 모종삽","Trowel/trowel_01_1k.fbx",game.stall,new Vector3(-.43f,.98f,0),.43f,50,
            Surface("DisplayTrowel","Trowel/textures/trowel_01_diff_1k.jpg"));
        Place("진열 탐지기","MetalDetector/metal_detector_1k.fbx",game.stall,new Vector3(.40f,.98f,0),.69f,-25,
            Surface("DisplayDetector","MetalDetector/textures/metal_detector_diff_1k.jpg"));
        game.handSandSound=Asset<AudioClip>("Audio/Dig_1_preview.mp3");
        game.shovelSandSound=Asset<AudioClip>("Audio/Dig_2_preview.mp3");
        BeachDemoBuilder.DressOutskirts(game);
        MatchShore(game);
    }

    static void DressShells(BeachSearch game)
    {
        var shell=Surface("BeachShell","Shell/textures/lambis_shell_diff_1k.jpg");
        foreach(var item in game.finds)
        {
            if(!item.traces) continue;
            var trace=item.traces.transform;
            for(int i=trace.childCount-1;i>=0;i--)
            {
                var old=trace.GetChild(i);
                if (old.name!="조개 흔적") continue;
                var position=old.localPosition;
                UnityEngine.Object.DestroyImmediate(old.gameObject);
                Place("탐색 단서 · 소라","Shell/lambis_shell_1k.fbx",trace,position,i==0?.14f:.085f,28+i*83,shell);
            }
        }
    }

    static void MatchShore(BeachSearch game)
    {
        var sand=game.sand.surfaceMaterial;
        sand.color=new Color(1.65f,1.55f,1.35f);
        EditorUtility.SetDirty(sand);
        foreach(Transform prop in game.stall.parent)
        {
            if(prop.name=="작은 부두") prop.localScale=new Vector3(1,.48f,1);
            if(!prop.name.EndsWith("해변") && prop.name!="북쪽 물가") continue;
            string path=Art+"Shore-"+prop.name+".mat";
            var edge=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!edge){edge=new Material(sand);AssetDatabase.CreateAsset(edge,path);}
            edge.CopyPropertiesFromMaterial(sand);
            edge.mainTextureScale=new Vector2(prop.localScale.x,prop.localScale.z)*.28f;
            prop.GetComponent<Renderer>().sharedMaterial=edge;
            EditorUtility.SetDirty(edge);
        }
    }

    public static void ApplyExisting()
    {
        var scene=EditorSceneManager.OpenScene(ScenePath);
        var game=UnityEngine.Object.FindFirstObjectByType<BeachSearch>();
        if(!game.stall.parent.Find("물가 야자수")) Dress(game);
        else
        {
            BeachDemoBuilder.UpgradeCoast(game);
            DressShells(game);
            MatchShore(game);
            BeachDemoBuilder.DressOutskirts(game);
        }
        if(!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed");
        AssetDatabase.SaveAssets();
        Debug.Log("BEACH SAMPLES SAVED");
    }
}
