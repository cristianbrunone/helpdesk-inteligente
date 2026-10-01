# ADR-0011 — Estratégia de embeddings: documentos por origem, dimensão fixa e reconciliação por modelo

- **Status:** Aceita (validada na PoC de 2026-10-01; ver "Resultado da PoC")
- **Data:** 2026-09-30
- **Fase:** 3 — Modelagem de dados
- **Requisitos relacionados:** RF-15, RF-16, RF-21, RF-31, RN-11, NFR-06, NFR-10, NFR-12

## Contexto

Precisamos decidir **o que** vira vetor, **em que unidade** (chunk), **com qual dimensão** e **como sobreviver à troca de modelo**. Há três restrições fortes:

- Só texto **mascarado** pode ser vetorizado (RN-11).
- O fake e o Gemini precisam **caber na mesma coluna**, porque o pgvector fixa a dimensão por coluna (ADR-0007).
- Os testes precisam de embeddings determinísticos e sem chave (NFR-10).

## Alternativas consideradas

### A) Colunas de embedding nas próprias tabelas (`chamados.embedding`, `artigos.embedding`)
- ✅ É simples: nenhuma tabela nova, e o vetor fica junto do dado.
- ❌ Não suporta chunking. Um artigo longo vira um único vetor "médio" e perde precisão.
- ❌ Duas buscas separadas (chamados e artigos) que precisam ser mescladas na aplicação.
- ❌ Os metadados de indexação (modelo, hash, data) se espalham pelas tabelas de negócio.
- ❌ Com um vetor por linha de chamado, ainda seria necessário garantir que só os resolvidos entram na busca.

### B) Tabela única `documentos_rag` (um chunk por linha), com FK para a origem
- ✅ **Uma busca unificada** para chamados e artigos, com filtro por tipo quando necessário.
- ✅ Suporta vários chunks por artigo.
- ✅ Os metadados de indexação (`embedding_modelo`, `hash_conteudo`, `indexado_em`) ficam num lugar só. Isso é o que viabiliza a reconciliação do ADR-0010.
- ✅ O **conteúdo mascarado fica armazenado** junto com o vetor. É exatamente o que vai para o prompt, então não é preciso mascarar de novo na leitura, e é auditável.
- ✅ A integridade é garantida por **duas FKs anuláveis** (`chamado_id`, `artigo_id`) com `CHECK` de exatamente uma preenchida e `ON DELETE CASCADE`.
- ❌ O texto fica duplicado (a origem + a versão mascarada). O volume é pequeno.

## Decisão

Escolhemos **B**, com estas regras:

| Aspecto | Regra |
|---|---|
| **Chamado resolvido** | 1 documento = título + descrição + comentários (incluindo o de resolução), **mascarados**, e truncados em ~2.000 caracteres priorizando a descrição e o último comentário. |
| **Artigo** | Chunk por seção (títulos Markdown), com limite de ~1.500 caracteres e sobreposição de 1 parágrafo entre chunks consecutivos. |
| **Dimensão** | Fixa por configuração (`EMBEDDING_DIMENSIONS`, padrão **768**). O fake gera 768. No provedor real, pede-se 768 via redução de dimensão (os modelos de embedding do Gemini suportam dimensionalidade de saída configurável). |
| **Normalização** | Os vetores são normalizados (norma L2 = 1) antes de gravar. A distância é cosseno. |
| **Fake determinístico** | Hash dos tokens (feature hashing) → vetor esparso normalizado. Textos com palavras em comum ficam próximos. Isso permite testar a recuperação *de verdade* sem chave de API. |
| **Troca de modelo** | O reconciliador reindexa todo documento com `embedding_modelo` diferente do configurado (ADR-0010). Uma dimensão diferente de 768 exige uma migration (documentada no README). |
| **Recuperação** | Top-k configurável (`RAG_TOP_K`, padrão 3 chamados + 3 trechos de artigo), com limiar mínimo de similaridade (`RAG_MIN_SIMILARITY`). Abaixo do limiar, o documento não entra no prompt. |

## Trade-offs aceitos

- Fixar 768 dimensões abre mão da precisão máxima do modelo real. Em troca, o fake e o real ficam compatíveis e o índice fica menor.
- O fake por feature hashing captura só a sobreposição lexical, não a semântica. É suficiente para testar o pipeline, não para avaliar a qualidade do RAG.
- Tanto os documentos misturados de modelos diferentes quanto a busca durante uma reindexação são tratados filtrando por `embedding_modelo = modelo atual`.

## Consequências

- A busca sempre filtra por `embedding_modelo = @modeloAtual`. Assim, vetores de modelos diferentes nunca são comparados entre si.
- **PoC na Fase 4:** confirmar que o endpoint compatível com OpenAI aceita o parâmetro `dimensions` para o modelo de embedding do Gemini. Se não aceitar, o plano B é truncar e renormalizar o vetor no adaptador (válido para modelos treinados com Matryoshka) ou usar a API nativa para embeddings.
- As fontes recuperadas (tipo, id, similaridade) são gravadas em `triagens_ia.fontes` (jsonb) e exibidas no painel da IA (RF-16).
- **Gatilho de reavaliação:** qualidade de recuperação baixa (medida pela taxa de rejeição por categoria). Nesse caso, avaliar chunking semântico, busca híbrida ou re-ranking.

## Resultado da PoC (Sprint 0, 2026-10-01)

Teste `Embeddings_ComDimensions768_RetornaVetorDe768Posicoes` (`Category=ProvedorReal`), com o `gemini-embedding-001` pelo endpoint OpenAI-compatível:

- **`dimensions = 768` é respeitado:** o vetor vem com exatamente 768 posições, em 0,3 a 0,8 s. O plano B (truncar no adaptador ou usar a API nativa) **não é necessário**.
- **O vetor reduzido não vem normalizado** (norma L2 ≈ 0,588). A regra "normalizar antes de gravar" desta decisão é, portanto, **obrigatória** no adaptador real, e não só no fake. Um teste unitário da Sprint 3 garante norma 1.
- **Cota do free tier:** 100 RPM / 1.000 RPD para o Gemini Embedding 1. Folga suficiente para indexar o seed (~200 chamados resolvidos + artigos) e para o embedding da pergunta em cada triagem e no copiloto.
