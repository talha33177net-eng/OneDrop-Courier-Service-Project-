using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Application.Abstractions;
using Application.Delivery.PlanTrips;
using Application.Grouping;
using Application.Grouping.LockDueGroups;
using Application.Payments.SettleMerchants;
using Domain.Customers;
using Domain.Grouping;
using Domain.Orders;
using Infrastructure.Jobs;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 2.5: the lock job closes each tenant's groups at the end of Day 2. It runs here with a fake clock set in
/// January 2026, before the deadline of any group another test class opens (they use today or October 2026), so
/// a run can never lock a group that a test running in parallel is still filling.
/// </summary>
public class LockDueGroupsJobTests(WebAppFactory factory)
{
    // Monday 10:00 in Dhaka and Chattogram (UTC+6): the group locks on Wednesday 00:00 local, Tuesday 18:00 UTC
    private static readonly DateTimeOffset Monday = new(2026, 1, 5, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Deadline = new(2026, 1, 6, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Locks_the_groups_past_their_deadline_as_of_the_deadline_and_leaves_the_others_open()
    {
        WebAppFactory.RequireDatabase();
        var clock = new FakeTimeProvider(Monday);
        var due = await PlaceAsync(clock, await NewAddressAsync("dhaka"));
        clock.Advance(TimeSpan.FromDays(1));
        var notDue = await PlaceAsync(clock, await NewAddressAsync("dhaka"));

        clock.SetUtcNow(Deadline.AddHours(3));
        await RunLockJobAsync("dhaka", clock);

        var locked = await FindGroupAsync("dhaka", due.Id);
        Assert.Equal(DeliveryGroupStatus.Locked, locked.Status);
        Assert.Equal(Deadline, locked.LockedOn);
        Assert.Equal(DeliveryGroupStatus.Open, (await FindGroupAsync("dhaka", notDue.Id)).Status);
    }

    [Fact]
    public async Task A_run_for_one_tenant_leaves_another_tenants_groups_alone()
    {
        WebAppFactory.RequireDatabase();
        var clock = new FakeTimeProvider(Monday);
        var dhaka = await PlaceAsync(clock, await NewAddressAsync("dhaka"));
        var chattogram = await PlaceAsync(clock, await NewAddressAsync("chattogram"));
        clock.SetUtcNow(Deadline.AddDays(1));

        await RunLockJobAsync("dhaka", clock);
        var chattogramAfterDhakaRun = await FindGroupAsync("chattogram", chattogram.Id);
        await RunLockJobAsync("chattogram", clock);

        Assert.Equal(DeliveryGroupStatus.Locked, (await FindGroupAsync("dhaka", dhaka.Id)).Status);
        Assert.Equal(DeliveryGroupStatus.Open, chattogramAfterDhakaRun.Status);
        Assert.Equal(DeliveryGroupStatus.Locked, (await FindGroupAsync("chattogram", chattogram.Id)).Status);
    }

    [Fact]
    public async Task An_order_after_the_lock_opens_a_new_group()
    {
        WebAppFactory.RequireDatabase();
        var clock = new FakeTimeProvider(Monday);
        var address = await NewAddressAsync("dhaka");
        var first = await PlaceAsync(clock, address);
        clock.SetUtcNow(Deadline.AddMinutes(5));
        await RunLockJobAsync("dhaka", clock);

        var next = await PlaceAsync(clock, address);

        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal(DeliveryGroupStatus.Open, next.Status);
        Assert.Equal(Deadline.AddDays((await TenantAsync("dhaka")).GroupJoinDays), next.LocksAt);
        Assert.Equal(DeliveryGroupStatus.Locked, (await FindGroupAsync("dhaka", first.Id)).Status);
    }

    /// <summary>An order past the deadline locks a group while the job holds it: the job skips it and goes on.</summary>
    [Fact]
    public async Task A_group_locked_by_someone_else_mid_run_is_skipped_and_the_rest_are_locked()
    {
        WebAppFactory.RequireDatabase();
        var clock = new FakeTimeProvider(Monday);
        var first = await PlaceAsync(clock, await NewAddressAsync("dhaka"));
        var second = await PlaceAsync(clock, await NewAddressAsync("dhaka"));
        clock.SetUtcNow(Deadline.AddDays(2));
        var rival = new BeforeFirstSave(async () =>
        {
            await using var scope = await ScopeForAsync("dhaka");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var group = await db.DeliveryGroups.SingleAsync(g => g.Id == first.Id);
            group.LockIfDue(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync();
        });

        await using var jobScope = await ScopeForAsync("dhaka");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(WebAppFactory.ConnectionString)
            .AddInterceptors(jobScope.ServiceProvider.GetRequiredService<TenantSaveInterceptor>(), rival)
            .Options;
        await using var jobDb = new AppDbContext(
            options,
            jobScope.ServiceProvider.GetRequiredService<ITenantContext>(),
            jobScope.ServiceProvider.GetRequiredService<ICurrentUser>());
        await new LockDueGroupsJob(jobDb, clock, NullLogger<LockDueGroupsJob>.Instance)
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.True(rival.Ran);
        Assert.Equal(DeliveryGroupStatus.Locked, (await FindGroupAsync("dhaka", first.Id)).Status);
        Assert.Equal(DeliveryGroupStatus.Locked, (await FindGroupAsync("dhaka", second.Id)).Status);
    }

    [Fact]
    public async Task The_runner_sets_the_jobs_tenant_from_its_parameter()
    {
        WebAppFactory.RequireDatabase();
        var chattogram = await TenantAsync("chattogram");
        await using var scope = factory.Services.CreateAsyncScope();
        var runner = ProbeRunner(scope, new RecordingJobClient());
        TenantProbeJob.Seen = null;

        await runner.RunAsync(nameof(TenantProbeJob), chattogram.Id, TestContext.Current.CancellationToken);

        Assert.Equal(chattogram.Slug, TenantProbeJob.Seen);
    }

    [Fact]
    public async Task The_runner_skips_a_tenant_that_is_not_active_and_refuses_an_unknown_job()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        await using var scope = factory.Services.CreateAsyncScope();
        var runner = ProbeRunner(scope, new RecordingJobClient());
        TenantProbeJob.Seen = null;

        await runner.RunAsync(nameof(TenantProbeJob), long.MaxValue, TestContext.Current.CancellationToken);

        Assert.Null(TenantProbeJob.Seen);
        Assert.False(scope.ServiceProvider.GetRequiredService<ITenantContext>().HasTenant);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("NoSuchJob", dhaka.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_recurring_entry_queues_one_job_per_active_tenant_carrying_its_id()
    {
        WebAppFactory.RequireDatabase();
        var tenants = await factory.Services.GetRequiredService<ITenantCatalog>().ListAsync(TestContext.Current.CancellationToken);
        var client = new RecordingJobClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var runner = ActivatorUtilities.CreateInstance<TenantJobRunner>(scope.ServiceProvider, client);

        await runner.EnqueueForEveryTenantAsync(nameof(LockDueGroupsJob), TestContext.Current.CancellationToken);

        Assert.Equal(tenants.Select(t => t.Id).Order(), client.Jobs.Select(job => (long)job.Args[1]).Order());
        Assert.All(client.Jobs, job =>
        {
            Assert.Equal(nameof(TenantJobRunner.RunAsync), job.Method.Name);
            Assert.Equal(nameof(LockDueGroupsJob), job.Args[0]);
        });
    }

    /// <summary>
    /// The scheduled job and the per-tenant job it queues come back out of Hangfire's storage format: a job
    /// Hangfire cannot load (a generic method, found by the live check) fails only on the server, never in a direct call.
    /// </summary>
    [Fact]
    public async Task The_scheduled_job_and_the_jobs_it_queues_survive_hangfires_storage_format()
    {
        WebAppFactory.RequireDatabase();
        var recurring = new RecordingRecurringJobs();
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        new ServiceCollection().AddSingleton<IRecurringJobManager>(recurring).BuildServiceProvider()
            .ScheduleJobs(configuration);
        var client = new RecordingJobClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var runner = ActivatorUtilities.CreateInstance<TenantJobRunner>(scope.ServiceProvider, client);

        foreach (var (_, scheduled) in recurring.Jobs)
        {
            await runner.EnqueueForEveryTenantAsync((string)scheduled.Job.Args[0], TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            [
                ("lock-due-groups", configuration["Jobs:LockDueGroups"]),
                ("plan-trips", configuration["Jobs:PlanTrips"]),
                ("settle-merchants", configuration["Jobs:SettleMerchants"])
            ],
            recurring.Jobs.Select(job => (job.Key, (string?)job.Value.Cron)).OrderBy(job => job.Key));
        Assert.All(
            [.. recurring.Jobs.Values.Select(scheduled => scheduled.Job), .. client.Jobs],
            job => Assert.Equal(job.Method, InvocationData.SerializeJob(job).DeserializeJob().Method));
        Assert.Equal(
            [nameof(LockDueGroupsJob), nameof(PlanTripsJob), nameof(SettleMerchantsJob)],
            client.Jobs.Select(job => (string)job.Args[0]).Distinct().Order());
        Assert.Equal(typeof(PlanTripsJob), factory.Services.GetRequiredService<TenantJobRegistry>().Find(nameof(PlanTripsJob)));
    }

    [Fact]
    public void The_test_host_runs_no_job_server_and_no_outbox_dispatcher()
    {
        WebAppFactory.RequireDatabase();

        var hosted = factory.Services.GetServices<IHostedService>().Select(service => service.GetType().Name);

        Assert.DoesNotContain(hosted, name => name.Contains("BackgroundJobServer"));
        Assert.DoesNotContain(nameof(OutboxDispatcher), hosted);
    }

    private static TenantJobRunner ProbeRunner(AsyncServiceScope scope, IBackgroundJobClient client)
    {
        return ActivatorUtilities.CreateInstance<TenantJobRunner>(
            scope.ServiceProvider,
            client,
            new TenantJobRegistry(typeof(TenantProbeJob)));
    }

    private async Task RunLockJobAsync(string slug, TimeProvider clock)
    {
        await using var scope = await ScopeForAsync(slug);

        await new LockDueGroupsJob(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                clock,
                NullLogger<LockDueGroupsJob>.Instance)
            .RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>An order from the tenant's first shop to <paramref name="address"/>, placed at the clock's time.</summary>
    private async Task<DeliveryGroup> PlaceAsync(FakeTimeProvider clock, CustomerAddress address)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var tenant = await TenantByIdAsync(address.TenantId);
        await using var scope = await ScopeForAsync(tenant.Slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pickupPoint = await db.PickupPoints.Where(p => p.IsDefault).OrderBy(p => p.Id).FirstAsync(cancellation);
        var hubId = await db.Areas
            .Where(a => a.Id == address.AreaId)
            .Select(a => a.Zone!.HubId)
            .SingleAsync(cancellation);
        var order = Order.Create(new NewOrder(
            pickupPoint.MerchantId,
            address.CustomerId,
            address.Id,
            pickupPoint.Id,
            "Lock job test",
            500,
            500,
            DeliverySpeed.Combine,
            false,
            [new NewPackage("Box", 500)])).Value;
        await new DeliveryGrouping(db, scope.ServiceProvider.GetRequiredService<ITenantContext>(), clock)
            .SaveInGroupAsync(order, hubId, cancellation);

        return order.DeliveryGroup!;
    }

    private async Task<CustomerAddress> NewAddressAsync(string slug)
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var scope = await ScopeForAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phone = "013" + Random.Shared.Next(0, 100_000_000).ToString("D8");
        var customer = new Customer(PhoneNumber.Parse(phone).Value, "Lock job test");
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellation);

        var areaId = await db.Areas.Select(a => a.Id).FirstAsync(cancellation);
        var address = new CustomerAddress(customer.Id, areaId, "House 3, Road 8", null, null);
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(cancellation);

        return address;
    }

    private async Task<DeliveryGroup> FindGroupAsync(string slug, long id)
    {
        await using var scope = await ScopeForAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeliveryGroups
            .AsNoTracking()
            .SingleAsync(g => g.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task<AsyncServiceScope> ScopeForAsync(string slug)
    {
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(await TenantAsync(slug));

        return scope;
    }

    private async Task<TenantInfo> TenantAsync(string slug)
    {
        return (await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug))!;
    }

    private async Task<TenantInfo> TenantByIdAsync(long id)
    {
        return (await factory.Services.GetRequiredService<ITenantCatalog>().FindByIdAsync(id))!;
    }

    /// <summary>Records the tenant it runs for. Not registered: the runner creates it from the job's scope.</summary>
    private sealed class TenantProbeJob(ITenantContext tenantContext) : ITenantJob
    {
        // Static: a value set inside the awaited runner would not flow back through an AsyncLocal. The tests of one
        // class run one at a time
        public static string? Seen { get; set; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            Seen = tenantContext.Tenant?.Slug;

            return Task.CompletedTask;
        }
    }

    /// <summary>Keeps queued jobs in memory instead of Hangfire's storage.</summary>
    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public List<Job> Jobs { get; } = [];

        public string Create(Job job, IState state)
        {
            Jobs.Add(job);

            return Jobs.Count.ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState)
        {
            return true;
        }
    }

    /// <summary>Keeps recurring jobs in memory instead of Hangfire's storage.</summary>
    private sealed class RecordingRecurringJobs : IRecurringJobManager
    {
        public Dictionary<string, (Job Job, string Cron)> Jobs { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        {
            Jobs[recurringJobId] = (job, cronExpression);
        }

        public void Trigger(string recurringJobId)
        {
        }

        public void RemoveIfExists(string recurringJobId)
        {
            Jobs.Remove(recurringJobId);
        }
    }

    /// <summary>Runs <paramref name="action"/> once, just before the context's first save reaches the database.</summary>
    private sealed class BeforeFirstSave(Func<Task> action) : SaveChangesInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Ran)
            {
                Ran = true;
                await action();
            }

            return result;
        }
    }
}
