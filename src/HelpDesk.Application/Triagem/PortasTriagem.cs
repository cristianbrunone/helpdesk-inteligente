using HelpDesk.Domain.Triagem;

namespace HelpDesk.Application.Triagem;

/// <summary>
/// Porta de escrita das triagens. Compartilha a unidade de trabalho com <see cref="Chamados.IRepositorioChamados"/>:
/// o <c>SalvarAsync</c> de lá grava chamado e triagem na mesma transação (RF-02).
/// </summary>
public interface IRepositorioTriagens
{
    void Adicionar(TriagemIA triagem);
}
