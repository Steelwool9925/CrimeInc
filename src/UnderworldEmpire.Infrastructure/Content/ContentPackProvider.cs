namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Singleton implementation of <see cref="IContentPackProvider"/> that holds the
/// active pack loaded at startup.
/// </summary>
internal sealed class ContentPackProvider : IContentPackProvider
{
    public ContentPack ActivePack { get; }

    public ContentPackProvider(ContentPack pack)
    {
        ActivePack = pack;
    }
}
