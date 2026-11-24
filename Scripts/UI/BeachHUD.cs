using UnityEngine;
using Style = BeachCanvas.Style;

// Lays the HUD out each frame as rectangles; BeachCanvas turns them into uGUI elements
// skinned with the Kenney UI Pack.
public sealed partial class BeachHUD : MonoBehaviour
{
    public BeachSearch game;
    public BeachCanvas Canvas => ui;
    Font font;
    BeachCanvas ui;
    AudioSource clicker;
    AudioClip click;
    static readonly Color Mint = new Color(.51f,.91f,.76f);
    static readonly Color Gold = new Color(1f,.82f,.45f);
    static readonly Color Muted = new Color(.76f,.84f,.84f);
    static readonly Color Track = new Color(.22f,.31f,.32f);
    static readonly Style normal = new Style{size=17,anchor=TextAnchor.MiddleCenter,wrap=true};
    static readonly Style heading = new Style{size=30,weight=FontStyle.Bold,anchor=TextAnchor.MiddleCenter,wrap=true};
    static readonly Style label = new Style{size=15,anchor=TextAnchor.MiddleLeft};
    static readonly Style value = new Style{size=15,weight=FontStyle.Bold,anchor=TextAnchor.MiddleLeft};
    static readonly Style rightValue = new Style{size=15,weight=FontStyle.Bold,anchor=TextAnchor.MiddleRight};
    static readonly Style button = new Style{size=18,weight=FontStyle.Bold,anchor=TextAnchor.MiddleCenter};
    static readonly Style tips = new Style{size=15,anchor=TextAnchor.MiddleCenter,wrap=true};
    static readonly Style title = new Style{size=36,weight=FontStyle.Bold,anchor=TextAnchor.UpperLeft,wrap=true};
    static readonly Style menuCopy = new Style{size=15,anchor=TextAnchor.UpperLeft,wrap=true};
    enum MenuPage { Home, Characters, Beaches, Help }
    MenuPage menuPage;
    BeachCharacterPortraits characters;
    bool soloReady;
    int partyPage;

    void DrawCharacters(float w,float h)
    {
        var net=BeachNet.Instance;
        bool online=net && net.Online;
        game.player.FrameMainMenu(.7f);
        Fill(new Rect(0,0,w,90),new Color(.055f,.14f,.17f,.94f));
        Text(new Rect(30,15,w-60,40),"해변에 모여요",heading);
        Text(new Rect(30,56,w-60,25),"모습을 고르고 준비하세요 · 목표는 함께 바늘 찾기",tips);
        var members=new System.Collections.Generic.List<BeachNet.LobbyMember>();
        if(online) members.AddRange(net.Members);
        else members.Add(new BeachNet.LobbyMember(0,BeachNet.SelectedLook,soloReady));
        int pages=Mathf.Max(1,(members.Count+3)/4);
        partyPage=Mathf.Clamp(partyPage,0,pages-1);
        float pitch=(w-48)/4f, card=pitch-12;
        float bottom=h-138;
        for(int slot=0;slot<4;slot++)
        {
            float x=30+slot*pitch;
            int index=partyPage*4+slot;
            Panel(new Rect(x,108,card,bottom-108));
            if(index>=members.Count)
            {
                Text(new Rect(x+8,170,card-16,70),online ? "빈 자리\n친구를 기다리는 중" : "혼자서도 찾을 수 있어요",tips);
                continue;
            }
            var member=members[index];
            bool mine=!online || member.Id==net.LocalId;
            Text(new Rect(x+4,116,card-8,26),mine ? "나" : member.Id==0 ? "방장" : "친구 "+member.Id,value);
            if(online && (mine ? net.Voice.LocalTalking : net.Voice.Talking((int)member.Id)))
                StatusLine(new Rect(x+4,116,card-8,26),"말하는 중",Mint,rightValue);
            var image=characters.Get(member.Look);
            if(image) ui.Icon(new Rect(x+14,145,card-28,Mathf.Max(60,bottom-225)),image);
            Text(new Rect(x+4,bottom-77,card-8,26),BeachBody.LookName(member.Look),tips);
            StatusLine(new Rect(x+4,bottom-48,card-8,24),member.Ready ? "준비 완료" : "선택 중",member.Ready ? Mint : Gold,tips);
        }
        if(ui.Button(new Rect(30,h-122,48,40),"<",button)) { BeachNet.SelectLook(BeachNet.SelectedLook-1); soloReady=false; }
        Text(new Rect(84,h-122,174,40),BeachBody.LookName(BeachNet.SelectedLook),tips);
        if(ui.Button(new Rect(264,h-122,48,40),">",button)) { BeachNet.SelectLook(BeachNet.SelectedLook+1); soloReady=false; }
        bool ready=online ? net.LocalReady : soloReady;
        if(ui.Button(new Rect(w-212,h-122,182,42),ready ? "준비 취소" : "준비 완료",button,!online || members.Exists(m=>m.Id==net.LocalId)))
        {
            if(online) net.SetLobbyChoice(BeachNet.SelectedLook,!ready);
            else soloReady=!ready;
        }
        if(pages>1 && ui.Button(new Rect(w*.5f-65,h-122,130,40),$"동료 {partyPage+1}/{pages} >",tips)) partyPage=(partyPage+1)%pages;
        if(ui.Button(new Rect(30,h-64,140,40),online ? "방 나가기" : "돌아가기",button))
        {
            if(online) net.Leave();
            soloReady=false; menuPage=MenuPage.Home;
        }
        bool invite=online && net.CanInvite;
        if(invite && ui.Button(new Rect(182,h-64,150,40),"친구 초대",button)) net.Invite();
        if(!online || net.Hosting)
        {
            bool canStart=online ? net.AllReady : soloReady;
            if(ui.Button(new Rect(w-212,h-64,182,40),"해변 선택 →",button,canStart)) menuPage=MenuPage.Beaches;
        }
        float note=invite ? 344 : 182;
        Text(new Rect(note,h-64,w-228-note,40),online ? net.Status : "외형만 달라요 · 능력은 같아요",tips);
    }
    Texture2D needleTexture;
    Sprite needlePreview;

