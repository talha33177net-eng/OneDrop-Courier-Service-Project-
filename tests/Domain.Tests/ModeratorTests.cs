using Domain.Merchants;

namespace Domain.Tests;

/// <summary>A moderator's rules: a name, something they may do, and nothing at all once stopped.</summary>
public class ModeratorTests
{
    [Fact]
    public void A_moderator_is_added_with_what_they_may_do()
    {
        var added = Moderator.Add(5, 42, "  Nusrat Jahan ", "+8801711222333", MerchantPermissions.Default);

        Assert.True(added.IsSuccess);
        Assert.Equal("Nusrat Jahan", added.Value.Name);
        Assert.Equal(5, added.Value.AccountId);
        Assert.Equal(42, added.Value.UserId);
        Assert.True(added.Value.May(MerchantPermissions.Parcels));
        Assert.True(added.Value.May(MerchantPermissions.Booking));
        Assert.False(added.Value.May(MerchantPermissions.Payments));
        Assert.False(added.Value.May(MerchantPermissions.Settings));
    }

    [Fact]
    public void Someone_with_no_name_or_nothing_to_do_is_refused()
    {
        Assert.Equal("moderator.name", Moderator.Add(5, 42, "  ", null, MerchantPermissions.Default).Error!.Code);
        Assert.Equal("moderator.name", Moderator.Add(5, 42, new string('a', Moderator.MaxNameLength + 1), null, MerchantPermissions.All).Error!.Code);
        Assert.Equal("moderator.permissions", Moderator.Add(5, 42, "Nusrat", null, MerchantPermissions.None).Error!.Code);
    }

    [Fact]
    public void What_they_may_do_is_changed_but_never_emptied()
    {
        var moderator = Moderator.Add(5, 42, "Nusrat", null, MerchantPermissions.Default).Value;

        Assert.Equal("moderator.permissions", moderator.ChangePermissions(MerchantPermissions.None).Error!.Code);
        Assert.True(moderator.May(MerchantPermissions.Booking));
        Assert.True(moderator.ChangePermissions(MerchantPermissions.Dashboard | MerchantPermissions.Payments).IsSuccess);
        Assert.True(moderator.May(MerchantPermissions.Payments));
        Assert.False(moderator.May(MerchantPermissions.Booking));
    }

    [Fact]
    public void A_stopped_moderator_may_do_nothing_until_they_are_let_back_in()
    {
        var moderator = Moderator.Add(5, 42, "Nusrat", null, MerchantPermissions.All).Value;

        moderator.Stop();

        Assert.True(moderator.Archived);
        Assert.False(moderator.May(MerchantPermissions.Dashboard));
        Assert.False(moderator.May(MerchantPermissions.None));
        Assert.Equal("moderator.stopped", moderator.ChangePermissions(MerchantPermissions.Dashboard).Error!.Code);

        moderator.LetBackIn();

        Assert.True(moderator.May(MerchantPermissions.Dashboard));
    }
}
