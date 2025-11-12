# Используем официальный образ .NET 8 SDK для сборки
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копируем файл проекта и восстанавливаем зависимости
COPY *.csproj ./
RUN dotnet restore

# Копируем остальные файлы и собираем приложение
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Используем легковесный runtime образ
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Создаем директорию для данных
RUN mkdir -p /app/data

# Копируем собранное приложение
COPY --from=build /app/publish .

# Устанавливаем переменные окружения для оптимизации
# PORT будет установлен Render автоматически
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_GCHeapHardLimit=400000000
ENV ASPNETCORE_ENVIRONMENT=Production

# Открываем порт (Render использует переменную PORT)
EXPOSE 10000

# Запускаем приложение
ENTRYPOINT ["dotnet", "SteamAnaliticWorker.dll"]

