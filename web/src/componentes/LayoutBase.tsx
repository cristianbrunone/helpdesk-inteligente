import { AppShell, Burger, Group, NavLink, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { Link, Outlet, useLocation } from 'react-router';

const ITENS_DO_MENU = [
  { rotulo: 'Chamados', destino: '/chamados' },
  { rotulo: 'Novo chamado', destino: '/chamados/novo' },
];

/** Casca responsiva: no celular (< sm) a navegação vira um menu aberto pelo botão hambúrguer. */
export function LayoutBase() {
  const [menuAberto, { toggle, close }] = useDisclosure();
  const { pathname } = useLocation();

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
          <Title order={1} size="h4">
            HelpDesk Inteligente
          </Title>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="sm" component="nav" aria-label="Navegação">
        {ITENS_DO_MENU.map(({ rotulo, destino }) => (
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
