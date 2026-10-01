using HelpDesk.Domain.Chamados;

namespace HelpDesk.Application.Chamados;

/// <summary>Porta de escrita do agregado <see cref="Chamado"/> (específica, sem repositório genérico: ADR-0002).</summary>
public interface IRepositorioChamados
{
    Task<bool> CategoriaExisteAsync(short categoriaId, CancellationToken cancellationToken);

    void Adicionar(Chamado chamado);

    /// <summary>Grava o agregado inteiro (chamado, histórico e comentários) numa única transação (RN-02).</summary>
    Task SalvarAsync(CancellationToken cancellationToken);
}

/// <summary>Porta de leitura: detalhe (com a versão para o <c>ETag</c>) e listagem projetada.</summary>
public interface IConsultaChamados
{
    Task<ChamadoVersionado?> ObterDetalheAsync(Guid id, CancellationToken cancellationToken);

    Task<ResultadoPaginado<ChamadoResumo>> ListarAsync(FiltroChamados filtro, CancellationToken cancellationToken);
}
