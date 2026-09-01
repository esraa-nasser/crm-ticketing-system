using CrmTicketing.Domain.Tickets;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrmTicketing.Infrastructure.Identity;

/// <summary>
/// Seeds two demo users and a dozen tickets, so the system is demonstrable from an
/// empty database without hand-crafting rows through curl.
/// </summary>
/// <remarks>
/// <para>
/// Off unless <c>Seed:Demo:Enabled</c> is exactly <c>true</c>. Once it is, every
/// subsequent problem is loud: the operator has asked for a demo, so silently
/// producing none - or one missing a third of its roles - would send them debugging
/// the wrong thing.
/// </para>
/// <para>
/// Seeds no Admin. Story 06's bootstrap Admin is the single path by which a
/// privileged account comes into existence; this seeder requires one to exist
/// already. Two mechanisms creating privileged accounts is the duplication that ends
/// with a production Admin nobody remembers configuring.
/// </para>
/// </remarks>
internal static partial class DemoDataSeeder
{
    // Source-generated rather than logger.LogInformation(...): the analyzer rejects
    // eagerly-evaluated arguments on a call that may be disabled (CA1873).
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Demo seeding skipped: the ticket table already holds {Count} row(s). Drop and recreate the database to reseed.")]
    private static partial void LogSkippedNotEmpty(ILogger logger, int count);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Demo seeding created {Users} users, {Tickets} tickets, and {Comments} comments.")]
    private static partial void LogSeeded(ILogger logger, int users, int tickets, int comments);

    private const string DemoSection = "Seed:Demo";
    private const string BootstrapAdminSection = "Identity:BootstrapAdmin";

    /// <summary>The Agent and the Customer. This seeder creates no Admin.</summary>
    private const int DemoUserCount = 2;

    // Fixed and obviously fake, so a demo URL captured today still resolves after a
    // reseed. Assigned before CreateAsync, which would otherwise generate one.
    private static readonly Guid AgentId = Guid.Parse("dddddddd-0000-0000-0000-00000000a6e7");
    private static readonly Guid CustomerId = Guid.Parse("dddddddd-0000-0000-0000-0000000c0570");

    internal static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configuration = services.GetRequiredService<IConfiguration>();

        // Guard 1: nobody asked for a demo. Not a warning - this is the default state.
        if (!configuration.GetValue<bool>($"{DemoSection}:Enabled"))
        {
            return;
        }

        var agentEmail = configuration[$"{DemoSection}:AgentEmail"];
        var customerEmail = configuration[$"{DemoSection}:CustomerEmail"];
        var password = configuration[$"{DemoSection}:Password"];

        // Guard 2: the flag expressed intent, so missing keys are an error rather than
        // a silent no-op. This differs from the bootstrap Admin, where absent
        // configuration is the only signal available.
        var missing = new List<string>(3);

