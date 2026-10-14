FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

# Install OpenSSH server, cron, networking tools, docker tools, and runtime utilities
RUN apt-get update && apt-get install -y \
    openssh-server \
    cron \
    curl \
    wget \
    rsync \
    tar \
    gzip \
    unzip \
    git \
    sudo \
    procps \
    ca-certificates \
    python3 \
    python3-pip \
    docker.io \
    docker-compose-v2 \
    postgresql-client \
    sqlite3 \
    net-tools \
    iproute2 \
    nano \
    && rm -rf /var/lib/apt/lists/*

# Create SSH run directory and standard project folders
RUN mkdir -p /var/run/sshd /var/backups/vpsmanager /var/www/my_app /var/www/uploads

# Set root password to 'rootpass'
RUN echo 'root:rootpass' | chpasswd

# Create 'deploy' user with sudo access and password 'deploypass'
RUN useradd -rm -d /home/deploy -s /bin/bash -g root -G sudo deploy && \
    echo 'deploy:deploypass' | chpasswd && \
    echo 'deploy ALL=(ALL) NOPASSWD:ALL' >> /etc/sudoers

# Configure SSH daemon: allow root login, password authentication, and keepalive
RUN sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin yes/' /etc/ssh/sshd_config && \
    sed -i 's/#PasswordAuthentication yes/PasswordAuthentication yes/' /etc/ssh/sshd_config && \
    sed -i 's/PasswordAuthentication no/PasswordAuthentication yes/' /etc/ssh/sshd_config && \
    sed -i 's/#TCPKeepAlive yes/TCPKeepAlive yes/' /etc/ssh/sshd_config && \
    echo "ClientAliveInterval 30" >> /etc/ssh/sshd_config && \
    echo "ClientAliveCountMax 5" >> /etc/ssh/sshd_config

# Copy entrypoint script
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Expose SSH and standard web ports
EXPOSE 22 80 443 8000

ENTRYPOINT ["/entrypoint.sh"]
