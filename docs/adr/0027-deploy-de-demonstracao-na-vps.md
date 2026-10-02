# ADR-0027 — Deploy de demonstração na VPS com link público, HTTPS e Gemini real

- **Status:** Aceita
- **Data:** 2026-10-02
- **Fase:** 5 — Entrega (Sprint 8), diferencial de produto
- **Requisitos relacionados:** enunciado §9 (diferencial: deploy em nuvem com link acessível); NFR-06, NFR-08; ADR-0005, ADR-0006, ADR-0021, ADR-0023, ADR-0026

## Contexto

O enunciado do desafio prevê como diferencial (§9) um **deploy em nuvem com link acessível**, permitindo que avaliadores e recrutadores naveguem pelo sistema real sem precisar clonar o repositório ou rodar contêineres na própria máquina.

O projeto já cumpre rigorosamente o requisito de rodar localmente com um comando (`docker compose up --build`), sem arquivo `.env` e com a IA em modo fake determinístico (ADR-0005, ADR-0022). Para a demonstração pública, o objetivo é colocar a aplicação completa no ar com:
1. Um domínio público com certificado HTTPS válido (requisito obrigatório para o cookie de autenticação `Secure`, ADR-0026).
2. O provedor de IA real ativo (**Google Gemini** no plano gratuito, ADR-0006), demonstrando a triagem por IA, o copiloto com streaming SSE e a recuperação semântica vetorial (RAG) em funcionamento real.
3. Segurança dos segredos e isolamento: a chave do Gemini, a chave de assinatura de sessão (`JWT_CHAVE`) e as senhas do banco nunca devem transitar pelo Git ou pelo CI (ADR-0023).
4. Sem custos adicionais de infraestrutura.

## Alternativas consideradas

### A) Deploy na VPS existente (Ubuntu 24.04, 4 GB RAM, 1 vCPU) com subdomínio próprio e Nginx reverso
- O desenvolvedor já possui uma VPS Hostinger KVM 1 ativa (4 GB RAM, 50 GB NVMe, em Campinas/SP com baixa latência para o Brasil), rodando um projeto leve utilizado apenas aos sábados, com ~3 GB de RAM livres e 2% de uso de CPU durante a semana.
- Criação de um registro DNS `A helpdesk` apontando para o IP da VPS, sob o domínio `projetoesperanca.tech`.
- As portas internas do Docker Compose ficam estritamente presas ao `127.0.0.1` (`WEB_PORTA_HOST=127.0.0.1:8085`, `API_PORTA_HOST=127.0.0.1:5080`, `DB_PORTA_HOST=127.0.0.1:55432`), e apenas o Nginx do host expõe as portas 80/443 com certificado Let's Encrypt (Certbot).
- ✅ Custo zero ($0 adicionais).
- ✅ Sem *cold start* ou suspensão: os contêineres ficam ligados e prontos para navegação imediata.
- ✅ Convivência pacífica: o HelpDesk consome ~500 MB de RAM em repouso, deixando mais de 2 GB de folga na VPS.
- ❌ Máquina compartilhada com 1 vCPU: compilação inicial das imagens Docker leva de 10 a 20 minutos.

### B) Plataforma PaaS / Serverless gratuita (Render, Railway, Fly.io)
- Subir a aplicação em serviços gerenciados em nuvem.
- ❌ O HelpDesk Inteligente é composto por 4 contêineres perenes (`db` com extensão pgvector, `migrator` one-shot, `api`, `worker` e `web`). Planos gratuitos de PaaS impõem limites severos de memória (geralmente 512 MB por projeto total) e cobram por múltiplos serviços e volumes persistentes.
- ❌ Suspensão por inatividade (*cold start* de 30 a 60 segundos), prejudicando a experiência de avaliação do recrutador.

### C) Contratar uma nova VPS dedicada exclusiva
- Contratar uma segunda VPS (ex: Hetzner, DigitalOcean ou Hostinger) isolada.
- ✅ Isolamento absoluto de processos.
- ❌ Custo financeiro redundante e desnecessário para uma demonstração temporária de portfólio.

## Decisão

Escolhemos a **Alternativa A**: deploy de demonstração na VPS existente, com as seguintes diretrizes:

1. **Domínio e HTTPS:** subdomínio `helpdesk.projetoesperanca.tech` com terminação TLS gerenciada pelo Nginx do host e certificado Let's Encrypt gratuito renovado via Certbot.
2. **Isolamento de rede:** nenhuma porta de serviço interno (Postgres, API, Web Nginx interno) é exposta externamente na interface pública. As variáveis de porta do Compose são vinculadas exclusivamente a `127.0.0.1`.
3. **Segredos exclusivos no `.env` da VPS:** `LLM_API_KEY`, `JWT_CHAVE` (48 bytes base64) e `POSTGRES_PASSWORD` gerados diretamente na VPS com permissão `600`, fora do controle de versão e do CI (ADR-0023).
4. **Provedor de IA real:** Gemini 2.5 Flash Lite via interface compatível com OpenAI (`LLM_PROVIDER=openai-compatible`, endpoint Google AI Studio), com modelo de embeddings `gemini-embedding-001`.
5. **Proteção de taxa (Rate Limit):** `COPILOTO_RATE_LIMIT_POR_MINUTO=10` para conter abusos e proteger a cota gratuita do Gemini (15 RPM / limite diário).
6. **Invariância do ambiente local e CI:** o repositório principal continua subindo sem `.env` e com a IA fake no `docker compose up --build` local e no GitHub Actions.

## Riscos aceitos

- **Cota do free-tier do Gemini:** se múltiplos recrutadores testarem o copiloto simultaneamente, pode ocorrer retorno de 429. O cliente do copiloto já implementa retry com backoff exponencial (ADR-0024) e o rate limit protege contra esgotamento súbito.
- **Pico de consumo aos sábados:** o outro projeto da VPS tem atividade aos sábados. Como o HelpDesk consome ~500 MB e a VPS tem 4 GB com swap de segurança, o risco de OOM é baixo.

## Consequências e Gatilho de reavaliação

- O link `https://helpdesk.projetoesperanca.tech` passa a ser documentado no topo do `README.md` como demonstração oficial em nuvem.
- O guia operacional completo de deploy fica documentado em `docs/deploy-vps.md`.
- **Gatilho de reavaliação:** se a VPS atingir > 85% de RAM sustentada ou se o HelpDesk necessitar de uso comercial contínuo além do período de avaliação, provisionar uma máquina dedicada com pipeline de CI/CD automatizado via SSH/Webhooks.
