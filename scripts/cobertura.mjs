#!/usr/bin/env node
// Une os relatórios Cobertura dos projetos de teste do backend (unitários, integração e arquitetura) e imprime a
// cobertura de linhas combinada: uma linha conta como coberta se qualquer suíte passou por ela. O Coverlet gera um
// relatório por projeto de teste e não os junta; sem isso, cada número mostraria só uma parte do quadro.
//
// Uso: node scripts/cobertura.mjs <pasta com os coverage.cobertura*.xml> [--markdown]
// Sem dependências: o formato Cobertura é simples o bastante para ler com expressões regulares.

import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const [pasta, ...opcoes] = process.argv.slice(2);
if (!pasta) {
  console.error('Uso: node scripts/cobertura.mjs <pasta com os coverage.cobertura*.xml> [--markdown]');
  process.exit(2);
}

const arquivos = readdirSync(pasta, { recursive: true })
  .map(String)
  .filter((nome) => /coverage\.cobertura.*\.xml$/.test(nome))
  .map((nome) => join(pasta, nome));
if (arquivos.length === 0) {
  console.error(`Nenhum relatório Cobertura em ${pasta}.`);
  process.exit(1);
}

// projeto → arquivo de código → linha → coberta?
const linhas = new Map();
for (const arquivo of arquivos) {
  const xml = readFileSync(arquivo, 'utf8');
  // Cada relatório tem a sua raiz (<source>): o mesmo arquivo pode vir como "src/X.cs" num e "X.cs" noutro.
  const raiz = (xml.match(/<source>([^<]*)<\/source>/)?.[1] ?? '').replaceAll('\\', '/');
  for (const pacote of xml.matchAll(/<package name="([^"]+)"[\s\S]*?<\/package>/g)) {
    const projeto = pacote[1];
    if (!linhas.has(projeto)) linhas.set(projeto, new Map());
    const doProjeto = linhas.get(projeto);
    for (const classe of pacote[0].matchAll(/<class [^>]*filename="([^"]+)"[\s\S]*?<\/class>/g)) {
      const fonte = (raiz + classe[1]).replaceAll('\\', '/').replace(/\/{2,}/g, '/');
      // Código gerado na compilação (LoggerMessage.g.cs): não é nosso, não entra na conta.
      if (fonte.includes('/obj/')) continue;
      if (!doProjeto.has(fonte)) doProjeto.set(fonte, new Map());
      const doArquivo = doProjeto.get(fonte);
      // Só as linhas da classe (as de cada método se repetem dentro de <methods>).
      const bloco = classe[0].replace(/<methods>[\s\S]*?<\/methods>/, '');
      for (const linha of bloco.matchAll(/<line number="(\d+)" hits="(\d+)"/g)) {
        const numero = Number(linha[1]);
        doArquivo.set(numero, (doArquivo.get(numero) ?? false) || Number(linha[2]) > 0);
      }
    }
  }
}

const resumo = [...linhas]
  .map(([projeto, fontes]) => {
    let validas = 0;
    let cobertas = 0;
    for (const doArquivo of fontes.values()) {
      for (const coberta of doArquivo.values()) {
        validas++;
        if (coberta) cobertas++;
      }
    }
    return { projeto, validas, cobertas };
  })
  .filter((p) => p.validas > 0)
  .sort((a, b) => a.projeto.localeCompare(b.projeto));

const total = resumo.reduce((t, p) => ({ validas: t.validas + p.validas, cobertas: t.cobertas + p.cobertas }), {
  validas: 0,
  cobertas: 0,
});
const pct = (c, v) => (v === 0 ? '—' : `${((100 * c) / v).toFixed(1)}%`);

if (opcoes.includes('--markdown')) {
  console.log('| Projeto | Linhas cobertas | Cobertura |');
  console.log('|---|---:|---:|');
  for (const p of resumo) console.log(`| ${p.projeto} | ${p.cobertas}/${p.validas} | ${pct(p.cobertas, p.validas)} |`);
  console.log(`| **Total (${arquivos.length} relatórios unidos)** | **${total.cobertas}/${total.validas}** | **${pct(total.cobertas, total.validas)}** |`);
} else {
  for (const p of resumo) {
    console.log(`${p.projeto.padEnd(28)} ${String(p.cobertas).padStart(5)}/${String(p.validas).padEnd(5)} ${pct(p.cobertas, p.validas).padStart(6)}`);
  }
  console.log(`${'Total'.padEnd(28)} ${String(total.cobertas).padStart(5)}/${String(total.validas).padEnd(5)} ${pct(total.cobertas, total.validas).padStart(6)}`);
}
