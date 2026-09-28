namespace Application.Abstractions;

/// <summary>
/// Background work done for one tenant. The job runner sets <see cref="ITenantContext"/> from the job's tenant
/// parameter before calling it, so the job queries through the tenant filters like a request does and never takes
/// a tenant id of its own.
/// </summary>
public interface ITenantJob
{
    Task RunAsync(CancellationToken cancellationToken);
}
