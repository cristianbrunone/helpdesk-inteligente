import { notifications } from '@mantine/notifications';
import { ErroApi, mensagemDeErro } from '../api/cliente';

/**
 * Avisa o atendente quando uma escrita sobre o chamado falha. Em 412 (outra pessoa alterou) e 409 (o estado mudou),
 * quem chama recarrega o detalhe: a tela passa a mostrar a versão atual e os botões certos.
 */
export function notificarErroDeEscrita(erro: unknown): void {
  if (erro instanceof ErroApi && erro.status === 412) {
    notifications.show({
      color: 'yellow',
      title: 'Este chamado foi alterado por outra pessoa',
      message: 'Carregamos a versão mais recente. Confira as mudanças e tente de novo.',
    });
    return;
  }

  const rastreio =
    erro instanceof ErroApi && erro.correlationId
      ? ` (código de rastreio: ${erro.correlationId})`
      : '';
  notifications.show({
    color: 'red',
    title:
      erro instanceof ErroApi && erro.status === 409
        ? 'Ação não permitida'
        : 'Não foi possível concluir',
    message: `${mensagemDeErro(erro)}${rastreio}`,
  });
}

/** 412 e 409 significam que a tela está desatualizada: vale recarregar o detalhe. */
export function telaDesatualizada(erro: unknown): boolean {
  return erro instanceof ErroApi && (erro.status === 412 || erro.status === 409);
}
