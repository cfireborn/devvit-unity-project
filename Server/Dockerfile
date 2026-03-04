FROM ubuntu:22.04

ARG DEBIAN_FRONTEND=noninteractive
ARG SERVER_BUILD_PATH=Builds/EdgegapServer

COPY ${SERVER_BUILD_PATH} /root/build/
COPY Server/nginx.conf /etc/nginx/nginx.conf
COPY Server/start.sh   /root/start.sh

WORKDIR /root/

RUN chmod +x /root/build/ServerBuild /root/start.sh

RUN apt-get update && \
    apt-get install -y ca-certificates libglu1-mesa nginx openssl && \
    apt-get clean && \
    update-ca-certificates

# Generate a self-signed cert for WSS termination.
# Browsers accept self-signed certs for WebSocket connections (unlike HTTPS page loads).
RUN mkdir -p /etc/nginx/certs && \
    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
        -keyout /etc/nginx/certs/server.key \
        -out    /etc/nginx/certs/server.crt \
        -subj   "/CN=compersion-server"

# 7770/udp — Tugboat (UDP, editor/standalone clients)
# 7772/tcp — nginx WSS proxy → Bayou (WebGL clients)
# 7771 is Bayou plain ws, internal only — not exposed
EXPOSE 7770/udp
EXPOSE 7772/tcp

CMD ["/root/start.sh"]
