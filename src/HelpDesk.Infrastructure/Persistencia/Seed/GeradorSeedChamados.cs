using System.Text;
using Bogus;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;

namespace HelpDesk.Infrastructure.Persistencia.Seed;

/// <summary>Chamados do seed e as triagens deles, gravados juntos na mesma transação.</summary>
public sealed record DadosSeed(IReadOnlyList<Chamado> Chamados, IReadOnlyList<TriagemIA> Triagens);

/// <summary>
/// Gera os chamados de demonstração (modelo §7) com semente fixa: o mesmo conteúdo a cada execução, só as datas
/// acompanham o "agora". Tudo passa pelo domínio (<see cref="Chamado.Abrir"/>, <see cref="Chamado.MudarStatus"/>,
/// <see cref="Chamado.Comentar"/>), então histórico, <c>resolvidoEm</c> e comentários saem coerentes por construção.
/// Os dados pessoais são fictícios (<c>example.com</c>, RFC 2606); algumas descrições trazem CPF, telefone ou e-mail
/// falsos de propósito, para demonstrar o mascaramento da Sprint 2.
/// </summary>
public static class GeradorSeedChamados
{
    public const int Semente = 42;
    public const int Quantidade = 200;

    private static readonly TimeSpan _janela = TimeSpan.FromDays(90);

    private static readonly string[] _codigos = ["ERR-401", "ERR-403", "ERR-500", "ERR-502", "ERR-504", "E1043", "E2210"];

    private static readonly string[] _atendentes = ["Ana (suporte)", "Bruno (suporte)", "Carla (infra)", "Diego (financeiro)"];

    private static readonly StatusChamado[] _status = Enum.GetValues<StatusChamado>();
    private static readonly float[] _pesosStatus = [0.20f, 0.20f, 0.20f, 0.25f, 0.15f];

    private static readonly Prioridade[] _prioridades = Enum.GetValues<Prioridade>();
    private static readonly float[] _pesosPrioridade = [0.25f, 0.40f, 0.25f, 0.10f];

    // Triagem do seed (modelo §7): ~70% dos chamados, com status variados para o dashboard e o painel.
    // Nunca "Pendente": o Worker processaria todas na subida, e com um provedor real isso consome a cota.
    private enum DesfechoTriagem { SemTriagem, Aceita, Rejeitada, Concluida, Falhou }

    private static readonly DesfechoTriagem[] _desfechos = Enum.GetValues<DesfechoTriagem>();
    private static readonly float[] _pesosDesfecho = [0.30f, 0.385f, 0.14f, 0.105f, 0.07f];

    // Processamento registrado como o do fake: os dados de demonstração não fingem ter passado por um modelo real.
    private static readonly ExecucaoTriagem _execucaoSeed = new("fake", "fake-triagem-v1", "triagem.v1");

    private static readonly string[] _motivosFalha =
    [
        "O provedor de IA não respondeu a tempo. Tente refazer a triagem.",
        "A IA retornou uma resposta fora do formato esperado.",
        "O provedor de IA atingiu o limite de uso. Tente refazer em alguns minutos.",
    ];

    private sealed record Evento(TimeSpan Quando, bool ComComentario, Action<Chamado, DateTimeOffset> Aplicar);

