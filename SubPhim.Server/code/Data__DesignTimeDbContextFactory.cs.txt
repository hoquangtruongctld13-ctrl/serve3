using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SubPhim.Server.Data;

namespace SubPhim.Server.Data
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            // Lấy đường dẫn tới thư mục project hiện tại
            var basePath = Directory.GetCurrentDirectory();

            // Xây dựng đối tượng configuration để đọc appsettings.json
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)
                .Build();

            // Tạo một DbContextOptionsBuilder
            var builder = new DbContextOptionsBuilder<AppDbContext>();

            // Đọc chuỗi kết nối từ configuration
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // Cấu hình cho builder sử dụng Sqlite với chuỗi kết nối vừa lấy
            builder.UseSqlite(connectionString);

            // Trả về một instance của AppDbContext với options đã được cấu hình
            return new AppDbContext(builder.Options);
        }
    }
}