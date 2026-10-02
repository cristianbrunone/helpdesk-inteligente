using System;
using HelpDesk.Domain.Usuarios;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelpDesk.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Usuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:perfil_usuario", "atendente,solicitante")
                .Annotation("Npgsql:Enum:prioridade_chamado", "baixa,media,alta,critica")
                .Annotation("Npgsql:Enum:status_chamado", "aberto,em_andamento,resolvido,fechado,cancelado")
                .Annotation("Npgsql:Enum:status_triagem", "pendente,concluida,falhou,aceita,rejeitada")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:Enum:prioridade_chamado", "baixa,media,alta,critica")
                .OldAnnotation("Npgsql:Enum:status_chamado", "aberto,em_andamento,resolvido,fechado,cancelado")
                .OldAnnotation("Npgsql:Enum:status_triagem", "pendente,concluida,falhou,aceita,rejeitada")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    perfil = table.Column<PerfilUsuario>(type: "perfil_usuario", nullable: false),
                    senha_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                    table.CheckConstraint("ck_usuarios_email_formato", "email ~* '^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$'");
                    table.CheckConstraint("ck_usuarios_email_minusculo", "email = lower(email)");
                    table.CheckConstraint("ck_usuarios_nome_preenchido", "char_length(btrim(nome)) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usuarios");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:prioridade_chamado", "baixa,media,alta,critica")
                .Annotation("Npgsql:Enum:status_chamado", "aberto,em_andamento,resolvido,fechado,cancelado")
                .Annotation("Npgsql:Enum:status_triagem", "pendente,concluida,falhou,aceita,rejeitada")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:Enum:perfil_usuario", "atendente,solicitante")
                .OldAnnotation("Npgsql:Enum:prioridade_chamado", "baixa,media,alta,critica")
                .OldAnnotation("Npgsql:Enum:status_chamado", "aberto,em_andamento,resolvido,fechado,cancelado")
                .OldAnnotation("Npgsql:Enum:status_triagem", "pendente,concluida,falhou,aceita,rejeitada")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