    /// <param name="categorias">Id de cada categoria pelo nome (as do seed de categorias).</param>
    /// <param name="agora">Referência da janela de 90 dias; nenhum evento é gerado depois dela.</param>
    public static DadosSeed Gerar(
        IReadOnlyDictionary<string, short> categorias,
        DateTimeOffset agora,
        int quantidade = Quantidade)
    {
        var faker = new Faker("pt_BR") { Random = new Randomizer(Semente) };
        agora = new DateTimeOffset(agora.UtcTicks - (agora.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

        // Os primeiros chamados cobrem todas as combinações de status × prioridade (menos Crítica cancelada, RN-05).
        var combinacoes = (
            from status in _status
            from prioridade in _prioridades
            where !(status == StatusChamado.Cancelado && prioridade == Prioridade.Critica)
            select (status, prioridade)).ToArray();

        var chamados = new List<Chamado>(quantidade);
        var triagens = new List<TriagemIA>();
        for (var i = 0; i < quantidade; i++)
        {
            var (status, prioridade) = i < combinacoes.Length ? combinacoes[i] : Sortear(faker);
            var (chamado, triagem) = GerarChamado(faker, categorias, agora, status, prioridade);
            chamados.Add(chamado);
            if (triagem is not null)
            {
                triagens.Add(triagem);
            }
        }

        // Ordem de criação: o número amigável (identity) cresce com a data.
        return new DadosSeed([.. chamados.OrderBy(c => c.CriadoEm)], triagens);
    }

    private static (StatusChamado, Prioridade) Sortear(Faker faker)
    {
        var status = faker.Random.WeightedRandom(_status, _pesosStatus);
        var prioridade = faker.Random.WeightedRandom(_prioridades, _pesosPrioridade);
        return status == StatusChamado.Cancelado && prioridade == Prioridade.Critica
            ? (status, Prioridade.Alta)
            : (status, prioridade);
    }

    private static (Chamado Chamado, TriagemIA? Triagem) GerarChamado(
        Faker faker,
        IReadOnlyDictionary<string, short> categorias,
        DateTimeOffset agora,
        StatusChamado status,
        Prioridade prioridade)
    {
        var nomeCategoria = faker.PickRandom(ModelosChamado.PorCategoria.Keys.ToArray());
        var modelo = faker.PickRandom(ModelosChamado.PorCategoria[nomeCategoria]);
        var sistema = faker.PickRandom(ModelosChamado.Sistemas);
        var modulo = faker.PickRandom(ModelosChamado.Modulos);
        var codigo = faker.PickRandom(_codigos);
        string Preencher(string texto) =>
            texto.Replace("{sistema}", sistema).Replace("{modulo}", modulo).Replace("{codigo}", codigo);

        var primeiroNome = faker.Name.FirstName();
        var sobrenome = faker.Name.LastName();
        var descricao = Preencher(modelo.Descricao)
            + (faker.Random.Bool(0.15f) ? DadoPessoalFalso(faker, primeiroNome) : string.Empty);
        short? categoriaCorreta = categorias.TryGetValue(nomeCategoria, out var id) ? id : null;
        var desfecho = categoriaCorreta is null
            ? DesfechoTriagem.SemTriagem
            : faker.Random.WeightedRandom(_desfechos, _pesosDesfecho);
        // P-02: a categoria fica nula até o aceite. Com a triagem aceita, ela (e a prioridade) vêm do aceite, na linha
        // do tempo; nos demais, metade dos solicitantes já escolheu a categoria ao abrir.
        var aceita = desfecho == DesfechoTriagem.Aceita;
        short? categoriaId = !aceita && faker.Random.Bool(0.5f) ? categoriaCorreta : null;

        var atendente = faker.PickRandom(_atendentes);
        var solicitante = $"{primeiroNome} {sobrenome}";
        var (passos, duracao) = LinhaDoTempo(faker, status, atendente, Preencher(modelo.Resolucao));

        // Criação dentro da janela de 90 dias, com espaço para toda a linha do tempo terminar antes de "agora".
        var idade = duracao + Entre(faker, TimeSpan.FromHours(2), _janela - duracao);
        var criadoEm = agora - idade;
        var finalizado = status is StatusChamado.Fechado or StatusChamado.Cancelado;
        var limiteComentarios = finalizado ? duracao : idade;

        var eventos = new List<Evento>(passos);
        // De 1 a 5 comentários no total, contando os registrados junto com a mudança de status (P-09).
        var extras = Math.Max(0, faker.Random.Int(1, 5) - passos.Count(p => p.ComComentario));
        for (var i = 0; i < extras && limiteComentarios > TimeSpan.FromMinutes(2); i++)
        {
            var quando = Entre(faker, TimeSpan.FromMinutes(1), limiteComentarios - TimeSpan.FromMinutes(1));
            var (autor, texto) = faker.Random.Bool()
                ? (atendente, faker.PickRandom(ModelosChamado.PerguntasAtendente))
                : (solicitante, faker.PickRandom(ModelosChamado.RespostasSolicitante));
            eventos.Add(new Evento(quando, ComComentario: true, (c, em) => c.Comentar(autor, texto, em)));
        }

        var chamado = Chamado.Abrir(
            Capitalizar(Preencher(modelo.Titulo)),
            descricao,
            solicitante,
            $"{Slug(primeiroNome)}.{Slug(sobrenome)}@example.com",
            categoriaId,
            aceita ? null : prioridade,
            criadoEm);

        var triagem = desfecho == DesfechoTriagem.SemTriagem
            ? null
            : GerarTriagem(faker, chamado, desfecho, categoriaCorreta!.Value, categorias, prioridade, atendente, eventos);

        foreach (var evento in eventos.OrderBy(e => e.Quando))
        {
            evento.Aplicar(chamado, criadoEm + evento.Quando);
        }

        return (chamado, triagem);
    }

    /// <summary>
    /// Triagem processada segundos depois da abertura. A decisão do atendente entra na linha do tempo do chamado
    /// entre 1 e 3 minutos após a abertura: antes de qualquer mudança de status (a primeira acontece depois de 3 min),
    /// porque chamado finalizado não aceita decisão (P-11). O aceite passa pelo domínio e aplica categoria e
    /// prioridade ao chamado, como na API.
    /// </summary>
    private static TriagemIA GerarTriagem(
        Faker faker,
        Chamado chamado,
        DesfechoTriagem desfecho,
        short categoriaCorreta,
        IReadOnlyDictionary<string, short> categorias,
        Prioridade prioridade,
        string atendente,
        List<Evento> eventos)
    {
        var triagem = TriagemIA.Criar(chamado, chamado.CriadoEm);
        var processadaEm = chamado.CriadoEm + Entre(faker, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(20));
        var decisao = Entre(faker, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3) - TimeSpan.FromSeconds(1));

        if (desfecho == DesfechoTriagem.Falhou)
        {
            triagem.Falhar(faker.PickRandom(_motivosFalha), _execucaoSeed, processadaEm);
            return triagem;
        }

        // Rejeitadas sugeriram outra categoria (o motivo mais comum de rejeição); as demais acertaram a categoria.
        var (categoriaSugerida, prioridadeSugerida, confianca) = desfecho == DesfechoTriagem.Rejeitada
            ? (faker.PickRandom(categorias.Values.Where(c => c != categoriaCorreta).ToArray()),
                faker.PickRandom(_prioridades), faker.Random.Decimal(0.40m, 0.75m))
            : (categoriaCorreta, prioridade, faker.Random.Decimal(0.70m, 0.97m));
        triagem.Concluir(new SugestaoTriagem(
                categoriaSugerida,
                prioridadeSugerida,
                $"Solicitante relata: {chamado.Titulo}.",
                $"Olá! Recebemos seu chamado sobre \"{chamado.Titulo}\" e nossa equipe já está analisando. "
                    + "Retornaremos assim que tivermos novidades.",
                Math.Round(confianca, 2)),
            _execucaoSeed,
            processadaEm);

        if (desfecho == DesfechoTriagem.Aceita)
        {
            eventos.Add(new Evento(decisao, ComComentario: false, (c, em) => triagem.Aceitar(c, atendente, em)));
        }
        else if (desfecho == DesfechoTriagem.Rejeitada)
        {
            var motivo = faker.Random.Bool() ? "A categoria sugerida não corresponde ao problema." : null;
            eventos.Add(new Evento(decisao, ComComentario: false, (c, em) => triagem.Rejeitar(c, atendente, motivo, em)));
        }

        return triagem;
    }

    /// <summary>
    /// Passos de status até o destino, em deslocamentos a partir da criação. A resolução fica entre 1 h e 10 dias
    /// depois da abertura (modelo §7); ~15% dos que passaram por Resolvido foram reabertos uma vez.
    /// </summary>
    private static (List<Evento> Passos, TimeSpan Duracao) LinhaDoTempo(
        Faker faker,
        StatusChamado destino,
        string atendente,
        string resolucao)
    {
        var passos = new List<Evento>();
        void Passo(TimeSpan quando, StatusChamado status, string? comentario) =>
            passos.Add(new Evento(quando, comentario is not null, (c, em) => c.MudarStatus(status, atendente, comentario, em)));

        if (destino == StatusChamado.Cancelado)
        {
            var cancelamento = Entre(faker, TimeSpan.FromMinutes(10), TimeSpan.FromDays(3));
            Passo(cancelamento, StatusChamado.Cancelado, faker.PickRandom(ModelosChamado.MotivosCancelamento));
            return (passos, cancelamento);
        }

        if (destino == StatusChamado.Aberto)
        {
            return (passos, TimeSpan.Zero);
        }

        var reaberto = faker.Random.Bool(0.15f);
        var resolvido = Entre(faker, TimeSpan.FromHours(1), TimeSpan.FromDays(reaberto ? 6 : 10));
        var emAndamento = TimeSpan.FromSeconds(Math.Round(resolvido.TotalSeconds * faker.Random.Double(0.05, 0.5)));
        Passo(emAndamento, StatusChamado.EmAndamento, null);

        if (destino == StatusChamado.EmAndamento && !reaberto)
        {
            return (passos, emAndamento);
        }

        var ultimo = resolvido;
        if (reaberto)
        {
            var reabertura = resolvido + Entre(faker, TimeSpan.FromHours(1), TimeSpan.FromDays(2));
            Passo(resolvido, StatusChamado.Resolvido, "Primeira correção aplicada; aguardando confirmação.");
            Passo(reabertura, StatusChamado.EmAndamento, "Solicitante informou que o problema voltou.");
            ultimo = reabertura;
            if (destino == StatusChamado.EmAndamento)
            {
                return (passos, ultimo);
            }

            ultimo = reabertura + Entre(faker, TimeSpan.FromHours(1), TimeSpan.FromDays(2));
        }

        Passo(ultimo, StatusChamado.Resolvido, resolucao);
        if (destino == StatusChamado.Fechado)
        {
            ultimo += Entre(faker, TimeSpan.FromDays(1), TimeSpan.FromDays(5));
            Passo(ultimo, StatusChamado.Fechado, null);
        }

        return (passos, ultimo);
    }

    private static string DadoPessoalFalso(Faker faker, string primeiroNome) => faker.Random.Int(0, 2) switch
    {
        0 => $" Meu CPF é {faker.Random.ReplaceNumbers("###.###.###-##")}.",
        1 => $" Se precisar, me ligue no {faker.Random.ReplaceNumbers("(##) 9####-####")}.",
        _ => $" Meu e-mail alternativo é {Slug(primeiroNome)}{faker.Random.Int(10, 99)}@example.com.",
    };

    /// <summary>Intervalo aleatório em segundos inteiros (o PostgreSQL guarda microssegundos; evita arredondamento).</summary>
    private static TimeSpan Entre(Faker faker, TimeSpan minimo, TimeSpan maximo) =>
        TimeSpan.FromSeconds(faker.Random.Long((long)minimo.TotalSeconds, (long)maximo.TotalSeconds));

    private static string Capitalizar(string texto) => string.Concat(char.ToUpperInvariant(texto[0]), texto[1..]);

    /// <summary>"João" → "joao": a decomposição separa os acentos, e só letras e dígitos ASCII ficam.</summary>
    private static string Slug(string texto) =>
        string.Concat(texto.Normalize(NormalizationForm.FormD)
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToLowerInvariant));
}
