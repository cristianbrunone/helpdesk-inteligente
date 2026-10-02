import { notifications } from '@mantine/notifications';

/**
 * Confirma uma escrita que deu certo (Sprint 7, item A3 da análise de experiência). Só nas ações cujo resultado não
 * aparece onde o usuário está olhando: o modal que fecha, a página que troca, a sugestão que muda o chamado.
 */
export function notificarSucesso(mensagem: string): void {
  notifications.show({ color: 'green', message: mensagem });
}
