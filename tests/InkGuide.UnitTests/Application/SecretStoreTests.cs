using System.Threading;
using System.Threading.Tasks;
using InkGuide.Domain.Common;
using InkGuide.UnitTests.Fakes;
using Xunit;

namespace InkGuide.UnitTests.Application;

public sealed class SecretStoreTests
{
    [Fact]
    public async Task SetGetDeleteRoundTrip()
    {
        InMemorySecretStore store = new();

        Assert.True((await store.SetAsync("ai.key", "secret", CancellationToken.None)).IsSuccess);

        Result<string?, Failure> loaded = await store.GetAsync("ai.key", CancellationToken.None);
        Assert.True(loaded.IsSuccess);
        Assert.Equal("secret", loaded.Value!);

        Assert.True((await store.DeleteAsync("ai.key", CancellationToken.None)).IsSuccess);
        Assert.Null((await store.GetAsync("ai.key", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task MissingKeysReadNullAndDeleteSucceeds()
    {
        InMemorySecretStore store = new();

        Assert.Null((await store.GetAsync("missing", CancellationToken.None)).Value);
        Assert.True((await store.DeleteAsync("missing", CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task SetOverwritesPreviousValues()
    {
        InMemorySecretStore store = new();
        await store.SetAsync("k", "one", CancellationToken.None);
        await store.SetAsync("k", "two", CancellationToken.None);

        Assert.Equal("two", (await store.GetAsync("k", CancellationToken.None)).Value);
    }
}
