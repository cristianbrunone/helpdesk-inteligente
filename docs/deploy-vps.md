# Deploy de demonstração na VPS

Guia para publicar o HelpDesk Inteligente em `https://helpdesk.projetoesperanca.tech`, numa VPS que já roda outro projeto, com o Gemini (plano gratuito) como provedor real de IA.

**Premissas:** VPS Linux com Docker, 4 GB de RAM e 1 vCPU; o registro DNS `A helpdesk → IP da VPS` já existe; o sistema fica ligado só durante a avaliação.

**Regras:** a chave do Gemini, a `JWT_CHAVE` e a senha do banco ficam só no `.env` da VPS, nunca no Git, em print ou em mensagem.

---

## 1. Conferir a VPS (só leitura)

```bash
ssh root@helpdesk.projetoesperanca.tech

docker --version && docker compose version   # Docker 24+ e Compose v2.24+
free -h && df -h /                           # memória e disco livres
ss -tlnp | grep -E ':(80|443|8085|5080|55432)\b'   # quem já usa as portas
docker ps --format '{{.Names}}\t{{.Ports}}'  # contêineres do outro projeto
nginx -v 2>&1; systemctl is-active nginx     # existe Nginx no host?
```

Anote:

- **Quem ocupa as portas 80 e 443.** Se for o Nginx do host, siga a seção 5A. Se for um contêiner (Traefik, Caddy, nginx-proxy...), siga a 5B. Se estiverem livres, a 5A também serve (instalando o Nginx).
- **Se 8085, 5080 ou 55432 estão em uso.** Se estiverem, troque os números no `.env` (seção 3).

Sem Docker: `curl -fsSL https://get.docker.com | sh`.

## 2. Clonar o projeto

```bash
cd /opt
git clone https://github.com/cristianbrunone/helpdesk-inteligente.git
cd helpdesk-inteligente
git checkout v1.2.0      # ou a tag mais recente
```

## 3. Criar o `.env` da VPS

```bash
cp .env.example .env
chmod 600 .env
nano .env
```

Altere só estas linhas (o resto fica como está):

```dotenv
# Banco: senha forte, só para esta VPS
POSTGRES_PASSWORD=<gere com: openssl rand -base64 24>

# Portas presas ao localhost: nada fica aberto para a internet além do Nginx do host.
# (O Docker ignora o firewall do Linux ao publicar portas; por isso o 127.0.0.1 aqui.)
WEB_PORTA_HOST=127.0.0.1:8085
API_PORTA_HOST=127.0.0.1:5080
DB_PORTA_HOST=127.0.0.1:55432

# IA real (Gemini, plano gratuito)
LLM_PROVIDER=openai-compatible
LLM_BASE_URL=https://generativelanguage.googleapis.com/v1beta/openai/
LLM_API_KEY=<sua chave do Google AI Studio>
LLM_CHAT_MODEL=gemini-3.5-flash-lite
LLM_EMBEDDING_MODEL=gemini-embedding-001

# Sessão: chave fixa (sem ela, todos caem a cada reinício da API)
JWT_CHAVE=<gere com: openssl rand -base64 48>
SESSAO_COOKIE_SEGURO=true

# Copiloto: limite por minuto (atrás de dois proxies, vale para todos os usuários juntos)
COPILOTO_RATE_LIMIT_POR_MINUTO=10
```

Confira sem mostrar os segredos:

```bash
grep -E '^(LLM_PROVIDER|LLM_BASE_URL|LLM_CHAT_MODEL|WEB_PORTA_HOST|API_PORTA_HOST|DB_PORTA_HOST)=' .env
grep -cE '^(LLM_API_KEY|JWT_CHAVE|POSTGRES_PASSWORD)=.+' .env    # deve mostrar 3
```

## 4. Subir

```bash
docker compose up --build -d --wait --wait-timeout 1200
docker compose ps
curl -s http://127.0.0.1:5080/health          # API saudável
curl -sI http://127.0.0.1:8085 | head -1       # front respondendo (200)
```

- O **primeiro build** com 1 vCPU compila duas imagens .NET e o front: conte **10 a 20 minutos**. Os próximos são rápidos.
- Se o build morrer por falta de memória (`Killed`, código 137), crie um swap de 2 GB e repita:
  ```bash
  fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  ```
