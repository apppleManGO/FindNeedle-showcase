using UnityEngine;

// New kinds go at the end: the scene stores these as numbers.
public enum FindKind { Coin, Bracelet, Ring, Needle, Cap, PullTab, FestivalBand, Hairpin, Toothpick, PopsicleStick, PlasticCap, FishingNet,
    CigaretteButt, PlasticBottle, Straw, SnackWrapper, FoamChunk, PlasticBag, RopeScrap }

public enum WasteBin { Metal, Bottle, General }

public sealed class BeachFind : MonoBehaviour
{
    public static void AttachLitterModel(FindKind kind,Transform parent)
    {
        var model = Instantiate(Resources.Load<GameObject>("BeachProps/"+kind),parent,false);
        model.GetComponentInChildren<MeshRenderer>().sharedMaterial = Resources.Load<Material>("BeachProps/LitterFinish");
    }

    public FindKind kind;
    public GameObject traces;
    [Tooltip("Height of the top edge above the object's centre")] public float topOffset;
    public Vector2 Position => new Vector2(transform.position.x, transform.position.z);
    public bool Exposed(SandPatch sand) => sand.Height(Position) <= transform.position.y + topOffset;
    // What the detector answers to. Everything added as marine litter is plastic, paper or foam.
    public bool IsMetal => kind is FindKind.Coin or FindKind.Bracelet or FindKind.Ring or FindKind.Needle or FindKind.Cap or FindKind.PullTab or FindKind.Hairpin;
    // What the sorting magnet lifts out of a scoop.
    public bool IsIron => Magnetic(kind);
    public static bool Magnetic(FindKind kind) => kind is FindKind.Needle or FindKind.Cap or FindKind.Hairpin;
    public bool IsTrash => IsTrashKind(kind);
    public static readonly FindKind[] Trash = {FindKind.Cap, FindKind.PullTab, FindKind.FestivalBand,
        FindKind.Hairpin, FindKind.Toothpick, FindKind.PopsicleStick, FindKind.PlasticCap, FindKind.FishingNet,
        FindKind.CigaretteButt, FindKind.PlasticBottle, FindKind.Straw, FindKind.SnackWrapper, FindKind.FoamChunk, FindKind.PlasticBag, FindKind.RopeScrap};
    public static bool IsTrashKind(FindKind kind) => System.Array.IndexOf(Trash,kind)>=0;
    // These are the three collection streams offered by the in-game beach stall.
    public static WasteBin BinFor(FindKind kind) => kind switch {
        FindKind.Cap or FindKind.PullTab or FindKind.Hairpin => WasteBin.Metal,
        FindKind.PlasticBottle => WasteBin.Bottle,
        _ => WasteBin.General };
    public bool CanHook => kind is FindKind.PlasticBottle or FindKind.SnackWrapper or FindKind.PlasticBag
        or FindKind.FoamChunk or FindKind.RopeScrap or FindKind.PopsicleStick;
    public string Label => NameOf(kind);
    // The name with 을 or 를, as its last syllable takes: "반지를", "동전을".
    public static string NameWithObject(FindKind kind) => WithObject(NameOf(kind));
    public static string WithObject(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        char last = word[word.Length-1];
        bool closed = last>='가' && last<='힣' && (last-'가')%28!=0;
        return word+(closed ? "을" : "를");
    }

    public static string NameOf(FindKind kind) => kind switch {
        FindKind.Coin => "동전", FindKind.Bracelet => "팔찌", FindKind.Ring => "반지", FindKind.Cap => "병뚜껑", FindKind.PullTab => "캔 풀탭", FindKind.FestivalBand => "축제 입장 팔찌",
        FindKind.Hairpin => "머리핀", FindKind.Toothpick => "이쑤시개", FindKind.PopsicleStick => "아이스크림 막대", FindKind.PlasticCap => "플라스틱 뚜껑", FindKind.FishingNet => "폐어망",
        FindKind.CigaretteButt => "담배꽁초", FindKind.PlasticBottle => "페트병", FindKind.Straw => "빨대", FindKind.SnackWrapper => "과자봉지",
        FindKind.FoamChunk => "스티로폼 조각", FindKind.PlasticBag => "비닐봉지", FindKind.RopeScrap => "밧줄 토막", _ => "바늘" };
    public int Price => PriceOf(kind);
    public static int PriceOf(FindKind kind) => kind switch {
        FindKind.Coin => 2, FindKind.Bracelet => 5, FindKind.Ring => 4, FindKind.Needle => 0, _ => 1 };
}
