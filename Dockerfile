FROM mcr.microsoft.com/dotnet/sdk:9.0.1 AS build
WORKDIR /app

# Copiar archivo del proyecto desde la subcarpeta
COPY AcademiaNovit/*.csproj ./AcademiaNovit/
WORKDIR /app/AcademiaNovit
RUN dotnet restore

# Copiar el resto del código fuente
WORKDIR /app
COPY AcademiaNovit/ ./AcademiaNovit/
WORKDIR /app/AcademiaNovit

# Compilar la aplicación
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0.1 AS runtime
WORKDIR /app

# Copiar la aplicación compilada
COPY --from=build /app/publish .

# Crear directorio para logs
RUN mkdir -p /app/Logs

# Crear usuario no-root para seguridad
RUN groupadd -r appuser && useradd -r -g appuser appuser
RUN chown -R appuser:appuser /app
USER appuser

# Exponer puerto
EXPOSE 8080

# Configurar variables de entorno
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Comando para ejecutar la aplicación
ENTRYPOINT ["dotnet", "AcademiaNovit.dll"]