import { zodResolver } from '@hookform/resolvers/zod';
import {
  Alert,
  Button,
  Center,
  Paper,
  PasswordInput,
  Stack,
  TextInput,
  Title,
} from '@mantine/core';
import { useForm } from 'react-hook-form';
import { Navigate, useNavigate, useSearchParams } from 'react-router';
import { z } from 'zod';
import { useEntrar, useSessao } from '../api/autenticacao';
import { ErroApi, mensagemDeErro } from '../api/cliente';
import { AlertaErro } from '../componentes/AlertaErro';
import { useTituloDaPagina } from '../hooks/useTituloDaPagina';

const esquema = z.object({
  email: z.string().trim().min(1, 'Informe o e-mail.').email('Informe um e-mail válido.'),
  senha: z.string().min(1, 'Informe a senha.'),
});

type Valores = z.infer<typeof esquema>;

/** Só caminhos internos: um `?voltar=https://outro-site` não pode virar um redirecionamento aberto. */
function destinoSeguro(voltar: string | null): string {
  return voltar && voltar.startsWith('/') && !voltar.startsWith('//') ? voltar : '/chamados';
}

/** Tela de login (ADR-0026). A sessão fica num cookie httpOnly gravado pela API; o front nunca vê o token. */
export function Entrar() {
  const [parametros] = useSearchParams();
  const destino = destinoSeguro(parametros.get('voltar'));
  const navegar = useNavigate();
  useTituloDaPagina('Entrar');
  const sessao = useSessao();
  const entrar = useEntrar();
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Valores>({ resolver: zodResolver(esquema), defaultValues: { email: '', senha: '' } });

  if (sessao.data) {
    return <Navigate to={destino} replace />;
  }

  const enviar = handleSubmit(async (valores) => {
    try {
      await entrar.mutateAsync(valores);
      await navegar(destino, { replace: true });
    } catch {
      // O erro aparece no alerta abaixo.
    }
  });

  const credenciaisErradas = entrar.error instanceof ErroApi && entrar.error.status === 401;

  return (
    <Center mih="100vh" p="md">
      <Stack w="100%" maw={400} gap="md">
        <Title order={1} size="h3" ta="center">
          HelpDesk Inteligente
        </Title>
        <Paper withBorder p="lg" radius="md" component="form" onSubmit={enviar} noValidate>
          <Stack gap="sm">
            <Title order={2} size="h4">
              Entrar
            </Title>
            {credenciaisErradas && (
              <Alert color="red" variant="light" role="alert">
                {mensagemDeErro(entrar.error)}
              </Alert>
            )}
            {entrar.isError && !credenciaisErradas && (
              <AlertaErro titulo="Não foi possível entrar" erro={entrar.error} />
            )}
            <TextInput
              label="E-mail"
              type="email"
              autoComplete="username"
              autoFocus
              withAsterisk
              error={errors.email?.message}
              {...register('email')}
            />
            <PasswordInput
              label="Senha"
              autoComplete="current-password"
              withAsterisk
              error={errors.senha?.message}
              {...register('senha')}
            />
            <Button type="submit" loading={isSubmitting} fullWidth>
              Entrar
            </Button>
          </Stack>
        </Paper>
      </Stack>
    </Center>
  );
}
