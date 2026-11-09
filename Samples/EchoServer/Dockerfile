FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Samples/EchoServer/EchoServer.csproj", "Samples/EchoServer/"]
COPY ["Samples/Common/Common.csproj", "Samples/Common/"]
COPY ["Core/SP.Core/SP.Core.csproj", "Core/SP.Core/"]
COPY ["Engine/SP.Engine.Common/SP.Engine.Common.csproj", "Engine/SP.Engine.Common/"]
COPY ["Engine/SP.Engine.Runtime/SP.Engine.Runtime.csproj", "Engine/SP.Engine.Runtime/"]
COPY ["Engine/SP.Engine.Client/SP.Engine.Client.csproj", "Engine/SP.Engine.Client/"]
COPY ["Engine/SP.Engine.Server/SP.Engine.Server.csproj", "Engine/SP.Engine.Server/"]
COPY ["Engine/SP.Engine.SourceGenerator/SP.Engine.SourceGenerator.csproj", "Engine/SP.Engine.SourceGenerator/"]

RUN dotnet restore "Samples/EchoServer/EchoServer.csproj"

COPY . .
WORKDIR "/src/Samples/EchoServer"
RUN dotnet publish "EchoServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 10000/tcp
EXPOSE 20000/udp

ENTRYPOINT ["dotnet", "EchoServer.dll"]
CMD ["10000"]