using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// Holds every request that records <see cref="Action"/> until <c>parties</c> of them arrive, so
/// concurrent requests deterministically pass their checks before any of them saves. The audit
/// entry is recorded just before <c>SaveChanges</c>, which is exactly the race window.
/// </summary>
public sealed class BarrierAuditTrail : IAuditTrail, IDisposable
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);
    private readonly Barrier _barrier;
    private int _arrived;
    private IAuditTrail? _inner;

    private BarrierAuditTrail(string action, int parties)
    {
        Action = action;
        _barrier = new Barrier(parties);
    }

    public string Action { get; }

    /// <summary>How many requests reached the barrier.</summary>
    public int Arrived => Volatile.Read(ref _arrived);

    /// <summary>Replaces the host's audit trail with a barrier in front of it.</summary>
    public static BarrierAuditTrail Decorate(IServiceCollection services, string action, int parties)
    {
        var barrier = new BarrierAuditTrail(action, parties);
        var original = services.Last(d => d.ServiceType == typeof(IAuditTrail));
        services.Remove(original);
        services.AddSingleton<IAuditTrail>(provider =>
        {
            barrier._inner ??= (IAuditTrail)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!);
            return barrier;
        });
        return barrier;
    }

    public void Record(DbContext context, AuditRecord record)
    {
        if (record.Action == Action)
        {
            Interlocked.Increment(ref _arrived);
            _barrier.SignalAndWait(MaxWait);
        }

        _inner!.Record(context, record);
    }

    public void Dispose() => _barrier.Dispose();
}
