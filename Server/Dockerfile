FROM ubuntu:22.04

ARG DEBIAN_FRONTEND=noninteractive
ARG SERVER_BUILD_PATH=Builds/EdgegapServer

COPY ${SERVER_BUILD_PATH}              /root/build/
COPY Server/cloudflare-credentials.json /etc/cloudflared/credentials.json
COPY Server/cloudflare-tunnel.yml       /etc/cloudflared/config.yml
COPY Server/start.sh                    /root/start.sh

WORKDIR /root/

RUN chmod +x /root/build/ServerBuild /root/start.sh

RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates curl && \
    apt-get clean && \
    update-ca-certificates

# Install cloudflared (provides WSS via Cloudflare Tunnel — no cert management needed)
RUN curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64 \
        -o /usr/local/bin/cloudflared && \
    chmod +x /usr/local/bin/cloudflared

# 7777/udp — Tugboat (UDP, editor/standalone clients)
# WebSocket (Bayou) goes through Cloudflare Tunnel — no inbound TCP port needed
EXPOSE 7777/udp

CMD ["/root/start.sh"]
