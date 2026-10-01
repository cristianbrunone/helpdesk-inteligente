using System;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HelpDesk.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Triagem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "triagens_ia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chamado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<StatusTriagem>(type: "status_triagem", nullable: false),
                    categoria_sugerida_id = table.Column<short>(type: "smallint", nullable: true),
                    prioridade_sugerida = table.Column<Prioridade>(type: "prioridade_chamado", nullable: true),
                    resumo = table.Column<string>(type: "text", nullable: true),
                    resposta_sugerida = table.Column<string>(type: "text", nullable: true),
                    confianca = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: true),
                    provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    modelo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    prompt_versao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    erro_motivo = table.Column<string>(type: "text", nullable: true),
                    tentativas = table.Column<short>(type: "smallint", nullable: false),
                    proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lock_expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trace_parent = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concluida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decidida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decidida_por = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    motivo_rejeicao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_triagens_ia", x => x.id);
                    table.CheckConstraint("ck_triagens_ia_confianca_faixa", "confianca IS NULL OR confianca BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_triagens_ia_decisao_coerente", "(status IN ('aceita', 'rejeitada')) = (decidida_em IS NOT NULL)");
                    table.CheckConstraint("ck_triagens_ia_execucao_registrada", "status = 'pendente' OR (provedor IS NOT NULL AND modelo IS NOT NULL AND prompt_versao IS NOT NULL)");
                    table.CheckConstraint("ck_triagens_ia_falha_com_motivo", "status <> 'falhou' OR erro_motivo IS NOT NULL");
                    table.CheckConstraint("ck_triagens_ia_resumo_tamanho", "resumo IS NULL OR char_length(resumo) <= 200");
                    table.CheckConstraint("ck_triagens_ia_sugestao_completa", "status NOT IN ('concluida', 'aceita', 'rejeitada') OR (categoria_sugerida_id IS NOT NULL AND prioridade_sugerida IS NOT NULL AND resumo IS NOT NULL AND resposta_sugerida IS NOT NULL AND confianca IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_triagens_ia_categorias_categoria_sugerida_id",
                        column: x => x.categoria_sugerida_id,
                        principalTable: "categorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_triagens_ia_chamados_chamado_id",
                        column: x => x.chamado_id,
                        principalTable: "chamados",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "uso_llm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    operacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    triagem_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chamado_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    modelo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tokens_entrada = table.Column<int>(type: "integer", nullable: true),
                    tokens_saida = table.Column<int>(type: "integer", nullable: true),
                    latencia_ms = table.Column<int>(type: "integer", nullable: false),
                    sucesso = table.Column<bool>(type: "boolean", nullable: false),
                    erro_tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uso_llm", x => x.id);
                    table.CheckConstraint("ck_uso_llm_erro_coerente", "sucesso = (erro_tipo IS NULL)");
                    table.CheckConstraint("ck_uso_llm_operacao", "operacao IN ('triagem', 'copiloto', 'embedding')");
                    table.CheckConstraint("ck_uso_llm_valores_positivos", "latencia_ms >= 0 AND (tokens_entrada IS NULL OR tokens_entrada >= 0) AND (tokens_saida IS NULL OR tokens_saida >= 0)");
                    table.ForeignKey(
                        name: "fk_uso_llm_triagens_ia_triagem_id",
                        column: x => x.triagem_id,
                        principalTable: "triagens_ia",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_triagens_ia_categoria_sugerida_id",
                table: "triagens_ia",
                column: "categoria_sugerida_id");

            migrationBuilder.CreateIndex(
                name: "ix_triagens_ia_chamado_id_criado_em",
                table: "triagens_ia",
                columns: new[] { "chamado_id", "criado_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_triagens_ia_fila",
                table: "triagens_ia",
                column: "proxima_tentativa_em",
                filter: "status = 'pendente'");

            migrationBuilder.CreateIndex(
                name: "ux_triagens_ia_uma_pendente_por_chamado",
                table: "triagens_ia",
                column: "chamado_id",
                unique: true,
                filter: "status = 'pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_uso_llm_criado_em",
                table: "uso_llm",
                column: "criado_em");

            migrationBuilder.CreateIndex(
                name: "ix_uso_llm_triagem_id",
                table: "uso_llm",
                column: "triagem_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "uso_llm");

            migrationBuilder.DropTable(
                name: "triagens_ia");
        }
    }
}
