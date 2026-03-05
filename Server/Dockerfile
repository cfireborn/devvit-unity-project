FROM ubuntu:22.04

ARG DEBIAN_FRONTEND=noninteractive
ARG SERVER_BUILD_PATH=Builds/EdgegapServer

COPY ${SERVER_BUILD_PATH} /root/build/
COPY Server/stunnel.conf /etc/stunnel/stunnel.conf
COPY Server/start.sh     /root/start.sh

WORKDIR /root/

RUN chmod +x /root/build/ServerBuild /root/start.sh

RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates stunnel4 openssl && \
    apt-get clean && \
    update-ca-certificates

# Generate self-signed cert for WSS termination (combined .pem for stunnel)
RUN mkdir -p /etc/stunnel && \
    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
        -keyout /etc/stunnel/server.key \
        -out    /etc/stunnel/server.crt \
        -subj   "/CN=compersion-server" && \
    cat /etc/stunnel/server.crt /etc/stunnel/server.key > /etc/stunnel/server.pem

# 7770/udp — Tugboat (UDP, editor/standalone clients)
# 7772/tcp — stunnel WSS proxy → Bayou (WebGL clients)
# 7771 is Bayou plain ws, internal only — not exposed
EXPOSE 7777/udp
EXPOSE 7772/tcp

CMD ["/root/start.sh"]
