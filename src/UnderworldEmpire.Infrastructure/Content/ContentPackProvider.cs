namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Singleton implementation of <see cref="IContentPackProvider"/> that holds the
/// active pack loaded at startup.
/// </summary>
internal sealed class ContentPackProvider : IContentPackProvider
{
    public ContentPack ActivePack { get; }

    public string PackRoot { get; }

    public JobCatalog JobCatalog { get; }

    public ItemCatalog ItemCatalog { get; }

    public PropertyCatalog PropertyCatalog { get; }

    public ObjectiveCatalog ObjectiveCatalog { get; }

    public DailyRewardCatalog DailyRewardCatalog { get; }

    public ContentPackProvider(
        ContentPack pack,
        string packRoot,
        JobCatalog jobCatalog,
        ItemCatalog itemCatalog,
        PropertyCatalog propertyCatalog,
        ObjectiveCatalog objectiveCatalog,
        DailyRewardCatalog dailyRewardCatalog)
    {
        ActivePack = pack;
        PackRoot = packRoot;
        JobCatalog = jobCatalog;
        ItemCatalog = itemCatalog;
        PropertyCatalog = propertyCatalog;
        ObjectiveCatalog = objectiveCatalog;
        DailyRewardCatalog = dailyRewardCatalog;
    }
}
