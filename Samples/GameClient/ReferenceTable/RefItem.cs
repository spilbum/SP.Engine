
using SP.Shared.Resource.Table;

namespace SP.Shared.Resource
{
    public sealed class RefItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public byte ItemType { get; set; }
        public int ItemId { get; set; }
        public int ItemValue { get; set; }

        public static RefItem Create(RefTableSchema schema, RefRow row)
        {
            var idxId = schema.GetColumnIndex("Id");
            var idxName = schema.GetColumnIndex("Name");
            var idxAssetName = schema.GetColumnIndex("AssetName");
            var idxItemType = schema.GetColumnIndex("ItemType");
            var idxItemId = schema.GetColumnIndex("ItemId");
            var idxItemValue = schema.GetColumnIndex("ItemValue");

            return new RefItem
            {
                Id = row.Get<int>(idxId),
                Name = row.Get<string>(idxName),
                AssetName = row.Get<string>(idxAssetName),
                ItemType = row.Get<byte>(idxItemType),
                ItemId = row.Get<int>(idxItemId),
                ItemValue = row.Get<int>(idxItemValue)
            };
        }
    }

    public sealed partial class ReferenceTableManager
    {
        public Dictionary<int, RefItem> Items { get; private set; } = new();

        private void BuildRefItem()
        {
            const string tableName = "Item";
            if (!TryGet(tableName, out var schema, out var table))
            {
                return;
            }

            Items.Clear();

            var dict = new Dictionary<int, RefItem>(table.Rows.Count);
            foreach (var row in table.Rows)
            {
                var obj = RefItem.Create(schema, row);
                dict[obj.Id] = obj;
            }
            Items = dict;
        }

        public RefItem? GetItem(int id)
        {
            return Items.TryGetValue(id, out var v) ? v : null;
        }
    }
}
