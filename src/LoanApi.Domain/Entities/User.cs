using System;
using System.Collections.Generic;

namespace LoanApi.Domain.Entities;

public partial class User
{
    public int Id { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string Username { get; set; } = null!;

    public string Email { get; set; } = null!;

    public byte Age { get; set; }

    public decimal MonthlyIncome { get; set; }

    public bool IsBlocked { get; set; }

    public DateTime? BlockedUntil { get; set; }

    public string PasswordHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<LoanHistory> LoanHistories { get; set; } = new List<LoanHistory>();

    public virtual ICollection<Loan> Loans { get; set; } = new List<Loan>();

    public virtual ICollection<UserBlockHistory> UserBlockHistories { get; set; } = new List<UserBlockHistory>();
}
