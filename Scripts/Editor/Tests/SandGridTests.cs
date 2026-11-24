using NUnit.Framework;
using UnityEngine;

// Rules of the diggable sand. Requirement ids refer to Docs/기능/모래질감.md.
public class SandGridTests
{
    const float Step = 10f/160f;
    static SandGrid Beach() => new SandGrid(160,160,Step);

    static int SettleFully(SandGrid grid)
    {
        int passes = 0;
        while (grid.Settle() && passes<5000) passes++;
        Assert.Less(passes,5000,"sand never came to rest");
        return passes;
    }

    static float SteepestExcess(SandGrid grid)
    {
        float worst = 0;
        for (int z=1; z<grid.Rows; z++)
        for (int x=1; x<grid.Columns; x++)
        for (int dz=-1; dz<=1; dz++)
        for (int dx=-1; dx<=1; dx++)
        {
            int nx=x+dx, nz=z+dz;
            if ((dx==0 && dz==0) || nx<1 || nz<1 || nx>=grid.Columns || nz>=grid.Rows) continue;
            float limit = SandGrid.Repose*grid.Step*(dx!=0 && dz!=0 ? 1.41421356f : 1f);
            worst = Mathf.Max(worst,grid.Heights[grid.Index(x,z)]-grid.Heights[grid.Index(nx,nz)]-limit);
        }
        return worst;
    }

    static int CellsBelow(SandGrid grid,float level)
    {
        int count = 0;
        foreach (float h in grid.Heights) if (h<level) count++;
        return count;
    }

    [Test] public void R8_BeachSizesStayExactAtTheFinerStep()
    {
        Assert.AreEqual(160,Mathf.RoundToInt(10f/Step));
        Assert.AreEqual(200,Mathf.RoundToInt(12.5f/Step));
        Assert.AreEqual(180,Mathf.RoundToInt(11.25f/Step));
        Assert.AreEqual(5f,Beach().HalfSize.x,1e-5f);
    }

    [Test] public void ScrapeReturnsExactlyWhatItRemoved()
    {
        var grid = Beach();
        float removed = grid.Scrape(new Vector2(.3f,-.2f),.45f,.14f);
        Assert.Greater(removed,0);
        Assert.AreEqual(-removed,grid.Total,1e-3f);
    }

    [Test] public void PourAddsExactlyTheAmount()
    {
        var grid = Beach();
        grid.Pour(new Vector2(1,1),1.5f,.45f);
        Assert.AreEqual(1.5f,grid.Total,1e-3f);
    }

    [TestCase(.28f,.078f,TestName="R1a_OneHandfulKeepsItsShape")]
    [TestCase(.45f,.14f,TestName="R1a_OneTrowelScoopKeepsItsShape")]
    public void SingleStrokeDoesNotSlide(float radius,float depth)
    {
        var grid = Beach();
        grid.Scrape(Vector2.zero,radius,depth);
        var before = (float[])grid.Heights.Clone();
        Assert.IsFalse(grid.Settle());
        CollectionAssert.AreEqual(before,grid.Heights);
    }

    [Test] public void R1a_TutorialCoinIsStillUncoveredByOneHandful()
    {
        var grid = Beach();
        var coin = new Vector2(0,-1.9f);
        const float coinTop = -.052f+.015f;
        grid.Scrape(coin,.28f,.078f);
        SettleFully(grid);
        Assert.LessOrEqual(grid.Height(coin),coinTop);
    }

    [Test] public void R1_DeepHoleSlidesWidensAndKeepsItsSand()
    {
        var grid = Beach();
        for (int i=0; i<4; i++) grid.Scrape(Vector2.zero,.45f,.14f);
        float total = grid.Total;
        int hollow = CellsBelow(grid,-.001f);
        float bottom = grid.Height(Vector2.zero);
        Assert.Greater(SteepestExcess(grid),.01f,"four scoops should be steeper than sand can hold");

        int passes = SettleFully(grid);

        Assert.Greater(passes,1);
        Assert.AreEqual(total,grid.Total,2e-3f,"sliding must not create or destroy sand");
        Assert.Greater(CellsBelow(grid,-.001f),hollow,"the hole should have widened");
        Assert.Greater(grid.Height(Vector2.zero),bottom,"the bottom should have filled in a little");
        Assert.LessOrEqual(SteepestExcess(grid),.002f,"no slope steeper than the angle of repose remains");
    }

    [Test] public void R1_SteepMoundSpreadsOut()
    {
        var grid = Beach();
        grid.Pour(new Vector2(2,2),8f,.20f);
        float peak = grid.Height(new Vector2(2,2)), total = grid.Total;
        SettleFully(grid);
        Assert.Less(grid.Height(new Vector2(2,2)),peak);
        Assert.AreEqual(total,grid.Total,2e-3f);
        Assert.LessOrEqual(SteepestExcess(grid),.002f);
    }

    [Test] public void R1_BorderStaysLevelWhenSandSlidesNextToIt()
    {
        var grid = Beach();
        grid.Pour(new Vector2(-4.9f,0),8f,.25f);
        SettleFully(grid);
        for (int z=0; z<=grid.Rows; z++)
        {
            Assert.AreEqual(0f,grid.Heights[grid.Index(0,z)]);
            Assert.AreEqual(0f,grid.Heights[grid.Index(grid.Columns,z)]);
        }
    }

