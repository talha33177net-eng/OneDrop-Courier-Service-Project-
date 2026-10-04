using Domain.Merchants;

namespace Domain.Tests;

/// <summary>Task 4.8: a shop lists itself in the operator's shopping window, or leaves it.</summary>
public class ShopWindowTests
{
    private static Merchant NewMerchant()
    {
        return new Merchant("Nakshi Crafts", zoneId: 1, "01711000001", null);
    }

    [Fact]
    public void A_shop_is_not_listed_until_it_says_where_customers_shop()
    {
        var merchant = NewMerchant();

        Assert.False(merchant.IsListed);
        Assert.True(merchant.ListInWindow(" https://www.facebook.com/nakshicrafts ", "  Handmade kantha  ").IsSuccess);
        Assert.True(merchant.IsListed);
        Assert.Equal("https://www.facebook.com/nakshicrafts", merchant.ShopUrl);
        Assert.Equal("Handmade kantha", merchant.ShopAbout);
    }

    [Theory]
    [InlineData("https://nakshi.com.bd/")]
    [InlineData("http://nakshi.com.bd/shop")]
    public void A_website_over_http_or_https_is_listed(string url)
    {
        var merchant = NewMerchant();

        Assert.True(merchant.ListInWindow(url, null).IsSuccess);
        Assert.Equal(url, merchant.ShopUrl);
        Assert.Null(merchant.ShopAbout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("nakshi.com.bd")]
    [InlineData("/shop")]
    [InlineData("ftp://nakshi.com.bd/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@nakshi.com.bd/")]
    public void Anything_but_a_web_address_is_refused_and_changes_nothing(string? url)
    {
        var merchant = NewMerchant();
        merchant.ListInWindow("https://nakshi.com.bd/", "Kantha");

        var listed = merchant.ListInWindow(url, "Something else");

        Assert.Equal("merchant.shop.url", listed.Error!.Code);
        Assert.Equal("https://nakshi.com.bd/", merchant.ShopUrl);
        Assert.Equal("Kantha", merchant.ShopAbout);
    }

    [Fact]
    public void Too_long_an_address_or_line_is_refused()
    {
        var merchant = NewMerchant();

        Assert.Equal(
            "merchant.shop.url",
            merchant.ListInWindow("https://nakshi.com.bd/" + new string('a', Merchant.MaxShopUrlLength), null).Error!.Code);
        Assert.Equal(
            "merchant.shop.about",
            merchant.ListInWindow("https://nakshi.com.bd/", new string('a', Merchant.MaxShopAboutLength + 1)).Error!.Code);
        Assert.True(merchant.ListInWindow("https://nakshi.com.bd/", new string('a', Merchant.MaxShopAboutLength)).IsSuccess);
    }

    [Fact]
    public void Leaving_the_window_removes_the_address_and_the_line()
    {
        var merchant = NewMerchant();
        merchant.ListInWindow("https://nakshi.com.bd/", "Kantha");

        merchant.LeaveWindow();

        Assert.False(merchant.IsListed);
        Assert.Null(merchant.ShopUrl);
        Assert.Null(merchant.ShopAbout);
    }
}
