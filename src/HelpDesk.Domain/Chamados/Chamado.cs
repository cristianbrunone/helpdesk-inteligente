using HelpDesk.Domain.Erros;

namespace HelpDesk.Domain.Chamados;

/// <summary>
/// Agregado do chamado. A máquina de estados (RN-01 a RN-06) vive só aqui: a API e o front recebem
/// <see cref="TransicoesPermitidas"/> e <see cref="PodeComentar"/> prontos e nunca replicam as regras.
/// </summary>
public sealed class Chamado
{
    public const int TituloTamanhoMinimo = 5;
    public const int TituloTamanhoMaximo = 150;
    public const int DescricaoTamanhoMinimo = 10;
    public const int DescricaoTamanhoMaximo = 5000;
    public const int SolicitanteNomeTamanhoMaximo = 120;
    public const int SolicitanteEmailTamanhoMaximo = 254;
    public const string AutorSistema = "sistema";

    // RN-01: as únicas transições existentes. A RN-05 (Crítica não cancela) é aplicada por cima desta tabela.
    private static readonly Dictionary<StatusChamado, StatusChamado[]> _transicoes = new()
    {
        [StatusChamado.Aberto] = [StatusChamado.EmAndamento, StatusChamado.Cancelado],
        [StatusChamado.EmAndamento] = [StatusChamado.Resolvido],
        [StatusChamado.Resolvido] = [StatusChamado.Fechado, StatusChamado.EmAndamento],
        [StatusChamado.Fechado] = [],
        [StatusChamado.Cancelado] = [],
    };

    private readonly List<Comentario> _comentarios = [];
    private readonly List<HistoricoStatus> _historico = [];

    public Guid Id { get; private set; }

    /// <summary>Número amigável (<c>#1042</c>), gerado pelo banco.</summary>
    public long Numero { get; private set; }

    public string Titulo { get; private set; }

    public string Descricao { get; private set; }

    public string SolicitanteNome { get; private set; }

    public string SolicitanteEmail { get; private set; }

    /// <summary>Nula até a triagem ser aceita ou o chamado ser editado (P-02).</summary>
    public short? CategoriaId { get; private set; }

    public Prioridade Prioridade { get; private set; }

