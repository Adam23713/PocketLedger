using System.ComponentModel.DataAnnotations;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.Transactions;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Models.ViewModels.Planner;

public class PlannerFormViewModel
{
    public Guid Id { get; set; }
    public DateOnly Month { get; set; }
    public DateOnly? PlannedDate { get; set; }
    public TransactionType Type { get; set; } = TransactionType.Expense;
    [Required] public Guid? AccountId { get; set; }
    public Guid? TargetAccountId { get; set; }
    public Guid? CategoryId { get; set; }
    [Range(typeof(decimal), "0.0001", "999999999999999.9999")] public decimal Amount { get; set; }
    [Required, StringLength(3)] public string Currency { get; set; } = "HUF";
    [Range(typeof(decimal), "0.0001", "999999999999999.9999")] public decimal AccountAmount { get; set; }
    public decimal? TargetAmount { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    public IReadOnlyList<AccountOptionViewModel> Accounts { get; set; } = [];
    public IReadOnlyList<CategoryOptionViewModel> Categories { get; set; } = [];
    public PlannerItemInput ToInput() => new(Month, PlannedDate, Type, AccountId ?? Guid.Empty, TargetAccountId, CategoryId, Amount, Currency, AccountAmount, TargetAmount, Note);
}

public record PlannerTableViewModel(string Title, TransactionType Type, DateOnly Month, IReadOnlyList<PlannerEvent> Items, bool ReadOnly = false);
public record PlannerMetricViewModel(string Label, string Icon, string Tone, IReadOnlyList<PlannerMetricAmount> Amounts);
public record PlannerMetricAmount(string Currency, decimal Amount, decimal Previous);
