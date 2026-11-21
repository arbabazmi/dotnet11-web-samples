# Build context: repository root.
#   docker build -f bench/Dockerfile --target jit -t aotbench:C --build-arg DOTNET=11.0 --build-arg TFM=net11.0 .
#   docker build -f bench/Dockerfile --target aot -t aotbench:D --build-arg DOTNET=11.0 --build-arg TFM=net11.0 .
ARG DOTNET=11.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET} AS build-jit
ARG TFM=net11.0
WORKDIR /src
COPY src/Aot.Bench/ .
RUN dotnet publish -f ${TFM} -c Release -o /out

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET} AS build-aot
ARG TFM=net11.0
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY src/Aot.Bench/ .
# Publish output (including compiler warnings) is kept in /out/publish.log for the results write-up.
RUN dotnet publish -f ${TFM} -c Release -p:Aot=true -o /out 2>&1 | tee /publish.log

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET} AS jit
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build-jit /out /app
WORKDIR /app
ENTRYPOINT ["dotnet", "Aot.Bench.dll"]

FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET} AS aot
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build-aot /out /app
WORKDIR /app
ENTRYPOINT ["./Aot.Bench"]
