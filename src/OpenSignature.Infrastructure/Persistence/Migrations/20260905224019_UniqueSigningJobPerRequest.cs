using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenSignature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueSigningJobPerRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SigningJobs_SignatureRequestId",
                table: "SigningJobs");

            migrationBuilder.CreateIndex(
                name: "IX_SigningJobs_SignatureRequestId",
                table: "SigningJobs",
                column: "SignatureRequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SigningJobs_SignatureRequestId",
                table: "SigningJobs");

            migrationBuilder.CreateIndex(
                name: "IX_SigningJobs_SignatureRequestId",
                table: "SigningJobs",
                column: "SignatureRequestId");
        }
    }
}
