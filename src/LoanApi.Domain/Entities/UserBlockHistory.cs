using System;
using System.Collections.Generic;

namespace LoanApi.Domain.Entities;

public partial class UserBlockHistory
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public int AccountantId { get; set; }

    public DateTime BlockedFrom { get; set; }

    public DateTime BlockedUntil { get; set; }

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Accountant Accountant { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
