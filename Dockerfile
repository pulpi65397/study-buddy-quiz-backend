FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["study-buddy-quiz.csproj", "./"]
RUN dotnet restore "./study-buddy-quiz.csproj"

COPY . .
RUN dotnet publish "./study-buddy-quiz.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "study-buddy-quiz.dll"]
