using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmTicketing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ticket_comment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_comment", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_comment_asp_net_users_author_id",
                        column: x => x.author_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_comment_ticket_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "ticket",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_comment_author_id",
                table: "ticket_comment",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_comment_ticket_id_created_at",
                table: "ticket_comment",
                columns: new[] { "ticket_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_comment");
        }
    }
}
