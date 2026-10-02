import { AppShell, Badge, Burger, Button, Group, NavLink, Text, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import {
  IconChartBar,
  IconCirclePlus,
  IconLifebuoy,
  IconLogout,
  IconTicket,
} from '@tabler/icons-react';
import { Link, Outlet, useLocation, useNavigate } from 'react-router';
import { useSair, useSessao } from '../api/autenticacao';

// O solicitante só vê os próprios chamados (ADR-0026): o rótulo diz isso. Os ícones são decorativos (o nome do item
// é o texto) e ajudam a achar o item de relance (Sprint 7, item B1 da análise de experiência).
const ITENS_DO_MENU = [
  {
    rotulo: 'Chamados',
    rotuloSolicitante: 'Meus chamados',
    destino: '/chamados',
    Icone: IconTicket,
  },
  { rotulo: 'Novo chamado', destino: '/chamados/novo', Icone: IconCirclePlus },
  { rotulo: 'Dashboard', destino: '/dashboard', Icone: IconChartBar },
];

const TAMANHO_ICONE = 18;

/**
 * O item fica marcado também nas sub-rotas dele (o detalhe `/chamados/:id` marca "Chamados"), desde que nenhum outro
 * item seja exatamente a página atual (`/chamados/novo` marca só "Novo chamado"). Sprint 7, item M1 da análise.
 */
function itemAtivo(destino: string, pathname: string): boolean {
  return (
    pathname === destino ||
    (pathname.startsWith(`${destino}/`) && !ITENS_DO_MENU.some((item) => item.destino === pathname))
  );
}

/** Casca responsiva: no celular (< sm) a navegação vira um menu aberto pelo botão hambúrguer. */
export function LayoutBase() {
  const [menuAberto, { toggle, close }] = useDisclosure();
  const { pathname } = useLocation();
  const navegar = useNavigate();
  const { data: usuario } = useSessao();
  const sair = useSair();
  const ehSolicitante = usuario?.perfil === 'Solicitante';

  const aoSair = () =>
    sair.mutate(undefined, { onSettled: () => void navegar('/entrar', { replace: true }) });

  return (
    <AppShell
      header={{ height: 56 }}
      navbar={{ width: 240, breakpoint: 'sm', collapsed: { mobile: !menuAberto } }}
      padding="md"
    >
      <AppShell.Header>
        <Group h="100%" px="md" gap="sm" wrap="nowrap">
          <Burger
            opened={menuAberto}
            onClick={toggle}
            hiddenFrom="sm"
            size="sm"
            aria-label={menuAberto ? 'Fechar menu' : 'Abrir menu'}
          />
          <Group gap={8} wrap="nowrap" style={{ flex: 1 }}>
            <IconLifebuoy
              size={24}
              stroke={1.75}
              color="var(--mantine-primary-color-filled)"
              aria-hidden
            />
            <Title order={1} size="h4">
              HelpDesk Inteligente
            </Title>
          </Group>
          {usuario && (
            <Group gap="xs" wrap="nowrap" aria-label="Usuário da sessão" role="group">
              <Text size="sm" fw={500} visibleFrom="sm">
                {usuario.nome}
              </Text>
              <Badge variant="light" visibleFrom="sm">
                {usuario.perfil}
              </Badge>
              <Button
                size="xs"
                variant="default"
                onClick={aoSair}
                loading={sair.isPending}
                leftSection={<IconLogout size={14} aria-hidden />}
              >
                Sair
              </Button>
            </Group>
          )}
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="sm" component="nav" aria-label="Navegação">
        {/* No celular o cabeçalho não tem espaço para o nome e o perfil: eles abrem o menu (item M2 da análise). */}
        {usuario && (
          <Group
            hiddenFrom="sm"
            gap="xs"
            px="sm"
            pb="sm"
            mb="xs"
            wrap="nowrap"
            role="group"
            aria-label="Usuário da sessão no menu"
            style={{ borderBottom: '1px solid var(--mantine-color-gray-3)' }}
          >
            <Text size="sm" fw={500}>
              {usuario.nome}
            </Text>
            <Badge variant="light">{usuario.perfil}</Badge>
          </Group>
        )}
        {ITENS_DO_MENU.filter((item) => !(ehSolicitante && item.destino === '/dashboard')).map(
          ({ rotulo, rotuloSolicitante, destino, Icone }) => (
            <NavLink
              key={destino}
              component={Link}
              to={destino}
              label={ehSolicitante && rotuloSolicitante ? rotuloSolicitante : rotulo}
              leftSection={<Icone size={TAMANHO_ICONE} stroke={1.75} aria-hidden />}
              active={itemAtivo(destino, pathname)}
              // O leitor de tela só ouve "página atual" na própria página: no detalhe, a página não é a lista.
              aria-current={pathname === destino ? 'page' : undefined}
              onClick={close}
            />
          ),
        )}
      </AppShell.Navbar>

      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  );
}