        if (string.IsNullOrWhiteSpace(agentEmail))
        {
            missing.Add($"{DemoSection}:AgentEmail");
        }

        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            missing.Add($"{DemoSection}:CustomerEmail");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            missing.Add($"{DemoSection}:Password");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"'{DemoSection}:Enabled' is true but required configuration is missing: "
                + string.Join(", ", missing)
                + ". Set it, or set the flag to false.");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        // Guard 3: before the emptiness check. A database holding tickets but no Admin
        // is still misconfigured, and "skipped, not empty" would hide that. The message
        // names story 06's keys because that is where the mistake lives.
        var admins = await userManager.GetUsersInRoleAsync(RoleNames.Admin).ConfigureAwait(false);

        if (admins.Count == 0)
        {
            throw new InvalidOperationException(
                $"Demo seeding requires an existing {RoleNames.Admin}, and none was found. "
                + $"Configure '{BootstrapAdminSection}:Email' and '{BootstrapAdminSection}:Password' "
                + "so story 06's bootstrap Admin is created first.");
        }

        var repository = services.GetRequiredService<ITicketRepository>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DemoDataSeeder));

        // TicketAccess.All() deliberately bypasses the row-level rule: the seeder is not
        // a user, and it needs to know whether anyone's data is here, not whether some
        // caller may see it.
        var existing = await repository
            .CountAsync(TicketQuery.Create(TicketAccess.All()), cancellationToken)
            .ConfigureAwait(false);

        // Guard 4: refuse rather than merge. Adding demo rows to a database someone is
        // using cannot be undone by re-running anything.
        if (existing > 0)
        {
            LogSkippedNotEmpty(logger, existing);

            return;
        }

        var agent = await EnsureUserAsync(userManager, AgentId, agentEmail!, password!, RoleNames.Agent)
            .ConfigureAwait(false);
        var customer = await EnsureUserAsync(userManager, CustomerId, customerEmail!, password!, RoleNames.Customer)
            .ConfigureAwait(false);

        await SeedTicketsAsync(services, repository, agent.Id, customer.Id, cancellationToken)
            .ConfigureAwait(false);

        LogSeeded(logger, DemoUserCount, Specifications.Count, CommentSpecifications.Count);
    }

    /// <summary>
    /// The demo set, declared rather than constructed. Internal and pure so its shape
    /// can be asserted without a database.
    /// </summary>
    internal static IReadOnlyList<DemoTicketSpecification> Specifications { get; } =
    [
        new("Printer offline in Meeting Room 3", "The printer shows a red light and will not wake.", "Hardware", TicketPriority.Normal, DemoRequester.Customer, TicketStatus.New, false, 0),
        new("Cannot open the monthly export", "The download finishes but the file will not open.", null, TicketPriority.High, DemoRequester.Customer, TicketStatus.New, false, 1),
        new("Laptop will not charge", "The charger light is on but the battery stays at zero.", "Hardware", TicketPriority.Urgent, DemoRequester.Customer, TicketStatus.Open, true, 2),
        new("Duplicate line on last invoice", "The same line appears twice on the December invoice.", "Billing", TicketPriority.Normal, DemoRequester.Customer, TicketStatus.Open, true, 3),
        new("Request access to the reports folder", "I need read access to the shared reports folder.", "Access", TicketPriority.Low, DemoRequester.Customer, TicketStatus.Open, false, 4),
        new("Billing address is out of date", "Waiting on the finance team to confirm the new address.", "Billing", TicketPriority.Normal, DemoRequester.Customer, TicketStatus.Pending, true, 5),
        new("Second monitor not detected", "Waiting for the customer to confirm the cable type.", null, TicketPriority.High, DemoRequester.Customer, TicketStatus.Pending, false, 6),
        new("Password reset for the shared mailbox", "Reset and confirmed working with the customer.", "Access", TicketPriority.Normal, DemoRequester.Customer, TicketStatus.Resolved, true, 8),
        new("Spare keyboard request", "Withdrawn - a spare was found on site.", "Hardware", TicketPriority.Low, DemoRequester.Customer, TicketStatus.Closed, false, 10),
        new("Ticket list slow to load in the morning", "Raised by support after several reports.", "Internal", TicketPriority.High, DemoRequester.Agent, TicketStatus.Open, true, 7),
        new("Stale entries in the status filter", "Fixed by clearing the cached metadata response.", "Internal", TicketPriority.Normal, DemoRequester.Agent, TicketStatus.Resolved, false, 9),
        new("Review the demo dataset", "Completed - the seeded set now covers every status.", "Internal", TicketPriority.Urgent, DemoRequester.Agent, TicketStatus.Closed, false, 12),
    ];

    /// <summary>
    /// The demo thread, declared rather than constructed. Internal and pure so its
    /// shape can be asserted without a database.
    /// </summary>
    /// <remarks>
    /// Deliberately uneven. Some tickets carry a conversation, some carry one note, and
    /// several carry nothing - a demo where every ticket has the same thread shows the
    /// feature without showing what it looks like in use, and never reaches the empty
    /// state. No comment targets a closed ticket: the API refuses one, so seeding it
    /// would put the database in a state the running system cannot produce.
    /// </remarks>
    internal static IReadOnlyList<DemoCommentSpecification> CommentSpecifications { get; } =
    [
        // A customer conversation with an internal note the customer never sees. This
        // one ticket is what makes the visibility rule visible in a demo: sign in as
        // each user and the thread differs.
        new(2, DemoRequester.Customer, "I tried a different socket and it still will not charge.", false, 2),
        new(2, DemoRequester.Agent, "Thanks - I have ordered a replacement charger for you.", false, 5),
        new(2, DemoRequester.Agent, "Charger stock is low; using the last spare from the cupboard.", true, 6),

        // The core loop, with nothing hidden: requester writes, agent replies.
        new(3, DemoRequester.Customer, "The duplicate line is the second one, dated the 14th.", false, 3),
        new(3, DemoRequester.Agent, "Confirmed with finance - a credit note is on its way.", false, 8),

        // A second internal comment, on a Pending ticket, so the rule is not carried by
        // a single row.
        new(5, DemoRequester.Agent, "We are waiting on finance to confirm the new address.", false, 4),
        new(5, DemoRequester.Agent, "Finance have not replied in two days; chasing again on Monday.", true, 30),

        new(6, DemoRequester.Agent, "Could you confirm whether the cable is HDMI or DisplayPort?", false, 6),

        new(7, DemoRequester.Customer, "The new password works, thank you.", false, 20),
        new(7, DemoRequester.Agent, "Glad to hear it - marking this resolved.", false, 22),

        // On an agent-raised ticket, so an internal comment exists that no customer can
        // reach by any route.
        new(9, DemoRequester.Agent, "Reproduced on the staging box at 09:00 with a warm cache.", true, 9),
    ];

    /// <summary>
    /// The legal path from <see cref="TicketStatus.New"/> to a target. Pure, so a test
    /// can assert every step against the transition table without starting anything.
    /// </summary>
    /// <remarks>
    /// Closed is reached through Open rather than directly. New to Closed is legal but
    /// reads as a withdrawn ticket rather than a completed one.
    /// </remarks>
    internal static IReadOnlyList<TicketStatus> PathTo(TicketStatus target) => target switch
    {
        TicketStatus.New => [],
        TicketStatus.Open => [TicketStatus.Open],
        TicketStatus.Pending => [TicketStatus.Open, TicketStatus.Pending],
        TicketStatus.Resolved => [TicketStatus.Open, TicketStatus.Resolved],
        TicketStatus.Closed => [TicketStatus.Open, TicketStatus.Closed],
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        Guid id,
        string email,
        string password,
        string role)
    {
        // Idempotent by existence check, like the bootstrap Admin. Never resets the
        // password of an account that already exists.
        if (await userManager.FindByEmailAsync(email).ConfigureAwait(false) is { } existing)
        {
            return existing;
        }

        var user = new ApplicationUser { Id = id, UserName = email, Email = email };
        var created = await userManager.CreateAsync(user, password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the demo {role} from '{DemoSection}': "
                + IdentityErrors.Describe(created));
        }

        var assigned = await userManager.AddToRoleAsync(user, role).ConfigureAwait(false);

        if (!assigned.Succeeded)
        {
            // An account with no role can authenticate and then be refused everywhere.
            await userManager.DeleteAsync(user).ConfigureAwait(false);

            throw new InvalidOperationException(
                $"Could not place the demo user in the '{role}' role: "
                + IdentityErrors.Describe(assigned));
        }

        return user;
    }

    private static async Task SeedTicketsAsync(
        IServiceProvider services,
        ITicketRepository repository,
        Guid agentId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        // Never a literal date: a hardcoded instant ages badly and drifts out of any
        // window the list view or a future SLA cares about.
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        // Kept in specification order so DemoCommentSpecification.TicketIndex resolves
        // to the ticket it names.
        var seeded = new List<Ticket>(Specifications.Count);

        foreach (var spec in Specifications)
        {
            var requesterId = spec.Requester == DemoRequester.Agent ? agentId : customerId;
            var createdAt = now.AddDays(-spec.AgeInDays);

            // Built through the aggregate, never an object initialiser or INSERT, so a
            // seeded row cannot exist in a state Ticket forbids.
            var ticket = Ticket.Open(
                Guid.CreateVersion7(),
                TicketTitle.Create(spec.Title),
                spec.Description,
                requesterId,
                createdAt,
                requesterId,
                spec.Priority,
                spec.Category);

            // Each move gets its own later instant, so UpdatedAt differs from CreatedAt
            // and the list has something to order by.
            var path = PathTo(spec.TargetStatus);
            var step = 1;

            foreach (var status in path)
            {
                // Staff move the ticket through the workflow; the requester raised it.
                ticket.TransitionTo(status, createdAt.AddHours(step * 3), agentId);
                step++;
            }

            if (spec.AssignToAgent)
            {
                // After the transitions: Ticket.Assign throws on a closed ticket, and
                // assigning before would leave an assignee the demo set does not claim.
                ticket.Assign(agentId, createdAt.AddHours(step * 3), agentId);
            }

            await repository.AddAsync(ticket, cancellationToken).ConfigureAwait(false);
            seeded.Add(ticket);
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // After the tickets are committed: ticket_comment.ticket_id carries a foreign
        // key, so a comment inserted in the same batch could reach the database first.
        await SeedCommentsAsync(services, seeded, agentId, customerId, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task SeedCommentsAsync(
        IServiceProvider services,
        List<Ticket> seeded,
        Guid agentId,
        Guid customerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var repository = services.GetRequiredService<ITicketCommentRepository>();

        foreach (var spec in CommentSpecifications)
        {
            var ticket = seeded[spec.TicketIndex];

            // Asked, not assumed. The seeder holds the ticket, so it can put the same
            // question to the domain that the API does - rather than relying on the
            // specification list being right about which tickets are closed. A demo set
            // that quietly seeds a comment the running system would refuse is a
            // database in a state the application cannot produce, and this throws
            // instead.
            ticket.EnsureCanBeCommentedOn();

            var createdAt = now.AddDays(-Specifications[spec.TicketIndex].AgeInDays);
            var authorId = spec.Author == DemoRequester.Agent ? agentId : customerId;

            // Built through the aggregate, never an object initialiser or INSERT, so a
            // seeded comment cannot exist in a state TicketComment forbids.
            var comment = TicketComment.Write(
                Guid.CreateVersion7(),
                ticket.Id,
                authorId,
                spec.Body,
                spec.IsInternal,
                createdAt.AddHours(spec.HoursAfterTicket));

            await repository.AddAsync(comment, cancellationToken).ConfigureAwait(false);
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
