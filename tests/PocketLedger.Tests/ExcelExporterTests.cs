using OfficeOpenXml;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class ExcelExporterTests
{
    [Fact]
    public void Create_ReturnsPasswordProtectedWorkbookWithTypedTransactionData()
    {
        const string password = "strong-passphrase";
        var transactions = new[]
        {
            new Transaction
            {
                TransactionDate = new DateOnly(2026, 9, 15),
                Account = new Account { Name = "Daily account", Currency = "HUF" },
                Type = TransactionType.Expense,
                Category = new Category { Name = "Groceries" },
                Amount = 1234.5m,
                Note = "Weekly shop"
            }
        };

        var content = ExcelExporter.Create(transactions, password);

        Assert.ThrowsAny<Exception>(() => new ExcelPackage(new MemoryStream(content), "wrong-password"));
        using var package = new ExcelPackage(new MemoryStream(content), password);
        var worksheet = package.Workbook.Worksheets["Transactions"];
        Assert.NotNull(worksheet);
        Assert.Equal("Date", worksheet.Cells[1, 1].Text);
        Assert.Equal(new DateTime(2026, 9, 15), worksheet.Cells[2, 1].GetValue<DateTime>());
        Assert.Equal("Daily account", worksheet.Cells[2, 2].Text);
        Assert.Equal("Expense", worksheet.Cells[2, 3].Text);
        Assert.Equal("Groceries", worksheet.Cells[2, 4].Text);
        Assert.Equal(1234.5, worksheet.Cells[2, 5].GetValue<double>());
        Assert.Equal("HUF", worksheet.Cells[2, 6].Text);
        Assert.Equal("Weekly shop", worksheet.Cells[2, 7].Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456789")]
    public void Create_RejectsMissingOrShortPassword(string password)
    {
        var exception = Assert.Throws<BusinessRuleException>(() => ExcelExporter.Create([], password));

        Assert.Contains("password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
