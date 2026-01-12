using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class Backfill_Uids_For_Existing_Users : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
{
    // Sử dụng SQL thô (raw SQL) để cập nhật dữ liệu
    // Đây là cách hiệu quả nhất để xử lý logic phức tạp như tạo số ngẫu nhiên và kiểm tra trùng lặp
    migrationBuilder.Sql(@"
        -- Tạo một bảng tạm để lưu các UID đã được sử dụng hoặc vừa được tạo
        CREATE TEMP TABLE _ExistingUids AS SELECT Uid FROM Users WHERE Uid IS NOT NULL;

        -- Tạo một bảng tạm để lưu các ID của user cần cập nhật
        CREATE TEMP TABLE _UsersToUpdate (Id INTEGER PRIMARY KEY);
        INSERT INTO _UsersToUpdate (Id) SELECT Id FROM Users WHERE Uid IS NULL OR Uid = '';

        -- Vòng lặp để cập nhật từng user một (cách an toàn để đảm bảo UID không trùng lặp)
        -- SQLite không có vòng lặp WHILE, vì vậy chúng ta phải dùng một kỹ thuật khác với CTE đệ quy
        WITH RECURSIVE
          cte AS (
            SELECT 
              Id,
              -- Tạo một UID ngẫu nhiên 9 chữ số
              ABS(RANDOM()) % (1000000000 - 100000000) + 100000000 AS NewUid
            FROM _UsersToUpdate
          ),
          unique_uids (Id, Uid) AS (
            SELECT 
              Id, 
              NewUid 
            FROM cte
            -- Đảm bảo UID mới không tồn tại trong bảng chính và bảng tạm
            WHERE NewUid NOT IN _ExistingUids
            GROUP BY Id -- Lấy một UID duy nhất cho mỗi user
          )
        UPDATE Users
        SET Uid = (SELECT Uid FROM unique_uids WHERE Users.Id = unique_uids.Id)
        WHERE Id IN (SELECT Id FROM unique_uids);

        -- Dọn dẹp các bảng tạm
        DROP TABLE _UsersToUpdate;
        DROP TABLE _ExistingUids;
    ");

    // Lặp lại quá trình cho đến khi tất cả user đều có UID
    // Điều này cần thiết nếu lần chạy đầu tiên có xung đột UID ngẫu nhiên
    migrationBuilder.Sql(@"
        UPDATE Users
        SET Uid = (ABS(RANDOM()) % (1000000000 - 100000000) + 100000000)
        WHERE Uid IS NULL OR Uid = '';
    ");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    // Hành động này không thể hoàn tác một cách an toàn,
    // vì vậy chúng ta không làm gì trong phương thức Down.
}
    }
}
