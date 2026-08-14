using UnderworldEmpire.Domain;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Placeholder smoke tests proving the solution wiring (project references,
/// test runner) is intact. Real domain tests replace these from M1 onward.
/// </summary>
public class ScaffoldingSmokeTests
{
    [Fact]
    public void Solution_wiring_is_intact()
    {
        Assert.Equal("crime", BuildInfo.DefaultContentPack);
    }
}
