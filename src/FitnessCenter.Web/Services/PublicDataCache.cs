using Microsoft.Extensions.Caching.Memory;

namespace FitnessCenter.Web.Services;

public sealed class PublicDataCache(IMemoryCache memory)
{
    private long catalogVersion;
    private long siteVersion;

    public Task<T> CatalogAsync<T>(string key, Func<Task<T>> read) =>
        ReadAsync(("catalog", Volatile.Read(ref catalogVersion), key), read);

    public Task<SiteContent> SiteAsync(Func<Task<SiteContent>> read) =>
        ReadAsync(("site", Volatile.Read(ref siteVersion)), read);

    public void InvalidateCatalog() => Interlocked.Increment(ref catalogVersion);
    public void InvalidateSite() => Interlocked.Increment(ref siteVersion);

    private async Task<T> ReadAsync<T>(object key, Func<Task<T>> read) =>
        (await memory.GetOrCreateAsync(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return read();
        }))!;
}
