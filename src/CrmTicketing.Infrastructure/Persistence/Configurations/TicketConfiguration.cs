using CrmTicketing.Domain.Tickets;
using CrmTicketing.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmTicketing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Ticket"/> to the ticket table. Names are written in PascalCase
/// here; ApplySnakeCaseNames rewrites them centrally.
/// </summary>
internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Ticket");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .HasConversion(v => v.Value, v => TicketTitle.Create(v))
            .HasMaxLength(TicketTitle.MaxLength)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasMaxLength(Ticket.MaxDescriptionLength)
            .IsRequired();

        builder.Property(t => t.Category)
            .HasMaxLength(Ticket.MaxCategoryLength);

        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.Priority)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // CreatedAt and RequesterId are get-only, so convention does not discover
        // them; they are mapped explicitly. Both keep the provider's default column
        // type - timestamp with time zone for DateTimeOffset, uuid for Guid.
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.RequesterId).IsRequired();
        builder.Property(t => t.CreatedBy).IsRequired();
        builder.Property(t => t.UpdatedBy).IsRequired();

        // Declared without a navigation property: Ticket never learns that
        // ApplicationUser exists. No matching key on assignee_id in this story —
        // that would break the assign endpoint the same way, and assignment is
        // staff-only with its own validation story. See docs/architecture.md.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.RequesterId);
        builder.HasIndex(t => t.Status);
    }
}
