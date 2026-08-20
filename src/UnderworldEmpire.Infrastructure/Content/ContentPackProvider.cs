namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Singleton implementation of <see cref="IContentPackProvider"/> that holds the
/// active pack loaded at startup.
/// </summary>
internal sealed class ContentPackProvider : IContentPackProvider
{
    public ContentPack ActivePack { get; }

    public JobCatalog JobCatalog { get; }

    public ItemCatalog ItemCatalog { get; }

    public PropertyCatalog PropertyCatalog { get; }

    public ContentPackProvider(
        ContentPack pack,
        JobCatalog jobCatalog,
        ItemCatalog itemCatalog,
        PropertyCatalog propertyCatalog)
    {
        ActivePack = pack;
        JobCatalog = jobCatalog;
        ItemCatalog = itemCatalog;
        PropertyCatalog = propertyCatalog;
    }
}
