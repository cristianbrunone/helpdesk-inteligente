namespace HelpDesk.Infrastructure.Persistencia.Seed;

/// <summary>
/// Base de conhecimento de demonstração (modelo §7, RF-30): 25 artigos escritos à mão, 5 por categoria, em Markdown
/// com seções <c>##</c> para exercitar o chunking (ADR-0011). Os temas acompanham os chamados do seed, para que o RAG
/// tenha o que recuperar; nenhum artigo traz dado pessoal.
/// </summary>
internal static class ModelosArtigo
{
    internal sealed record Modelo(string Categoria, string Titulo, string Conteudo);

    public static readonly Modelo[] Todos =
    [
        // ---------- Acesso/Login ----------
        new("Acesso/Login", "Desbloqueio de conta após tentativas de login", """
            ## Sintoma

            O usuário vê "usuário ou senha inválidos" mesmo digitando a senha correta, ou a mensagem "conta bloqueada".
            O e-mail de recuperação de senha pode não chegar enquanto a conta estiver bloqueada.

            ## Causa

            Depois de 5 tentativas de login sem sucesso em 15 minutos, a conta é bloqueada automaticamente por segurança.
            O bloqueio também impede o envio do link de redefinição.

            ## Como resolver

            1. Em Administração > Usuários, localize o usuário e confira o campo "Situação".
            2. Clique em "Desbloquear conta".
            3. Reenvie o link de redefinição de senha pela mesma tela.
            4. Peça ao usuário para conferir a caixa de spam: o remetente é o endereço de notificações do sistema.

            ## Quando escalar

            Se a conta voltar a bloquear sozinha em poucos minutos, pode haver um serviço ou celular antigo tentando
            entrar com a senha anterior. Encaminhe para a equipe de segurança.
            """),
        new("Acesso/Login", "Política de senhas e troca de senha expirada", """
            ## Regras da política

            - Mínimo de 12 caracteres, com letras maiúsculas, minúsculas e números.
            - As últimas 5 senhas não podem ser reutilizadas.
            - A senha expira a cada 90 dias; o sistema avisa 7 dias antes.

            ## Erro ao salvar a nova senha

            Quando a troca de senha expirada falha com um código de erro, quase sempre a nova senha não atende à política.
            A mensagem nem sempre diz qual regra foi violada.

            ## Como orientar o usuário

            Peça uma senha nova que atenda a todas as regras acima e que não seja parecida com as anteriores.
            Se a troca continuar falhando, redefina a senha pela administração e marque "trocar no próximo acesso".
            """),
        new("Acesso/Login", "Erro 403: permissões de módulo por perfil", """
            ## Sintoma

            O usuário entra no sistema normalmente, mas ao abrir um módulo específico recebe erro 403 (acesso negado).
            Os outros módulos funcionam.

            ## Causa

            O acesso a cada módulo é concedido pelo perfil do usuário. O 403 indica que o perfil não tem a permissão do
            módulo, geralmente após uma troca de área ou uma revisão de perfis.

            ## Como resolver

            1. Em Administração > Perfis, abra o perfil do usuário.
            2. Confira se o módulo aparece marcado em "Permissões".
            3. Reaplique a permissão e peça ao usuário para sair e entrar de novo: a permissão vale a partir do próximo login.

            ## Observação

            Não conceda o perfil de administrador para contornar o 403. Use o perfil mínimo necessário.
            """),
        new("Acesso/Login", "Recadastrar a autenticação em dois fatores (2FA)", """
            ## Sintoma

            O código de 6 dígitos do aplicativo autenticador é recusado no login, mesmo digitado dentro do prazo.

            ## Causas comuns

            - O segredo do 2FA ficou dessincronizado (troca de celular ou restauração de backup).
            - O relógio do celular está fora do horário automático.

            ## Como resolver

            1. Peça ao usuário para ativar o horário automático no celular e tentar de novo.
            2. Se continuar falhando, em Administração > Usuários clique em "Redefinir 2FA".
            3. No próximo login, o usuário lê o novo QR Code e recadastra o autenticador.

            ## Segurança

            Antes de redefinir o 2FA, confirme a identidade do solicitante pelo canal oficial da área.
            """),
        new("Acesso/Login", "Liberação de acesso para usuário novo", """
            ## Quando usar

            Colaborador recém-contratado ou transferido que ainda não consegue entrar nos sistemas da área.

            ## Pré-requisitos

            - Solicitação feita pelo gestor da área.
            - Usuário criado pelo RH no cadastro de colaboradores.

            ## Passo a passo

            1. Crie o usuário em Administração > Usuários, se ainda não existir.
            2. Inclua o usuário no grupo de acesso da equipe: é o grupo que concede os perfis.
            3. Envie o link de primeiro acesso.

            ## Problema frequente

            Usuário criado sem grupo de acesso consegue autenticar, mas recebe erro ao entrar nos sistemas. Basta incluir
            no grupo correto.
            """),

        // ---------- Financeiro ----------
        new("Financeiro", "Erro 403 no módulo de boletos", """
            ## Sintoma

            O usuário acessa o portal financeiro, mas ao abrir o módulo de boletos recebe erro 403 (acesso negado).
            A emissão e a segunda via de boletos ficam indisponíveis para ele.

            ## Causa

            O módulo de boletos exige a permissão "Financeiro - Cobrança" no perfil. Ela costuma ser perdida quando o
            usuário muda de equipe ou quando o perfil financeiro é revisado.

            ## Como resolver

            1. Em Administração > Perfis, abra o perfil financeiro do usuário.
            2. Marque a permissão "Financeiro - Cobrança".
            3. Peça ao usuário para sair e entrar de novo.

            ## Quando escalar

            Se o 403 aparecer para toda a equipe financeira ao mesmo tempo, o problema não é de perfil: encaminhe para
            infraestrutura verificar o gateway do portal.
            """),
        new("Financeiro", "Emissão de nota fiscal: certificado digital e erros de transmissão", """
            ## Sintoma

            A nota fiscal fica com a situação "pendente" e o sistema mostra um código de erro na transmissão.

            ## Causas mais comuns

            - Certificado digital da empresa vencido.
            - Serviço da prefeitura ou da SEFAZ fora do ar.
            - Dados do cliente incompletos (endereço ou inscrição).

            ## Como resolver

            1. Em Financeiro > Configurações, confira a validade do certificado digital.
            2. Se estiver vencido, solicite a renovação à contabilidade e instale o novo certificado.
            3. Depois, use "Reprocessar pendentes" para transmitir as notas que ficaram paradas.

            ## Dica

            O sistema avisa 30 dias antes do vencimento do certificado. Cadastre um e-mail da equipe para receber o aviso.
            """),
        new("Financeiro", "Baixa de pagamentos e arquivo de retorno bancário", """
            ## Como a baixa funciona

            O banco envia diariamente um arquivo de retorno com os pagamentos compensados. O sistema importa o arquivo e
            baixa os boletos automaticamente.

            ## Boleto pago que continua em aberto

            1. Confira se já se passaram 3 dias úteis do pagamento (prazo de compensação).
            2. Em Financeiro > Retornos, veja se o arquivo do dia foi importado.
            3. Se não foi, faça a importação manual do arquivo disponível no portal do banco.

            ## Comprovante do cliente

            O comprovante sozinho não baixa o boleto, mas pode ser anexado ao chamado para conferência. Nunca baixe
            manualmente sem conferir o retorno do banco.
            """),
        new("Financeiro", "Segunda via de boleto vencido", """
            ## Regra

            Boletos vencidos há até 60 dias podem ter segunda via com juros e multa recalculados. Depois disso, é
            necessário emitir uma nova cobrança.

            ## Passo a passo

            1. Em Financeiro > Boletos, localize o boleto pelo número do documento.
            2. Clique em "Segunda via" e confirme a nova data de vencimento.
            3. Envie o novo boleto ao cliente.

            ## Tela carregando sem fim

            Se a geração da segunda via fica carregando e termina em erro, o serviço de registro do banco pode estar
            instável. Aguarde alguns minutos e tente de novo; se persistir, encaminhe para infraestrutura.
            """),
        new("Financeiro", "Cobrança em duplicidade: cancelamento e estorno", """
            ## Sintoma

            O cliente recebe duas cobranças para o mesmo pedido.

            ## Causa comum

            Uma integração reenviou o pedido e o sistema gerou uma segunda cobrança.

            ## Como resolver

            1. Confirme que as duas cobranças são do mesmo pedido e do mesmo valor.
            2. Cancele a cobrança mais recente em Financeiro > Cobranças.
            3. Se o cliente já pagou as duas, abra a solicitação de estorno: o prazo é de até 10 dias úteis.

            ## Prevenção

            Informe a equipe de integrações sobre o pedido duplicado, para que a causa seja corrigida.
            """),

        // ---------- Bug no sistema ----------
        new("Bug no sistema", "Como reportar um bug ao time de desenvolvimento", """
            ## Informações obrigatórias

            - Sistema, módulo e tela onde o problema acontece.
            - Passo a passo para reproduzir.
            - O que era esperado e o que aconteceu.
            - Código de erro exibido, se houver, e um print da tela.

            ## Antes de encaminhar

            1. Tente reproduzir em outro navegador ou em uma janela anônima.
            2. Confira se outro usuário tem o mesmo problema.
            3. Verifique se já existe um chamado aberto para o mesmo erro.

            ## Prioridade

            Bug que impede o trabalho de uma equipe inteira é Alta ou Crítica. Bug com contorno simples é Média.
            """),
        new("Bug no sistema", "Exportação de planilhas grandes", """
            ## Sintoma

            A exportação para Excel falha com um código de erro ou fica carregando quando o relatório tem muitas linhas.

            ## Causa

            Exportações acima de mil linhas podem exceder o tempo limite da tela.

            ## Contorno

            - Filtre por um período menor e exporte em partes.
            - Use a opção "Exportar em segundo plano": o arquivo é enviado por e-mail quando estiver pronto.

            ## Quando abrir bug

            Se a exportação em segundo plano também falhar, registre um bug com o filtro usado e o horário da tentativa.
            """),
        new("Bug no sistema", "Filtros de data e registros do último dia", """
            ## Sintoma

            Ao filtrar um relatório de um dia a outro, os registros do último dia não aparecem.

            ## Causa

            Alguns filtros comparam a data final como "menor que" a meia-noite daquele dia, excluindo o dia inteiro.

            ## Contorno

            Inclua um dia a mais na data final até a correção ser publicada.

            ## Como registrar

            Informe o relatório, o período filtrado e um registro do último dia que deveria aparecer.
            """),
        new("Bug no sistema", "Tela trava ao salvar: diagnóstico inicial", """
            ## Sintoma

            Ao clicar em salvar, a página congela e nada é gravado.

            ## Diagnóstico inicial

            1. Peça ao usuário para tentar em outro navegador e em uma janela anônima.
            2. Confira se algum campo obrigatório ficou vazio ou com formato inválido.
            3. Peça um print do console do navegador (tecla F12), se o usuário souber fazer.

            ## Encaminhamento

            Se o problema acontece em qualquer navegador, é bug do sistema. Encaminhe com o passo a passo e o print.

            ## Configurações que não ficam salvas

            Quando a tela diz que salvou, mas a configuração volta ao valor anterior, registre também como bug: o dado pode
            estar ficando só no navegador.
            """),
        new("Bug no sistema", "App mobile fecha sozinho", """
            ## Sintoma

            O aplicativo fecha sem mensagem ao abrir uma seção específica.

            ## Verificações

            1. Confira se o app está na versão mais recente da loja.
            2. Peça ao usuário para reiniciar o celular e limpar o cache do app.
            3. Anote o modelo do celular e a versão do sistema operacional.

            ## Encaminhamento

            Se o problema continuar na versão mais recente, registre um bug com o modelo do aparelho e a seção que fecha
            o app. O time mobile publica as correções na loja.
            """),

        // ---------- Dúvida ----------
        new("Dúvida", "Gerar relatórios personalizados", """
            ## Onde fica

            Os relatórios ficam no menu Relatórios. Cada módulo tem os seus: boletos, notas fiscais, pedidos, contratos.

            ## Passo a passo

            1. Acesse Relatórios e escolha o módulo.
            2. Em "Período", selecione "Personalizado" e informe as datas.
            3. Escolha as colunas desejadas.
            4. Clique em "Exportar" para baixar em Excel ou PDF.

            ## Relatórios salvos

            Use "Salvar filtro" para repetir o mesmo relatório todo mês sem configurar de novo.
            """),
        new("Dúvida", "Atualizar dados de perfil e contato", """
            ## Onde alterar

            Em Meu perfil > Dados de contato, o próprio usuário altera telefone e endereço.

            ## O que não pode ser alterado pelo usuário

            - Nome e documento: alterados apenas pelo RH.
            - E-mail corporativo: alterado pela equipe de acessos.

            ## Confirmação

            A alteração de telefone envia um código de confirmação por SMS. Sem a confirmação, o número antigo continua
            valendo.
            """),
        new("Dúvida", "Prazos de compensação de pagamentos", """
            ## Prazos

            - Boleto: até 3 dias úteis após o pagamento.
            - PIX: compensação imediata, com baixa em até 1 hora.
            - Transferência bancária: até 1 dia útil.

            ## Atualização do status

            O status do boleto é atualizado automaticamente quando o retorno do banco é importado. Não é preciso enviar
            comprovante, a menos que o prazo já tenha passado.

            ## Prazo vencido

            Se o prazo passou e o boleto continua em aberto, siga o artigo de baixa de pagamentos e arquivo de retorno.
            """),
        new("Dúvida", "Configurar notificações por e-mail", """
            ## Onde configurar

            Em Configuração > Notificações, cada usuário escolhe quais avisos recebe por e-mail.

            ## Opções disponíveis

            - Atualização de chamados.
            - Vencimento de boletos.
            - Aprovação de pedidos.
            - Resumo diário.

            ## Dica

            Desmarque os avisos que não usa e mantenha o resumo diário: ele concentra os avisos em um único e-mail.
            """),
        new("Dúvida", "Cancelamento e devolução de pedidos faturados", """
            ## Regra

            Pedido ainda não faturado pode ser cancelado direto na tela de pedidos. Pedido faturado exige uma nota de
            devolução.

            ## Passo a passo para pedido faturado

            1. Preencha o modelo de solicitação de devolução, com o número do pedido e o motivo.
            2. Envie ao setor fiscal, que emite a nota de devolução.
            3. Após a nota, o pedido aparece como cancelado e o financeiro faz o estorno, se houver.

            ## Prazo

            A devolução precisa ser solicitada em até 30 dias após o faturamento.
            """),

        // ---------- Infraestrutura ----------
        new("Infraestrutura", "Lentidão geral nos sistemas: checklist", """
            ## Primeiro, delimite o problema

            - Afeta um usuário, uma equipe ou todos?
            - Afeta um sistema ou todos?
            - Outros sites abrem normalmente?

            ## Checklist da infraestrutura

            1. Confira o painel de monitoramento: CPU, memória e disco dos servidores do sistema.
            2. Verifique se há backup ou job pesado rodando fora do horário programado.
            3. Confira a latência do link de internet da unidade.

            ## Causa recorrente

            Jobs de backup reagendados por engano para o horário comercial consomem o disco e deixam tudo lento.
            """),
        new("Infraestrutura", "VPN: instalação, atualização e quedas", """
            ## Sintoma

            A VPN conecta, mas cai a cada poucos minutos, e os sistemas internos ficam inacessíveis.

            ## Como resolver

            1. Confira a versão do cliente de VPN: versões antigas perdem a conexão com o novo concentrador.
            2. Atualize pelo portal de software da empresa.
            3. Se usar Wi-Fi público, teste com a rede do celular.

            ## Quando escalar

            Se várias pessoas relatarem quedas no mesmo horário, o problema está no concentrador. Abra um incidente para
            infraestrutura.
            """),
        new("Infraestrutura", "Impressoras e fila de impressão", """
            ## Sintoma

            Os documentos aparecem na fila, mas a impressora não imprime.

            ## Como resolver

            1. Confira se a impressora está ligada, com papel e sem erro no visor.
            2. Cancele o documento travado no início da fila.
            3. Se a fila continuar parada, reinicie o serviço de spooler no servidor de impressão.

            ## Pasta compartilhada e impressão em rede

            Impressoras e pastas compartilhadas dependem dos grupos de acesso da rede. Se o usuário recebe "acesso
            negado", confira se ele continua no grupo da equipe.
            """),
        new("Infraestrutura", "Erros 502 e 504 de gateway", """
            ## O que significam

            502 e 504 indicam que o balanceador não conseguiu resposta do servidor da aplicação. O problema não está no
            computador do usuário.

            ## Como diagnosticar

            1. Confirme se o erro acontece para toda a equipe.
            2. No painel do balanceador, verifique as instâncias marcadas como fora do ar.
            3. Substitua ou reinicie a instância com falha.

            ## Comunicação

            Erro de gateway que afeta todos os usuários é incidente de prioridade Crítica: avise as áreas afetadas.
            """),
        new("Infraestrutura", "Certificados TLS e avisos do navegador", """
            ## Sintoma

            O navegador avisa que o certificado do site é inválido e bloqueia o acesso ao sistema.

            ## Causa comum

            O certificado TLS do servidor expirou.

            ## Como resolver

            1. Confira a data de validade do certificado no navegador (cadeado na barra de endereço).
            2. Renove o certificado e instale no servidor.
            3. Configure a renovação automática para evitar a recorrência.

            ## Importante

            Nunca oriente o usuário a ignorar o aviso do navegador: o aviso pode indicar um ataque.
            """),
    ];
}
