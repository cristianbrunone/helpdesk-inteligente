import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { atendentePadrao } from '../testes/handlers';
import { renderizarApp } from '../testes/renderizar';
import { servidor } from '../testes/servidor';

const naoAutenticado = () =>
  HttpResponse.json(
    { status: 401, codigo: 'nao_autenticado', detail: 'Entre no sistema para continuar.' },
    { status: 401, headers: { 'Content-Type': 'application/problem+json' } },
  );

/** Sem sessão até o login dar certo; o login devolve a Ana e a partir daí o /eu também. */
function semSessaoAteEntrar() {
  let logado = false;
  const corpos: unknown[] = [];
  servidor.use(
    http.get('/api/auth/eu', () =>
      logado ? HttpResponse.json(atendentePadrao) : naoAutenticado(),
    ),
    http.post('/api/auth/login', async ({ request }) => {
      const corpo = (await request.json()) as { email: string; senha: string };
      corpos.push(corpo);
      if (corpo.senha !== 'HelpDesk@2026') {
        return HttpResponse.json(
          { status: 401, codigo: 'nao_autenticado', detail: 'E-mail ou senha incorretos.' },
          { status: 401, headers: { 'Content-Type': 'application/problem+json' } },
        );
      }
      logado = true;
      return HttpResponse.json(atendentePadrao);
    }),
  );
  return corpos;
}

async function preencher(email: string, senha: string) {
  await userEvent.type(screen.getByLabelText(/^E-mail/), email);
  await userEvent.type(screen.getByLabelText(/^Senha/), senha);
  await userEvent.click(screen.getByRole('button', { name: 'Entrar' }));
}

describe('Login e sessão', () => {
  it('sem sessão, uma página protegida leva ao login guardando o destino', async () => {
    semSessaoAteEntrar();

    const { roteador } = await renderizarApp('/dashboard');

    await waitFor(() => expect(roteador.state.location.pathname).toBe('/entrar'));
    expect(roteador.state.location.search).toBe('?voltar=%2Fdashboard');
    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
  });

  it('valida no cliente e não envia nada com os campos vazios', async () => {
    const corpos = semSessaoAteEntrar();
    await renderizarApp('/entrar');

    await userEvent.click(await screen.findByRole('button', { name: 'Entrar' }));

    expect(await screen.findByText('Informe o e-mail.')).toBeInTheDocument();
    expect(screen.getByText('Informe a senha.')).toBeInTheDocument();
    expect(corpos).toHaveLength(0);
  });

  it('com a senha errada, mostra a mensagem da API e continua no login', async () => {
    semSessaoAteEntrar();
    const { roteador } = await renderizarApp('/entrar');
    await screen.findByRole('button', { name: 'Entrar' });

    await preencher('ana.suporte@example.com', 'errada');

    expect(await screen.findByRole('alert')).toHaveTextContent('E-mail ou senha incorretos.');
    expect(roteador.state.location.pathname).toBe('/entrar');
  });

  it('com as credenciais certas, volta à página pedida e mostra o usuário no cabeçalho', async () => {
    const corpos = semSessaoAteEntrar();
    const { roteador } = await renderizarApp('/entrar?voltar=%2Fchamados%2Fnovo');
    await screen.findByRole('button', { name: 'Entrar' });

    await preencher('ana.suporte@example.com', 'HelpDesk@2026');

    await waitFor(() => expect(roteador.state.location.pathname).toBe('/chamados/novo'));
    expect(corpos).toEqual([{ email: 'ana.suporte@example.com', senha: 'HelpDesk@2026' }]);
    const usuario = await screen.findByLabelText('Usuário da sessão');
    expect(within(usuario).getByText('Ana (suporte)')).toBeInTheDocument();
    expect(within(usuario).getByText('Atendente')).toBeInTheDocument();
  });

  it('ignora um destino externo em ?voltar= (sem redirecionamento aberto)', async () => {
    semSessaoAteEntrar();
    const { roteador } = await renderizarApp('/entrar?voltar=%2F%2Fexemplo-malicioso.com');
    await screen.findByRole('button', { name: 'Entrar' });

    await preencher('ana.suporte@example.com', 'HelpDesk@2026');

    await waitFor(() => expect(roteador.state.location.pathname).toBe('/chamados'));
  });

  it('"Sair" encerra a sessão na API e volta ao login', async () => {
    let saiu = false;
    servidor.use(
      http.post('/api/auth/sair', () => {
        saiu = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { roteador } = await renderizarApp('/chamados');

    await userEvent.click(await screen.findByRole('button', { name: 'Sair' }));

    await waitFor(() => expect(roteador.state.location.pathname).toBe('/entrar'));
    expect(saiu).toBe(true);
  });

  it('se a sessão expira no meio do uso (401), volta ao login', async () => {
    servidor.use(http.get('/api/chamados', naoAutenticado));

    const { roteador } = await renderizarApp('/chamados');

    await waitFor(() => expect(roteador.state.location.pathname).toBe('/entrar'));
    expect(roteador.state.location.search).toBe('?voltar=%2Fchamados');
  });
});
