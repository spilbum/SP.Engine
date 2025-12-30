using SP.Shared.Resource.Table;

namespace OperationTool.Diff;

public static class RefsDiffService
{
    public static RefsDiffResult Diff(RefsSnapshot oldSnap, RefsSnapshot newSnap)
    {
        var result = new RefsDiffResult();
        
        var allNames = new HashSet<string>(oldSnap.Tables.Keys, StringComparer.Ordinal);
        allNames.UnionWith(newSnap.Tables.Keys);

        foreach (var name in allNames)
        {
            var hasOld = oldSnap.Tables.TryGetValue(name, out var oldTable);
            var hasNew = newSnap.Tables.TryGetValue(name, out var newTable);

            switch (hasOld)
            {
                case false when hasNew:
                    result.Tables.Add(new RefsTableDiff
                    {
                        Name = name,
                        Kind = TableDiffKind.Added
                    });
                    break;
                case true when !hasNew:
                    result.Tables.Add(new RefsTableDiff
                    {
                        Name = name,
                        Kind = TableDiffKind.Removed
                    });
                    break;
                default:
                {
                    var tableDiff = DiffTable(oldTable!, newTable!);
                    result.Tables.Add(tableDiff);
                    break;
                }
            }
        }
        
        return result;
    }

    private static List<RefsColumnDiff> DiffColumn(
        RefTableSchema oldSchema,
        RefTableSchema newSchema)
    {
        var oldCols = oldSchema.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var newCols = newSchema.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        
        var allNames = new HashSet<string>(oldCols.Keys, StringComparer.Ordinal);
        allNames.UnionWith(newCols.Keys);
        
        var diffs = new List<RefsColumnDiff>();

        foreach (var name in allNames)
        {
            var hasOld = oldCols.TryGetValue(name, out var oldCol);
            var hasNew = newCols.TryGetValue(name, out var newCol);
            
            switch (hasOld)
            {
                case false when hasNew:
                    diffs.Add(new RefsColumnDiff
                    {
                        Name = name,
                        Kind = ColumnDiffKind.Added,
                        OldType = null,
                        NewType = newCol!.Type
                    });
                    break;
                case true when !hasNew:
                    diffs.Add(new RefsColumnDiff
                    {
                        Name = name,
                        Kind = ColumnDiffKind.Removed,
                        OldType = oldCol!.Type,
                        NewType = null
                    });
                    break;
                default:
                {
                    var modified = oldCol!.Type != newCol!.Type ||
                                   oldCol.IsKey != newCol.IsKey;

                    if (modified)
                    {
                        diffs.Add(new RefsColumnDiff
                        {
                            Name = name,
                            Kind = ColumnDiffKind.Modified,
                            OldType = oldCol.Type,
                            NewType = newCol.Type,
                            OldIsKey = oldCol.IsKey,
                            NewIsKey = newCol.IsKey,
                        });
                    }

                    break;
                }
            }
        }
        
        return diffs;
    }

    private static int[] GetKeyIndexes(RefTableSchema schema)
    {
        var list = new List<int>();
        for (var i = 0; i < schema.Columns.Count; i++)
        {
            if (schema.Columns[i].IsKey)
                list.Add(i);
        }

        if (list.Count == 0)
            throw new InvalidOperationException($"No key columns defined for table: {schema.Name}");

        return list.ToArray();
    }

    private static string BuildKey(RefRow row, int[] keyIndexes)
    {
        var values = new string[keyIndexes.Length];
        for (var i = 0; i < keyIndexes.Length; i++)
        {
            var v = row.Get(keyIndexes[i]);
            values[i] = NormalizeKeyValue(v);
        }

        return string.Join("|", values);
    }

