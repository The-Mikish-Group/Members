using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Members.Data.Migrations
{
    /// <inheritdoc />
    public partial class HOADues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plot",
                table: "UserProfile",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HOADues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DueAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LastPaidDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PlotId = table.Column<int>(type: "int", nullable: true),
                    Plot = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HOADues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HOADues_UserProfile_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfile",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Plots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HOADues_UserId",
                table: "HOADues",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HOADues");

            migrationBuilder.DropTable(
                name: "Plots");

            migrationBuilder.DropColumn(
                name: "Plot",
                table: "UserProfile");
        }
    }
}
