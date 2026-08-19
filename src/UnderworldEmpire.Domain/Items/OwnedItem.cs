namespace UnderworldEmpire.Domain.Items;

/// <summary>
/// A player's holding paired with the content definition it refers to.
/// </summary>
/// <remarks>
/// <see cref="PlayerItem"/> stores only an item id, because item stats and slots are content that a
/// retune may change. Any rule that needs both — slot exclusivity when equipping, stat aggregation when
/// reading — therefore needs the pair. The caller resolves ids through the catalog (infrastructure) and
/// hands the domain the resolved pairs, keeping content loading out of the domain layer.
/// </remarks>
/// <param name="Holding">The player's persisted holding.</param>
/// <param name="Item">The definition <see cref="PlayerItem.ItemId"/> resolves to.</param>
public sealed record OwnedItem(PlayerItem Holding, Item Item);