- Na **primeira subida**, o Worker gera os embeddings de todo o seed com o Gemini (o reconciliador do RAG). Leva alguns minutos; um 429 do plano gratuito é tratado com novas tentativas. Acompanhe com `docker compose logs -f worker` até as mensagens de indexação pararem.

## 5. HTTPS com o domínio

O cookie da sessão é `Secure`: **sem HTTPS, o login não funciona.**

### 5A. Nginx no host (o caso mais provável)

```bash
apt install -y nginx certbot python3-certbot-nginx   # se ainda não houver
nano /etc/nginx/sites-available/helpdesk
```

```nginx
server {
    listen 80;
    server_name helpdesk.projetoesperanca.tech;

    client_max_body_size 1m;

    # Copiloto: resposta em streaming (SSE), sem buffer.
    location ~ ^/api/chamados/[^/]+/copiloto$ {
        proxy_pass http://127.0.0.1:8085;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 300s;
        gzip off;
    }

    # API sem compressão: comprimir troca o ETag forte por um fraco, e toda escrita passaria a dar 412.
    location /api/ {
        proxy_pass http://127.0.0.1:8085;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        gzip off;
    }

    location / {
        proxy_pass http://127.0.0.1:8085;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

```bash
ln -s /etc/nginx/sites-available/helpdesk /etc/nginx/sites-enabled/helpdesk
nginx -t && systemctl reload nginx            # nginx -t não pode dar erro: o outro projeto depende dele
certbot --nginx -d helpdesk.projetoesperanca.tech --redirect
```

O Certbot acrescenta o bloco HTTPS e renova o certificado sozinho.

### 5B. Proxy em contêiner (Traefik, Caddy, nginx-proxy)

Aponte o host `helpdesk.projetoesperanca.tech` para `127.0.0.1:8085` (ou ligue o contêiner `web` à rede do proxy) seguindo as regras do proxy que já existe. Mantenha as mesmas três regras do bloco acima: **sem buffer no copiloto, sem compressão em `/api/`, e `X-Forwarded-Proto`.**

## 6. Testar

1. Abra `https://helpdesk.projetoesperanca.tech` (cadeado válido) e entre como `ana.suporte@example.com`.
2. Abra um chamado novo: a **triagem** deve aparecer em segundos, com `gemini-3.5-flash-lite` no rodapé do painel (e não `fake-triagem-v1`).
3. No detalhe, pergunte ao **copiloto** "Já tivemos casos parecidos?": a resposta chega aos poucos (streaming) e cita chamados.
4. Mude o status de um chamado: se der **412**, a compressão está ligada em `/api/` (revise a seção 5).
5. Entre como `marina.costa@example.com` e confira "Meus chamados".
6. De fora da VPS, as portas internas devem estar fechadas:
   ```bash
   nc -zv helpdesk.projetoesperanca.tech 5080 55432 8085   # todas devem falhar
   ```

## 7. Durante e depois da avaliação

```bash
docker compose stop          # desliga (os dados ficam)
docker compose start         # liga de novo
docker compose logs -f api worker   # acompanhar (os logs não têm texto de chamado nem de prompt)

# Desligar a IA sem derrubar o sistema (kill switch, ADR-0021): edite o .env e recrie
#   IA_TRIAGEM_HABILITADA=false / IA_COPILOTO_HABILITADO=false
docker compose up -d

# Remover tudo ao final (apaga o banco)
docker compose down -v
rm /etc/nginx/sites-enabled/helpdesk && systemctl reload nginx
```

## Observações

- **Plano gratuito do Gemini:** limite diário de requisições (o `.env.example` registra 500 por dia para o modelo de chat) e o Google pode usar o conteúdo enviado para melhorar os produtos dele. Os dados do seed são fictícios, e o mascaramento remove nome e e-mail do solicitante antes de qualquer chamada.
- **Credenciais:** o usuário e a senha de demonstração vão para o recrutador por mensagem, junto com o link.
- **Atualizar a versão:** `git fetch --tags && git checkout <tag> && docker compose up --build -d --wait`.
