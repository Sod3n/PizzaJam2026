FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

# Copy everything and publish
COPY . .
RUN dotnet publish Template.Server/Template.Server.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /app/publish

# Install .NET diagnostic tools into an isolated folder so we can COPY them into the
# runtime image without dragging in the full SDK.
RUN dotnet tool install dotnet-trace --tool-path /app/diag \
    && dotnet tool install dotnet-counters --tool-path /app/diag \
    && dotnet tool install dotnet-gcdump --tool-path /app/diag

FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-amd64 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/diag /usr/local/dotnet-tools
ENV PATH="/usr/local/dotnet-tools:${PATH}"

# HTTP (SignalR/controllers)
EXPOSE 8080
# UDP (LiteNetLib)
EXPOSE 9050/udp

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Template.Server.dll"]
