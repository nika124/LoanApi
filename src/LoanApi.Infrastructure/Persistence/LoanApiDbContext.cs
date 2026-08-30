using System;
using System.Collections.Generic;
using LoanApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LoanApi.Infrastructure.Persistence;

public partial class LoanApiDbContext : DbContext
{
    public LoanApiDbContext(DbContextOptions<LoanApiDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Accountant> Accountants { get; set; }

    public virtual DbSet<Loan> Loans { get; set; }

    public virtual DbSet<LoanHistory> LoanHistories { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserBlockHistory> UserBlockHistories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Accountant>(entity =>
        {
            entity.HasIndex(e => e.Email, "UQ_Accountants_Email").IsUnique();

            entity.HasIndex(e => e.Username, "UQ_Accountants_Username").IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Accountants_CreatedAt");
            entity.Property(e => e.Email).HasMaxLength(254);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Accountants_IsActive");
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.Username).HasMaxLength(50);
        });

        modelBuilder.Entity<Loan>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_Loans_UserId");

            entity.HasIndex(e => new { e.UserId, e.IsDeleted }, "IX_Loans_UserId_IsDeleted");

            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Loans_CreatedAt");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.LoanType)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Pending", "DF_Loans_Status");

            entity.HasOne(d => d.User).WithMany(p => p.Loans)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Loans_Users");
        });

        modelBuilder.Entity<LoanHistory>(entity =>
        {
            entity.ToTable("LoanHistory");

            entity.HasIndex(e => e.ChangedByAccountantId, "IX_LoanHistory_ChangedByAccountantId");

            entity.HasIndex(e => e.ChangedByUserId, "IX_LoanHistory_ChangedByUserId");

            entity.HasIndex(e => e.LoanId, "IX_LoanHistory_LoanId");

            entity.Property(e => e.Action)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_LoanHistory_ChangedAt");
            entity.Property(e => e.FieldName).HasMaxLength(100);
            entity.Property(e => e.NewValue).HasMaxLength(1000);
            entity.Property(e => e.OldValue).HasMaxLength(1000);

            entity.HasOne(d => d.ChangedByAccountant).WithMany(p => p.LoanHistories)
                .HasForeignKey(d => d.ChangedByAccountantId)
                .HasConstraintName("FK_LoanHistory_Accountants");

            entity.HasOne(d => d.ChangedByUser).WithMany(p => p.LoanHistories)
                .HasForeignKey(d => d.ChangedByUserId)
                .HasConstraintName("FK_LoanHistory_Users");

            entity.HasOne(d => d.Loan).WithMany(p => p.LoanHistories)
                .HasForeignKey(d => d.LoanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LoanHistory_Loans");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.Email, "UQ_Users_Email").IsUnique();

            entity.HasIndex(e => e.Username, "UQ_Users_Username").IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Users_CreatedAt");
            entity.Property(e => e.Email).HasMaxLength(254);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.MonthlyIncome).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.Username).HasMaxLength(50);
        });

        modelBuilder.Entity<UserBlockHistory>(entity =>
        {
            entity.ToTable("UserBlockHistory");

            entity.HasIndex(e => e.AccountantId, "IX_UserBlockHistory_AccountantId");

            entity.HasIndex(e => e.UserId, "IX_UserBlockHistory_UserId");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_UserBlockHistory_CreatedAt");
            entity.Property(e => e.Reason).HasMaxLength(500);

            entity.HasOne(d => d.Accountant).WithMany(p => p.UserBlockHistories)
                .HasForeignKey(d => d.AccountantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserBlockHistory_Accountants");

            entity.HasOne(d => d.User).WithMany(p => p.UserBlockHistories)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserBlockHistory_Users");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
