using PocketLedger.Models.ViewModels;
using PocketLedger.Services;

namespace PocketLedger.Identity.Tests;

public sealed class AdminTableTests
{
    [Theory]
    [InlineData("3h59m12.5s", 14352.5)]
    [InlineData("30m", 1800)]
    [InlineData("167h0m0s", 601200)]
    [InlineData("1500ms", 1.5)]
    public void CrowdSecDuration_ParsesGoDurations(string value, double expectedSeconds)
        => Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), CrowdSecDuration.Parse(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("3h later")]
    public void CrowdSecDuration_RejectsUnknownFormats(string? value) => Assert.Null(CrowdSecDuration.Parse(value));

    [Fact]
    public void CrowdSecDuration_FormatsForHumans()
    {
        Assert.Equal("6d 23h 59m", CrowdSecDuration.Format(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1)));
        Assert.Equal("3h 59m", CrowdSecDuration.Format(TimeSpan.Parse("03:59:12")));
        Assert.Equal("4m 5s", CrowdSecDuration.Format(TimeSpan.FromSeconds(245)));
    }

    [Fact]
    public void TablePaging_SortsWithMissingValuesLastAndClampsThePage()
    {
        var items = new (string Name, int? Value)[] { ("alpha", 3), ("beta", null), ("gamma", 1), ("delta", 2), ("alpine", 5) };
        var keys = new Dictionary<string, Func<(string Name, int? Value), IComparable?>> { ["name"] = item => item.Name, ["value"] = item => item.Value };

        var descending = TablePaging.Apply(items, "value", true, keys, "name", 1, 10);
        var ascending = TablePaging.Apply(items, "value", false, keys, "name", 99, 2);

        Assert.Equal(["alpine", "alpha", "delta", "gamma", "beta"], descending.Items.Select(item => item.Name));
        Assert.Equal(3, ascending.Page);
        Assert.Equal(["beta"], ascending.Items.Select(item => item.Name));
        Assert.Equal("name", TablePaging.Apply(items, "unknown", false, keys, "name", 1, 10).Sort);
    }
}