    private static string NormalizeKeyValue(object? value)
    {
        if (value is null) return string.Empty;

        return value switch
        {
            bool b => b ? "1" : "0",
            DateTime dt => dt.ToUniversalTime().ToString("O"),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool AreValuesEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        return a switch
        {
            double da when b is double db => Math.Abs(da - db) < 1e-9,
            float fa when b is float fb => Math.Abs(fa - fb) < 1e-6f,
            DateTime dt1 when b is DateTime dt2 => dt1.ToUniversalTime() == dt2.ToUniversalTime(),
            _ => a.Equals(b)
        };
    }

    private static List<RefsCellDiff> DiffCells(
        RefRow oldRow,
        RefRow newRow,
        RefTableSchema schema)
    {
        var diffs = new List<RefsCellDiff>();

        for (var i = 0; i < schema.Columns.Count; i++)
        {
            var col = schema.Columns[i];
            var oldVal = oldRow.Get(i);
            var newVal = newRow.Get(i);

            if (!AreValuesEqual(oldVal, newVal))
            {
                diffs.Add(new RefsCellDiff
                {
                    ColumnName = col.Name,
                    OldValue = oldVal,
                    NewValue = newVal,
                });
            }
        }
        
        return diffs;
    }

    private static RefsTableDiff DiffTable(
        RefsTableSnapshot oldSnap,
        RefsTableSnapshot newSnap)
    {
        var oldSchema = oldSnap.Schema;
        var newSchema = newSnap.Schema;

        var tableDiff = new RefsTableDiff { Name = newSchema.Name };
        
        // 컬럼/스키마 비교
        var columnDiffs = DiffColumn(oldSchema, newSchema);
        tableDiff.Columns.AddRange(columnDiffs);
        
        // PK 정의 비교
        var oldKeyNames = oldSchema.Columns.Where(column => column.IsKey).Select(column => column.Name).ToArray();
        var newKeyNames = newSchema.Columns.Where(column => column.IsKey).Select(column => column.Name).ToArray();
        var pkChanged = !oldKeyNames.SequenceEqual(newKeyNames, StringComparer.Ordinal);
        tableDiff.PrimaryKeyChanged = pkChanged;
        
        // 데이터 비교
        if (!pkChanged)
        {
            var keyIndexes = GetKeyIndexes(newSchema);

            var oldRows = new Dictionary<string, RefRow>();
            foreach (var row in oldSnap.Data.Rows)
            {
                var key = BuildKey(row, keyIndexes);
                oldRows[key] = row;
            }
            
            var newRows = new Dictionary<string, RefRow>();
            foreach (var row in newSnap.Data.Rows)
            {
                var key = BuildKey(row, keyIndexes);
                newRows[key] = row;
            }

            var allKeys = new HashSet<string>(oldRows.Keys);
            allKeys.UnionWith(newRows.Keys);

            foreach (var key in allKeys)
            {
                var hasOld = oldRows.TryGetValue(key, out var oldRow);
                var hasNew = newRows.TryGetValue(key, out var newRow);

                switch (hasOld)
                {
                    case false when hasNew:
                    {
                        var cells = new List<RefsCellDiff>();
                        for (var i = 0; i < newSchema.Columns.Count; i++)
                        {
                            var col = newSchema.Columns[i];
                            var newVal = newRow!.Get(i);
                            
                            cells.Add(new RefsCellDiff
                            {
                                ColumnName = col.Name,
                                OldValue = null,
                                NewValue = newVal
                            });
                        }
                        
                        tableDiff.Rows.Add(new RefsRowDiff
                        {
                            Key = key,
                            Kind = RowDiffKind.Added,
                            Cells = cells
                        });
                        break;
                    }
                    case true when !hasNew:
                    {
                        var cells = new List<RefsCellDiff>();
                        for (var i = 0; i < oldSchema.Columns.Count; i++)
                        {
                            var col = oldSchema.Columns[i];
                            var oldVal = oldRow!.Get(i);
                            
                            cells.Add(new RefsCellDiff
                            {
                                ColumnName = col.Name,
                                OldValue = oldVal,
                                NewValue = null
                            });
                        }
                        
                        tableDiff.Rows.Add(new RefsRowDiff
                        {
                            Key = key,
                            Kind = RowDiffKind.Removed,
                            Cells = cells
                        });
                        break;
                    }
                    default:
                    {
                        var cells = DiffCells(oldRow!, newRow!, newSchema);
                        if (cells.Count > 0)
                        {
                            tableDiff.Rows.Add(new RefsRowDiff
                            {
                                Key = key,
                                Kind = RowDiffKind.Modified,
                                Cells = cells
                            });
                        }
                        break;
                    }
                }
            }
        }
        
        // 최종 테이블 상태
        if (tableDiff.Columns.Count == 0 && tableDiff.Rows.Count == 0)
            tableDiff.Kind = TableDiffKind.Unchanged;
        else
        {
            tableDiff.Kind = TableDiffKind.Modified;
        }
        
        return tableDiff;
        
    }
}
