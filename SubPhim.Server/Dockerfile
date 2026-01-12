# Dockerfile
# Giai đoạn 1: Lấy base image của ASP.NET Core Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
# Giai đoạn 2: Sử dụng SDK image để build ứng dụng
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy file project và restore các gói NuGet để tận dụng cache
COPY ["SubPhim.Server.csproj", "."]
RUN dotnet restore "./SubPhim.Server.csproj"

# Copy toàn bộ code còn lại và build ứng dụng
COPY . .
WORKDIR "/src/."
RUN dotnet build "SubPhim.Server.csproj" -c Release -o /app/build

# Giai đoạn 3: Publish ứng dụng
FROM build AS publish
RUN dotnet publish "SubPhim.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Giai đoạn 4: Tạo image cuối cùng, chỉ chứa những gì cần thiết để chạy
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Lệnh để khởi động ứng dụng khi container chạy
ENTRYPOINT ["dotnet", "SubPhim.Server.dll"]