    void DrawNeedleGoal(float x,float width,float h)
    {
        if (!needlePreview)
        {
            // A magnified sewing needle silhouette with an open eye, independent of the buried item.
            const int tw=64, th=512;
            needleTexture=new Texture2D(tw,th,TextureFormat.RGBA32,false);
            var pixels=new Color[tw*th];
            for(int py=0;py<th;py++)
            for(int px=0;px<tw;px++)
            {
                float dx=px-31.5f;
                float radius=py<110 ? 6f*py/110f : py>478 ? 10f*Mathf.Sqrt(Mathf.Max(0,1f-Mathf.Pow((py-478)/33f,2))) : Mathf.Lerp(6f,10f,Mathf.InverseLerp(365,430,py));
                bool eye=Mathf.Pow(dx/4.5f,2)+Mathf.Pow((py-465)/27f,2)<1f;
                if(Mathf.Abs(dx)<=radius && !eye)
                    pixels[py*tw+px]=Color.Lerp(new Color(.35f,.46f,.52f),Color.white,Mathf.Clamp01(1f-Mathf.Abs(dx+2f)/12f));
            }
            needleTexture.SetPixels(pixels);
            needleTexture.Apply(false,true);
            needlePreview=Sprite.Create(needleTexture,new Rect(0,0,tw,th),new Vector2(.5f,.5f));
        }
        float center=x+width*.5f;
        float height=Mathf.Min(290,h*.46f);
        float top=(h-height)*.5f-25f;
        ui.Icon(new Rect(center-40,top,80,height),needlePreview);
        Panel(new Rect(center-Mathf.Min(180,width*.44f),top+height+18,Mathf.Min(360,width*.88f),74));
        Text(new Rect(x,top+height+23,width,30),"찾아야 할 것, 바늘 하나",button);
        Text(new Rect(x+12,top+height+53,width-24,28),"모래 속에 숨은 이 바늘을 찾아주세요.",tips);
    }

