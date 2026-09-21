namespace LakeCompanion.ConsoleApp;

/// <summary>Schedules rare companion dialogue after a randomized number of successful player moves.</summary>
public sealed class CompanionAmbientScheduler(Random random)
{
    private int movesUntilRemark = NextInterval(random);

    /// <summary>Records one valid movement and returns true only when Rowan should make an ambient remark.</summary>
    public bool RecordMove()
    {
        movesUntilRemark--;
        if (movesUntilRemark > 0)
        {
            return false;
        }

        movesUntilRemark = NextInterval(random);
        return true;
    }

    private static int NextInterval(Random random) => random.Next(35, 71);
}
