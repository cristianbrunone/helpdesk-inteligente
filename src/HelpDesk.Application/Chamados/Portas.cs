using HelpDesk.Domain.Chamados;

namespace HelpDesk.Application.Chamados;

/// <summary>Porta de escrita do agregado <see cref="Chamado"/> (específica, sem repositório genérico: ADR-0002).</summary>
public interface IRepositorioChamados
{
    Task<bool> CategoriaExisteAsync(short categoriaId, CancellationToken cancellationToken);

    void Adicionar(Chamado chamado);

    /// <summary>Carrega o agregado completo (com histórico e comentários) para alteração.</summary>
    Task<Chamado?> ObterParaAlteracaoAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Versão do chamado no momento em que foi carregado (o mesmo valor do <c>ETag</c>).</summary>
    string Versao(Chamado chamado);

    /// <summary>
    /// Grava o agregado inteiro (chamado, histórico e comentários) numa única transação (RN-02). Se outra gravação
    /// alterou o chamado depois da leitura, lança <see cref="VersaoDesatualizadaException"/>.
    /// </summary>
    Task SalvarAsync(CancellationToken cancellationToken);
}

/// <summary>Porta de leitura: detalhe (com a versão para o <c>ETag</c>) e listagem projetada.</summary>
public interface IConsultaChamados
{
    Task<ChamadoVersionado?> ObterDetalheAsync(Guid id, CancellationToken cancellationToken);

    Task<ResultadoPaginado<ChamadoResumo>> ListarAsync(FiltroChamados filtro, CancellationToken cancellationToken);
}
