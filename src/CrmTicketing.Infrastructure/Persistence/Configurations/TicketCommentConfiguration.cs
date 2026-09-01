using CrmTicketing.Domain.Tickets;
using CrmTicketing.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmTicketing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="TicketComment"/> to the ticket_comment table. Names are written in
/// PascalCase here; ApplySnakeCaseNames rewrites them centrally.
/// </summary>
internal sealed class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TicketComment");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TicketId).IsRequired();
        builder.Property(c => c.AuthorId).IsRequired();

        builder.Property(c => c.Body)
            .HasMaxLength(TicketComment.MaxBodyLength)
            .IsRequired();

        // Not nullable, and present from the first migration. Adding a visibility flag
        // to a table that already holds comments means deciding what every historical
        // comment was, and there is no honest answer.
        builder.Property(c => c.IsInternal).IsRequired();

        builder.Property(c => c.CreatedAt).IsRequired();

        // Declared without navigation properties in either direction: TicketComment
        // never learns that ApplicationUser exists, and Ticket never learns that
        // TicketComment does.
        //
        // Cascade on the ticket, Restrict on the author, and the asymmetry is
        // deliberate. A comment has no meaning without its ticket, so were a ticket
        // ever deleted its thread should go with it. A user is not owned by their
        // comments, and deleting an account must not silently erase what they wrote.
        // Nothing deletes a ticket today; this states the intent for whoever adds it.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(c => c.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Supports the only query there is: newest-first by ticket.
        builder.HasIndex(c => new { c.TicketId, c.CreatedAt })
            .IsDescending(false, true);
    }
}
