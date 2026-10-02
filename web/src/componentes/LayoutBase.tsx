import { AppShell, Badge, Burger, Button, Group, NavLink, Text, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { Link, Outlet, useLocation, useNavigate } from 'react-router';
import { useSair, useSessao } from '../api/autenticacao';

const ITENS_DO_MENU = [
  { rotulo: 'Chamados', destino: '/chamados' },
  { rotulo: 'Novo chamado', destino: '/chamados/novo' },
  { rotulo: 'Dashboard', destino: '/dashboard' },
];

/** Casca responsiva: no celular (< sm) a navegação vira um menu aberto pelo botão hambúrguer. */
export function LayoutBase() {
  const [menuAberto, { toggle, close }] = useDisclosure();
  const { pathname } = useLocation();
  const navegar = useNavigate();
  const { data: usuario } = useSessao();
  const sair = useSair();

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
          <Title order={1} size="h4" style={{ flex: 1 }}>
            HelpDesk Inteligente
          </Title>
          {usuario && (
            <Group gap="xs" wrap="nowrap" aria-label="Usuário da sessão" role="group">
              <Text size="sm" fw={500} visibleFrom="sm">
                {usuario.nome}
              </Text>
              <Badge variant="light" visibleFrom="sm">
                {usuario.perfil}
              </Badge>
              <Button size="xs" variant="default" onClick={aoSair} loading={sair.isPending}>
                Sair
              </Button>
            </Group>
          )}
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="sm" component="nav" aria-label="Navegação">
        {ITENS_DO_MENU.filter(
          (item) => !(usuario?.perfil === 'Solicitante' && item.destino === '/dashboard'),
        ).map(({ rotulo, destino }) => (
          <NavLink
            key={destino}
            component={Link}
            to={destino}
            label={rotulo}
            active={pathname === destino}
            aria-current={pathname === destino ? 'page' : undefined}
            onClick={close}
          />
        ))}
      </AppShell.Navbar>

      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  );
}
