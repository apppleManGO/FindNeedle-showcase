using System.Collections.Generic;
using UnityEngine;

// Sack is appended last so the wire codes of the others stay as they were.
public enum BeachGear { None, Hands, Trowel, Detector, Magnet, Rake, Parasol, Bucket, Ball, Sack }

public readonly struct BeachSlot : System.IEquatable<BeachSlot>
{
    public readonly BeachGear Gear;
    public readonly FindKind? Find;
    public BeachSlot(BeachGear gear) { Gear=gear; Find=null; }
    public BeachSlot(FindKind kind) { Find=kind; Gear=BeachGear.None; }
    public bool Empty => Gear==BeachGear.None && !Find.HasValue;
    public bool Shovel => Gear is BeachGear.Trowel or BeachGear.Detector or BeachGear.Magnet;
    // One byte for the wire: gear below 100, a find as 100 plus its kind.
    public byte Code => Find.HasValue ? (byte)(100+(int)Find.Value) : (byte)Gear;
    public static BeachSlot FromCode(byte code) =>
        code>=100 ? new BeachSlot((FindKind)(code-100)) :
        code<=(byte)BeachGear.Sack ? new BeachSlot((BeachGear)code) : default;
    public bool Equals(BeachSlot other) => Gear==other.Gear && Find==other.Find;
    public override bool Equals(object obj) => obj is BeachSlot other && Equals(other);
    public override int GetHashCode() => Find.HasValue ? 100+(int)Find.Value : (int)Gear;
    public string Label => Find.HasValue ? BeachFind.NameOf(Find.Value) : Gear switch {
        BeachGear.Hands => "맨손", BeachGear.Trowel => "모종삽", BeachGear.Detector => "탐지기",
        BeachGear.Magnet => "선별 자석", BeachGear.Rake => "모래 갈퀴", BeachGear.Parasol => "파라솔", BeachGear.Bucket=>"양동이", BeachGear.Ball=>"배구공", BeachGear.Sack=>"수거 자루", _ => "빈 칸" };
}

// One player's hotbar and hands. The hotbar holds gear only. A find picked up is held in the hand, one at a
// time, until it goes into a sack or a bin; finds are gathered in sacks on the beach, not carried in a bag.
// Gold belongs to the team: the beach supplies it through Funds and takes payments through Spend.
public sealed class BeachInventory : MonoBehaviour
{
    public const int TrowelPrice = 2, DetectorPrice = 7, MagnetPrice = 5, ParasolPrice = 2;
    public const int SlotCount = 9;
    readonly BeachSlot[] slots = new BeachSlot[SlotCount];
    FindKind? pending;
    public BeachSlot Slot(int index) => slots[index];
    int FreeSlot => System.Array.FindIndex(slots,1,slot=>slot.Empty);
    public bool HasSpace => FreeSlot>=0;
    public int GearSlot(BeachGear gear) => System.Array.FindIndex(slots,slot=>slot.Gear==gear);

    // The find in this player's hand, if any.
    public FindKind? Hand { get; private set; }
    public bool HandFree => !Hand.HasValue && !pending.HasValue;
    public int Count => Hand.HasValue ? 1 : 0;
    public bool CanStore(FindKind kind) => kind==FindKind.Needle || HandFree;

    public bool Store(BeachFind item) => Store(item.kind);

    public bool Store(FindKind kind)
    {
        if (kind==FindKind.Needle || Hand.HasValue) return false;
        if (pending==kind) pending = null;
        if (pending.HasValue) return false;
        Hand = kind;
        return true;
    }

    // Empties the hand, returning what was in it.
    public FindKind? TakeHand()
    {
        var held = Hand;
        Hand = null;
        return held;
    }

    // A guest keeps the hand free until the host settles the pickup.
    public bool Reserve(FindKind kind)
    {
        if (pending.HasValue || !CanStore(kind)) return false;
        pending = kind;
        return true;
    }
    public void Release(FindKind kind)
    {
        if (pending==kind) pending = null;
    }

    // ---- Gold ----

    // Set by the beach: the team's gold, and paying out of it. Without a beach (tests) the bag keeps its own.
    public System.Func<int> Funds;
    public System.Func<int,bool> Spend;
    int purse;
    public int Wallet => Funds!=null ? Funds() : purse;
    public void Credit(int gold) => purse += gold;
    bool Charge(int price)
    {
        if (Wallet<price) return false;
        if (Spend!=null) return Spend(price);
        purse -= price;
        return true;
    }

    // ---- Gear ----

    public bool HasTrowel { get; private set; }
    public bool HasDetector { get; private set; }
    public bool HasMagnet { get; private set; }
    public bool HasParasol { get; private set; }
    public BeachTheme Theme { get; private set; }

    public void StartVisit(BeachTheme theme)
    {
        Theme = theme;
        pending = null;
        Hand = null;
        System.Array.Clear(slots,0,slots.Length);
        slots[0]=new BeachSlot(BeachGear.Hands);
        HasRake = HasTrowel = false;
        HasDetector = false;
        HasMagnet = false;
        HasParasol = false;
        purse = 0;
    }

    public const int RakePrice = 2, SortedPrice = 2;
    public bool HasRake { get; private set; }

    public bool BuyRake()
    {
        if (HasRake || !HasSpace || !Charge(RakePrice)) return false;
        slots[FreeSlot]=new BeachSlot(BeachGear.Rake);
        return HasRake = true;
    }

    public int NextPrice => !HasTrowel ? TrowelPrice :
        Theme != BeachTheme.Neighborhood && !HasDetector ? DetectorPrice :
        Theme == BeachTheme.Festival && !HasMagnet ? MagnetPrice : 0;
    public string NextTool => !HasTrowel ? "모종삽" : !HasDetector ? "탐지기" : "선별 자석";

    // Sold on every beach, outside the tool chain: the first 2 G buys either the trowel or shade.
    public bool BuyParasol()
    {
        if (HasParasol || !HasSpace || !Charge(ParasolPrice)) return false;
        slots[FreeSlot]=new BeachSlot(BeachGear.Parasol);
        return HasParasol = true;
    }

    public bool BuyNext()
    {
        int price = NextPrice;
        if (price == 0 || !HasSpace || !Charge(price)) return false;
        slots[FreeSlot]=new BeachSlot(!HasTrowel ? BeachGear.Trowel : !HasDetector ? BeachGear.Detector : BeachGear.Magnet);
        if (!HasTrowel) HasTrowel = true;
        else if (!HasDetector) HasDetector = true;
        else HasMagnet = true;
        return true;
    }
}
