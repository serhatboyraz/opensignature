using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenSignature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPadesAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AppearancePageNumber",
                table: "SignatureRequests",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "AppearanceImageFileId",
                table: "SignatureRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureNote",
                table: "SignatureRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VisibleSignature",
                table: "SignatureRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppearancePageNumber",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "AppearanceImageFileId",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "SignatureNote",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "VisibleSignature",
                table: "SignatureRequests");
        }
    }
}
