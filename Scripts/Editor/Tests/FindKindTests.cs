using NUnit.Framework;

// What each kind of find is worth and how the tools treat it. See Docs/아이템목록.md.
public class FindKindTests
{
    static readonly FindKind[] All = (FindKind[])System.Enum.GetValues(typeof(FindKind));

    [Test] public void EveryKindHasItsOwnName()
    {
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (var kind in All) Assert.IsTrue(names.Add(BeachFind.NameOf(kind)),$"{kind} shares a name with another kind");
    }

    [Test] public void OnlyTheNeedleIsWorthNothing()
    {
        foreach (var kind in All)
            Assert.AreEqual(kind==FindKind.Needle,BeachFind.PriceOf(kind)==0,kind.ToString());
    }

    [Test] public void LitterSellsForOneGold()
    {
        foreach (var kind in BeachFind.Trash) Assert.AreEqual(1,BeachFind.PriceOf(kind),kind.ToString());
        Assert.IsFalse(BeachFind.IsTrashKind(FindKind.Coin));
        Assert.IsFalse(BeachFind.IsTrashKind(FindKind.Needle));
        Assert.IsTrue(BeachFind.IsTrashKind(FindKind.Toothpick));
    }

    [Test] public void NamesTakeTheRightObjectParticle()
    {
        Assert.AreEqual("반지를",BeachFind.WithObject("반지"));
        Assert.AreEqual("동전을",BeachFind.WithObject("동전"));
        Assert.AreEqual("팔찌를",BeachFind.WithObject("팔찌"));
        foreach (var kind in All) StringAssert.StartsWith(BeachFind.NameOf(kind),BeachFind.NameWithObject(kind));
    }

    [Test] public void TheHandHoldsOneFindAtATime()
    {
        var bag = new UnityEngine.GameObject("hands").AddComponent<BeachInventory>();
        try
        {
            bag.StartVisit(BeachTheme.Festival);
            Assert.IsTrue(bag.Store(FindKind.Coin));
            Assert.IsFalse(bag.Store(FindKind.Ring),"a second find went into a full hand");
            Assert.IsTrue(bag.CanStore(FindKind.Needle),"the needle ends the search, whatever is in hand");
            Assert.AreEqual(1,bag.Count);
            Assert.AreEqual(FindKind.Coin,bag.TakeHand());
            Assert.IsFalse(bag.Hand.HasValue);
            // A guest keeps the hand free while the host decides.
            Assert.IsTrue(bag.Reserve(FindKind.FishingNet));
            Assert.IsFalse(bag.HandFree);
            Assert.IsFalse(bag.Store(FindKind.Coin));
            Assert.IsTrue(bag.Store(FindKind.FishingNet));
            Assert.AreEqual(FindKind.FishingNet,bag.Hand);
            // The hotbar is gear only.
            for (int i=0;i<BeachInventory.SlotCount;i++) Assert.IsFalse(bag.Slot(i).Find.HasValue);
        }
        finally { UnityEngine.Object.DestroyImmediate(bag.gameObject); }
    }

    [Test] public void GearIsPaidForFromTheTeamsGold()
    {
        var bag = new UnityEngine.GameObject("hotbar").AddComponent<BeachInventory>();
        try
        {
            bag.StartVisit(BeachTheme.Festival);
            int team = 10;
            bag.Funds = () => team;
            bag.Spend = price => { team -= price; return true; };
            Assert.IsTrue(bag.BuyNext());
            Assert.AreEqual(8,team);
            Assert.IsTrue(bag.BuyRake());
            Assert.IsFalse(bag.BuyRake());
            Assert.IsTrue(bag.BuyParasol());
            Assert.AreEqual(4,team);
            Assert.IsFalse(bag.BuyNext(),"the detector costs more than the team has");
            Assert.AreEqual(4,team);
            Assert.AreEqual(BeachGear.Rake,bag.Slot(2).Gear);

            // Without a beach the bag keeps its own purse.
            bag.Funds = null; bag.Spend = null;
            bag.StartVisit(BeachTheme.Neighborhood);
            bag.Credit(3);
            Assert.IsTrue(bag.BuyNext());
            Assert.AreEqual(1,bag.Wallet);
            Assert.AreEqual(BeachInventory.TrowelPrice,2);
        }
        finally { UnityEngine.Object.DestroyImmediate(bag.gameObject); }
    }

    // The detector must keep pointing at the needle and the valuables, however much litter is added.
    [Test] public void MarineLitterIsTrashTheDetectorIgnores()
    {
        var item = new UnityEngine.GameObject("litter").AddComponent<BeachFind>();
        for (var kind = FindKind.CigaretteButt; kind<=FindKind.RopeScrap; kind++)
        {
            item.kind = kind;
            Assert.IsTrue(item.IsTrash,kind.ToString());
            Assert.IsFalse(item.IsMetal,kind.ToString());
            Assert.IsFalse(item.IsIron,kind.ToString());
        }
        UnityEngine.Object.DestroyImmediate(item.gameObject);
    }
}
