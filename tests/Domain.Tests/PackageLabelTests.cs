using Domain.Orders;

namespace Domain.Tests;

public class PackageLabelTests
{
    [Fact]
    public void A_label_reads_the_order_number_then_the_package()
    {
        Assert.Equal("OD-100001-2", new PackageLabel("OD-100001", 2).ToString());
    }

    [Theory]
    [InlineData("OD-100001-1", "OD-100001", 1)]
    [InlineData("  od-100001-12 ", "OD-100001", 12)] // typed by hand
    [InlineData("OD-1234567-20", "OD-1234567", 20)] // numbers grow past six digits
    public void A_scanned_label_is_read_back(string text, string order, int sequence)
    {
        Assert.True(PackageLabel.TryParse(text, out var label));
        Assert.Equal(new PackageLabel(order, sequence), label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OD-100001")] // no package
    [InlineData("OD-100001-0")]
    [InlineData("OD-100001-21")] // more than an order can have
    [InlineData("DG-100001-1")] // a delivery number, not an order
    [InlineData("OD-100001-1; DROP TABLE")]
    [InlineData("OD-12-1")]
    public void Anything_else_is_not_a_label(string? text)
    {
        Assert.False(PackageLabel.TryParse(text, out _));
    }

    [Fact]
    public void Every_package_an_order_can_have_gets_a_label_that_reads_back()
    {
        for (var sequence = 1; sequence <= Order.MaxPackages; sequence++)
        {
            Assert.True(PackageLabel.TryParse(new PackageLabel("OD-100001", sequence).ToString(), out var label));
            Assert.Equal(sequence, label.Sequence);
        }
    }
}
