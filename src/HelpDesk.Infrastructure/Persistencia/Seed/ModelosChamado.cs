namespace HelpDesk.Infrastructure.Persistencia.Seed;

/// <summary>
/// Textos de chamado escritos à mão por categoria (modelo §7), combinados com variações pelo gerador. Os marcadores
/// <c>{sistema}</c>, <c>{modulo}</c> e <c>{codigo}</c> são trocados por valores das listas abaixo.
/// </summary>
internal static class ModelosChamado
{
    internal sealed record Modelo(string Titulo, string Descricao, string Resolucao);

    public static readonly string[] Sistemas = ["portal financeiro", "ERP", "CRM", "intranet", "portal do cliente", "app mobile"];

    public static readonly string[] Modulos = ["boletos", "notas fiscais", "relatórios", "cadastro de clientes", "pedidos", "contratos"];

    public static readonly IReadOnlyDictionary<string, Modelo[]> PorCategoria = new Dictionary<string, Modelo[]>
    {
        ["Acesso/Login"] =
        [
            new("Não consigo acessar o {sistema}",
                "Desde hoje cedo aparece a mensagem \"usuário ou senha inválidos\" ao entrar no {sistema}. Já tentei redefinir a senha, mas o e-mail de recuperação não chega.",
                "A conta estava bloqueada após várias tentativas. Desbloqueei o usuário e reenviei o link de redefinição de senha."),
            new("Senha expirada no {sistema}",
                "O {sistema} pede para trocar a senha, mas ao salvar a nova senha aparece o erro {codigo}.",
                "A nova senha não atendia à política (mínimo de 12 caracteres). Orientei o usuário e a troca foi concluída."),
            new("Erro 403 ao abrir {modulo}",
                "Consigo entrar no {sistema}, mas ao abrir o módulo de {modulo} aparece erro 403 (acesso negado).",
                "O perfil do usuário não tinha a permissão do módulo. Permissão reaplicada no perfil e acesso confirmado."),
            new("Autenticação em dois fatores não funciona",
                "O código do autenticador é recusado no login do {sistema}. O relógio do celular parece estar correto.",
                "O segredo do 2FA estava dessincronizado. Gerei um novo QR Code e o usuário recadastrou o autenticador."),
            new("Usuário novo sem acesso ao {sistema}",
                "Um colaborador entrou na equipe esta semana e ainda não consegue acessar o {sistema}. O login dele retorna {codigo}.",
                "O usuário foi criado sem grupo de acesso. Incluí no grupo da equipe e o acesso foi liberado."),
            new("Sessão expira a todo momento",
                "No {sistema}, a sessão cai depois de poucos minutos e preciso fazer login de novo, perdendo o que estava preenchido.",
                "Um cookie antigo conflitava com o novo domínio. Após limpar os cookies do navegador, a sessão ficou estável."),
        ],
        ["Financeiro"] =
        [
            new("Boleto com valor divergente",
                "O boleto emitido pelo {sistema} está com valor diferente do contrato. A diferença parece ser de juros cobrados antes do vencimento.",
                "A regra de juros estava aplicando a data de emissão em vez do vencimento. Boleto reemitido com o valor correto."),
            new("Não consigo emitir nota fiscal",
                "Ao emitir a nota fiscal no módulo de {modulo}, o {sistema} retorna o erro {codigo} e a nota fica pendente.",
                "O certificado digital da empresa havia vencido. Certificado renovado e notas pendentes reprocessadas."),
            new("Pagamento não baixado",
                "Paguei o boleto há três dias e ele ainda aparece em aberto no {sistema}. Tenho o comprovante.",
                "O arquivo de retorno do banco não tinha sido importado. Importação feita manualmente e pagamento baixado."),
            new("Relatório financeiro com totais errados",
                "O relatório de faturamento mensal do {sistema} soma valores cancelados no total do mês.",
                "O filtro de status do relatório ignorava cancelamentos. Correção aplicada e relatório reprocessado."),
            new("Erro {codigo} ao gerar segunda via",
                "Ao tentar gerar a segunda via de um boleto vencido no {sistema}, a tela fica carregando e depois mostra {codigo}.",
                "O serviço de registro do banco estava instável. Após a normalização, a segunda via foi gerada."),
            new("Cobrança em duplicidade",
                "Recebi duas cobranças do mesmo pedido no módulo de {modulo}. Preciso cancelar uma delas.",
                "Uma integração reenviou o pedido. Cancelei a cobrança duplicada e o estorno foi solicitado."),
        ],
        ["Bug no sistema"] =
        [
            new("Tela de {modulo} trava ao salvar",
                "No {sistema}, ao clicar em salvar na tela de {modulo}, a página congela e nada é gravado. Acontece em qualquer navegador.",
                "Um campo obrigatório sem valor padrão gerava erro não tratado. Correção publicada na versão de hoje."),
            new("Erro {codigo} ao exportar planilha",
                "A exportação de {modulo} para Excel no {sistema} falha com o erro {codigo} quando há mais de mil linhas.",
                "A exportação estourava o tempo limite. Ela passou a ser gerada em segundo plano e enviada por e-mail."),
            new("Filtro de datas ignora o último dia",
                "No relatório de {modulo}, filtrando de 01 a 31, os registros do dia 31 não aparecem.",
                "O filtro usava 'menor que' a data final. Ajustado para incluir o dia inteiro."),
            new("Configuração não é salva",
                "Altero a configuração de notificações no {sistema}, salvo, e ao voltar para a tela ela está como antes.",
                "A tela gravava a configuração no cache local e não no servidor. Correção publicada e preferências migradas."),
            new("Caracteres especiais quebram a busca",
                "Buscar um cliente com acento ou cedilha no {sistema} não retorna nada, mesmo o cadastro existindo.",
                "A busca não normalizava acentos. Ela passou a ignorar acentos e maiúsculas."),
            new("App fecha sozinho ao abrir {modulo}",
                "No app mobile, ao abrir a seção de {modulo}, o aplicativo fecha sem mensagem. Celular Android atualizado.",
                "Uma imagem sem tamanho definido causava falta de memória. Correção publicada na loja."),
        ],
        ["Dúvida"] =
        [
            new("Como gerar relatório de {modulo}?",
                "Preciso de um relatório de {modulo} do último trimestre no {sistema}, mas não encontro a opção.",
                "Enviei o passo a passo: Relatórios > {modulo} > período personalizado > exportar."),
            new("Onde altero meus dados de contato?",
                "Mudei de telefone e preciso atualizar meus dados de contato no {sistema}. Onde fica essa opção?",
                "Orientei o acesso em Meu perfil > Dados de contato. Atualização confirmada pelo usuário."),
            new("Qual o prazo de compensação do boleto?",
                "Gostaria de saber em quanto tempo o pagamento de um boleto aparece como pago no {sistema}.",
                "Expliquei que a compensação leva até 3 dias úteis e que o status é atualizado automaticamente."),
            new("Configuração de notificações por e-mail",
                "Recebo muitas notificações do {sistema}. É possível escolher quais avisos chegam por e-mail?",
                "Sim: em Configuração > Notificações é possível marcar só os avisos desejados. Usuário ajustou."),
            new("Posso cancelar um pedido já faturado?",
                "Tenho um pedido no módulo de {modulo} que já foi faturado. Ainda consigo cancelar?",
                "Pedidos faturados exigem nota de devolução. Enviei o procedimento e o modelo de solicitação."),
            new("Como dar acesso a um colega?",
                "Um colega precisa ver os relatórios de {modulo} no {sistema}. Como faço para dar acesso a ele?",
                "O acesso é concedido pelo gestor da área em Administração > Usuários. Orientei o gestor."),
        ],
        ["Infraestrutura"] =
        [
            new("{sistema} muito lento",
                "Desde a manhã, qualquer tela do {sistema} demora mais de 30 segundos para carregar. Outros sites abrem normalmente.",
                "Um job de backup rodava fora do horário e consumia o disco do servidor. Job reagendado e desempenho normalizado."),
            new("VPN desconectando",
                "A VPN cai a cada 10 minutos e preciso reconectar para acessar o {sistema}.",
                "O cliente de VPN estava desatualizado. Versão atualizada e conexão estável há 24 h."),
            new("Impressora da recepção não imprime",
                "A impressora da recepção mostra os documentos na fila, mas não imprime. Já reiniciei a impressora.",
                "O spooler do servidor de impressão estava travado. Serviço reiniciado e fila liberada."),
            new("Erro {codigo} ao acessar o {sistema}",
                "O navegador mostra {codigo} (gateway) ao acessar o {sistema}. Acontece com toda a equipe.",
                "Uma instância do balanceador estava fora do ar. Instância substituída e acesso normalizado."),
            new("Sem acesso à pasta compartilhada",
                "Não consigo abrir a pasta compartilhada do financeiro. Aparece \"acesso negado\" desde ontem.",
                "A permissão foi removida numa limpeza de grupos. Permissão restaurada para a equipe."),
            new("Certificado inválido no {sistema}",
                "O navegador avisa que o certificado do {sistema} é inválido e bloqueia o acesso.",
                "O certificado TLS do servidor tinha expirado. Certificado renovado e renovação automática configurada."),
        ],
    };

    public static readonly string[] PerguntasAtendente =
    [
        "Pode me enviar um print da tela com o erro?",
        "Isso acontece em outro navegador também?",
        "Desde quando o problema começou?",
        "Consegue me informar o número do pedido ou documento?",
        "Estamos analisando e retornamos em breve.",
        "Encaminhei para a equipe responsável.",
    ];

    public static readonly string[] RespostasSolicitante =
    [
        "Segue o print em anexo.",
        "Sim, acontece no Chrome e no Edge.",
        "Começou ontem à tarde.",
        "Obrigado pelo retorno, fico no aguardo.",
        "O problema continua acontecendo.",
        "Algum colega também relatou o mesmo erro.",
    ];

    public static readonly string[] MotivosCancelamento =
    [
        "Solicitante informou que o problema foi resolvido por conta própria.",
        "Chamado aberto em duplicidade.",
        "Solicitação fora do escopo do suporte; orientado a procurar a área responsável.",
    ];
}
