import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { renderizar } from '../testes/renderizar';
import { servidor } from '../testes/servidor';
import { PainelCopiloto } from './PainelCopiloto';

const CHAMADO_ID = '0192f0c1-0000-7000-8000-0000000000bb';

describe('PainelCopiloto', () => {
  it('renderiza incrementalmente as ferramentas e deltas da resposta SSE', async () => {
    const sseChunks = [
      'event: ferramenta\n' +
        'data: {"nome":"buscar_chamados_similares","fase":"iniciada","descricao":"Buscando chamados semelhantes"}\n\n',
      'event: ferramenta\n' +
        'data: {"nome":"buscar_chamados_similares","fase":"concluida","resultados":2}\n\n',
      'event: delta\n' + 'data: {"texto":"Sim, encontramos casos parecidos. "}\n\n',
      'event: delta\n' + 'data: {"texto":"Veja o chamado #877."}\n\n',
      'event: fontes\n' +
        'data: {"itens":[{"tipo":"chamado","id":"c-877","numero":877,"titulo":"Erro 403 em boletos"}]}\n\n',
      'event: fim\n' + 'data: {"tokensEntrada":100,"tokensSaida":40}\n\n',
    ];

    servidor.use(
      http.post(`/api/chamados/${CHAMADO_ID}/copiloto`, () => {
        const encoder = new TextEncoder();
        const stream = new ReadableStream<Uint8Array>({
          start(controller) {
            for (const chunk of sseChunks) {
              controller.enqueue(encoder.encode(chunk));
            }
            controller.close();
          },
        });

        return new HttpResponse(stream, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' },
        });
      }),
    );

    renderizar(<PainelCopiloto chamadoId={CHAMADO_ID} />);

    // Painel e sugestões visíveis inicialmente
    expect(screen.getByText('Copiloto do Chamado')).toBeInTheDocument();
    const botaoSugestao = screen.getByRole('button', { name: 'Já tivemos casos parecidos?' });

    const usuario = userEvent.setup();
    await usuario.click(botaoSugestao);

    // Mensagem do usuário enviada
    await waitFor(() => {
      expect(screen.getByText('Já tivemos casos parecidos?')).toBeInTheDocument();
    });

    // Texto incremental e ferramentas renderizadas
    await waitFor(() => {
      expect(
        screen.getByText(/Sim, encontramos casos parecidos. Veja o chamado #877./),
      ).toBeInTheDocument();
    });

    // Fonte clicável renderizada
    const linkFonte = screen.getByTestId('fonte-chamado');
    expect(linkFonte).toHaveTextContent('#877 · Erro 403 em boletos');
    expect(linkFonte).toHaveAttribute('href', '/chamados/c-877');
  });

  it('exibe selo de referência não verificada quando o guardrail de saída sinaliza', async () => {
    const sseComAviso =
      'event: delta\n' +
      'data: {"texto":"Cito o chamado #999999 inventado."}\n\n' +
      'event: aviso\n' +
      'data: {"tipo":"referencia_nao_verificada","referencias":["#999999"]}\n\n' +
      'event: fim\n' +
      'data: {}\n\n';

    servidor.use(
      http.post(`/api/chamados/${CHAMADO_ID}/copiloto`, () => {
        return new HttpResponse(sseComAviso, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' },
        });
      }),
    );

    renderizar(<PainelCopiloto chamadoId={CHAMADO_ID} />);
    const usuario = userEvent.setup();

    const input = screen.getByTestId('input-copiloto');
    await usuario.type(input, 'Pergunta teste');
    await usuario.click(screen.getByTestId('botao-enviar-copiloto'));

    await waitFor(() => {
      expect(screen.getByTestId('selo-referencia-nao-verificada')).toHaveTextContent(
        'Contém referências não verificadas: #999999',
      );
    });
  });

  it('exibe selo de resposta truncada quando excede o limite de tokens', async () => {
    const sseTruncada =
      'event: delta\n' +
      'data: {"texto":"Resposta cortada no meio..."}\n\n' +
      'event: aviso\n' +
      'data: {"tipo":"resposta_truncada"}\n\n' +
      'event: fim\n' +
      'data: {}\n\n';

    servidor.use(
      http.post(`/api/chamados/${CHAMADO_ID}/copiloto`, () => {
        return new HttpResponse(sseTruncada, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' },
        });
      }),
    );

    renderizar(<PainelCopiloto chamadoId={CHAMADO_ID} />);
    const usuario = userEvent.setup();

    const input = screen.getByTestId('input-copiloto');
    await usuario.type(input, 'Pergunta que gera resposta longa');
    await usuario.click(screen.getByTestId('botao-enviar-copiloto'));

    await waitFor(() => {
      expect(screen.getByTestId('selo-resposta-truncada')).toHaveTextContent(
        'Resposta interrompida pelo limite de tamanho',
      );
    });
  });

  it('permite interromper a resposta com o botão Parar', async () => {
    const encoder = new TextEncoder();
    servidor.use(
      http.post(`/api/chamados/${CHAMADO_ID}/copiloto`, () => {
        const stream = new ReadableStream<Uint8Array>({
          start(controller) {
            controller.enqueue(
              encoder.encode('event: delta\ndata: {"texto":"Começo da resposta..."}\n\n'),
            );
          },
        });
        return new HttpResponse(stream, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' },
        });
      }),
    );

    renderizar(<PainelCopiloto chamadoId={CHAMADO_ID} />);
    const usuario = userEvent.setup();

    await usuario.type(screen.getByTestId('input-copiloto'), 'Pergunta');
    await usuario.click(screen.getByTestId('botao-enviar-copiloto'));

    // Botão Parar fica visível enquanto responde
    await waitFor(() => {
      expect(screen.getByTestId('botao-parar-copiloto')).toBeInTheDocument();
    });

    // Clica em parar
    await usuario.click(screen.getByTestId('botao-parar-copiloto'));

    // O botão Parar some e o botão Enviar volta
    await waitFor(() => {
      expect(screen.queryByTestId('botao-parar-copiloto')).not.toBeInTheDocument();
      expect(screen.getByTestId('botao-enviar-copiloto')).toBeInTheDocument();
    });

    // O texto recebido até o momento é preservado
    expect(screen.getByText('Começo da resposta...')).toBeInTheDocument();
  });

  it('fica totalmente oculto quando o kill switch do copiloto está desligado', async () => {
    servidor.use(
      http.get('/api/config/ia', () => HttpResponse.json({ triagem: true, copiloto: false })),
    );

    renderizar(<PainelCopiloto chamadoId={CHAMADO_ID} />);

    await waitFor(() => {
      expect(screen.queryByTestId('painel-copiloto')).not.toBeInTheDocument();
    });
  });
});
