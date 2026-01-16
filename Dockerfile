# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy toàn bộ source code vào
COPY . .

# Restore và Publish (Build ra file chạy)
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime (Chạy ứng dụng)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Copy file đã build từ Stage 1 sang
COPY --from=build /app/publish .

# Tạo thư mục dữ liệu để lưu DB và Key
RUN mkdir -p /data/keys
RUN mkdir -p /data/TempUploads

# Biến môi trường quan trọng để kích hoạt logic lưu DB vào /data/subphim.db
# (Dựa trên logic trong file Program.cs: if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLY_APP_NAME"))))
ENV FLY_APP_NAME="SubPhimServer"
ENV ASPNETCORE_URLS="http://+:8080"

# Mở port 8080 trong container
EXPOSE 8080

# Chạy ứng dụng (Tên DLL dựa trên namespace trong code bạn gửi)
ENTRYPOINT ["dotnet", "SubPhim.Server.dll"]
