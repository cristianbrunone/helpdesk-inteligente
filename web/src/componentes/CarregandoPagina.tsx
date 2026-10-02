import { Center, Loader } from '@mantine/core';

/** Enquanto o pedaço da página chega na primeira carga (as navegações seguintes mantêm a tela anterior). */
export function CarregandoPagina() {
  return (
    <Center h="50vh">
      <Loader aria-label="Carregando a página" />
    </Center>
  );
}
