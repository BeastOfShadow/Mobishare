# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /src

# Copia TUTTO prima del restore per assicurare che la soluzione veda tutti i progetti
COPY . .

# Restore sulla soluzione completa
RUN dotnet restore "Mobishare.sln"

# Compila il progetto che genera codice (source generators) se necessario prima degli altri
RUN dotnet build "Mobishare.Ai/Mobishare.Ai.csproj" -c Release --no-restore
RUN dotnet build "Mobishare.Core/Mobishare.Core.csproj" -c Release --no-restore
RUN dotnet build "Mobishare.Infrastructure/Mobishare.Infrastructure.csproj" -c Release --no-restore
RUN dotnet build "Mobishare.App/Mobishare.App.csproj" -c Release --no-restore

# Compila l'intera soluzione
RUN dotnet build "Mobishare.sln" -c Release -o /app/build --no-restore -v:detailed

FROM build-env AS publish
RUN dotnet publish "Mobishare.App/Mobishare.App.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM publish AS final
WORKDIR /app
# Copia i file pubblicati
COPY --from=publish /app/publish .
# Copia tutti i sorgenti per permettere a dotnet ef di trovare i progetti
# COPY . .
# Aggiungi certificato https
RUN mkdir -p /https
RUN dotnet dev-certs https -ep /https/aspnetapp.pfx -p your_password
RUN dotnet dev-certs https --trust
ENV PATH="$PATH:/root/.dotnet/tools"
EXPOSE 7027
ENTRYPOINT ["dotnet", "Mobishare.App.dll"]
