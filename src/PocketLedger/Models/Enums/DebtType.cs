using System.ComponentModel.DataAnnotations;

namespace PocketLedger.Models.Enums;

public enum DebtType
{
    Bank,
    [Display(Name = "Private Person")]
    PrivatePerson
}
