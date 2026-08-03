using PocketLedger.Services;

namespace PocketLedger.Models.ViewModels.ImportExport;

public class CsvImportViewModel
{
    public string Csv { get; set; } = string.Empty;
    public CsvImportPreview? Preview { get; set; }
}

public class RestoreViewModel
{
    public string Json { get; set; } = string.Empty;
    public RestorePreview? Preview { get; set; }
    public bool Confirm { get; set; }
}
