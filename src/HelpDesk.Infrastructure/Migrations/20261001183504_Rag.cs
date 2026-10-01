using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace HelpDesk.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Rag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "artigos_conhecimento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    conteudo = table.Column<string>(type: "text", nullable: false),
                    categoria_id = table.Column<short>(type: "smallint", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artigos_conhecimento", x => x.id);
                    table.CheckConstraint("ck_artigos_conhecimento_conteudo_preenchido", "char_length(btrim(conteudo)) > 0");
                    table.CheckConstraint("ck_artigos_conhecimento_titulo_preenchido", "char_length(btrim(titulo)) > 0");
                    table.ForeignKey(
                        name: "fk_artigos_conhecimento_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalTable: "categorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documentos_rag",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chamado_id = table.Column<Guid>(type: "uuid", nullable: true),
                    artigo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chunk_indice = table.Column<short>(type: "smallint", nullable: false),
                    conteudo_mascarado = table.Column<string>(type: "text", nullable: false),
                    hash_conteudo = table.Column<string>(type: "char(64)", nullable: false),
                    categoria_id = table.Column<short>(type: "smallint", nullable: true),
                    embedding = table.Column<Vector>(type: "vector(768)", nullable: true),
                    embedding_modelo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    indexado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos_rag", x => x.id);
                    table.CheckConstraint("ck_documentos_rag_chunk_do_chamado", "chamado_id IS NULL OR chunk_indice = 0");
                    table.CheckConstraint("ck_documentos_rag_chunk_indice", "chunk_indice >= 0");
                    table.CheckConstraint("ck_documentos_rag_indexacao_coerente", "(embedding IS NULL) = (embedding_modelo IS NULL) AND (embedding IS NULL) = (indexado_em IS NULL)");
                    table.CheckConstraint("ck_documentos_rag_uma_origem", "num_nonnulls(chamado_id, artigo_id) = 1");
                    table.ForeignKey(
                        name: "fk_documentos_rag_artigos_conhecimento_artigo_id",
                        column: x => x.artigo_id,
                        principalTable: "artigos_conhecimento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_documentos_rag_chamados_chamado_id",
                        column: x => x.chamado_id,
                        principalTable: "chamados",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_artigos_conhecimento_categoria_id",
                table: "artigos_conhecimento",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_rag_artigo_id",
                table: "documentos_rag",
                column: "artigo_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_rag_embedding_hnsw",
                table: "documentos_rag",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_rag_fila",
                table: "documentos_rag",
                column: "indexado_em",
                filter: "embedding IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_documentos_rag_origem_chunk",
                table: "documentos_rag",
                columns: new[] { "chamado_id", "artigo_id", "chunk_indice" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documentos_rag");

            migrationBuilder.DropTable(
                name: "artigos_conhecimento");
        }
    }
}
