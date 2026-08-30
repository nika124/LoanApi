using System;
using System.Collections.Generic;

namespace LoanApi.Domain.Entities;

public partial class Accountant
{
    public int Id { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string Username { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<LoanHistory> LoanHistories { get; set; } = new List<LoanHistory>();

    public virtual ICollection<UserBlockHistory> UserBlockHistories { get; set; } = new List<UserBlockHistory>();
}
