using System;
using System.Collections.Generic;

namespace LoanApi.Domain.Entities;

public partial class Loan
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string LoanType { get; set; } = null!;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = null!;

    public short PeriodMonths { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<LoanHistory> LoanHistories { get; set; } = new List<LoanHistory>();

    public virtual User User { get; set; } = null!;
}
