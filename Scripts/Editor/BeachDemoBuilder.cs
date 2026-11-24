using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Rebuilds the authored demo scene; all geometry and references are saved as ordinary scene objects.
public static class BeachDemoBuilder
{
    const string Art="Assets/FindNeedle/Beach/Art";
    const string ScenePath="Assets/Scenes/SampleScene.unity";
    static readonly Dictionary<string,Material> palette=new();
    static Transform group;
    static Material Paint(string hex)
    {
        if(palette.TryGetValue(hex,out var cached)) return cached;
        string path=Art+"/"+hex+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat){
            ColorUtility.TryParseHtmlString("#"+hex,out var color);
            mat=new Material(GraphicsSettings.defaultRenderPipeline.defaultMaterial){color=color};
            mat.SetFloat("_Smoothness",.18f);
            AssetDatabase.CreateAsset(mat,path);
        }
        return palette[hex]=mat;
    }
    static Transform Empty(string name,Transform parent=null)
    {
        var t=new GameObject(name).transform;
        t.SetParent(parent,false);
        return t;
    }
    static Transform Shape(string name,PrimitiveType type,Vector3 pos,Vector3 scale,string hex,Transform parent=null)
    {
        var t=GameObject.CreatePrimitive(type).transform;
        t.name=name;
        t.SetParent(parent?parent:group,false);
        t.localPosition=pos; t.localScale=scale;
        t.GetComponent<Renderer>().sharedMaterial=Paint(hex);
        Object.DestroyImmediate(t.GetComponent<Collider>());
        return t;
    }
    static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
    static Transform Box(string n,Vector3 p,Vector3 s,string c,Transform parent=null)=>Shape(n,PrimitiveType.Cube,p,s,c,parent);
    static Transform Ball(string n,Vector3 p,Vector3 s,string c,Transform parent=null)=>Shape(n,PrimitiveType.Sphere,p,s,c,parent);
    static Transform Rod(string n,Vector3 p,Vector3 s,string c,Transform parent=null)=>Shape(n,PrimitiveType.Cylinder,p,s,c,parent);

    static void Umbrella(float x,float z,string model)
    {
        var root=Empty("수선한 파라솔",group);
        root.localPosition=V(x,0,z);
        CoastProp.Place(model,"파라솔",root,Vector3.zero,V(2.8f,2.48f,2.8f));
        CoastProp.Place("Mat","줄무늬 돗자리",root,V(.85f,.006f,0),V(.72f,.026f,1.36f));
    }

    static Transform Shop()
    {
        var stall=Empty("조개 매입소",group);
        stall.position=V(-4.545f,0,-.9f);
        stall.rotation=Quaternion.Euler(0,-90,0);
        DressStall(stall);
        var sign=Empty("매입소 표지",stall);
        sign.localPosition=V(0,.55f,-.58f);
        var letters=sign.gameObject.AddComponent<TextMesh>();
        letters.text="TRADE";
        letters.anchor=TextAnchor.MiddleCenter;
        letters.characterSize=.058f;
        letters.fontSize=48;
        letters.color=Color.white;
        var blocker=stall.gameObject.AddComponent<BoxCollider>();
        blocker.center=V(0,.94f,0);
        blocker.size=V(1.92f,1.88f,1.10f);
        return stall;
    }

    static void DressStall(Transform stall)
    {
        var model=CoastProp.Place("Stall","매입 가판대",stall,Vector3.zero,V(1.92f,2.04f,1.1f),180);
        model.SetAsFirstSibling();
        CoastProp.Place("Wall","매입소 뒤 나무벽",stall,V(-1.04f,0,.60f),V(4.3f,1.52f,.14f));
    }

    public static void DressOutskirts(BeachSearch game)
    {
        var coast=game.stall.parent;
        if (coast.Find("해변 산책로")) return;
        var seats=SceneryGroup(coast,"코트 관람석",V(7.7f,0,3.4f),-112);
        game.spectatorArea=seats.gameObject;
        CoastProp.Place("ParasolBlue","관람석 파라솔",seats,V(0,0,-.38f),V(2.5f,2.3f,2.5f));
        foreach(float x in new[]{-.46f,.46f})
            CoastProp.Place("Chair","접이식 의자",seats,V(x,0,.08f),V(.61f,.90f,.72f));
        var towel=CoastProp.Place("Mat","걸친 수건",seats,V(.46f,.69f,-.22f),V(.24f,.018f,.38f));
        towel.localRotation=Quaternion.Euler(-67,0,0);
        game.spectatorArea.SetActive(false);
        CoastProp.Place("Lighthouse","섬 등대",coast,V(-17,4.26f,37),V(1.7f,4.35f,1.8f));
        game.fishingBoat=SceneryGroup(coast,"연안 어선",V(-3,-.10f,26),0);
        CoastProp.Place("Boat","어선 모델",game.fishingBoat,V(0,-.12f,0),V(3.1f,1.65f,1.10f));

        var surf=SceneryGroup(coast,"서쪽 보드 대여소",V(-11,0,.1f),80);
        CoastProp.Place("SurfShop","서핑숍",surf,Vector3.zero,V(4.6f,3.1f,3.3f));
        CoastProp.Place("BoardRack","보드 거치대",surf,V(3.3f,0,1.1f),V(2.2f,1.55f,.65f));
        var board=CoastProp.Place("Board","대여용 서프보드",surf,V(1.98f,.03f,1.76f),V(.53f,2.12f,.18f));
        board.localRotation=Quaternion.Euler(-12,0,-8);
        var watch=SceneryGroup(coast,"동쪽 인명구조대",V(13.5f,0,-1.7f),-72);
        CoastProp.Place("Tower","구조 망루",watch,Vector3.zero,V(3.1f,3.8f,3.2f));
        CoastProp.Place("Flag","안전 깃발",watch,V(-2f,0,.25f),V(.4f,4.2f,1.1f),90);

        var walk=SceneryGroup(coast,"해변 산책로",V(0,0,-11.8f),0);
        for(int i=-5;i<=5;i++)
            CoastProp.Place("Deck","나무 산책로",walk,V(i*2.82f,-.20f,0),V(2.82f,.38f,2.45f));
        foreach(float x in new[]{-12f,-7f,6f,12f})
            CoastProp.Place("Rail","난간",walk,V(x,.18f,1.08f),V(3.8f,.65f,.12f));
        for(int i=0;i<3;i++)
            CoastProp.Place("Hut","탈의실",walk,V(-4.4f+i*1.85f,.18f,-.43f),V(1.54f,2.28f,1.64f));

        var dunes=SceneryGroup(coast,"해안 사구",V(0,0,-15),0);
        void Dune(Vector3 basePoint,Vector3 size,float treeSize)
        {
            CoastProp.Place("Dune","모래언덕",dunes,basePoint-Vector3.up*.68f,size);
            CoastProp.Place("Pine","해송",dunes,basePoint+V(-1,.4f,-.7f),V(2.9f,4.1f,2.5f)*treeSize,basePoint.x*7);
            CoastProp.Place("DuneGrass","사구 풀",dunes,basePoint+V(1.4f,.2f,2),V(1.9f,.76f,1.3f),basePoint.x*5);
        }
        for(int i=0;i<7;i++)
            Dune(V((i-3)*6.8f,0,Mathf.Abs(i-3)*1.15f),V(12,2.2f+i%3*.5f,6.5f),.85f+i%3*.18f);
        foreach(float side in new[]{-1f,1f})
            for(int i=0;i<3;i++)
                Dune(V(side*(18+i%2*1.5f),0,10+i*4.4f),V(7,2.3f+i%2,8),1+i%2*.23f);
        foreach(var renderer in dunes.GetComponentsInChildren<Renderer>())
            renderer.shadowCastingMode=ShadowCastingMode.Off;
    }

    static Transform SceneryGroup(Transform parent,string name,Vector3 position,float yaw)
    {
        var root=Empty(name,parent);
        root.localPosition=position;
        root.localRotation=Quaternion.Euler(0,yaw,0);
        return root;
    }

    static void Coast()
    {
        CoastProp.Place("BeachTile","서쪽 해변",group,V(-13,-.12f,-5.1f),V(16,.12f,25.8f));
        CoastProp.Place("BeachTile","동쪽 해변",group,V(13,-.12f,-5.1f),V(16,.12f,25.8f));
        CoastProp.Place("BeachTile","남쪽 해변",group,V(0,-.12f,-11.5f),V(10,.12f,13));
        CoastProp.Place("BeachTile","북쪽 물가",group,V(0,-.12f,6.4f),V(10,.12f,2.8f));
        CoastProp.Place("Sea","바다",group,V(0,-.16f,47),V(180,.055f,80));
        for(int i=0;i<25;i++)
            CoastProp.Place("Wave","파도",group,V((i%9-4)*2.8f,-.102f,8+i/9*2.5f+.20f*Mathf.Sin(i)),V(1.7f,.034f,.16f),Mathf.Sin(i)*9);
        for(int i=0;i<6;i++)
            CoastProp.Place("Island","먼 섬",group,V(-23+i*3,-.65f,37+i%2*2),V(8,2+i%3*1.6f,5),i*27);
        Umbrella(-5.9f,2.7f,"Parasol");Umbrella(5.8f,1.7f,"ParasolBlue");Umbrella(5.8f,-4.2f,"Parasol");
        for(int i=0;i<5;i++)
            CoastProp.Place("Fence","경계 로프",group,V(-3.68f+i*1.84f,0,5.10f),V(1.84f,.61f,.10f));
    }
    // Upgrade the saved scene in place, including custom placements and gameplay references.
    public static void UpgradeCoast(BeachSearch game)
    {
        var coast=game.stall.parent;
        if (game.stall.Find("매입 가판대")) return;
        var oldCoast=new HashSet<string>{"수선한 파라솔","코트 관람석","연안 어선","섬 등대","서쪽 보드 대여소",
            "동쪽 인명구조대","해변 산책로","해안 사구","경계 말뚝","경계 로프","먼 섬","파도","바다",
            "서쪽 해변","동쪽 해변","남쪽 해변","북쪽 물가"};
        for(int i=coast.childCount-1;i>=0;i--)
            if (oldCoast.Contains(coast.GetChild(i).name)) Object.DestroyImmediate(coast.GetChild(i).gameObject);
        for(int i=game.stall.childCount-1;i>=0;i--)
        {
            var child=game.stall.GetChild(i);
            if (child.name=="매입소 표지") child.localPosition=V(0,.55f,-.58f);
            else if (child.name is "카운터" or "상판" or "기둥" or "차양" or "매입소 뒤 나무벽")
                Object.DestroyImmediate(child.gameObject);
        }
        DressStall(game.stall);
        group=coast;
        Coast();
        DressOutskirts(game);
        var clues=game.finds[0].transform.parent;
        int step=0;
        for(int i=clues.childCount-1;i>=0;i--)
        {
            var old=clues.GetChild(i);
            if (old.name!="옅은 발자국") continue;
            var position=old.localPosition;
            Object.DestroyImmediate(old.gameObject);
            position.y=.007f;
            CoastProp.Place("Footprint","옅은 발자국",clues,position,V(.115f,1,.21f),step++%2==0?-9:9);
        }
    }

    static Transform Limb(string n,Transform root,Vector3 joint,Vector3 size,string c){
        var pivot=Empty(n,root);pivot.localPosition=joint;
        Ball("형태",V(0,-size.y*.43f,0),size,c,pivot);return pivot;
    }
    static BeachWalker Walker(Camera camera,Transform stall){
        var root=Empty("플레이어 · 수선공");root.position=V(0,0,-2.55f);
        var walker=root.gameObject.AddComponent<BeachWalker>();
        walker.view=camera;walker.stall=stall;
        Ball("작업 셔츠",V(0,.70f,0),V(.49f,.54f,.34f),"387F83",root);
        Ball("얼굴",V(0,1.11f,.025f),V(.37f,.40f,.34f),"D59F76",root);
        for(int i=-1;i<=1;i+=2)Ball("눈",V(i*.075f,1.13f,.178f),V(.038f,.043f,.026f),"36332E",root);
        Rod("밀짚모자 챙",V(0,1.28f,0),V(.60f,.022f,.54f),"D4AD68",root);
        Ball("밀짚모자",V(0,1.32f,0),V(.41f,.23f,.39f),"E4C686",root);
        Rod("모자 리본",V(0,1.31f,0),V(.41f,.027f,.39f),"A4503C",root);
        Box("공구 가방",V(.04f,.66f,-.17f),V(.30f,.23f,.085f),"AD794C",root);
        Box("가방 덮개",V(.04f,.74f,-.22f),V(.31f,.10f,.036f),"DAA970",root);
        Box("어깨끈",V(0,.86f,-.14f),V(.055f,.31f,.05f),"AD794C",root).localRotation=Quaternion.Euler(0,0,-30);
        walker.leftLeg=Limb("왼다리",root,V(-.12f,.45f,0),V(.17f,.42f,.18f),"384D60");
        walker.rightLeg=Limb("오른다리",root,V(.12f,.45f,0),V(.17f,.42f,.18f),"384D60");
        foreach(var leg in new[]{walker.leftLeg,walker.rightLeg})Ball("신발",V(0,-.38f,.045f),V(.18f,.13f,.27f),"654B38",leg);
        var left=Limb("왼팔",root,V(-.25f,.84f,0),V(.13f,.38f,.14f),"D59F76");
        walker.rightArm=Limb("오른팔",root,V(.25f,.84f,0),V(.13f,.38f,.14f),"D59F76");
        var tool=Empty("모종삽",walker.rightArm);tool.localPosition=V(0,-.31f,.04f);
        Rod("손잡이",V(0,-.09f,0),V(.065f,.14f,.065f),"945D3D",tool);
        Ball("삽날",V(0,-.28f,.035f),V(.15f,.24f,.066f),"ABC3C8",tool);
        walker.trowel=tool.gameObject;walker.trowel.SetActive(false);
        var sensor=Empty("금속탐지기",left);sensor.localPosition=V(0,-.31f,.01f);
        Box("축",V(0,-.13f,.19f),V(.043f,.044f,.53f),"3D4B50",sensor).localRotation=Quaternion.Euler(30,0,0);
        Rod("탐색 코일",V(0,-.26f,.43f),V(.34f,.022f,.24f),"D7A941",sensor);
        walker.detector=sensor.gameObject;walker.detector.SetActive(false);
        return walker;
    }

    static BeachFind Find(FindKind k,float x,float z,float depth){
        var t=Empty(k.ToString(),group);t.position=V(x,-depth,z);
        if(k==FindKind.Coin)Rod("동전",Vector3.zero,V(.17f,.018f,.17f),"F3B948",t);
        else if(k==FindKind.Needle){
            Box("바늘 몸통",Vector3.zero,V(.024f,.018f,.32f),"DAEBF1",t);
            Ball("바늘귀",V(0,0,-.145f),V(.044f,.023f,.063f),"9FAEB7",t);
        }else{
            float r=k==FindKind.Bracelet?.13f:.071f;
            for(int i=0;i<12;i++){
                float a=i*Mathf.PI/6;
                Ball("장식",V(Mathf.Sin(a)*r,0,Mathf.Cos(a)*r),V(r*.62f,.045f,r*.62f),k==FindKind.Ring?"E4B459":i%2==0?"65B2AE":"F0DCBC",t);
            }
        }
        var f=t.gameObject.AddComponent<BeachFind>();f.kind=k;f.topOffset=.015f;
        var traces=Empty("조개 무리",group);f.traces=traces.gameObject;
        for(int i=0;i<3;i++)Ball("조개 흔적",V(x+(i-1)*.11f,.014f,z+.08f*Mathf.Cos(i*2)),V(.09f,.035f,.06f),i%2==0?"E7BCA2":"FCEDCE",traces);
        return f;
    }

    [MenuItem("Find Needle/Rebuild Demo Scene")]
    public static void Build(){
        if(!Application.isBatchMode && !EditorUtility.DisplayDialog("데모 씬 재생성","SampleScene의 내용을 기본 데모 배치로 교체할까요?","재생성","취소")) return;
        if(!AssetDatabase.IsValidFolder(Art)) AssetDatabase.CreateFolder("Assets/FindNeedle/Beach","Art");
        palette.Clear();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
        var camera=Camera.main;camera.fieldOfView=52;camera.nearClipPlane=.05f;camera.farClipPlane=150;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.70f,.86f,.89f);
        var light=Object.FindFirstObjectByType<Light>();light.transform.rotation=Quaternion.Euler(48,-35,0);
        light.color=new Color(1,.91f,.79f);light.intensity=1.6f;light.shadows=LightShadows.Soft;
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.73f,.76f,.76f);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=camera.backgroundColor;RenderSettings.fogStartDistance=15;RenderSettings.fogEndDistance=78;
        group=Empty("해변 · 배경");Coast();
        var stall=Shop();
        var sandRoot=Empty("탐색 구역 · 모래");var sand=sandRoot.gameObject.AddComponent<SandPatch>();
        var preview=Box("모래 표면 · 편집용",V(0,-.05f,0),V(10,.1f,10),"E5C28F",sandRoot);
        sand.preview=preview.gameObject;
        group=Empty("매장물과 조개 흔적");
        var finds=new[]{Find(FindKind.Coin,0,-1.9f,.052f),Find(FindKind.Bracelet,-1.65f,-.48f,.10f),
            Find(FindKind.Ring,1.4f,.56f,.14f),Find(FindKind.Coin,-2.8f,1.72f,.12f),
            Find(FindKind.Ring,2.9f,-2.3f,.13f),Find(FindKind.Needle,1.85f,3.18f,.054f)};
        for(int i=0;i<18;i++){
            float z=-1.6f+i*.27f, x=Mathf.Sin(i*.55f)*.13f + i*.10f;
            CoastProp.Place("Footprint","옅은 발자국",group,V(x,.007f,z),V(.115f,1,.21f),i%2==0?-9:9);
        }
        var player=Walker(camera,stall);
        var root=Empty("게임 진행");var game=root.gameObject.AddComponent<BeachSearch>();
        game.sand=sand;game.player=player;game.finds=finds;game.stall=stall;game.inventory=root.gameObject.AddComponent<BeachInventory>();
        root.gameObject.AddComponent<BeachHUD>().game=game;
        var line=Empty("파기 범위",root).gameObject.AddComponent<LineRenderer>();
        line.loop=true;line.positionCount=40;line.widthMultiplier=.018f;line.sharedMaterial=Paint("FFFADE");line.enabled=false;
        game.digRing=line;
        camera.transform.SetPositionAndRotation(player.transform.position+V(0,2.7f,-3.3f),Quaternion.Euler(30,0,0));
        BeachSampleDressing.Dress(game);
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        Debug.Log("FIND NEEDLE: authored third-person scene saved.");
    }
}