    [Test] public void R2_DugSandIsDampThenDries()
    {
        var grid = Beach();
        grid.Scrape(Vector2.zero,.28f,.078f);
        int centre = grid.Index(80,80);
        Assert.AreEqual(1f,grid.Damp[centre],1e-4f);
        Assert.IsTrue(grid.Dry(SandGrid.DryingSeconds*.5f));
        Assert.AreEqual(.5f,grid.Damp[centre],1e-3f);
        grid.Dry(SandGrid.DryingSeconds);
        Assert.AreEqual(0f,grid.Damp[centre]);
        Assert.IsFalse(grid.AnyDamp);
        Assert.IsFalse(grid.Dry(1f),"nothing left to dry");
    }

    [Test] public void R2_PouredSandLandsDamp()
    {
        var grid = Beach();
        grid.Pour(Vector2.zero,1f,.45f);
        Assert.AreEqual(.55f,grid.Damp[grid.Index(80,80)],1e-3f);
    }

    [Test] public void R4_FootprintsNeverDigBelowTheirFloor()
    {
        var grid = Beach();
        for (int i=0; i<50; i++) grid.Scrape(new Vector2(1,1),.085f,.011f,-.014f);
        float lowest = 0;
        foreach (float h in grid.Heights) lowest = Mathf.Min(lowest,h);
        Assert.AreEqual(-.014f,lowest,1e-5f);
        Assert.Greater(lowest,-.052f+.015f,"a buried coin's top stays covered");
    }

    [Test] public void R4_FootprintLeavesSandDry()
    {
        var grid = Beach();
        Assert.Greater(grid.Scrape(new Vector2(1,1),.085f,.011f,-.014f,false),0);
        Assert.IsFalse(grid.AnyDamp);
    }

    [Test] public void R4_FootprintInAHoleDoesNotRaiseIt()
    {
        var grid = Beach();
        grid.Scrape(Vector2.zero,.45f,.14f);
        var before = (float[])grid.Heights.Clone();
        float removed = grid.Scrape(Vector2.zero,.085f,.011f,-.014f);
        Assert.AreEqual(0f,removed);
        CollectionAssert.AreEqual(before,grid.Heights);
    }

    [Test] public void ChangedBlockCoversEveryCellThatMoved()
    {
        var grid = Beach();
        Assert.IsFalse(grid.TakeChanged(out _,out _,out _,out _),"an untouched beach has nothing to redraw");
        var before = (float[])grid.Heights.Clone();
        var dampBefore = (float[])grid.Damp.Clone();
        for (int i=0; i<4; i++) grid.Scrape(new Vector2(1,-2),.45f,.14f);
        grid.Pour(new Vector2(-3,2),2f,.20f);
        for (int pass=0; pass<40; pass++) grid.Settle();

        Assert.IsTrue(grid.TakeChanged(out int x0,out int x1,out int z0,out int z1));
        for (int z=0; z<=grid.Rows; z++)
        for (int x=0; x<=grid.Columns; x++)
        {
            int i = grid.Index(x,z);
            if (before[i]==grid.Heights[i] && dampBefore[i]==grid.Damp[i]) continue;
            Assert.IsTrue(x>=x0 && x<=x1 && z>=z0 && z<=z1,$"cell {x},{z} changed outside the reported block");
        }
        Assert.IsFalse(grid.TakeChanged(out _,out _,out _,out _),"the block is forgotten once taken");
    }

    [Test] public void ChangedBlockStaysSmallForOneStroke()
    {
        var grid = Beach();
        grid.Scrape(Vector2.zero,.28f,.078f);
        Assert.IsTrue(grid.TakeChanged(out int x0,out int x1,out int z0,out int z1));
        Assert.LessOrEqual((x1-x0+1)*(z1-z0+1),12*12,"one handful should not redraw the whole beach");
    }

    [Test] public void OnlyWorkedTilesAreWaitingToBeSent()
    {
        var grid = Beach();
        grid.Scrape(Vector2.zero,.28f,.078f);
        int waiting = 0;
        for (int tile=0; tile<grid.TileCount; tile++) if (grid.TakeUnsent(tile)) waiting++;
        Assert.That(waiting,Is.InRange(1,4),"one handful touches the tiles around it, not the whole beach");
        for (int tile=0; tile<grid.TileCount; tile++) Assert.IsFalse(grid.TakeUnsent(tile),"a tile is handed over once");
    }

    [Test] public void RestoreAndSetCellReproduceAnotherBeach()
    {
        var host = Beach();
        for (int i=0; i<4; i++) host.Scrape(new Vector2(1,-2),.45f,.14f);
        host.Pour(new Vector2(-3,2),2f,.20f);
        SettleFully(host);

        var late = Beach();
        late.Restore(host.Heights,host.Damp);
        CollectionAssert.AreEqual(host.Heights,late.Heights);
        Assert.IsTrue(late.AnyDamp);
        Assert.IsTrue(late.TakeChanged(out _,out _,out _,out _),"a restored beach must be redrawn");

        var patched = Beach();
        for (int z=0; z<=host.Rows; z++)
        for (int x=0; x<=host.Columns; x++)
            patched.SetCell(x,z,host.Heights[host.Index(x,z)],host.Damp[host.Index(x,z)]);
        CollectionAssert.AreEqual(host.Heights,patched.Heights);
        CollectionAssert.AreEqual(host.Damp,patched.Damp);
    }

    [Test] public void HeightInterpolatesBetweenCells()
    {
        var grid = Beach();
        grid.Heights[grid.Index(80,80)] = .10f;
        Assert.AreEqual(.10f,grid.Height(Vector2.zero),1e-5f);
        Assert.AreEqual(.05f,grid.Height(new Vector2(Step*.5f,0)),1e-5f);
        Assert.AreEqual(0f,grid.Height(new Vector2(Step,0)),1e-5f);
    }
}
