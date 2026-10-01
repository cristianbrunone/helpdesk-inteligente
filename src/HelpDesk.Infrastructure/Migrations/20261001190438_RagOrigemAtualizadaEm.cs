using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelpDesk.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RagOrigemAtualizadaEm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "origem_atualizada_em",
                table: "documentos_rag",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "'-infinity'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "origem_atualizada_em",
                table: "documentos_rag");
        }
    }
}
