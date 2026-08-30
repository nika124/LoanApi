using System;
using System.Collections.Generic;

namespace LoanApi.Domain.Entities;

public partial class LoanHistory
{
    public long Id { get; set; }

    public int LoanId { get; set; }

    public int? ChangedByUserId { get; set; }

    public int? ChangedByAccountantId { get; set; }

    public string Action { get; set; } = null!;

    public string? FieldName { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime ChangedAt { get; set; }

    public virtual Accountant? ChangedByAccountant { get; set; }

    public virtual User? ChangedByUser { get; set; }

    public virtual Loan Loan { get; set; } = null!;
}
