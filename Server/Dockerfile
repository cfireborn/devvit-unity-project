FROM cloudflare/cloudflared:2026.7.3 AS cloudflared

FROM ubuntu:22.04

ARG DEBIAN_FRONTEND=noninteractive
ARG SERVER_BUILD_PATH=Builds/EdgegapServer

COPY ${SERVER_BUILD_PATH} /opt/compersion/
COPY Server/start.sh /usr/local/bin/compersion-start
COPY --from=cloudflared /usr/local/bin/cloudflared /usr/local/bin/cloudflared

WORKDIR /opt/compersion

RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates util-linux && \
    rm -rf /var/lib/apt/lists/* && \
    groupadd --system tunnel && useradd --system --gid tunnel --no-create-home tunnel && \
    groupadd --system gameserver && useradd --system --gid gameserver --home-dir /var/lib/compersion --create-home gameserver && \
    chown -R gameserver:gameserver /opt/compersion /var/lib/compersion && \
    chmod 0555 /opt/compersion/ServerBuild /usr/local/bin/compersion-start /usr/local/bin/cloudflared

# 7777/udp — Tugboat (UDP, editor/standalone clients)
# WebSocket (Bayou) goes through Cloudflare Tunnel — no inbound TCP port needed
EXPOSE 7777/udp

CMD ["/usr/local/bin/compersion-start"]
