using Domain.Customers;

namespace Domain.Tests;

public class CustomerAddressTests
{
    [Theory]
    [InlineData("House 12, Road 5", "Flat 3B")]
    [InlineData("h-12 rd 5", "flat 3b")]
    [InlineData("  HOUSE 12  ROAD-5 ", "F 3 B")]
    [InlineData("H12 R5", "F3B")]
    public void Two_spellings_of_one_address_share_a_match_key(string line1, string line2)
    {
        Assert.Equal("h 12 r 5 f 3 b", CustomerAddress.BuildMatchKey(line1, line2));
    }

    [Fact]
    public void Home_and_office_are_different_addresses()
    {
        Assert.NotEqual(
            CustomerAddress.BuildMatchKey("House 12, Road 5", null),
            CustomerAddress.BuildMatchKey("House 14, Road 5", null));
    }

    [Fact]
    public void The_landmark_does_not_change_the_match()
    {
        var withLandmark = new CustomerAddress(1, 1, "House 12, Road 5", null, "Near the mosque");
        var without = new CustomerAddress(1, 1, "House 12, Road 5", null, null);

        Assert.Equal(without.MatchKey, withLandmark.MatchKey);
    }
}