    void DrawMainMenu(float w,float h)
    {
        var session=BeachNet.Instance;
        bool inRoom=session && session.Online;
        if(inRoom && menuPage==MenuPage.Beaches && !session.AllReady) menuPage=MenuPage.Characters;
        if(menuPage==MenuPage.Characters || (inRoom && menuPage!=MenuPage.Beaches))
        {
            DrawCharacters(w,h);
            return;
        }
        float width = Mathf.Min(360f,w*.37f);
        width = Mathf.Max(288f,width);
        float left = 32f, inner = width-64f;
        float top = Mathf.Max(24f,(h-480f)*.5f+24f);
        // A quiet solid backing keeps the menu legible over bright sand and water.
        Fill(new Rect(0,0,width,h),new Color(.055f,.14f,.17f,.94f));
        Fill(new Rect(width,0,2,h),new Color(.63f,.86f,.80f,.3f));
        StatusLine(new Rect(left,top,inner,22),"F I N D  N E E D L E",Mint,label);
        Text(new Rect(left,top+34,inner,98),"모래사장에서\n바늘 찾기",title);
        float y=top+150f;
        var net=BeachNet.Instance;
        bool online=net && net.Online;
        bool typing=net && net.Typing;
        game.player.FrameMainMenu((width/w+1f)*.5f);
        DrawNeedleGoal(width,w-width,h);

        if (typing)
        {
            Text(new Rect(left,y,inner,28),"친구의 방에 참가",value);
            Text(new Rect(left,y+34,inner,45),BeachNet.SteamReady ? "Steam 친구는 초대를 수락하면 바로 들어가요.\n같은 와이파이라면 방장 주소를 입력하세요." : "방장 주소를 입력하세요.\n숫자·점 입력 / Backspace 지우기",menuCopy);
            Fill(new Rect(left,y+88,inner,44),Track);
            Text(new Rect(left+10,y+88,inner-20,44),net.Address+"_",value);
            if(ui.Button(new Rect(left,y+144,inner,46),"접속",button)) net.Join();
            if(ui.Button(new Rect(left,y+200,inner,40),"취소",button)) net.CancelTyping();
        }
        else if (menuPage==MenuPage.Help)
        {
            Text(new Rect(left,y,inner,26),"플레이 방법",value);
            Text(new Rect(left,y+36,inner,190),"WASD  이동 · 마우스  둘러보기\nShift  몸 낮추기\n좌클릭  모래 헤치기\n우클릭  모래 떠서 옮기기\nE  발견물 줍기 · 휠  도구 선택\nG  위치 표시 · 휠 버튼 꾹  감정 표현\n\n햇볕이 뜨거우면 그늘에서 쉬세요.\n한 사람이 바늘을 찾으면 모두 성공!",menuCopy);
            if(ui.Button(new Rect(left,y+232,inner,40),"돌아가기",button)) menuPage=MenuPage.Home;
        }
        else if (online || menuPage==MenuPage.Beaches)
        {
            Text(new Rect(left,y,inner,26),game.Guest ? "친구와 함께 찾기" : "수색할 해변 선택",value);
            if(game.Guest)
                Text(new Rect(left,y+40,inner,112),"방장이 해변을 고르면\n함께 수색을 시작합니다.",menuCopy);
            else
            {
                MainThemeButton(left,y+36,inner,BeachTheme.Neighborhood,"맨손으로 첫 동전 찾기");
                MainThemeButton(left,y+106,inner,BeachTheme.Tournament,"코트 옆에서 공 피하기");
                MainThemeButton(left,y+176,inner,BeachTheme.Festival,"병뚜껑 사이에서 금속 선별");
            }
            if(ui.Button(new Rect(left,y+250,inner,38),"캐릭터 선택으로",button))
            {
                menuPage=MenuPage.Characters;
            }
        }
        else
        {
            StatusLine(new Rect(left,y,inner,42),"파라솔을 수선하다 잃어버린 바늘,\n이번에는 함께 찾아볼까요?",Muted,menuCopy);
            if(ui.Button(new Rect(left,y+58,inner,50),"함께 찾기 · 방 만들기",button)) BeachNet.Ensure().Host();
            Color secondary=new Color(.78f,.9f,.9f);
            if(ui.Button(new Rect(left,y+120,inner,46),"방 참가",button,true,secondary)) BeachNet.Ensure().BeginTyping();
            if(ui.Button(new Rect(left,y+178,inner,46),"혼자 찾기",button,true,secondary)) { soloReady=false; menuPage=MenuPage.Characters; }
            if(ui.Button(new Rect(left,y+242,inner*.64f,36),"플레이 방법",tips,true,secondary)) menuPage=MenuPage.Help;
            if(ui.Button(new Rect(left+inner*.68f,y+242,inner*.32f,36),"종료",tips,true,secondary))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying=false;
#else
                Application.Quit();
#endif
            }
        }
        // Connection messages have their own space, outside the menu's action stack.
        if(net && !string.IsNullOrEmpty(net.Status))
        {
            float statusX=width+24f;
            Panel(new Rect(statusX,h-76,w-statusX-24f,52));
            Text(new Rect(statusX+12,h-72,w-statusX-48f,44),net.Status,tips);
        }
    }

    void MainThemeButton(float x,float y,float width,BeachTheme theme,string activity)
    {
        bool open=theme==BeachTheme.Neighborhood || game.ThemesUnlocked;
        if(ui.Button(new Rect(x,y,width,42),BeachSearch.ThemeName(theme),button,open)) game.Visit(theme);
        StatusLine(new Rect(x,y+44,width,24),open ? activity : "첫 바늘 발견 후 열림",Muted,label);
    }

    void Start()
    {
        font = Font.CreateDynamicFontFromOSFont(new[] {"Apple SD Gothic Neo","Malgun Gothic","Noto Sans CJK KR","Arial Unicode MS"},24);
        ui = new BeachCanvas(transform,font);
        characters = gameObject.AddComponent<BeachCharacterPortraits>();
        CreateHeatEffect();
        CreateMicIcons();
        CreateWheel();
        click = Resources.Load<AudioClip>("BeachUI/click");
        clicker = gameObject.AddComponent<AudioSource>();
        ui.Clicked += () => { if (click) clicker.PlayOneShot(click,.6f); };
    }

    // The HUD is laid out every frame, so each changing line is kept until its value changes.
    int shownSecond=-1, shownHealth=-1, shownWallet=-1, shownScanTenths=-1;
    string clock, health, wallet, signal, shownSignal, pickUp, shownTarget;

    void DrawHotbar(float w,float h)
    {
        var me=game.Local;
        var bag=game.inventory;
        const int side=48, pitch=52;
        float left=(w-(BeachInventory.SlotCount*pitch-4))*.5f;
        for(int i=0;i<BeachInventory.SlotCount;i++)
        {
            var slot=bag.Slot(i);
            if (i==0 && me.HoldingBall) slot=new BeachSlot(BeachGear.Ball);
            if (i==0 && game.Bucket.Held) slot=new BeachSlot(BeachGear.Bucket);
            if (i==0 && me.CarriedSack) slot=new BeachSlot(BeachGear.Sack);
            float x=left+i*pitch,y=h-side-7;
            Fill(new Rect(x,y,side,side),i==me.Selected ? Gold : Track);
            Fill(new Rect(x+2,y+2,side-4,side-4),new Color(.09f,.15f,.17f));
            if (slot.Empty) continue;
            var portrait=me.SlotIcon(slot);
            if (portrait) ui.Icon(new Rect(x+3,y+3,side-6,side-6),portrait);
            else Text(new Rect(x+2,y+2,side-4,side-4),slot.Label,tips);
            if (i==0 && me.CarriedSack) StatusLine(new Rect(x+4,y+26,40,20),me.CarriedSack.Count.ToString(),Color.white,rightValue);
        }
        string name=me.CarriedSack ? $"수거 자루 {me.CarriedSack.Count}/{BeachSack.Capacity}" : me.HoldingBall ? "비치발리볼" :
            game.Bucket.Held ? "모래성 양동이" : bag.Hand is FindKind held ? BeachFind.NameOf(held)+" (손)" : me.Equipped.Label;
        string action=me.Equipped.Gear switch {
            BeachGear.Trowel => me.Loaded ? "좌클릭 쏟기" : "좌클릭 뜨기",
            BeachGear.Detector => "Q 탐색 · 좌클릭 모종삽", BeachGear.Magnet => "F 삽 속 선별 · 금속함에서 E",
            BeachGear.Rake => "좌클릭 표면 훑기", BeachGear.Parasol => "좌클릭 그늘 꽂기",
            _ => me.Equipped.Find is FindKind find ?
                (BeachFind.IsTrashKind(find) ? "수거함에서 E 넣기" : "매입소에서 판매") : "좌클릭 헤치기 · 우클릭 모래 운반" };
        if (me.HoldingBall) action="좌클릭 던지기";
        if (bag.Hand.HasValue) action="자루에 E로 넣기 · 쓰레기는 수거함";
        if (me.CarriedSack) action="E 내려놓기 · 매입소 B 판매 · 수거함 E 분리배출";
        if (game.Bucket.Held) action="우클릭 모래 담기 · 좌클릭 탑 찍기 · E 내려놓기";
        Panel(new Rect((w-500)/2,h-88,500,28));
        // This player's own sign (keys 1-4) takes the line for a moment, since their head is out of view.
        string signing = game.player.Body ? BeachBody.Said(game.player.Body.Showing) : null;
        if (signing!=null) StatusLine(new Rect((w-500)/2,h-88,500,28),"손짓   "+signing,Gold,tips);
        else Text(new Rect((w-500)/2,h-88,500,28),game.player.Captured ?
            $"{name}   ·   {action}     |     휠 선택" : "클릭하여 시점 조작 재개",tips);
    }

    void Panel(Rect rect) => ui.Panel(rect);
    void Fill(Rect rect,Color color) => ui.Fill(rect,color);
    void StatusLine(Rect rect,string text,Color color,Style style) => ui.Label(rect,text,style,color);
    void Text(Rect rect,string text,Style style) => ui.Label(rect,text,style,Color.white);

    void DrawPlayerStatus(float w)
    {
        var bag = game.inventory;
        Panel(new Rect(12,12,212,102));
        StatusLine(new Rect(23,16,193,22),BeachSearch.ThemeName(game.Theme),Muted,label);
        int second = (int)game.Elapsed;
        if (second!=shownSecond) { shownSecond = second; clock = BeachSearch.Clock(second); }
        StatusLine(new Rect(99,16,114,22),clock,Muted,rightValue);
        StatusLine(new Rect(23,40,80,22),"체력",Color.white,label);
        if (game.player.Health!=shownHealth) { shownHealth = game.player.Health; health = $"{shownHealth} / {BeachWalker.MaxHealth}"; }
        StatusLine(new Rect(99,40,114,22),health,Color.white,rightValue);
        var track = new Rect(23,64,190,6);
        Fill(track,Track);
        track.width *= (float)game.player.Health / BeachWalker.MaxHealth;
        Fill(track,Mint);
        StatusLine(new Rect(23,77,80,24),game.Outbox!=null ? "팀 골드" : "골드",Gold,label);
        if (bag.Wallet!=shownWallet) { shownWallet = bag.Wallet; wallet = $"{shownWallet} G"; }
        StatusLine(new Rect(99,77,114,24),wallet,Gold,rightValue);
        // Heat is deliberately absent here; the player reads it from the mender's skin.

        if (!game.Local.HoldingBall && game.Local.Equipped.Gear==BeachGear.Detector)
        {
            const float width=218,signalY=12;
            float x=w-width-12;
            Panel(new Rect(x,signalY,width,32));
            // The countdown is shown to a tenth of a second, so the line changes ten times a second at most.
            float wait = game.Local.ScanWait;
            int tenths = wait>0 ? Mathf.RoundToInt(wait*10) : -1;
            if (signal==null || tenths!=shownScanTenths || !ReferenceEquals(game.Local.Signal,shownSignal))
            {
                shownScanTenths = tenths;
                shownSignal = game.Local.Signal;
                signal = shownSignal ?? "Q  금속 탐색";
                if (tenths>=0) signal += $"   {tenths/10}.{tenths%10}초";
            }
            Text(new Rect(x,signalY,width,32),signal,normal);
        }
    }
    static readonly FindKind[] tradeGoods = {FindKind.Coin, FindKind.Bracelet, FindKind.Ring};

    // What has been brought to the stall: carried here, set down beside it, or in hand.
    readonly System.Collections.Generic.Dictionary<FindKind,int> atStall = new();

    void DrawShop(float w,float h)
    {
        var bag = game.inventory;
        const float tall = 486;
        float x = (w-732)/2, y = (h-tall)/2;
        Panel(new Rect(x,y,732,tall));
        Text(new Rect(x+24,y+10,145,46),"조개 매입소",heading);
        StatusLine(new Rect(x+355,y+17,242,30),$"{(game.Outbox!=null ? "팀 골드" : "보유 골드")}   {bag.Wallet} G",Gold,rightValue);
        if (ui.Button(new Rect(x+617,y+13,92,40),"닫기",button)) game.Local.CloseShop();
        Fill(new Rect(x+22,y+65,687,1),Muted);
        Fill(new Rect(x+350,y+83,1,342),Muted);
        StatusLine(new Rect(x+24,y+80,300,30),"가져온 자루 판매",Mint,value);
        StatusLine(new Rect(x+372,y+80,337,30),"장비 구매",Mint,value);

        atStall.Clear();
        int sacks = 0;
        foreach (var sack in game.SacksAtStall)
        {
            sacks++;
            foreach (var pair in sack.Contents) { atStall.TryGetValue(pair.Key,out int n); atStall[pair.Key] = n+pair.Value; }
        }
        if (bag.Hand is FindKind held && held!=FindKind.Needle) { atStall.TryGetValue(held,out int n); atStall[held] = n+1; }
        // Every kind of litter sells for the same price, so the shop lists it as one row.
        int litter = 0;
        foreach (var kind in BeachFind.Trash)
            if (atStall.TryGetValue(kind,out int brought)) litter += brought;
        float rowY = y+119;
        foreach (var kind in tradeGoods)
        {
            atStall.TryGetValue(kind,out int count);
            Text(new Rect(x+24,rowY,106,30),BeachFind.NameOf(kind),label);
            Text(new Rect(x+132,rowY,197,30),$"개당 {BeachFind.PriceOf(kind)} G    {count}개",rightValue);
            rowY += 24;
        }
        Text(new Rect(x+24,rowY,106,30),"쓰레기",label);
        Text(new Rect(x+132,rowY,197,30),$"개당 1 G    {litter}개",rightValue);
        StatusLine(new Rect(x+24,rowY+30,304,26),sacks>0 ? $"매입소 앞 자루 {sacks}개" : "자루를 매입소 앞에 가져오자",sacks>0 ? Color.white : Muted,label);
        int worth = game.Local.SaleValue;
        if (ui.Button(new Rect(x+24,y+271,304,46),$"모두 판매    +{worth} G",button,worth>0)) game.Local.Sell();
        Text(new Rect(x+24,y+324,304,40),"쓰레기는 매입소 옆 수거함에 E · 개당 2 G",tips);

        DrawGear(x+372,y+116,"모종삽","더 넓고 깊게 뜨기",BeachInventory.TrowelPrice,true,bag.HasTrowel);
        DrawGear(x+372,y+168,"탐지기","전방이나 삽 위의 금속 탐색",BeachInventory.DetectorPrice,bag.Theme!=BeachTheme.Neighborhood,bag.HasDetector);
        DrawGear(x+372,y+220,"선별 자석","삽 위에서 철제 선별",BeachInventory.MagnetPrice,bag.Theme==BeachTheme.Festival,bag.HasMagnet);
        DrawParasol(x+372,y+272);
        Text(new Rect(x+372,y+324,196,24),"모래 갈퀴   2 G",value);
        StatusLine(new Rect(x+372,y+349,212,24),"서서 표면 훑고 쓰레기 끌기",Muted,label);
        bool canBuy = !bag.HasRake && bag.HasSpace && !game.Local.Loaded && bag.Wallet>=BeachInventory.RakePrice;
        if (ui.Button(new Rect(x+584,y+326,125,42),bag.HasRake ? "보유 중" : game.Local.Loaded ? "모래 운반 중" :
            !bag.HasSpace ? "하단바 가득" : canBuy ? "구매" : "골드 부족",button,canBuy)) game.Local.BuyRake();
        Text(new Rect(x+372,y+376,196,24),$"수거 자루   {BeachSack.Price} G",value);
        StatusLine(new Rect(x+372,y+401,212,24),$"함께 쓰는 자루 · 지금 {game.Sacks.Count}/{BeachSack.MostSacks}개",Muted,label);
        bool room = game.Sacks.Count<BeachSack.MostSacks, sackAffordable = bag.Wallet>=BeachSack.Price;
        if (ui.Button(new Rect(x+584,y+378,125,42),!room ? "최대" : sackAffordable ? "구매" : "골드 부족",button,room && sackAffordable)) game.Local.BuySack();
        Fill(new Rect(x+22,y+433,687,1),Muted);
        string status = game.Local.Message;
        if (string.IsNullOrEmpty(status)) status = game.Local.Loaded ? "장비를 사려면 들고 있는 모래를 먼저 내려놓으세요." : "Esc   해변으로 돌아가기";
        StatusLine(new Rect(x+24,y+442,686,30),status,Color.white,label);
    }

    void DrawGear(float x,float y,string name,string use,int price,bool offered,bool owned)
    {
        var bag = game.inventory;
        Text(new Rect(x,y,196,24),$"{name}   {price} G",value);
        StatusLine(new Rect(x,y+25,212,24),use,Muted,label);
        bool available = !owned && offered && bag.NextTool==name && bag.NextPrice==price;
        string action = owned ? "보유 중" : !offered ? "다른 해변 장비" :
            !available ? "이전 장비 필요" : !bag.HasSpace ? "하단바 가득" : game.Local.Loaded ? "모래 운반 중" : bag.Wallet<price ? "골드 부족" : "구매";
        if (ui.Button(new Rect(x+212,y+2,125,42),action,button,available && bag.HasSpace && !game.Local.Loaded && bag.Wallet>=price)) game.Local.Buy();
    }

    void DrawParasol(float x,float y)
    {
        var bag = game.inventory;
        Text(new Rect(x,y,196,24),$"휴대용 파라솔   {BeachInventory.ParasolPrice} G",value);
        StatusLine(new Rect(x,y+25,212,24),"T로 원하는 곳에 그늘 꽂기",Muted,label);
        bool affordable = bag.Wallet>=BeachInventory.ParasolPrice;
        string action = bag.HasParasol ? "보유 중" : !bag.HasSpace ? "하단바 가득" : affordable ? "구매" : "골드 부족";
        if (ui.Button(new Rect(x+212,y+2,125,42),action,button,!bag.HasParasol && bag.HasSpace && affordable)) game.Local.BuyParasol();
    }

    // Under the status panel in co-op: this player's microphone. Over teammates who are speaking: a mark,
    // so a voice can be matched to a figure.
    void DrawVoice(float w,float h)
    {
        var net = BeachNet.Instance;
        if (!net || !net.Online) return;
        var voice = net.Voice;
        DrawMic(w,h,voice);
        var view = game.player.view;
        float across = w/Mathf.Max(1,view.pixelWidth), down = h/Mathf.Max(1,view.pixelHeight);
        foreach (var (id,figure) in net.Figures())
        {
            if (!voice.Talking(id)) continue;
            Vector3 point = view.WorldToScreenPoint(figure.position+Vector3.up*1.85f);
            if (point.z<=0) continue;
            // Kept on screen, so someone standing close, head out of view, still shows as speaking.
            float x = Mathf.Clamp(point.x*across,70,w-70), y = Mathf.Clamp(h-point.y*down,22,h-130);
            Panel(new Rect(x-56,y-15,112,30));
            StatusLine(new Rect(x-56,y-15,112,30),"말하는 중",Mint,tips);
        }
    }

    // Over a teammate making a sign (keys 1-4): what it says, above the speaking mark if both show.
    void DrawSigns(float w,float h)
    {
        var net = BeachNet.Instance;
        if (!net || !net.Online) return;
        var view = game.player.view;
        float across = w/Mathf.Max(1,view.pixelWidth), down = h/Mathf.Max(1,view.pixelHeight);
        foreach (var (id,figure) in net.Figures())
        {
            var ghost = figure.GetComponent<BeachGhost>();
            string said = ghost && ghost.Body ? BeachBody.Said(ghost.Body.Showing) : null;
            if (said==null) continue;
            Vector3 point = view.WorldToScreenPoint(figure.position+Vector3.up*1.72f);
            if (point.z<=0) continue;
            float x = Mathf.Clamp(point.x*across,70,w-70), y = Mathf.Clamp(h-point.y*down,22,h-130);
            Bubble(x,net.Voice.Talking(id) ? y-34 : y,said);
        }
    }

    void Bubble(float x,float y,string said)
    {
        Panel(new Rect(x-44,y-15,88,30));
        StatusLine(new Rect(x-44,y-15,88,30),said,Gold,tips);
    }

    // Right of the hotbar: this player's microphone. Crossed out while it is off (V), lit while speaking.
    // Without Steam there is no voice at all, so it stays crossed out and says why.
    public const float MicGap = 8;
    static readonly Style micKey = new Style{size=12,weight=FontStyle.Bold,anchor=TextAnchor.MiddleCenter};
    void DrawMic(float w,float h,BeachVoice voice)
    {
        const int side=48, pitch=52;
        float x=(w-(BeachInventory.SlotCount*pitch-4))*.5f+BeachInventory.SlotCount*pitch-4+MicGap, y=h-side-7;
        bool can = BeachVoice.Available, off = !can || voice.Muted, talking = !off && voice.LocalTalking;
        Fill(new Rect(x,y,side,side),talking ? Mint : off ? new Color(.62f,.24f,.24f) : Track);
        Fill(new Rect(x+2,y+2,side-4,side-4),new Color(.09f,.15f,.17f));
        // The mic above, its key below, inside the one slot.
        ui.Icon(new Rect(x+10,y+3,side-20,side-20),off ? micOffIcon : talking ? micTalkingIcon : micIcon);
        if (off) ui.Icon(new Rect(x+8,y+2,side-16,side-16),crossIcon);
        StatusLine(new Rect(x,y+side-18,side,16),can ? "V" : "Steam",off ? Muted : talking ? Mint : Color.white,micKey);
    }

    Sprite micIcon, micTalkingIcon, micOffIcon, crossIcon;

    void CreateMicIcons()
    {
        micIcon = Painted(MicShape,Color.white);
        micTalkingIcon = Painted(MicShape,Mint);
        micOffIcon = Painted(MicShape,new Color(.55f,.6f,.6f));
        crossIcon = Painted(Cross,new Color(.95f,.32f,.3f));
    }

    // A 64-pixel icon from a distance function: inside where it is below zero, softened over a pixel.
    static Sprite Painted(System.Func<Vector2,float> shape,Color ink) => Painted(shape,ink,64);

    static Sprite Painted(System.Func<Vector2,float> shape,Color ink,int side)
    {
        var texture = new Texture2D(side,side,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
        var pixels = new Color[side*side];
        for (int y=0; y<side; y++)
        for (int x=0; x<side; x++)
        {
            float inside = Mathf.Clamp01(.5f-shape(new Vector2(x+.5f,y+.5f)));
            pixels[y*side+x] = new Color(ink.r,ink.g,ink.b,inside*ink.a);
        }
        texture.SetPixels(pixels);
        texture.Apply(false,true);
        return Sprite.Create(texture,new Rect(0,0,side,side),Vector2.one*.5f);
    }

    static float Segment(Vector2 p,Vector2 a,Vector2 b)
    {
        Vector2 ab = b-a;
        float t = Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude);
        return Vector2.Distance(p,a+ab*t);
    }

    // A microphone: a rounded head, the U of its holder, a stem and a foot.
    static float MicShape(Vector2 p)
    {
        float head = Segment(p,new Vector2(32,33),new Vector2(32,47))-8.5f;
        float ring = Mathf.Abs(Vector2.Distance(p,new Vector2(32,35))-15f)-2.2f;
        if (p.y>35) ring = Mathf.Min(Vector2.Distance(p,new Vector2(17,35)),Vector2.Distance(p,new Vector2(47,35)))-2.2f;
        float stem = Segment(p,new Vector2(32,20),new Vector2(32,13))-2.2f;
        float foot = Segment(p,new Vector2(23,12),new Vector2(41,12))-2.4f;
        return Mathf.Min(Mathf.Min(head,ring),Mathf.Min(stem,foot));
    }

    static float Cross(Vector2 p) =>
        Mathf.Min(Segment(p,new Vector2(10,10),new Vector2(54,54)),Segment(p,new Vector2(10,54),new Vector2(54,10)))-3.6f;

    // Over the needle moment: who found it, then the search time stamped large. No buttons, so a clip of
    // these seconds is clean.
    void DrawMoment(float w,float h)
    {
        var moment = game.Moment;
        float t = moment.Elapsed;
        bool solo = game.Outbox==null;
        // The line at the top gives way to the stamped time.
        if ((!solo || t>=NeedleMoment.Reveal) && t<NeedleMoment.Stamp)
        {
            string who = moment.FoundHere ? "바늘을 찾았다!" : FinderName(moment.Finder)+" 바늘을 찾았다!";
            float pop = Pop(t-(solo ? NeedleMoment.Reveal : .15f));
            if (pop>0) StatusLine(new Rect(0,h*.1f,w,64),who,Gold,new Style{size=Mathf.RoundToInt(40*pop),weight=FontStyle.Bold,anchor=TextAnchor.MiddleCenter});
        }
        if (t<NeedleMoment.Stamp) return;
        float stamp = Pop(t-NeedleMoment.Stamp);
        float y = h*.36f;
        // A dark band, so the numbers read over the figure behind them.
        float band = game.NewRecord && t>=NeedleMoment.Record || t>=NeedleMoment.Stamp+.35f ? 214 : 130;
        Fill(new Rect(0,y-8,w,band),new Color(.03f,.08f,.1f,.42f*Mathf.Clamp01((t-NeedleMoment.Stamp)/.12f)));
        Text(new Rect(0,y,w,30),"수색 시간",new Style{size=22,weight=FontStyle.Bold,anchor=TextAnchor.MiddleCenter});
        StatusLine(new Rect(0,y+28,w,96),BeachSearch.Clock(game.Elapsed),Color.white,
            new Style{size=Mathf.RoundToInt(84*stamp),weight=FontStyle.Bold,anchor=TextAnchor.MiddleCenter});
        if (game.NewRecord && t>=NeedleMoment.Record)
        {
            float record = Pop(t-NeedleMoment.Record);
            StatusLine(new Rect(0,y+124,w,44),"신기록!",Gold,new Style{size=Mathf.RoundToInt(34*record),weight=FontStyle.Bold,anchor=TextAnchor.MiddleCenter});
            ui.Icon(new Rect(w*.5f-112,y+134,26,26),ui.Star);
            ui.Icon(new Rect(w*.5f+86,y+134,26,26),ui.Star);
        }
        int tidied = 0;
        foreach (var count in game.Cleaned.Values) tidied += count;
        if (tidied>0 && t>=NeedleMoment.Stamp+.35f)
            StatusLine(new Rect(0,y+172,w,32),$"바늘 하나 찾는 동안 해변 쓰레기 {tidied}개를 치웠다",Mint,
                new Style{size=20,weight=FontStyle.Bold,anchor=TextAnchor.MiddleCenter});
    }

    // Text that lands a little too big and settles, over 0.18 seconds. 0 before it starts.
    static float Pop(float since)
    {
        if (since<0) return 0;
        float k = Mathf.Clamp01(since/.18f);
        return Mathf.Lerp(1.45f,1f,k*k*(3-2*k));
    }

    // "방장이", "친구 2가": the name with the subject particle its last sound takes.
    static string FinderName(int id)
    {
        if (id==0) return "방장이";
        int last = id%10;
        bool closed = last is 0 or 1 or 3 or 6 or 7 or 8;
        return "친구 "+id+(closed ? "이" : "가");
    }

    void DrawResult(float x,float y)
    {
        float best = BeachSearch.BestTime(game.Theme);
        string time = "수색 시간  "+BeachSearch.Clock(game.Elapsed)+"     "+
            (game.NewRecord ? "새 최고 기록!" : "최고 기록  "+BeachSearch.Clock(best));
        StatusLine(new Rect(x,y,640,26),time,game.NewRecord ? Gold : Color.white,normal);
        if (game.NewRecord)
        {
            ui.Icon(new Rect(x+150,y+1,24,24),ui.Star);
            ui.Icon(new Rect(x+466,y+1,24,24),ui.Star);
        }
        StatusLine(new Rect(x,y+27,640,26),$"주운 발견물  {game.Picked} / {game.TotalFinds}개     판매 수익  {game.Earned} G",Color.white,normal);
        string tidy = "";
        int tidied = 0, kinds = 0;
        foreach (var kind in BeachFind.Trash)
            if (game.Cleaned.TryGetValue(kind,out int count))
            {
                tidy += (tidy.Length>0 ? " · " : "")+BeachFind.NameOf(kind)+" "+count+"개";
                tidied += count;
                kinds++;
            }
        // A long list would run off the panel, so it falls back to totals.
        if (tidy.Length>34) tidy = $"{tidied}개 ({kinds}종)";
        int left = game.TotalFinds-game.Picked;
        string note = tidy.Length>0 ? "치운 쓰레기  "+tidy :
            left>0 ? $"모래 속에 아직 {left}개가 남아 있다" : "해변의 물건을 모두 찾아냈다";
        StatusLine(new Rect(x,y+54,640,26),note+$"   ·   직접 분리배출 {game.Sorted}개",Mint,normal);
    }

    // Under the beach buttons: open a room, join one by address, or see who is here.
    void DrawOnline(float x,float y)
    {
        var net = BeachNet.Instance;
        if (net && net.Online)
        {
            bool invite = net.CanInvite;
            Text(new Rect(x+12,y,invite ? 318 : 468,40),net.Status,tips);
            if (invite && ui.Button(new Rect(x+338,y,140,40),"친구 초대",button)) net.Invite();
            if (ui.Button(new Rect(x+488,y,140,40),net.Hosting ? "방 닫기" : "나가기",button)) net.Leave();
        }
        else if (net && net.Typing)
        {
            Text(new Rect(x+12,y,318,40),"방장 주소  "+net.Address+"_",normal);
            if (ui.Button(new Rect(x+338,y,140,40),"접속",button)) net.Join();
            if (ui.Button(new Rect(x+488,y,140,40),"취소",button)) net.CancelTyping();
        }
        else
        {
            if (ui.Button(new Rect(x+12,y,150,40),"방 만들기",button)) BeachNet.Ensure().Host();
            if (ui.Button(new Rect(x+170,y,150,40),"방 참가",button)) BeachNet.Ensure().BeginTyping();
            Text(new Rect(x+328,y,300,40),net ? net.Status : "친구와 함께 찾기",tips);
        }
    }

    void ThemeButton(float x,float y,BeachTheme theme,string activity)
    {
        bool open = (theme==BeachTheme.Neighborhood || game.ThemesUnlocked) && !game.Guest;
        if(ui.Button(new Rect(x,y,200,48),BeachSearch.ThemeName(theme),button,open)) game.Visit(theme);
        string tip=game.Guest ? "방장이 고른다" : !open ? "첫 바늘 발견 후 열림" : activity;
        float best=BeachSearch.BestTime(theme);
        if(best>0) tip+="\n최고 기록 "+BeachSearch.Clock(best);
        Text(new Rect(x,y+52,200,46),tip,tips);
    }

    // The result panel's height; the needle moment ends on the same framing the panel uses.
    const float tall = 336;

    void LateUpdate()
    {
        if (!game || ui==null) return;
        UpdateHeatEffect();
        Vector2 size = ui.Begin();
        float w=size.x, h=size.y;
        if (game.Moment) game.Moment.PortraitFree = 1f-(tall+12f)/h;
        if (game.Local.Collapsed)
        {
            // At the bottom, so the fallen mender stays in view above it.
            float x=(w-484)/2, y=h-204-12;
            Panel(new Rect(x,y,484,204));
            bool heatstroke = game.player.Heat>=1f;
            Text(new Rect(x,y+17,484,45),heatstroke ? "더위를 먹고 쓰러졌다" : "체력이 다해 쓰러졌다",heading);
            Text(new Rect(x+20,y+66,444,62),heatstroke ? "온몸이 새빨갛게 달아올라 있었다.\n살갗이 붉어지면 파라솔 그늘에서 식히자." :
                "해변에서 잠시 쉬어 가자.\n날아오는 공을 조심하자.",normal);
            // In a session the beach cannot restart for one player, so they get back up instead.
            if (game.Outbox!=null) { if(ui.Button(new Rect(x+130,y+140,224,46),"다시 일어나기",button)) game.Local.Revive(); }
            else if(ui.Button(new Rect(x+130,y+140,224,46),"이 해변 다시 시작",button)) game.Visit(game.Theme);
        }
        else if (!game.Started) DrawMainMenu(w,h);
        else if (game.Won && game.Moment.Playing) DrawMoment(w,h);
        else if (game.Won)
        {
            // The panel sits at the bottom so the mender stays in view above it.
            float x=(w-640)/2, y=h-tall-12;
            Panel(new Rect(x,y,640,tall));
            Text(new Rect(x,y+12,640,45),"바늘을 찾았다!",heading);
            DrawResult(x,y+62);
            float buttons = y+156;
            ThemeButton(x+12,buttons,BeachTheme.Neighborhood,"맨손으로 첫 동전 찾기");
            ThemeButton(x+220,buttons,BeachTheme.Tournament,"코트 옆에서 공 피하기");
            ThemeButton(x+428,buttons,BeachTheme.Festival,"병뚜껑 사이에서 금속 선별");
            DrawOnline(x,y+tall-50);
            game.player.FramePortrait(1f-(tall+12f)/h);
        }
        else if (game.Local.ShopOpen) DrawShop(w,h);
        else
        {
            DrawHotbar(w,h);
            DrawPlayerStatus(w);
            DrawVoice(w,h);
            DrawSigns(w,h);
            if(game.player.Captured && game.Local.NearShop && !game.Local.HoldingBall && !game.Bucket.Held && game.Local.InteractionPrompt==null)
            {
                Panel(new Rect((w-340)/2,h-130,340,37));
                Text(new Rect((w-340)/2,h-130,340,37),"조개 매입소     B  상점 열기",normal);
            }
            if(!string.IsNullOrEmpty(game.Local.Message)){
                Panel(new Rect((w-372)/2,h-170,372,36));
                Text(new Rect((w-372)/2,h-170,372,36),game.Local.Message,normal);
            }
            // While the emote wheel is open it takes the middle of the screen, crosshair and prompts included.
            if (game.Local.Wheel.Open) DrawWheel(w,h);
            else DrawCrosshair(w,h);
        }
        ui.End();
    }

    void DrawCrosshair(float w,float h)
    {
        Text(new Rect(w/2-8,h/2-12,16,24),"·",normal);
        if(game.Local.InteractionPrompt!=null) Text(new Rect(w/2-160,h/2+14,320,30),game.Local.InteractionPrompt,normal);
        else if(game.Local.BallInReach) Text(new Rect(w/2-140,h/2+14,280,30),game.Local.Loaded ? "모래를 내려놓고 공 잡기" : "E  비치발리볼 잡기",normal);
        else if(game.Local.TargetName!="")
        {
            if (!ReferenceEquals(game.Local.TargetName,shownTarget)) { shownTarget = game.Local.TargetName; pickUp = "E  "+shownTarget+" 줍기"; }
            Text(new Rect(w/2-100,h/2+14,200,30),pickUp,normal);
        }
    }
    UnityEngine.UI.RawImage haze;
    Texture2D hazeTexture;
    AudioSource breathing;
    AudioClip breath;
    float dizziness;

    void CreateHeatEffect()
    {
        var overlay = new GameObject("더위로 흐려지는 시야",typeof(RectTransform));
        haze = overlay.AddComponent<UnityEngine.UI.RawImage>();
        haze.rectTransform.SetParent(ui.Canvas.transform,false);
        haze.rectTransform.anchorMin = Vector2.zero;
        haze.rectTransform.anchorMax = Vector2.one;
        haze.rectTransform.offsetMin = haze.rectTransform.offsetMax = Vector2.zero;
        haze.raycastTarget = false;
        const int side = 64;
        hazeTexture = new Texture2D(side,side,TextureFormat.RGBA32,false);
        hazeTexture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[side*side];
        for (int y=0;y<side;y++)
        for (int x=0;x<side;x++)
        {
            float radius = new Vector2(2f*x/(side-1)-1,2f*y/(side-1)-1).magnitude;
            pixels[y*side+x] = new Color(1f,.77f,.64f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.49f,1.2f,radius)));
        }
        hazeTexture.SetPixels(pixels);
        hazeTexture.Apply(false,true);
        haze.texture = hazeTexture;
        haze.enabled = false;

        const int rate = 12000;
        var samples = new float[rate*3];
        var noise = new System.Random(23);
        float filtered = 0;
        for (int i=0;i<samples.Length;i++)
        {
            filtered = Mathf.Lerp(filtered,(float)noise.NextDouble()*2-1,.15f);
            float envelope = Mathf.Sin(2*Mathf.PI*i/samples.Length);
            samples[i] = filtered*envelope*envelope*(envelope>0 ? .65f : 1f);
        }
        breath = AudioClip.Create("더위에 가빠진 숨",samples.Length,1,rate,false);
        breath.SetData(samples,0);
        breathing = gameObject.AddComponent<AudioSource>();
        breathing.clip = breath;
        breathing.loop = true;
        breathing.playOnAwake = false;
    }

    void UpdateHeatEffect()
    {
        if (game.Moment && game.Moment.Playing)
        {
            // The edges close in while the needle comes up.
            haze.enabled = true;
            haze.color = new Color(0,0,0,.92f*game.Moment.Vignette);
            if (breathing.isPlaying) breathing.Stop();
            return;
        }
        bool searching = game.Started && !game.Won && !game.Local.Collapsed && !game.Local.ShopOpen;
        float rate = game.player.HeatDamageRate;
        float strength = rate>0 ? Mathf.Clamp01(.35f + rate*.08f) : 0;
        dizziness = searching ? Mathf.MoveTowards(dizziness,strength,Time.deltaTime*.7f) : 0;
        haze.enabled = dizziness>0;
        if (!haze.enabled)
        {
            if (breathing.isPlaying) breathing.Stop();
            return;
        }
        haze.color = new Color(1,1,1,dizziness*(.35f+.1f*Mathf.Sin(Time.time*2.1f)));
        breathing.volume = dizziness*.55f;
        if (!breathing.isPlaying) breathing.Play();
    }

    void OnDestroy()
    {
        if (needlePreview) Destroy(needlePreview);
        if (needleTexture) Destroy(needleTexture);
        if (font) Destroy(font);
        if (hazeTexture) Destroy(hazeTexture);
        foreach (var icon in new[]{micIcon,micTalkingIcon,micOffIcon,crossIcon})
            if (icon) { Destroy(icon.texture); Destroy(icon); }
        if (breath) Destroy(breath);
    }
}
