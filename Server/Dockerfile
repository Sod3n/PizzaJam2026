FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-amd64 AS runtime
WORKDIR /app
COPY publish/ .

# HTTP (SignalR/controllers)
EXPOSE 8080
# UDP (LiteNetLib)
EXPOSE 9050/udp

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Template.Server.dll"]
