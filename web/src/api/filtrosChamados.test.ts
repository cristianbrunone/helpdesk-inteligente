import { describe, expect, it } from 'vitest';
import { deQueryString, FILTROS_PADRAO, paraQueryString } from './filtrosChamados';

describe('filtrosChamados', () => {
  it('ida e volta pela URL preserva todos os filtros', () => {
    const filtros = {
      ...FILTROS_PADRAO,
      status: ['Aberto', 'EmAndamento'] as const,
      prioridade: ['Critica'] as const,
      categoriaId: [2, 5],
      semCategoria: true,
      q: 'boleto',
      criadoDe: '2026-09-01',
      criadoAte: '2026-09-30',
      ordenarPor: 'prioridade' as const,
      direcao: 'asc' as const,
      pagina: 3,
    };

    const query = paraQueryString({
      ...filtros,
      status: [...filtros.status],
      prioridade: [...filtros.prioridade],
    });

    expect(query.toString()).toBe(
      'status=Aberto&status=EmAndamento&prioridade=Critica&categoriaId=2&categoriaId=5&semCategoria=true' +
        '&q=boleto&criadoDe=2026-09-01&criadoAte=2026-09-30&ordenarPor=prioridade&direcao=asc&pagina=3',
    );
    expect(deQueryString(query)).toEqual({
      ...filtros,
      status: [...filtros.status],
      prioridade: [...filtros.prioridade],
    });
  });

  it('usa os padrões do contrato e deixa a URL vazia quando nada foi escolhido', () => {
    expect(deQueryString(new URLSearchParams())).toEqual(FILTROS_PADRAO);
    expect(paraQueryString(FILTROS_PADRAO).toString()).toBe('');
  });

  it('ignora valores inválidos de um link editado à mão', () => {
    const filtros = deQueryString(
      new URLSearchParams(
        'status=Voando&status=Aberto&status=Aberto&prioridade=urgente&categoriaId=abc&categoriaId=-1' +
          '&categoriaId=3&criadoDe=01/09/2026&ordenarPor=titulo&direcao=cima&pagina=0',
      ),
    );

    expect(filtros).toEqual({ ...FILTROS_PADRAO, status: ['Aberto'], categoriaId: [3] });
  });

  it('não envia busca com menos de 3 caracteres', () => {
    expect(paraQueryString({ ...FILTROS_PADRAO, q: 'bo' }).has('q')).toBe(false);
    expect(paraQueryString({ ...FILTROS_PADRAO, q: '  bol  ' }).get('q')).toBe('bol');
  });
});
