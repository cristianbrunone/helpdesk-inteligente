import { describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import { servidor } from '../testes/servidor';
import { ErroApi } from './cliente';
import {
  conversarComCopiloto,
  parsearSseStream,
  type MensagemCopiloto,
  type EventoCopiloto,
} from './copiloto';

function criarStreamDeTexto(chunks: string[]): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder();
  return new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) {
        controller.enqueue(encoder.encode(chunk));
      }
      controller.close();
    },
  });
}

describe('parsearSseStream', () => {
  it('parseia eventos SSE padrão separados por linhas em branco duplas', async () => {
    const raw = [
      'event: ferramenta\n' +
        'data: {"nome":"buscar_artigos","fase":"iniciada"}\n\n' +
        'event: delta\n' +
        'data: {"texto":"Olá mundo"}\n\n',
    ];

    const stream = criarStreamDeTexto(raw);
    const eventos = [];
    for await (const msg of parsearSseStream(stream)) {
      eventos.push(msg);
    }

    expect(eventos).toEqual([
      { event: 'ferramenta', data: '{"nome":"buscar_artigos","fase":"iniciada"}' },
      { event: 'delta', data: '{"texto":"Olá mundo"}' },
    ]);
  });

  it('processa chunks fragmentados que cortam dados e delimitadores no meio', async () => {
    const chunks = [
      'event: del',
      'ta\ndata: {"texto":',
      '"pedaço ',
      'dividido"}\n\nevent: fim\ndata: {}',
      '\n\n',
    ];

    const stream = criarStreamDeTexto(chunks);
    const eventos = [];
    for await (const msg of parsearSseStream(stream)) {
      eventos.push(msg);
    }

    expect(eventos).toEqual([
      { event: 'delta', data: '{"texto":"pedaço dividido"}' },
      { event: 'fim', data: '{}' },
    ]);
  });

  it('acumula múltiplas linhas data no mesmo evento e ignora comentários', async () => {
    const raw = [
      ': ping keep-alive\n' +
        'event: multi\n' +
        'data: linha 1\n' +
        ': outro comentário\n' +
        'data: linha 2\n\n',
    ];

    const stream = criarStreamDeTexto(raw);
    const eventos = [];
    for await (const msg of parsearSseStream(stream)) {
      eventos.push(msg);
    }

    expect(eventos).toEqual([{ event: 'multi', data: 'linha 1\nlinha 2' }]);
  });
});

describe('conversarComCopiloto', () => {
  const chamadoId = '0192f0c1-0000-7000-8000-000000000001';
  const mensagens: MensagemCopiloto[] = [{ papel: 'usuario', conteudo: 'Como resolver?' }];

  it('consome a sequência de eventos tipados do copiloto com sucesso', async () => {
    const corpoSse =
      'event: ferramenta\n' +
      'data: {"nome":"buscar_chamados_similares","fase":"iniciada","descricao":"Buscando"}\n\n' +
      'event: ferramenta\n' +
      'data: {"nome":"buscar_chamados_similares","fase":"concluida","resultados":2}\n\n' +
      'event: delta\n' +
      'data: {"texto":"Veja o caso #877."}\n\n' +
      'event: fontes\n' +
      'data: {"itens":[{"tipo":"chamado","id":"c-1","numero":877,"titulo":"Erro 403"}]}\n\n' +
      'event: aviso\n' +
      'data: {"tipo":"referencia_nao_verificada","referencias":["#999"]}\n\n' +
      'event: fim\n' +
      'data: {"tokensEntrada":100,"tokensSaida":50}\n\n';

    servidor.use(
      http.post(`/api/chamados/${chamadoId}/copiloto`, () => {
        return new HttpResponse(corpoSse, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' },
        });
      }),
    );

    const recebidos: EventoCopiloto[] = [];
    for await (const evento of conversarComCopiloto(chamadoId, mensagens)) {
      recebidos.push(evento);
    }

    expect(recebidos).toEqual([
      {
        tipo: 'ferramenta',
        nome: 'buscar_chamados_similares',
        fase: 'iniciada',
        descricao: 'Buscando',
        resultados: undefined,
      },
      {
        tipo: 'ferramenta',
        nome: 'buscar_chamados_similares',
        fase: 'concluida',
        descricao: undefined,
        resultados: 2,
      },
      {
        tipo: 'delta',
        texto: 'Veja o caso #877.',
      },
      {
        tipo: 'fontes',
        itens: [{ tipo: 'chamado', id: 'c-1', numero: 877, titulo: 'Erro 403' }],
      },
      {
        tipo: 'aviso',
        subtipo: 'referencia_nao_verificada',
        referencias: ['#999'],
      },
      {
        tipo: 'fim',
        tokensEntrada: 100,
        tokensSaida: 50,
      },
    ]);
  });

  it('lança ErroApi se a resposta HTTP falhar antes do stream (ex: 503 ia_indisponivel)', async () => {
    servidor.use(
      http.post(`/api/chamados/${chamadoId}/copiloto`, () => {
        return HttpResponse.json(
          {
            title: 'IA indisponível',
            detail: 'O copiloto está desativado no momento.',
            codigo: 'ia_indisponivel',
          },
          {
            status: 503,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        );
      }),
    );

    const tentativa = async () => {
      // eslint-disable-next-line @typescript-eslint/no-unused-vars
      for await (const _ of conversarComCopiloto(chamadoId, mensagens)) {
        // vazio
      }
    };

    await expect(tentativa).rejects.toThrow(ErroApi);
    await expect(tentativa).rejects.toMatchObject({
      status: 503,
      codigo: 'ia_indisponivel',
      message: 'O copiloto está desativado no momento.',
    });
  });

  it('lança ErroApi se receber o evento "erro" no meio do stream SSE', async () => {
    const sseComErro =
      'event: delta\n' +
      'data: {"texto":"Iniciando..."}\n\n' +
      'event: erro\n' +
      'data: {"title":"IA indisponível","detail":"Provedor atingiu limite","codigo":"ia_indisponivel"}\n\n';

    servidor.use(
      http.post(`/api/chamados/${chamadoId}/copiloto`, () => {
        return new HttpResponse(sseComErro, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' },
        });
      }),
    );

    const recebidos: EventoCopiloto[] = [];
    const tentativa = async () => {
      for await (const ev of conversarComCopiloto(chamadoId, mensagens)) {
        recebidos.push(ev);
      }
    };

    await expect(tentativa).rejects.toThrow(ErroApi);
    expect(recebidos).toHaveLength(1);
    expect(recebidos[0]).toEqual({ tipo: 'delta', texto: 'Iniciando...' });
  });

  it('encerra o stream limpo e sem erro quando cancelado via AbortController', async () => {
    const encoder = new TextEncoder();
    const abortController = new AbortController();

    const streamInfinito = new ReadableStream<Uint8Array>({
      async start(controller) {
        controller.enqueue(encoder.encode('event: delta\ndata: {"texto":"primeiro"}\n\n'));
      },
    });

    servidor.use(
      http.post(`/api/chamados/${chamadoId}/copiloto`, () => {
        return new HttpResponse(streamInfinito, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' },
        });
      }),
    );

    const recebidos: EventoCopiloto[] = [];
    for await (const ev of conversarComCopiloto(chamadoId, mensagens, abortController.signal)) {
      recebidos.push(ev);
      // Aborta logo após o primeiro evento
      abortController.abort();
    }

    expect(recebidos).toEqual([{ tipo: 'delta', texto: 'primeiro' }]);
  });
});
