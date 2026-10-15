FROM mcr.microsoft.com/devcontainers/dotnet:10.0

ARG NODE_VERSION=24
RUN . /usr/local/share/nvm/nvm.sh && nvm install ${NODE_VERSION} && nvm alias default ${NODE_VERSION} \
    && npm install -g npm-upgrade eslint

WORKDIR /workspace
