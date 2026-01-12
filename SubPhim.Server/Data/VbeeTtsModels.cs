// VỊ TRÍ: Data/VbeeTtsModels.cs
// Models cho hệ thống Vbee TTS với obfuscation và quota management

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubPhim.Server.Data
{
    /// <summary>
    /// Quản lý danh sách voices Vbee TTS
    /// </summary>
    public class VbeeVoice
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Voice ID (Internal)")]
        public string VoiceCode { get; set; } // Ví dụ: vi_quynhanh_review

        [Required]
        [StringLength(200)]
        [Display(Name = "Tên hiển thị")]
        public string DisplayName { get; set; } // Ví dụ: Ngọc Huyền (Hà Nội)

        [StringLength(20)]
        [Display(Name = "Giới tính")]
        public string Gender { get; set; } // Male, Female

        [StringLength(20)]
        [Display(Name = "Ngôn ngữ")]
        public string LanguageCode { get; set; } = "vi-VN"; // vi-VN, fil-PH, en-US

        [StringLength(500)]
        [Display(Name = "Mô tả")]
        public string? Description { get; set; }

        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;

        [Display(Name = "Thứ tự hiển thị")]
        public int DisplayOrder { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Cài đặt chung cho Vbee TTS
    /// </summary>
    public class VbeeTtsSetting
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; } = 1;

        [Required]
        [StringLength(300)]
        [Display(Name = "API URL (Encrypted)")]
        public string EncryptedApiUrl { get; set; } // Mã hóa URL: http://tts.ivoi.io:8001

        [Required]
        [StringLength(100)]
        public string ApiUrlIv { get; set; }

        [Display(Name = "Endpoint Path")]
        [StringLength(100)]
        public string SynthesizeEndpoint { get; set; } = "/synthesize";

        [Display(Name = "Request Timeout (seconds)")]
        public int RequestTimeoutSeconds { get; set; } = 60;

        [Display(Name = "Max Retries")]
        public int MaxRetries { get; set; } = 3;

        [Display(Name = "Retry Delay (ms)")]
        public int RetryDelayMs { get; set; } = 2000;

        [Display(Name = "Max chunk size (characters)")]
        public int MaxChunkSize { get; set; } = 1000;

        // Pause settings mặc định
        [Display(Name = "Pause Period (seconds)")]
        [Column(TypeName = "decimal(5, 3)")]
        public decimal PausePeriod { get; set; } = 0.35m;

        [Display(Name = "Pause Comma (seconds)")]
        [Column(TypeName = "decimal(5, 3)")]
        public decimal PauseComma { get; set; } = 0.025m;

        [Display(Name = "Pause Semicolon (seconds)")]
        [Column(TypeName = "decimal(5, 3)")]
        public decimal PauseSemicolon { get; set; } = 0.3m;

        [Display(Name = "Pause Newline (seconds)")]
        [Column(TypeName = "decimal(5, 3)")]
        public decimal PauseNewline { get; set; } = 0.6m;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Theo dõi TTS session đang chạy - dùng cho quota tracking realtime
    /// </summary>
    public class VbeeTtsSession
    {
        [Key]
        [StringLength(36)]
        public string SessionId { get; set; } = Guid.NewGuid().ToString();

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; }

        [StringLength(100)]
        public string VoiceCode { get; set; }

        [Display(Name = "Ký tự yêu cầu")]
        public long TotalCharactersRequested { get; set; }

        [Display(Name = "Ký tự đã xử lý")]
        public long CharactersProcessed { get; set; } = 0;

        [Display(Name = "Ký tự đã tính phí")]
        public long CharactersCharged { get; set; } = 0;

        public VbeeTtsSessionStatus Status { get; set; } = VbeeTtsSessionStatus.Pending;

        [StringLength(500)]
        public string? ErrorMessage { get; set; }

        // Obfuscation token - dùng để xác thực request từ client
        [StringLength(64)]
        public string? ObfuscationToken { get; set; }

        // Lưu ký tự dự phòng (encrypted) để recovery khi mất kết nối
        [StringLength(500)]
        public string? EncryptedBackupData { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public DateTime LastHeartbeatAt { get; set; } = DateTime.UtcNow;
    }

    public enum VbeeTtsSessionStatus
    {
        Pending,
        Processing,
        Completed,
        Failed,
        Cancelled,
        Disconnected // Client mất kết nối
    }

    /// <summary>
    /// Fake endpoint mapping - để tạo nhiễu cho kẻ tấn công
    /// </summary>
    public class VbeeFakeEndpoint
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        [Display(Name = "Fake Path")]
        public string FakePath { get; set; } // Ví dụ: /api/media/stream/{id}

        [Required]
        [StringLength(200)]
        [Display(Name = "Purpose (Internal only)")]
        public string Purpose { get; set; } // Ví dụ: "Heartbeat", "Quota Check", "Session Init"

        [Display(Name = "Response Delay (ms)")]
        public int ResponseDelayMs { get; set; } = 0;

        [Display(Name = "Fake Response Template (JSON)")]
        [Column(TypeName = "TEXT")]
        public string? FakeResponseTemplate { get; set; }

        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
