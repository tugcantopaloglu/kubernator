ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled

FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY Directory.Packages.props Directory.Build.props ./
COPY src/Kubernator.Core/ ./src/Kubernator.Core/
COPY src/Kubernator.Runtime/ ./src/Kubernator.Runtime/
COPY src/Kubernator.Web/ ./src/Kubernator.Web/
ARG VERSION=0.0.0-dev
RUN dotnet restore src/Kubernator.Web/Kubernator.Web.csproj
RUN dotnet publish src/Kubernator.Web/Kubernator.Web.csproj \
        -c Release \
        --no-restore \
        -o /publish \
        -p:Version=${VERSION} \
        -p:UseAppHost=false \
        -p:PublishTrimmed=false \
        -p:DebugType=none \
        -p:DebugSymbols=false

FROM ${ASPNET_IMAGE} AS runtime
WORKDIR /app
COPY --from=build --chown=1654:1654 /publish/ /app/
ENV ASPNETCORE_HTTP_PORTS=5050 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0 \
    KUBERNATOR_HOME=/var/lib/kubernator
USER 1654:1654
EXPOSE 5050
VOLUME ["/var/lib/kubernator"]
ENTRYPOINT ["dotnet", "/app/Kubernator.Web.dll"]
CMD ["--bind=0.0.0.0:5050"]
