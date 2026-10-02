using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Storage;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Spec "Registry lock (BR-10, UC-11)": the lock takes effect atomically (design D8). A FiringChief
/// edit already inside its transaction holds the lock row <c>FOR SHARE</c>; locking waits for it, the
/// edit commits, and the next FiringChief edit is refused. The same holds for an insert; an upload
/// locked out after storing its image erases the image.
/// </summary>
public sealed class RegistryLockRaceTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Locking_waits_for_an_edit_in_flight_and_refuses_the_next_one()
    {
        GateAuditTrail gate = null!;
        await using var registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => gate = GateAuditTrail.Decorate(services, "ArquebusierUpdated"));
        using var disposeGate = gate;
        var target = (await registry.RegisterAsync(registry.Own.Id)).GetProperty("id").GetGuid();

        var edit = EditBody(await DetailAsync(registry, target), "+34 600 000 101");
        var inFlight = registry.FiringChief.PutAsJsonAsync($"/api/arquebusiers/{target}", edit, TestContext.Current.CancellationToken);
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "The edit never reached its audit entry.");

        var locking = RegistryLockTests.SetAsync(registry.Admin, true);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        Assert.False(locking.IsCompleted, "Locking did not wait for the edit in flight.");

        gate.Release();
        using var edited = await inFlight;
        using var locked = await locking;

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        using var next = await registry.FiringChief.PutAsJsonAsync(
            $"/api/arquebusiers/{target}", EditBody(await DetailAsync(registry, target), "+34 600 000 102"), TestContext.Current.CancellationToken);
        await AssertProblemAsync(next, HttpStatusCode.Conflict, "registry.locked");
        Assert.Equal("+34 600 000 101", (await DetailAsync(registry, target)).GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Locking_waits_for_a_registration_in_flight_and_refuses_the_next_one()
    {
        GateAuditTrail gate = null!;
        await using var registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => gate = GateAuditTrail.Decorate(services, "ArquebusierRegistered"));
        using var disposeGate = gate;

        var inFlight = registry.FiringChief.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(registry.Own.Id));
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "The registration never reached its audit entry.");

        var locking = RegistryLockTests.SetAsync(registry.Admin, true);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        Assert.False(locking.IsCompleted, "Locking did not wait for the registration in flight.");

        gate.Release();
        using var registered = await inFlight;
        using var locked = await locking;

        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        using var next = await registry.FiringChief.PostAsync("/api/arquebusiers", RegistryTestHost.NewArquebusier(registry.Own.Id));
        await AssertProblemAsync(next, HttpStatusCode.Conflict, "registry.locked");
    }

    [Fact]
    public async Task An_upload_locked_out_after_storing_its_image_is_refused_and_erases_the_image()
    {
        var storage = new LockingStorage();
        await using var registry = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => services.AddSingleton<IObjectStorage>(provider => storage.Wrap(provider.GetRequiredService<S3ObjectStorage>())));
        var target = (await registry.RegisterAsync(registry.Own.Id)).GetProperty("id").GetGuid();
        // The lock commits after the early check, while the image is being stored.
        storage.LockWith = registry.Admin;

        using var upload = await PhotoRequests.UploadAsync(registry.FiringChief, target, "id", TestImages.Jpeg(600, 800));

        await AssertProblemAsync(upload, HttpStatusCode.Conflict, "registry.locked");
        var stored = Assert.Single(storage.Stored);
        Assert.Contains(stored, storage.Deleted);
        using var photo = await registry.Admin.GetAsync($"/api/arquebusiers/{target}/photos/id", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, photo.StatusCode);
    }

    private static Dictionary<string, object?> EditBody(JsonElement current, string phone)
    {
        var edit = ArquebusierEditingTests.EditOf(current);
        edit["phone"] = phone;
        return edit;
    }

    private static async Task<JsonElement> DetailAsync(RegistryTestHost registry, Guid id)
    {
        using var response = await registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        return await ReadAsync<JsonElement>(response);
    }

    /// <summary>Locks the registry as the Admin while the first image is stored, and records keys stored and deleted.</summary>
    private sealed class LockingStorage
    {
        private readonly ConcurrentQueue<string> _stored = new();
        private readonly ConcurrentQueue<string> _deleted = new();

        public HttpClient? LockWith { get; set; }

        public IReadOnlyCollection<string> Stored => _stored;

        public IReadOnlyCollection<string> Deleted => _deleted;

        public IObjectStorage Wrap(IObjectStorage inner) => new Locking(inner, this);

        private sealed class Locking(IObjectStorage inner, LockingStorage owner) : IObjectStorage
        {
            public async Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
            {
                if (owner.LockWith is { } admin)
                {
                    using var locked = await RegistryLockTests.SetAsync(admin, true);
                    locked.EnsureSuccessStatusCode();
                }

                owner._stored.Enqueue(key);
                await inner.PutAsync(key, content, contentType, cancellationToken);
            }

            public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken) => inner.GetAsync(key, cancellationToken);

            public Task DeleteAsync(string key, CancellationToken cancellationToken)
            {
                owner._deleted.Enqueue(key);
                return inner.DeleteAsync(key, cancellationToken);
            }

            public IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, CancellationToken cancellationToken) => inner.ListAsync(prefix, cancellationToken);
        }
    }

    /// <summary>Holds the first request that records <c>action</c> until the test releases it.</summary>
    private sealed class GateAuditTrail : IAuditTrail, IDisposable
    {
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);
        private readonly ManualResetEventSlim _released = new();
        private readonly string _action;
        private IAuditTrail? _inner;
        private int _held;

        private GateAuditTrail(string action) => _action = action;

        /// <summary>Set once the held request reached its audit entry, inside its transaction.</summary>
        public ManualResetEventSlim Entered { get; } = new();

        public static GateAuditTrail Decorate(IServiceCollection services, string action)
        {
            var gate = new GateAuditTrail(action);
            var original = services.Last(d => d.ServiceType == typeof(IAuditTrail));
            services.Remove(original);
            services.AddSingleton<IAuditTrail>(provider =>
            {
                gate._inner ??= (IAuditTrail)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!);
                return gate;
            });
            return gate;
        }

        public void Release() => _released.Set();

        public void Dispose()
        {
            _released.Dispose();
            Entered.Dispose();
        }

        public void Record(DbContext context, AuditRecord record)
        {
            if (record.Action == _action && Interlocked.Exchange(ref _held, 1) == 0)
            {
                Entered.Set();
                _released.Wait(MaxWait);
            }

            _inner!.Record(context, record);
        }
    }
}
