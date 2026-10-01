import { TextInput } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { useEffect, useRef, useState } from 'react';
import { BUSCA_TAMANHO_MINIMO } from '../api/filtrosChamados';

const ESPERA_MS = 400;

interface Props {
  valor: string;
  aoBuscar: (termo: string) => void;
}

/**
 * Busca com debounce: só vai para a URL (e para a API) depois de uma pausa na digitação e com no mínimo
 * 3 caracteres, a mesma regra da API (ADR-0008). Apagar tudo limpa a busca.
 */
export function CampoBusca({ valor, aoBuscar }: Props) {
  const [texto, setTexto] = useState(valor);
  const [valorAnterior, setValorAnterior] = useState(valor);
  const [termo] = useDebouncedValue(texto.trim(), ESPERA_MS);
  const ultimoTermo = useRef(termo);

  // A URL mudou por fora (voltar do navegador, "Limpar filtros"): o campo acompanha.
  if (valor !== valorAnterior) {
    setValorAnterior(valor);
    setTexto(valor);
  }

  useEffect(() => {
    // Só reage quando o termo digitado muda, não quando a URL muda: evita reaplicar um termo antigo.
    if (termo === ultimoTermo.current) return;
    ultimoTermo.current = termo;
    if (termo === valor || (termo.length > 0 && termo.length < BUSCA_TAMANHO_MINIMO)) return;
    aoBuscar(termo);
  }, [termo, valor, aoBuscar]);

  const curto = texto.trim().length > 0 && texto.trim().length < BUSCA_TAMANHO_MINIMO;

  return (
    <TextInput
      type="search"
      label="Buscar no título e na descrição"
      placeholder="Ex.: boleto, ERR-504, configuração"
      value={texto}
      onChange={(evento) => setTexto(evento.currentTarget.value)}
      description={curto ? `Digite ao menos ${BUSCA_TAMANHO_MINIMO} caracteres.` : undefined}
    />
  );
}
