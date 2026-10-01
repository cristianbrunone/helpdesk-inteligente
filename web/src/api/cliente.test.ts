import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { servidor } from '../testes/servidor';
import { ErroApi, mensagemDeErro, requisitar } from './cliente';

describe('requisitar', () => {
  it('devolve o corpo JSON quando a resposta é de sucesso', async () => {
    servidor.use(http.get('/api/teste', () => HttpResponse.json({ ok: true })));

    await expect(requisitar<{ ok: boolean }>('/api/teste')).resolves.toEqual({ ok: true });
  });

  it('converte ProblemDetails em ErroApi com código e correlationId', async () => {
    servidor.use(
      http.get('/api/teste', () =>
        HttpResponse.json(
          {
            title: 'Recurso não encontrado',
            codigo: 'nao_encontrado',
            correlationId: 'abc-123',
          },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );

    const erro = await requisitar('/api/teste').catch((e: unknown) => e);

    expect(erro).toBeInstanceOf(ErroApi);
    expect(erro).toMatchObject({
      status: 404,
      codigo: 'nao_encontrado',
      correlationId: 'abc-123',
      message: 'Recurso não encontrado',
    });
  });

  it('gera mensagem genérica quando o erro não tem corpo JSON', async () => {
    servidor.use(http.get('/api/teste', () => new HttpResponse('Bad Gateway', { status: 502 })));

    const erro = await requisitar('/api/teste').catch((e: unknown) => e);

    expect(erro).toBeInstanceOf(ErroApi);
    expect(mensagemDeErro(erro)).toBe('A API respondeu com o status 502.');
  });

  it('traduz falha de rede para uma mensagem em pt-BR', async () => {
    servidor.use(http.get('/api/teste', () => HttpResponse.error()));

    const erro = await requisitar('/api/teste').catch((e: unknown) => e);

    expect(erro).not.toBeInstanceOf(ErroApi);
    expect(mensagemDeErro(erro)).toMatch(/Não foi possível conectar ao servidor/);
  });
});
