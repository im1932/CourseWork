using System;
using System.Collections.Generic;
using System.IO;
using SQLite;
using UnityEngine;
using UnityEngine.Networking;

public static class GiftCatalogDatabase
{
    private const string DatabaseFileName = "gift_catalog.db";
    private static List<string> cachedCollectionNames;
    private static readonly Dictionary<string, string> resolvedCollectionNameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Table("gift_items")]
    public sealed class GiftItemRecord
    {
        [PrimaryKey]
        public string id { get; set; }
        public string collection_name { get; set; }
        public string name { get; set; }
        public int rarity_permille { get; set; }
    }

    [Table("inventory")]
    public sealed class InventoryRecord
    {
        [PrimaryKey, AutoIncrement]
        public int id { get; set; }
        public string inventory_scope { get; set; }
        public int inventory_number { get; set; }
        public string gift_id { get; set; }
        public string gift_type_name { get; set; }
        public string model_id { get; set; }
        public string model_name { get; set; }
        public int model_rarity_permille { get; set; }
        public string background_name { get; set; }
        public string pattern_name { get; set; }
        public string created_at { get; set; }
    }

    [Table("achievements_progress")]
    public sealed class AchievementProgressRecord
    {
        [PrimaryKey]
        public string achievement_id { get; set; }
        public int progress { get; set; }
        public int is_unlocked { get; set; }
        public string unlocked_at { get; set; }
    }

    public static bool TryLoadGiftItems(string collectionName, out List<GiftItemRecord> items)
    {
        items = new List<GiftItemRecord>();

        if (string.IsNullOrWhiteSpace(collectionName))
            return false;

        try
        {
            using (SQLiteConnection connection = OpenConnection())
            {
                items = connection.Table<GiftItemRecord>()
                    .Where(row => row.collection_name == collectionName)
                    .ToList();

                if (items.Count == 0)
                {
                    string resolvedCollectionName = ResolveCollectionNameInternal(collectionName, connection);
                    if (!string.IsNullOrWhiteSpace(resolvedCollectionName) &&
                        !string.Equals(resolvedCollectionName, collectionName, StringComparison.OrdinalIgnoreCase))
                    {
                        items = connection.Table<GiftItemRecord>()
                            .Where(row => row.collection_name == resolvedCollectionName)
                            .ToList();
                    }
                }
            }

            return items.Count > 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GiftCatalogDatabase] Failed to load gift items: " + e.Message);
            return false;
        }
    }

    public static string ResolveCollectionName(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            return string.Empty;

        string normalizedRequest = collectionName.Trim();
        if (resolvedCollectionNameCache.TryGetValue(normalizedRequest, out string cachedResolvedName))
            return cachedResolvedName;

        try
        {
            using (SQLiteConnection connection = OpenConnection())
            {
                string resolvedCollectionName = ResolveCollectionNameInternal(normalizedRequest, connection);
                resolvedCollectionNameCache[normalizedRequest] = resolvedCollectionName;
                return resolvedCollectionName;
            }
        }
        catch
        {
            return normalizedRequest;
        }
    }

    public static bool CollectionNamesMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        string resolvedLeft = ResolveCollectionName(left);
        string resolvedRight = ResolveCollectionName(right);
        if (string.Equals(resolvedLeft, resolvedRight, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(NormalizeCollectionKeyLoose(left), NormalizeCollectionKeyLoose(right), StringComparison.OrdinalIgnoreCase);
    }

    public static GiftItemRecord FindGiftItem(string collectionName, string itemId, string itemName)
    {
        try
        {
            using (SQLiteConnection connection = OpenConnection())
            {
                if (!string.IsNullOrWhiteSpace(collectionName) && !string.IsNullOrWhiteSpace(itemId))
                {
                    GiftItemRecord byCollectionAndId = connection.Find<GiftItemRecord>(itemId);
                    if (byCollectionAndId != null &&
                        string.Equals(byCollectionAndId.collection_name, collectionName, StringComparison.OrdinalIgnoreCase))
                    {
                        return byCollectionAndId;
                    }
                }

                if (!string.IsNullOrWhiteSpace(collectionName) && !string.IsNullOrWhiteSpace(itemName))
                {
                    List<GiftItemRecord> byCollectionAndName = connection.Query<GiftItemRecord>(
                        "SELECT * FROM gift_items WHERE collection_name = ? AND name = ? LIMIT 1",
                        collectionName,
                        itemName);
                    if (byCollectionAndName.Count > 0)
                        return byCollectionAndName[0];
                }

                if (!string.IsNullOrWhiteSpace(itemId))
                    return connection.Find<GiftItemRecord>(itemId);

                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    List<GiftItemRecord> byName = connection.Query<GiftItemRecord>(
                        "SELECT * FROM gift_items WHERE name = ? LIMIT 1",
                        itemName);
                    if (byName.Count > 0)
                        return byName[0];
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GiftCatalogDatabase] Failed to find gift item: " + e.Message);
        }

        return null;
    }

    public static List<GiftItemRecord> LoadAllGiftItems()
    {
        try
        {
            using (SQLiteConnection connection = OpenConnection())
            {
                List<GiftItemRecord> rows = connection.Table<GiftItemRecord>().ToList();
                return rows;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GiftCatalogDatabase] Failed to load all gift items: " + e.Message);
            return new List<GiftItemRecord>();
        }
    }

    public static List<InventoryRecord> LoadInventory(string inventoryScope)
    {
        try
        {
            using (SQLiteConnection connection = OpenConnection())
            {
                EnsureInventoryTableSchema(connection);
                MigrateLegacyInventoryScope(connection, inventoryScope);
                List<InventoryRecord> rows = connection.Query<InventoryRecord>(
                    "SELECT * FROM inventory WHERE inventory_scope = ? ORDER BY id",
                    NormalizeInventoryScope(inventoryScope));
                return rows;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GiftCatalogDatabase] Failed to load inventory: " + e.Message);
            return new List<InventoryRecord>();
        }
    }

    public static bool HasInventoryRows(string inventoryScope)
    {
        try
        {
            using (SQLiteConnection connection = OpenConnection())
            {
                EnsureInventoryTableSchema(connection);
                MigrateLegacyInventoryScope(connection, inventoryScope);
                return connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM inventory WHERE inventory_scope = ?",
                    NormalizeInventoryScope(inventoryScope)) > 0;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GiftCatalogDatabase] Failed to check inventory rows: " + e.Message);
            return false;
        }
    }

    public static void ReplaceInventory(string inventoryScope, IEnumerable<InventoryRecord> rows)
    {
        using (SQLiteConnection connection = OpenConnection())
        {
            EnsureInventoryTableSchema(connection);
            int insertedCount = 0;
            string normalizedScope = NormalizeInventoryScope(inventoryScope);
            connection.RunInTransaction(() =>
            {
                connection.Execute("DELETE FROM inventory WHERE inventory_scope = ?", normalizedScope);
                if (rows == null)
                    return;

                foreach (InventoryRecord row in rows)
                {
                    if (row == null)
                        continue;

                    row.inventory_scope = normalizedScope;
                    connection.Insert(row);
                    insertedCount++;
                }
            });
        }
    }

    public static void ClearInventory(string inventoryScope)
    {
        using (SQLiteConnection connection = OpenConnection())
        {
            EnsureInventoryTableSchema(connection);
            connection.Execute("DELETE FROM inventory WHERE inventory_scope = ?", NormalizeInventoryScope(inventoryScope));
        }
    }

    public static List<AchievementProgressRecord> LoadAchievementProgress()
    {
        try
        {
            using (SQLiteConnection connection = OpenConnection())
            {
                EnsureAchievementProgressTableSchema(connection);
                return connection.Query<AchievementProgressRecord>(
                    "SELECT * FROM achievements_progress ORDER BY achievement_id");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GiftCatalogDatabase] Failed to load achievements progress: " + e.Message);
            return new List<AchievementProgressRecord>();
        }
    }

    public static void ReplaceAchievementProgress(IEnumerable<AchievementProgressRecord> rows)
    {
        using (SQLiteConnection connection = OpenConnection())
        {
            EnsureAchievementProgressTableSchema(connection);
            connection.RunInTransaction(() =>
            {
                connection.Execute("DELETE FROM achievements_progress");
                if (rows == null)
                    return;

                foreach (AchievementProgressRecord row in rows)
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.achievement_id))
                        continue;

                    connection.InsertOrReplace(row);
                }
            });
        }
    }

    private static SQLiteConnection OpenConnection()
    {
        string path = EnsureDatabaseFile();
        SQLiteConnection connection = new SQLiteConnection(path);
        if (HasTable(connection, "gift_items"))
            return connection;

        List<InventoryRecord> preservedInventoryRows = TryReadAllInventoryRows(connection);
        connection.Close();

        if (ReplaceDatabaseFile(path))
        {
            connection = new SQLiteConnection(path);
            RestoreInventoryRows(connection, preservedInventoryRows);
            if (HasTable(connection, "gift_items"))
                return connection;
        }

        Debug.LogWarning("[GiftCatalogDatabase] Database opened without table gift_items: " + path);
        return connection;
    }

    private static string EnsureDatabaseFile()
    {
        string destinationPath = Path.Combine(Application.persistentDataPath, DatabaseFileName);
        if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 0)
            return destinationPath;

        ReplaceDatabaseFile(destinationPath);

        return destinationPath;
    }

    private static bool ReplaceDatabaseFile(string destinationPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? Application.persistentDataPath);

            if (File.Exists(destinationPath))
                File.Delete(destinationPath);

            return TryCopyDatabaseFromStreamingAssets(destinationPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GiftCatalogDatabase] Failed to replace database file: " + e.Message);
            return false;
        }
    }

    private static bool TryCopyDatabaseFromStreamingAssets(string destinationPath)
    {
        string sourcePath = Path.Combine(Application.streamingAssetsPath, DatabaseFileName);

        try
        {
            if (sourcePath.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                using (UnityWebRequest request = UnityWebRequest.Get(sourcePath))
                {
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning("[GiftCatalogDatabase] Failed to copy database from StreamingAssets: " + request.error);
                        return false;
                    }

                    byte[] data = request.downloadHandler.data;
                    if (data == null || data.Length == 0)
                    {
                        Debug.LogWarning("[GiftCatalogDatabase] StreamingAssets database is empty: " + sourcePath);
                        return false;
                    }

                    File.WriteAllBytes(destinationPath, data);
                    return true;
                }
            }

            if (!File.Exists(sourcePath))
            {
                Debug.LogWarning("[GiftCatalogDatabase] StreamingAssets database was not found: " + sourcePath);
                return false;
            }

            File.Copy(sourcePath, destinationPath, true);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GiftCatalogDatabase] Failed to copy database file: " + e.Message);
            return false;
        }
    }

    private static bool HasTable(SQLiteConnection connection, string tableName)
    {
        if (connection == null || string.IsNullOrWhiteSpace(tableName))
            return false;

        try
        {
            List<SQLiteConnection.ColumnInfo> columns = connection.GetTableInfo(tableName);
            return columns != null && columns.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureInventoryTableSchema(SQLiteConnection connection)
    {
        connection.CreateTable<InventoryRecord>();

        List<SQLiteConnection.ColumnInfo> columns = connection.GetTableInfo("inventory");
        bool hasScopeColumn = false;
        for (int i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i].Name, "inventory_scope", StringComparison.OrdinalIgnoreCase))
            {
                hasScopeColumn = true;
                break;
            }
        }

        if (!hasScopeColumn)
            connection.Execute("ALTER TABLE inventory ADD COLUMN inventory_scope TEXT");
    }

    private static void EnsureAchievementProgressTableSchema(SQLiteConnection connection)
    {
        connection.CreateTable<AchievementProgressRecord>();
    }

    private static List<InventoryRecord> TryReadAllInventoryRows(SQLiteConnection connection)
    {
        if (connection == null || !HasTable(connection, "inventory"))
            return new List<InventoryRecord>();

        try
        {
            return connection.Query<InventoryRecord>("SELECT * FROM inventory ORDER BY id");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GiftCatalogDatabase] Failed to preserve inventory rows before DB replace: " + e.Message);
            return new List<InventoryRecord>();
        }
    }

    private static void RestoreInventoryRows(SQLiteConnection connection, List<InventoryRecord> rows)
    {
        if (connection == null || rows == null || rows.Count == 0)
            return;

        try
        {
            EnsureInventoryTableSchema(connection);
            int existingCount = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM inventory");
            if (existingCount > 0)
                return;

            connection.RunInTransaction(() =>
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    InventoryRecord row = rows[i];
                    if (row == null)
                        continue;

                    connection.Insert(row);
                }
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GiftCatalogDatabase] Failed to restore preserved inventory rows: " + e.Message);
        }
    }

    private static string NormalizeInventoryScope(string inventoryScope)
    {
        return string.IsNullOrWhiteSpace(inventoryScope) ? "default" : inventoryScope.Trim();
    }

    private static void MigrateLegacyInventoryScope(SQLiteConnection connection, string inventoryScope)
    {
        string normalizedScope = NormalizeInventoryScope(inventoryScope);
        int legacyCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM inventory WHERE inventory_scope IS NULL OR TRIM(inventory_scope) = ''");
        if (legacyCount <= 0)
            return;

        int scopedCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM inventory WHERE inventory_scope = ?",
            normalizedScope);
        if (scopedCount > 0)
            return;

        connection.Execute(
            "UPDATE inventory SET inventory_scope = ? WHERE inventory_scope IS NULL OR TRIM(inventory_scope) = ''",
            normalizedScope);
    }

    private static string ResolveCollectionNameInternal(string requestedName, SQLiteConnection connection)
    {
        string trimmedRequestedName = string.IsNullOrWhiteSpace(requestedName) ? string.Empty : requestedName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedRequestedName))
            return string.Empty;

        List<string> collectionNames = GetCollectionNames(connection);
        if (collectionNames.Count == 0)
            return trimmedRequestedName;

        for (int i = 0; i < collectionNames.Count; i++)
        {
            if (string.Equals(collectionNames[i], trimmedRequestedName, StringComparison.OrdinalIgnoreCase))
                return collectionNames[i];
        }

        string normalizedRequestedName = NormalizeCollectionKeyLoose(trimmedRequestedName);
        for (int i = 0; i < collectionNames.Count; i++)
        {
            if (string.Equals(NormalizeCollectionKeyLoose(collectionNames[i]), normalizedRequestedName, StringComparison.OrdinalIgnoreCase))
                return collectionNames[i];
        }

        string bestCandidate = string.Empty;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < collectionNames.Count; i++)
        {
            string candidate = collectionNames[i];
            string normalizedCandidate = NormalizeCollectionKeyLoose(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
                continue;

            bool containsMatch =
                normalizedCandidate.Contains(normalizedRequestedName) ||
                normalizedRequestedName.Contains(normalizedCandidate);

            int distance = ComputeLevenshteinDistance(normalizedRequestedName, normalizedCandidate);
            if (!containsMatch && distance > 3)
                continue;

            if (containsMatch)
                distance = Math.Min(distance, 1);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestCandidate = candidate;
            }
        }

        return string.IsNullOrWhiteSpace(bestCandidate) ? trimmedRequestedName : bestCandidate;
    }

    private static List<string> GetCollectionNames(SQLiteConnection connection)
    {
        if (cachedCollectionNames != null && cachedCollectionNames.Count > 0)
            return cachedCollectionNames;

        cachedCollectionNames = connection.Query<ScalarStringRow>(
                "SELECT DISTINCT collection_name AS Value FROM gift_items WHERE collection_name IS NOT NULL AND TRIM(collection_name) <> '' ORDER BY collection_name")
            .ConvertAll(row => row != null ? row.Value : string.Empty);

        return cachedCollectionNames;
    }

    private static string NormalizeCollectionKeyLoose(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        char[] buffer = new char[value.Length];
        int index = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (char.IsLetterOrDigit(current))
                buffer[index++] = char.ToLowerInvariant(current);
        }

        return new string(buffer, 0, index);
    }

    private static int ComputeLevenshteinDistance(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
            return string.IsNullOrEmpty(right) ? 0 : right.Length;

        if (string.IsNullOrEmpty(right))
            return left.Length;

        int[,] distances = new int[left.Length + 1, right.Length + 1];

        for (int i = 0; i <= left.Length; i++)
            distances[i, 0] = i;

        for (int j = 0; j <= right.Length; j++)
            distances[0, j] = j;

        for (int i = 1; i <= left.Length; i++)
        {
            for (int j = 1; j <= right.Length; j++)
            {
                int substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + substitutionCost);
            }
        }

        return distances[left.Length, right.Length];
    }

    private sealed class ScalarStringRow
    {
        public string Value { get; set; }
    }
}
