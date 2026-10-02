import { ErroApi, type Problema } from './cliente';

export interface MensagemCopiloto {
  papel: 'usuario' | 'assistente';
  conteudo: string;
}

export interface FonteCopiloto {
  tipo: 'chamado' | 'artigo';
  id: string;
  numero?: number;
  titulo: string;
}

export type EventoCopiloto =
  | { tipo: 'ferramenta'; nome: string; fase: 'iniciada'; descricao?: string }
  | { tipo: 'ferramenta'; nome: string; fase: 'concluida'; resultados?: number }
  | { tipo: 'delta'; texto: string }
  | { tipo: 'fontes'; itens: FonteCopiloto[] }
  | {
      tipo: 'aviso';
      subtipo: 'referencia_nao_verificada' | 'resposta_truncada';
      referencias?: string[];
    }
  | { tipo: 'fim'; tokensEntrada?: number; tokensSaida?: number };

export interface MensagemSse {
  event: string;
  data: string;
}

/**
 * Parser de Server-Sent Events (SSE) sobre ReadableStream (ADR-0012):
 * Processa chunks arbitrários, une múltiplos campos `data:`, ignora comentários e dispara
 * mensagens separadas por linhas em branco duplas.
 */
export async function* parsearSseStream(
  stream: ReadableStream<Uint8Array>,
): AsyncGenerator<MensagemSse> {
  const reader = stream.getReader();
  const decoder = new TextDecoder('utf-8');
  let buffer = '';

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      let separadorIndex: number;
      // Blocos de SSE são delimitados por \n\n ou \r\n\r\n
      while ((separadorIndex = buffer.indexOf('\n\n')) !== -1) {
        const bloco = buffer.slice(0, separadorIndex);
        buffer = buffer.slice(separadorIndex + 2);

        const mensagem = extrairMensagemSse(bloco);
        if (mensagem) {
          yield mensagem;
        }
      }
    }

    // Processa eventual resto final se tiver bloco terminado
    buffer += decoder.decode();
    if (buffer.trim().length > 0) {
      const mensagem = extrairMensagemSse(buffer);
      if (mensagem) {
        yield mensagem;
      }
    }
  } finally {
    reader.releaseLock();
  }
}

function extrairMensagemSse(bloco: string): MensagemSse | null {
  const linhas = bloco.split(/\r?\n/);
  let event = 'message';
  const dataLinhas: string[] = [];

  for (const linha of linhas) {
    if (linha.startsWith(':')) {
      // Comentário SSE / ping, ignora
      continue;
    }
    if (linha.startsWith('event:')) {
      event = linha.slice(6).trim();
    } else if (linha.startsWith('data:')) {
      dataLinhas.push(linha.slice(5).trimStart());
    }
  }

  if (dataLinhas.length === 0 && event === 'message') {
    return null;
  }

  return {
    event,
    data: dataLinhas.join('\n'),
  };
}

/**
 * Envia uma conversa para o copiloto via SSE e produz eventos tipados em tempo real (ADR-0012).
 * Suporta cancelamento limpo através de AbortSignal (AbortController).
 */
export async function* conversarComCopiloto(
  chamadoId: string,
  mensagens: MensagemCopiloto[],
  signal?: AbortSignal,
): AsyncGenerator<EventoCopiloto> {
  const url = new URL(`/api/chamados/${chamadoId}/copiloto`, window.location.origin);

  const resposta = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'text/event-stream',
    },
    body: JSON.stringify({ mensagens }),
    signal,
  });

  if (!resposta.ok) {
    let problema: Problema | undefined;
    try {
      problema = (await resposta.json()) as Problema;
    } catch {
      // Ignora erro de parse de JSON se a resposta de erro for texto plano
    }
    throw new ErroApi(resposta.status, problema);
  }

  if (!resposta.body) {
    throw new Error('A resposta do copiloto não possui corpo de streaming.');
  }

  try {
    for await (const { event, data } of parsearSseStream(resposta.body)) {
      if (signal?.aborted) {
        return;
      }

      if (event === 'erro') {
        let problema: Problema | undefined;
        try {
          problema = JSON.parse(data) as Problema;
        } catch {
          problema = { detail: data };
        }
        throw new ErroApi(500, problema);
      }

      const json = JSON.parse(data);

      switch (event) {
        case 'ferramenta':
          yield {
            tipo: 'ferramenta',
            nome: json.nome,
            fase: json.fase,
            descricao: json.descricao,
            resultados: json.resultados,
          };
          break;

        case 'delta':
          yield {
            tipo: 'delta',
            texto: json.texto,
          };
          break;

        case 'fontes':
          yield {
            tipo: 'fontes',
            itens: json.itens ?? [],
          };
          break;

        case 'aviso':
          yield {
            tipo: 'aviso',
            subtipo: json.tipo,
            referencias: json.referencias,
          };
          break;

        case 'fim':
          yield {
            tipo: 'fim',
            tokensEntrada: json.tokensEntrada,
            tokensSaida: json.tokensSaida,
          };
          break;
      }
    }
  } catch (erro) {
    // Se foi abortado explicitamente pelo usuário, encerra sem propagar erro fatal
    if (signal?.aborted || (erro instanceof DOMException && erro.name === 'AbortError')) {
      return;
    }
    throw erro;
  }
}
