using System.Text.Json;
using System.Text.Json.Serialization;

namespace PocketLedger.Services;

public static class BackupJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(PocketLedgerBackup backup) => JsonSerializer.Serialize(backup, Options);

    public static PocketLedgerBackup Deserialize(string json)
    {
        return JsonSerializer.Deserialize<PocketLedgerBackup>(json, Options) ?? throw new BusinessRuleException("Backup file is empty or invalid.");
    }
}
