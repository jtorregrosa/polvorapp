using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.Api.Tests.Storage;

/// <summary>Spec platform: Stored file cleanup (design D2).</summary>
public sealed class StoredObjectSweeperTests : IDisposable
{
    private const string Prefix = "registry/photos/";
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryStorage _storage = new();
    private readonly CapturingLoggerProvider _logs = new();

    public void Dispose() => _logs.Dispose();

    [Fact]
    public async Task Unreferenced_objects_older_than_an_hour_are_deleted()
    {
        _storage.Add($"{Prefix}orphan.jpg", Now.AddHours(-2));
        var owner = new FakeOwner(Prefix);

        await CreateSweeper(owner).SweepAsync(Token);

        Assert.Empty(_storage.Keys);
    }

    [Fact]
    public async Task Referenced_and_recent_objects_are_kept()
    {
        _storage.Add($"{Prefix}referenced.jpg", Now.AddDays(-30));
        _storage.Add($"{Prefix}recent.jpg", Now.AddMinutes(-59));
        _storage.Add($"{Prefix}orphan.jpg", Now.AddHours(-2));
        var owner = new FakeOwner(Prefix, $"{Prefix}referenced.jpg");

        await CreateSweeper(owner).SweepAsync(Token);

        Assert.Equal([$"{Prefix}recent.jpg", $"{Prefix}referenced.jpg"], _storage.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Objects_outside_the_owner_prefix_are_never_touched()
    {
        _storage.Add("catalog/logos/someone-elses.png", Now.AddDays(-30));
        var owner = new FakeOwner(Prefix);

        await CreateSweeper(owner).SweepAsync(Token);

        Assert.Equal(["catalog/logos/someone-elses.png"], _storage.Keys);
    }

    [Fact]
    public async Task References_are_asked_in_pages_of_five_hundred()
    {
        var keys = Enumerable.Range(0, 1201).Select(i => $"{Prefix}{i:D4}.jpg").ToList();
        keys.ForEach(key => _storage.Add(key, Now.AddHours(-2)));
        var owner = new FakeOwner(Prefix, [.. keys.Skip(10)]);

        await CreateSweeper(owner).SweepAsync(Token);

        Assert.Equal([500, 500, 201], owner.PageSizes);
        Assert.Equal(1191, _storage.Keys.Count);
    }

    [Fact]
    public async Task A_run_that_would_delete_most_objects_deletes_nothing_and_logs_an_error()
    {
        for (var i = 0; i < 150; i++)
        {
            _storage.Add($"{Prefix}{i:D4}.jpg", Now.AddHours(-2));
        }

        await CreateSweeper(new FakeOwner(Prefix)).SweepAsync(Token);

        Assert.Equal(150, _storage.Keys.Count);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_few_orphans_among_many_photos_are_deleted()
    {
        var keys = Enumerable.Range(0, 300).Select(i => $"{Prefix}{i:D4}.jpg").ToList();
        keys.ForEach(key => _storage.Add(key, Now.AddHours(-2)));

        await CreateSweeper(new FakeOwner(Prefix, [.. keys.Skip(120)])).SweepAsync(Token);

        Assert.Equal(180, _storage.Keys.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("registry")]
    [InlineData("registry/photos")]
    [InlineData("../photos/")]
    public async Task An_owner_with_a_malformed_prefix_is_skipped(string prefix)
    {
        _storage.Add($"{Prefix}orphan.jpg", Now.AddHours(-2));

        await CreateSweeper(new FakeOwner(prefix)).SweepAsync(Token);

        Assert.Single(_storage.Keys);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Owners_with_overlapping_prefixes_are_skipped()
    {
        _storage.Add($"{Prefix}orphan.jpg", Now.AddHours(-2));

        await CreateSweeper(new FakeOwner(Prefix), new FakeOwner(Prefix)).SweepAsync(Token);

        Assert.Single(_storage.Keys);
    }

    [Fact]
    public async Task A_failed_delete_does_not_stop_the_other_deletions()
    {
        _storage.Add($"{Prefix}a-stuck.jpg", Now.AddHours(-2));
        _storage.Add($"{Prefix}b-orphan.jpg", Now.AddHours(-2));
        _storage.Undeletable.Add($"{Prefix}a-stuck.jpg");

        await CreateSweeper(new FakeOwner(Prefix)).SweepAsync(Token);

        Assert.Equal([$"{Prefix}a-stuck.jpg"], _storage.Keys);
        var summary = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Information);
        Assert.Equal(1, summary.State["Failed"]);
    }

    [Fact]
    public async Task A_disabled_sweeper_never_runs()
    {
        _storage.Add($"{Prefix}orphan.jpg", Now.AddHours(-2));
        using var sweeper = CreateSweeper(enabled: false, new FakeOwner(Prefix));

        await sweeper.StartAsync(Token);
        _time.Advance(TimeSpan.FromDays(1));
        await Task.Delay(200, Token);
        await sweeper.StopAsync(Token);

        Assert.Single(_storage.Keys);
    }

    [Fact]
    public async Task A_failing_reference_query_deletes_nothing_and_logs_a_warning()
    {
        _storage.Add($"{Prefix}orphan.jpg", Now.AddHours(-2));
        var owner = new FakeOwner(Prefix) { Failure = new InvalidOperationException("database down") };

        await CreateSweeper(owner).SweepAsync(Token);

        Assert.Single(_storage.Keys);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning && e.Category.EndsWith(nameof(StoredObjectSweeper), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failing_owner_does_not_stop_the_others()
    {
        _storage.Add($"{Prefix}orphan.jpg", Now.AddHours(-2));
        _storage.Add("catalog/logos/orphan.png", Now.AddHours(-2));
        var failing = new FakeOwner(Prefix) { Failure = new InvalidOperationException("database down") };
        var healthy = new FakeOwner("catalog/logos/");

        await CreateSweeper(failing, healthy).SweepAsync(Token);

        Assert.Equal([$"{Prefix}orphan.jpg"], _storage.Keys);
    }

    [Fact]
    public async Task An_unreachable_storage_deletes_nothing_and_logs_a_warning()
    {
        _storage.Add($"{Prefix}orphan.jpg", Now.AddHours(-2));
        _storage.Unavailable = true;

        await CreateSweeper(new FakeOwner(Prefix)).SweepAsync(Token);

        _storage.Unavailable = false;
        Assert.Single(_storage.Keys);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Each_run_logs_counts_and_no_keys()
    {
        _storage.Add($"{Prefix}orphan-key-sentinel.jpg", Now.AddHours(-2));
        _storage.Add($"{Prefix}kept.jpg", Now.AddHours(-2));

        await CreateSweeper(new FakeOwner(Prefix, $"{Prefix}kept.jpg")).SweepAsync(Token);

        var summary = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Information);
        Assert.Equal(2, summary.State["Scanned"]);
        Assert.Equal(1, summary.State["Deleted"]);
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("sentinel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_sweeper_waits_one_interval_before_its_first_run()
    {
        _storage.Add($"{Prefix}orphan.jpg", Now.AddHours(-2));
        using var sweeper = CreateSweeper(new FakeOwner(Prefix));

        await sweeper.StartAsync(Token);
        await Task.Delay(200, Token);
        Assert.Single(_storage.Keys);

        // ExecuteAsync runs on a background thread: keep the clock moving until its timer fires.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_storage.Keys.Count > 0 && DateTime.UtcNow < deadline)
        {
            _time.Advance(TimeSpan.FromMinutes(60));
            await Task.Delay(20, Token);
        }

        await sweeper.StopAsync(Token);

        Assert.Empty(_storage.Keys);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private StoredObjectSweeper CreateSweeper(params FakeOwner[] owners) => CreateSweeper(enabled: true, owners);

    private StoredObjectSweeper CreateSweeper(bool enabled, params FakeOwner[] owners)
    {
        var services = new ServiceCollection();
        foreach (var owner in owners)
        {
            services.AddScoped<IStoredObjectOwner>(_ => owner);
        }

        var provider = services.BuildServiceProvider();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        return new StoredObjectSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(), _storage,
            Microsoft.Extensions.Options.Options.Create(new StorageOptions { SweepIntervalMinutes = 60, SweepEnabled = enabled }),
            _time, loggerFactory.CreateLogger<StoredObjectSweeper>());
    }

    private sealed class FakeOwner(string prefix, params string[] referenced) : IStoredObjectOwner
    {
        public string Prefix => prefix;

        public List<int> PageSizes { get; } = [];

        public Exception? Failure { get; init; }

        public Task<IReadOnlySet<string>> FilterReferencedAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            PageSizes.Add(keys.Count);
            return Failure is { } failure
                ? Task.FromException<IReadOnlySet<string>>(failure)
                : Task.FromResult<IReadOnlySet<string>>(keys.Where(referenced.Contains).ToHashSet(StringComparer.Ordinal));
        }
    }

    private sealed class InMemoryStorage : IObjectStorage
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _objects = new(StringComparer.Ordinal);

        public bool Unavailable { get; set; }

        public HashSet<string> Undeletable { get; } = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => [.. _objects.Keys];

        public void Add(string key, DateTimeOffset lastModified) => _objects[key] = lastModified;

        public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            ThrowIfUnavailable();
            if (Undeletable.Contains(key))
            {
                throw new StorageUnavailableException();
            }

            _objects.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ThrowIfUnavailable();
            await Task.Yield();
            foreach (var (key, modified) in _objects.Where(o => o.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(o => o.Key, StringComparer.Ordinal).ToList())
            {
                yield return new StoredObjectInfo(key, modified);
            }
        }

        private void ThrowIfUnavailable()
        {
            if (Unavailable)
            {
                throw new StorageUnavailableException();
            }
        }
    }
}
