using System;
using HelpDesk.Domain.Chamados;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HelpDesk.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Chamados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chamados",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: false),
                    solicitante_nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    solicitante_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    categoria_id = table.Column<short>(type: "smallint", nullable: true),
                    prioridade = table.Column<Prioridade>(type: "prioridade_chamado", nullable: false),
                    status = table.Column<StatusChamado>(type: "status_chamado", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolvido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chamados", x => x.id);
                    table.CheckConstraint("ck_chamados_critica_nao_cancelada", "NOT (prioridade = 'critica' AND status = 'cancelado')");
                    table.CheckConstraint("ck_chamados_descricao_tamanho", "char_length(descricao) BETWEEN 10 AND 5000");
                    table.CheckConstraint("ck_chamados_resolvido_em_apos_criacao", "resolvido_em IS NULL OR resolvido_em >= criado_em");
                    table.CheckConstraint("ck_chamados_resolvido_em_coerente", "(resolvido_em IS NOT NULL) = (status IN ('resolvido', 'fechado'))");
                    table.CheckConstraint("ck_chamados_solicitante_email_formato", "solicitante_email ~* '^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$'");
                    table.CheckConstraint("ck_chamados_titulo_tamanho", "char_length(titulo) BETWEEN 5 AND 150");
                    table.ForeignKey(
                        name: "fk_chamados_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalTable: "categorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "comentarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chamado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    texto = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comentarios", x => x.id);
                    table.CheckConstraint("ck_comentarios_texto_tamanho", "char_length(texto) BETWEEN 1 AND 4000");
                    table.ForeignKey(
                        name: "fk_comentarios_chamados_chamado_id",
                        column: x => x.chamado_id,
                        principalTable: "chamados",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "historico_status",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    chamado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status_anterior = table.Column<StatusChamado>(type: "status_chamado", nullable: true),
                    status_novo = table.Column<StatusChamado>(type: "status_chamado", nullable: false),
                    alterado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    alterado_por = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historico_status", x => x.id);
                    table.CheckConstraint("ck_historico_status_muda_status", "status_anterior IS DISTINCT FROM status_novo");
                    table.ForeignKey(
                        name: "fk_historico_status_chamados_chamado_id",
                        column: x => x.chamado_id,
                        principalTable: "chamados",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chamados_categoria_id_criado_em",
                table: "chamados",
                columns: new[] { "categoria_id", "criado_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_chamados_criado_em_id",
                table: "chamados",
                columns: new[] { "criado_em", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_chamados_prioridade_criado_em",
                table: "chamados",
                columns: new[] { "prioridade", "criado_em" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_chamados_status_criado_em",
                table: "chamados",
                columns: new[] { "status", "criado_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_chamados_numero",
                table: "chamados",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comentarios_chamado_id_criado_em",
                table: "comentarios",
                columns: new[] { "chamado_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_historico_status_chamado_id_alterado_em",
                table: "historico_status",
                columns: new[] { "chamado_id", "alterado_em" });

            // ADR-0008: unaccent() não é IMMUTABLE; o wrapper permite usá-la num índice de expressão.
            migrationBuilder.Sql("""
                CREATE FUNCTION f_unaccent(text) RETURNS text
                  LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                  AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, $1) $$;
                """);

            // Índice 5 do modelo §5: busca por substring (q), insensível a caixa e acento.
            migrationBuilder.Sql("""
                CREATE INDEX ix_chamados_busca_trgm ON chamados
                  USING gin (f_unaccent(lower(titulo || ' ' || descricao)) gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX ix_chamados_busca_trgm;");
            migrationBuilder.Sql("DROP FUNCTION f_unaccent(text);");

            migrationBuilder.DropTable(
                name: "comentarios");

            migrationBuilder.DropTable(
                name: "historico_status");

            migrationBuilder.DropTable(
                name: "chamados");
        }
    }
}