    public StatusChamado Status { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <summary>Preenchido ao resolver e limpo ao reabrir (RN-03).</summary>
    public DateTimeOffset? ResolvidoEm { get; private set; }

    public IReadOnlyList<Comentario> Comentarios => _comentarios;

    public IReadOnlyList<HistoricoStatus> Historico => _historico;

    public bool Finalizado => Status is StatusChamado.Fechado or StatusChamado.Cancelado;

    /// <summary>P-06: só os estados finais bloqueiam comentários (Resolvido aceita).</summary>
    public bool PodeComentar => !Finalizado;

    /// <summary>Os destinos válidos agora, já descontada a RN-05. É o que o front usa para montar os botões.</summary>
    public IReadOnlyList<StatusChamado> TransicoesPermitidas =>
        [.. _transicoes[Status].Where(destino => !BloqueadoPorPrioridade(destino))];

    private Chamado(
        Guid id,
        string titulo,
        string descricao,
        string solicitanteNome,
        string solicitanteEmail,
        short? categoriaId,
        Prioridade prioridade,
        StatusChamado status,
        DateTimeOffset criadoEm,
        DateTimeOffset atualizadoEm,
        DateTimeOffset? resolvidoEm)
    {
        Id = id;
        Titulo = titulo;
        Descricao = descricao;
        SolicitanteNome = solicitanteNome;
        SolicitanteEmail = solicitanteEmail;
        CategoriaId = categoriaId;
        Prioridade = prioridade;
        Status = status;
        CriadoEm = criadoEm;
        AtualizadoEm = atualizadoEm;
        ResolvidoEm = resolvidoEm;
    }

    /// <summary>
    /// Abre um chamado (RF-01). Valida todos os campos de uma vez (422 com a lista completa) e registra o
    /// histórico <c>null → Aberto</c> (P-10). Categoria e prioridade são opcionais (P-02).
    /// </summary>
    public static Chamado Abrir(
        string? titulo,
        string? descricao,
        string? solicitanteNome,
        string? solicitanteEmail,
        short? categoriaId,
        Prioridade? prioridade,
        DateTimeOffset agora)
    {
        var erros = new ErrosValidacao();
        var tituloValido = erros.Texto(
            nameof(Titulo), titulo, TituloTamanhoMinimo, TituloTamanhoMaximo, "o título");
        var descricaoValida = erros.Texto(
            nameof(Descricao), descricao, DescricaoTamanhoMinimo, DescricaoTamanhoMaximo, "a descrição");
        var nomeValido = erros.Texto(
            nameof(SolicitanteNome), solicitanteNome, 1, SolicitanteNomeTamanhoMaximo, "o nome do solicitante");
        var emailValido = erros.Email(nameof(SolicitanteEmail), solicitanteEmail, SolicitanteEmailTamanhoMaximo);

        if (prioridade is { } valor && !Enum.IsDefined(valor))
        {
            erros.Adicionar(nameof(Prioridade), "Informe uma prioridade válida.");
        }

        erros.LancarSeHouver();

        var chamado = new Chamado(
            Guid.CreateVersion7(agora),
            tituloValido,
            descricaoValida,
            nomeValido,
            emailValido,
            categoriaId,
            prioridade ?? Prioridade.Media,
            StatusChamado.Aberto,
            agora,
            agora,
            resolvidoEm: null);

        chamado._historico.Add(new HistoricoStatus(chamado.Id, null, StatusChamado.Aberto, agora, AutorSistema));
        return chamado;
    }

    /// <summary>
    /// Muda o status (RF-06). Gera o histórico e, se houver, o comentário (P-09) no mesmo agregado, para a
    /// persistência gravar tudo numa transação (RN-02). Em caso de erro, nada é alterado.
    /// </summary>
    public void MudarStatus(StatusChamado destino, string? alteradoPor, string? comentario, DateTimeOffset agora)
    {
        var erros = new ErrosValidacao();
        var autor = erros.Texto(
            "AlteradoPor", alteradoPor, 1, HistoricoStatus.AlteradoPorTamanhoMaximo, "quem alterou");
        var texto = string.IsNullOrWhiteSpace(comentario)
            ? null
            : erros.Texto("Comentario", comentario, 1, Comentario.TextoTamanhoMaximo, "o comentário");
        erros.LancarSeHouver();

        if (Finalizado)
        {
            throw new ChamadoFinalizadoException(Status);
        }

        if (!_transicoes[Status].Contains(destino))
        {
            throw new TransicaoInvalidaException(Status, destino, TransicoesPermitidas);
        }

        if (BloqueadoPorPrioridade(destino))
        {
            throw new CriticoNaoCancelavelException();
        }

        _historico.Add(new HistoricoStatus(Id, Status, destino, agora, autor));
        Status = destino;
        ResolvidoEm = destino switch
        {
            StatusChamado.Resolvido => agora,
            StatusChamado.EmAndamento => null, // reabertura (RN-03)
            _ => ResolvidoEm,
        };
        AtualizadoEm = agora;

        if (texto is not null)
        {
            _comentarios.Add(new Comentario(Guid.CreateVersion7(agora), Id, autor, texto, agora));
        }
    }

    /// <summary>Adiciona um comentário (RF-07). Bloqueado em Fechado e Cancelado (RN-04).</summary>
    public Comentario Comentar(string? autor, string? texto, DateTimeOffset agora)
    {
        var erros = new ErrosValidacao();
        var autorValido = erros.Texto(
            nameof(Comentario.Autor), autor, 1, Comentario.AutorTamanhoMaximo, "o autor");
        var textoValido = erros.Texto(
            nameof(Comentario.Texto), texto, 1, Comentario.TextoTamanhoMaximo, "o texto");
        erros.LancarSeHouver();

        if (Finalizado)
        {
            throw new ChamadoFinalizadoException(Status);
        }

        var novo = new Comentario(Guid.CreateVersion7(agora), Id, autorValido, textoValido, agora);
        _comentarios.Add(novo);
        AtualizadoEm = agora;
        return novo;
    }

    /// <summary>
    /// Aplica a categoria e a prioridade aceitas da triagem (RF-13). Estados finais não mudam (RN-08); um chamado
    /// que passe a Crítico deixa de oferecer o cancelamento (RN-05), sem regra extra aqui.
    /// </summary>
    public void AplicarSugestao(short categoriaId, Prioridade prioridade, DateTimeOffset agora)
    {
        if (Finalizado)
        {
            throw new ChamadoFinalizadoException(Status);
        }

        if (!Enum.IsDefined(prioridade))
        {
            throw new ArgumentOutOfRangeException(nameof(prioridade));
        }

        CategoriaId = categoriaId;
        Prioridade = prioridade;
        AtualizadoEm = agora;
    }

    private bool BloqueadoPorPrioridade(StatusChamado destino) =>
        destino == StatusChamado.Cancelado && Prioridade == Prioridade.Critica;
}
