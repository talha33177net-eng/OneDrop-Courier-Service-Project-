using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Integration.Tests;

/// <summary>
/// The courier's admin keeping its own coverage map: a new hub, a zone on it and an area merchants can book to, and
/// the guards that stop a part of the map moving out from under the parcels already priced and routed by it.
/// </summary>
public class CoverageAdminTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task An_admin_opens_a_hub_draws_a_zone_on_it_and_adds_an_area_merchants_can_book_to()
    {
        WebAppFactory.RequireDatabase();
        var tag = $"{Guid.NewGuid():N}"[..6].ToUpperInvariant();
        var admin = await SignInAsync("onedrop", "admin@onedrop.test");

        var hubs = await admin.SubmitAsync(
            "/Admin/Coverage?tab=Hubs",
            "/Admin/Coverage?tab=Hubs&handler=AddHub",
            ("code", $"T{tag}"),
            ("name", $"Test hub {tag}"),
            ("address", "Station Road, Testpur"),
            ("phone", "01700-900900"));
        Assert.Contains($"Test hub {tag}", hubs);

        var zones = await admin.SubmitAsync(
            "/Admin/Coverage?tab=Zones",
            "/Admin/Coverage?tab=Zones&handler=AddZone",
            ("code", $"Z{tag}"),
            ("name", $"Testpur {tag}"),
            ("city", $"Testpur {tag}"),
            ("hubId", (await HubIdAsync($"T{tag}")).ToString()),
            ("isSuburb", "false"));
        Assert.Contains($"Testpur {tag}", zones);
        Assert.Contains($"Test hub {tag}", zones);

        var zoneId = await ZoneIdAsync($"Z{tag}");
        var areas = await admin.SubmitAsync(
            "/Admin/Coverage?tab=Areas",
            "/Admin/Coverage?tab=Areas&handler=AddArea",
            ("name", $"Testpur Sadar {tag}"),
            ("zoneId", zoneId.ToString()));
        Assert.Contains($"Testpur Sadar {tag}", areas);

        // The whole courier sees it at once: the public map, the address list the API serves and a booking
        Assert.Contains($"Testpur Sadar {tag}", await admin.PageAsync("/Admin/Coverage"));
        Assert.Contains($"Testpur Sadar {tag}", await AreaNamesAsync());
        var shop = await NewMerchantAsync();
        var code = await BookAsync(shop.ApiKey, $"Testpur Sadar {tag}");
        Assert.Equal($"Test hub {tag}", await QueryAsync("onedrop", db => db.Hubs
            .Where(h => h.Id == db.Parcels.Single(p => p.TrackingCode == code).DeliveryHubId)
            .Select(h => h.Name)
            .SingleAsync(Cancel)));
    }

    [Fact]
    public async Task A_part_of_the_map_with_work_on_it_stays_put_and_an_empty_one_comes_off()
    {
        WebAppFactory.RequireDatabase();
        var tag = $"{Guid.NewGuid():N}"[..6].ToUpperInvariant();
        var admin = await SignInAsync("onedrop", "admin@onedrop.test");
        await admin.SubmitAsync(
            "/Admin/Coverage?tab=Hubs",
            "/Admin/Coverage?tab=Hubs&handler=AddHub",
            ("code", $"T{tag}"),
            ("name", $"Test hub {tag}"),
            ("address", "Station Road, Testpur"),
            ("phone", "01700-900900"));
        var hubId = await HubIdAsync($"T{tag}");
        await admin.SubmitAsync(
            "/Admin/Coverage?tab=Zones",
            "/Admin/Coverage?tab=Zones&handler=AddZone",
            ("code", $"Z{tag}"),
            ("name", $"Testpur {tag}"),
            ("city", $"Testpur {tag}"),
            ("hubId", hubId.ToString()),
            ("isSuburb", "false"));
        var zoneId = await ZoneIdAsync($"Z{tag}");
        await admin.SubmitAsync(
            "/Admin/Coverage?tab=Areas",
            "/Admin/Coverage?tab=Areas&handler=AddArea",
            ("name", $"Testpur Sadar {tag}"),
            ("zoneId", zoneId.ToString()));
        var areaId = await QueryAsync("onedrop", db => db.Areas
            .Where(a => a.Name == $"Testpur Sadar {tag}")
            .Select(a => a.Id)
            .SingleAsync(Cancel));

        // The hub serves a zone, so it cannot close
        var closing = await admin.SubmitAsync(
            "/Admin/Coverage?tab=Hubs",
            $"/Admin/Coverage?tab=Hubs&handler=HubActive&id={hubId}&active=false");
        Assert.Contains("cannot close yet", closing);
        Assert.Contains("1 zone it serves", closing);

        // The zone still covers an area, so it cannot leave the map
        var zoneOff = await admin.SubmitAsync(
            "/Admin/Coverage?tab=Zones",
            $"/Admin/Coverage?tab=Zones&handler=ZoneActive&id={zoneId}&active=false");
        Assert.Contains("still covers 1 area", zoneOff);

        // A parcel is on its way to the area, so the area cannot leave either
        var shop = await NewMerchantAsync();
        await BookAsync(shop.ApiKey, $"Testpur Sadar {tag}");
        var busy = await admin.SubmitAsync(
            "/Admin/Coverage?tab=Areas",
            $"/Admin/Coverage?tab=Areas&handler=AreaActive&id={areaId}&active=false");
        Assert.Contains("1 parcel on the way there", busy);
        Assert.Contains($"Testpur Sadar {tag}", await AreaNamesAsync());

        // An empty area does come off, and leaves the list merchants book from
        var empty = await admin.SubmitAsync(
            "/Admin/Coverage?tab=Areas",
            "/Admin/Coverage?tab=Areas&handler=AddArea",
            ("name", $"Testpur Bazar {tag}"),
            ("zoneId", zoneId.ToString()));
        Assert.Contains($"Testpur Bazar {tag}", empty);
        var emptyId = await QueryAsync("onedrop", db => db.Areas
            .Where(a => a.Name == $"Testpur Bazar {tag}")
            .Select(a => a.Id)
            .SingleAsync(Cancel));
        var off = await admin.SubmitAsync(
            "/Admin/Coverage?tab=Areas",
            $"/Admin/Coverage?tab=Areas&handler=AreaActive&id={emptyId}&active=false");
        Assert.Contains("Off the map", off);
        Assert.DoesNotContain($"Testpur Bazar {tag}", await AreaNamesAsync());
    }

    [Fact]
    public async Task Another_courier_cannot_see_or_change_this_one_s_coverage()
    {
        WebAppFactory.RequireDatabase();
        var tag = $"{Guid.NewGuid():N}"[..6].ToUpperInvariant();
        var admin = await SignInAsync("onedrop", "admin@onedrop.test");
        await admin.SubmitAsync(
            "/Admin/Coverage?tab=Hubs",
            "/Admin/Coverage?tab=Hubs&handler=AddHub",
            ("code", $"T{tag}"),
            ("name", $"Test hub {tag}"),
            ("address", "Station Road, Testpur"),
            ("phone", "01700-900900"));
        var hubId = await HubIdAsync($"T{tag}");

        var rival = await SignInAsync("rival", "admin@rival.test");
        Assert.DoesNotContain($"Test hub {tag}", await rival.PageAsync("/Admin/Coverage?tab=Hubs"));

        var refused = await rival.PostFormAsync(
            "/Admin/Coverage?tab=Hubs",
            $"/Admin/Coverage?tab=Hubs&handler=HubActive&id={hubId}&active=false");
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.False(await QueryAsync("onedrop", db => db.Hubs.AnyAsync(h => h.Id == hubId && h.Archived, Cancel)));
    }

    private async Task<long> HubIdAsync(string code)
    {
        return await QueryAsync("onedrop", db => db.Hubs.Where(h => h.Code == code).Select(h => h.Id).SingleAsync(Cancel));
    }

    private async Task<long> ZoneIdAsync(string code)
    {
        return await QueryAsync("onedrop", db => db.Zones.Where(z => z.Code == code).Select(z => z.Id).SingleAsync(Cancel));
    }

    /// <summary>The area list merchants book from, as GET /api/v1/areas serves it.</summary>
    private async Task<string> AreaNamesAsync()
    {
        var response = await Factory.ClientFor(WebAppFactory.Fashion).GetAsync("/api/v1/areas", Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, Cancel)).ToString();
    }
}
