using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "quota_mb",
                table: "users",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "quota_mb",
                table: "users");
        }
    }
}
