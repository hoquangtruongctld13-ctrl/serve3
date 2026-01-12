using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace SubPhim.Server.Data
{
    public enum TtsProvider
    {
        ElevenLabs = 1,
        Gemini = 2
    }

    public enum GeminiModelType
    {
        None = 0, // Dùng cho các provider không phải Gemini
        Flash = 1, // gemini-2.5-flash-preview-tts
        Pro = 2    // gemini-2.5-pro-preview-tts
    }

    public enum GoogleTtsModelType
    {
        Standard = 1,      // Standard voices (4M chars free/month)
        WaveNet = 2,       // WaveNet voices (1M chars free/month)
        Neural2 = 3,       // Neural2 voices (1M chars free/month)
        Chirp3HD = 4,      // Chirp 3: HD voices (1M chars free/month)
        ChirpHD = 5,       // Chirp HD legacy (1M chars free/month)
        Studio = 6,        // Studio voices (1M chars free/month)
        Polyglot = 7,      // Polyglot voices (1M chars free/month)
        News = 8,          // News voices (1M chars free/month)
        Casual = 9         // Casual voices (1M chars free/month)
    }

    [Flags]
    public enum GrantedFeatures
    {
        None = 0,
        SubPhim = 1,
        DichThuat = 2,
        OcrTruyen = 4,
        EditTruyen = 8,
        Capcut = 16,
        Jianying = 32
    }
    [Flags]
    public enum AllowedApis
    {
        None = 0,
        ChutesAI = 1,
        Gemini = 2,
        OpenRouter = 4,
        SmartCut = 8,
        Capcutvoice = 16
    }
    [Flags]
    public enum ApiPoolType
    {
        Paid = 1, // Dành cho các gói trả phí
        Free = 2  // Dành cho gói Free
    }
    public enum SubscriptionTier { Free, Daily, Monthly, Yearly, Lifetime }

    public class User
    {

        [Display(Name = "UID")]
        [StringLength(9)]
        public string Uid { get; set; }
        public int Id { get; set; }
        [Required]
        [StringLength(100)]
        public string Username { get; set; }
        [EmailAddress]
        [StringLength(255)]
        public string? Email { get; set; }
        [Required]
        public string PasswordHash { get; set; }
        public SubscriptionTier Tier { get; set; }
        public DateTime? SubscriptionExpiry { get; set; }
        
        [Display(Name = "Gói Năm Pro")]
        public bool IsYearlyPro { get; set; } = false; // false = thường, true = pro
        
        [Display(Name = "Ghi đè giới hạn Request/Ngày")]
        public int DailyRequestLimitOverride { get; set; } = -1;

        public AllowedApis AllowedApiAccess { get; set; } = AllowedApis.None;
        public GrantedFeatures GrantedFeatures { get; set; } = GrantedFeatures.None;
        public DateTime CreatedAt { get; set; }
        public bool IsBlocked { get; set; }
        public int MaxDevices { get; set; } = 1;
        [Display(Name = "Số lượt dịch đã dùng/ngày")]
        public int DailyRequestCount { get; set; } = 0;
        public DateTime LastRequestResetUtc { get; set; } = DateTime.UtcNow;
        public ICollection<Device> Devices { get; set; } = new List<Device>();
        [Display(Name = "Số video đã xử lý hôm nay")]
        public int VideosProcessedToday { get; set; } = 0;

        [Display(Name = "Lần cuối reset bộ đếm video (UTC)")]
        public DateTime LastVideoResetUtc { get; set; } = DateTime.UtcNow;

        [Display(Name = "Giới hạn thời lượng video (phút)")]
        public int VideoDurationLimitMinutes { get; set; } = 30;

        [Display(Name = "Giới hạn số video/ngày")]
        public int DailyVideoLimit { get; set; } = 2;
        [Display(Name = "Giới hạn dịch SRT Local/Ngày")]
        public int DailyLocalSrtLimit { get; set; } = 500;

        [Display(Name = "Số dòng SRT Local đã dùng/Ngày")]
        public int LocalSrtLinesUsedToday { get; set; } = 0;

        [Display(Name = "Lần cuối reset bộ đếm SRT Local (UTC)")]
        public DateTime LastLocalSrtResetUtc { get; set; } = DateTime.UtcNow;
        [Display(Name = "Giới hạn dịch SRT/Ngày")]
        public int DailySrtLineLimit { get; set; } = 1500; // Mặc định 1500 cho user mới

        [Display(Name = "Số dòng SRT đã dịch/Ngày")]
        public int SrtLinesUsedToday { get; set; } = 0;

        [Display(Name = "Lần cuối reset bộ đếm SRT (UTC)")]
        public DateTime LastSrtLineResetUtc { get; set; } = DateTime.UtcNow;
        public bool IsAdmin { get; set; } = false;
        [Display(Name = "Giới hạn ký tự TTS")]
        public long TtsCharacterLimit { get; set; } = 500; // Mặc định cho Free

        [Display(Name = "Số ký tự TTS đã dùng")]
        public long TtsCharactersUsed { get; set; } = 0;

        [Display(Name = "Lần cuối reset bộ đếm TTS (UTC)")]
        public DateTime LastTtsResetUtc { get; set; } = DateTime.UtcNow;

        [Display(Name = "Ký tự AIO đã dùng/Ngày")]
        public long AioCharactersUsedToday { get; set; } = 0;

        [Display(Name = "Lần cuối reset bộ đếm AIO (UTC)")]
        public DateTime LastAioResetUtc { get; set; } = DateTime.UtcNow;

        [Display(Name = "Ghi đè Giới hạn Ký tự AIO/Ngày")]
        public long AioCharacterLimitOverride { get; set; } = -1;

        [Display(Name = "Ghi đè Giới hạn Request AIO/Phút")]
        public int AioRpmOverride { get; set; } = -1;
        [Display(Name = "Lần cuối reset thiết bị (UTC)")]
        public DateTime? LastDeviceResetUtc { get; set; }
        
        // === VIP Translation Fields ===
        [Display(Name = "Giới hạn dịch VIP SRT/Ngày")]
        public int DailyVipSrtLimit { get; set; } = 0; // Default: Free = 0, Monthly = 3000, Yearly = 15000
        
        [Display(Name = "Số dòng VIP SRT đã dùng/Ngày")]
        public int VipSrtLinesUsedToday { get; set; } = 0;
        
        [Display(Name = "Lần cuối reset bộ đếm VIP SRT (UTC)")]
        public DateTime LastVipSrtResetUtc { get; set; } = DateTime.UtcNow;
    }
    public class TtsApiKey
    {
        public int Id { get; set; }

        [Required]
        public string EncryptedApiKey { get; set; }
        [Required]
        public string Iv { get; set; }

        [Display(Name = "Nhà cung cấp")]
        public TtsProvider Provider { get; set; }

        [Display(Name = "Tên Model (chỉ cho Gemini)")]
        [StringLength(100)]
        public string? ModelName { get; set; } // Sửa thành nullable, vì ElevenLabs không cần

        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;

        [Display(Name = "Số Request Hôm Nay (Gemini)")]
        public int RequestsToday { get; set; } = 0;
        [Display(Name = "Giới hạn Ký tự")]
        public long CharacterLimit { get; set; } = 10000;

        [Display(Name = "Ký tự Đã dùng")]
        public long CharactersUsed { get; set; } = 0;
        [Display(Name = "Lần cuối reset (UTC)")]
        public DateTime LastResetUtc { get; set; } = DateTime.UtcNow;

        [Display(Name = "Lý do bị vô hiệu hóa")]
        [StringLength(200)]
        public string? DisabledReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    public class TtsModelSetting
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Tên Model (chỉ cho Gemini)")]
        [StringLength(100)]
        public string? ModelName { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Mã nhận dạng (Flash/Pro)")]
        public string Identifier { get; set; }

        [Display(Name = "Giới hạn Request/Ngày (RPD)")]
        public int MaxRequestsPerDay { get; set; }

        [Display(Name = "Giới hạn Request/Phút (RPM)")]
        public int MaxRequestsPerMinute { get; set; }

        [Display(Name = "Nhà cung cấp")]
        public TtsProvider Provider { get; set; }

    }
    public class Device
    {
        public int Id { get; set; }

        // SỬA LỖI: Bỏ `required`
        [Required]
        public string Hwid { get; set; }
        public DateTime LastLogin { get; set; }
        public string? DeviceName { get; set; }
        public string? LastLoginIp { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
    }
    public class BannedDevice
    {
        public int Id { get; set; }

        [Required]
        public string Hwid { get; set; }
        public string? LastKnownIp { get; set; }
        public string? AssociatedUsername { get; set; } // Username liên quan lúc bị ban
        public string BanReason { get; set; }
        public DateTime BannedAt { get; set; } = DateTime.UtcNow;
    }
    public class ManagedApiKey
    {
        public int Id { get; set; }

        [Required]
        public string EncryptedApiKey { get; set; }
        [Required]
        public string Iv { get; set; }
        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;

        [Display(Name = "Tổng Tokens Đã Dùng")]
        public long TotalTokensUsed { get; set; } = 0;

        [Display(Name = "Số Request Hôm Nay")]
        public int RequestsToday { get; set; } = 0;

        [Display(Name = "Lần cuối reset bộ đếm Request (UTC)")]
        public DateTime LastRequestCountResetUtc { get; set; } = DateTime.UtcNow;

        [Display(Name = "Nhóm API (Local)")]
        public ApiPoolType PoolType { get; set; } = ApiPoolType.Paid;

        // === BẮT ĐẦU THÊM TRƯỜNG MỚI: Temporary Cooldown ===
        [Display(Name = "Tạm thời vô hiệu đến (UTC)")]
        public DateTime? TemporaryCooldownUntil { get; set; } // Null = không bị cooldown

        [Display(Name = "Lý do bị vô hiệu hóa")]
        [StringLength(300)]
        public string? DisabledReason { get; set; }

        [Display(Name = "Số lần gặp lỗi 429 liên tiếp")]
        public int Consecutive429Count { get; set; } = 0;
        // === KẾT THÚC THÊM TRƯỜNG MỚI ===

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    public enum JobStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }
    public class SaOcrServiceAccount
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string ClientEmail { get; set; }

        [Required]
        [StringLength(100)]
        public string ProjectId { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "ID Thư mục Google Drive")]
        public string DriveFolderId { get; set; }

        [Required]
        public string EncryptedJsonKey { get; set; } // Sẽ lưu toàn bộ file JSON đã được mã hóa

        [Required]
        public string Iv { get; set; }

        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    public class TranslationJobDb
    {
        [Key]
        public string SessionId { get; set; } 
        public int UserId { get; set; }
        public string Genre { get; set; }
        public string TargetLanguage { get; set; }

        public string SystemInstruction { get; set; } 
                                                    
        public JobStatus Status { get; set; } = JobStatus.Pending;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [Display(Name = "Số dòng bị lỗi")]
        public int FailedLinesCount { get; set; } = 0;

        [Display(Name = "Chi tiết lỗi (JSON)")]
        [Column(TypeName = "TEXT")]
        public string? ErrorDetails { get; set; } // Lưu dưới dạng JSON để track từng lỗi cụ thể

        [Display(Name = "Đã hoàn trả lượt dịch")]
        public bool HasRefunded { get; set; } = false;
        public ICollection<OriginalSrtLineDb> OriginalLines { get; set; }
        public ICollection<TranslatedSrtLineDb> TranslatedLines { get; set; }
    }
    public class OriginalSrtLineDb
    {
        public long Id { get; set; }
        public int LineIndex { get; set; }
        public string OriginalText { get; set; }
        public string SessionId { get; set; } // Khóa ngoại
        [ForeignKey("SessionId")]
        public TranslationJobDb Job { get; set; }
    }

    public class TranslatedSrtLineDb
    {
        public long Id { get; set; }
        public int LineIndex { get; set; }
        public string TranslatedText { get; set; }
        public bool Success { get; set; }
        [StringLength(50)]
        public string? ErrorType { get; set; } // "HTTP_429", "HTTP_500", "FINISH_REASON", "TIMEOUT", etc.

        [StringLength(500)]
        public string? ErrorDetail { get; set; }
        public string SessionId { get; set; } // Khóa ngoại
        [ForeignKey("SessionId")]
        public TranslationJobDb Job { get; set; }
    }
    public class ErrorTrackingInfo
    {
        public int LineIndex { get; set; }
        public string ErrorType { get; set; }
        public string ErrorDetail { get; set; }
        public int HttpStatusCode { get; set; }
        public DateTime OccurredAt { get; set; }
    }
    public class TierDefaultSetting
    {
        [Key]
        public int Id { get; set; }
        
        public SubscriptionTier Tier { get; set; }

        public int VideoDurationMinutes { get; set; }
        public int DailyVideoCount { get; set; }
        public int DailyTranslationRequests { get; set; }
        public AllowedApis AllowedApis { get; set; }
        public AllowedApis AllowedApiAccess { get; set; }
        public GrantedFeatures GrantedFeatures { get; set; }
        public int DailySrtLineLimit { get; set; }
        public int DailyLocalSrtLimit { get; set; }
        [Display(Name = "Giới hạn ký tự TTS")]
        public long TtsCharacterLimit { get; set; }

        // === BẮT ĐẦU THAY ĐỔI ===
        [Display(Name = "Giới hạn ký tự AIO/Ngày")]
        public long AioCharacterLimit { get; set; }

        [Display(Name = "Giới hạn Request AIO/Phút")]
        public int AioRequestsPerMinute { get; set; }
        // === KẾT THÚC THAY ĐỔI ===
        
        // === VIP Translation Limits ===
        [Display(Name = "Giới hạn dịch VIP SRT/Ngày")]
        public int DailyVipSrtLimit { get; set; } = 0;
        
        // === Yearly Tier Marking ===
        [Display(Name = "Áp dụng cho Gói Năm Pro")]
        public bool IsYearlyProSettings { get; set; } = false;
    }
    public class AioTranslationSetting
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; } = 1;
        [StringLength(100)]
        [Display(Name = "Model API mặc định")]
        public string DefaultModelName { get; set; } = "gemini-2.5-pro";

        [Display(Name = "Temperature (0-2)")]
        [Column(TypeName = "decimal(3, 2)")]
        public decimal Temperature { get; set; } = 0.7m;

        [Display(Name = "Max Output Tokens")]
        public int MaxOutputTokens { get; set; } = 8192;

        [Display(Name = "Bật Thinking Budget")]
        public bool EnableThinkingBudget { get; set; } = true;

        [Display(Name = "Thinking Budget")]
        public int ThinkingBudget { get; set; } = 8192;

        [Display(Name = "Request/Phút/Key")]
        public int RpmPerKey { get; set; } = 10;
        [Display(Name = "Request/Ngày/Key (RPD)")]
        public int RpdPerKey { get; set; } = 100;
        
        [Display(Name = "Request/Phút/Proxy")]
        public int RpmPerProxy { get; set; } = 60;
        
        [Display(Name = "Request/Ngày/Proxy (RPD)")]
        public int RpdPerProxy { get; set; } = 1500;
        
        [Display(Name = "Số lần thử lại API nếu lỗi")]
        public int MaxApiRetries { get; set; } = 3;

        [Display(Name = "Delay giữa các lần thử lại API (ms)")]
        public int RetryApiDelayMs { get; set; } = 10000;

        [Display(Name = "Delay giữa các file (ms)")]
        public int DelayBetweenFilesMs { get; set; } = 5000;

        [Display(Name = "Delay giữa các chunk (ms)")]
        public int DelayBetweenChunksMs { get; set; } = 5000;

        [Display(Name = "Ngưỡng gửi trực tiếp (ký tự)")]
        public int DirectSendThreshold { get; set; } = 8000;

        [Display(Name = "Kích thước Chunk (ký tự)")]
        public int ChunkSize { get; set; } = 3500;
    }
    public class AioApiKey
    {
        public int Id { get; set; }

        [Required]
        public string EncryptedApiKey { get; set; }

        [Required]
        public string Iv { get; set; }

        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;

        [Display(Name = "Số Request Hôm Nay")]
        public int RequestsToday { get; set; } = 0;

        [Display(Name = "Lần cuối reset (UTC)")]
        public DateTime LastResetUtc { get; set; } = DateTime.UtcNow;

        [Display(Name = "Lý do bị vô hiệu hóa")]
        [StringLength(200)]
        public string? DisabledReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    public class TranslationGenre
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Tên Thể loại")]
        public string GenreName { get; set; } // Ví dụ: "Huyền Huyễn Tiên Hiệp", "Ngôn Tình"

        [Required]
        [Display(Name = "Prompt Hệ thống (System Instruction)")]
        public string SystemInstruction { get; set; } // Nội dung prompt tương ứng

        [Display(Name = "Đang hoạt động")]
        public bool IsActive { get; set; } = true;
    }
    public enum AioJobStatus { Pending, Processing, Completed, Failed }

    public class AioTranslationJob
    {
        [Key]
        public string SessionId { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; }

        public AioJobStatus Status { get; set; } = AioJobStatus.Pending;

        [Column(TypeName = "TEXT")] // Dùng TEXT để lưu trữ nội dung lớn
        public string OriginalContent { get; set; }

        [Column(TypeName = "TEXT")]
        public string? TranslatedContent { get; set; }

        [Column(TypeName = "TEXT")]
        [Display(Name = "System Instruction")]
        public string SystemInstruction { get; set; }
        
        public string TargetLanguage { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
    public class AvailableApiModel
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Tên Model")]
        public string ModelName { get; set; }

        [Display(Name = "Đang được kích hoạt")]
        public bool IsActive { get; set; }
        [Display(Name = "Nhóm API")]
        public ApiPoolType PoolType { get; set; } = ApiPoolType.Paid;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    public class LocalApiSetting
    {
        // Dùng Id cố định là 1 để luôn chỉ có 1 dòng setting
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; } = 1;

        [Display(Name = "Request/Phút (RPM)")]
        public int Rpm { get; set; } = 100;

        [Display(Name = "Số dòng/Request (Batch Size)")]
        public int BatchSize { get; set; } = 40;

        // --- BẮT ĐẦU THÊM CÁC TRƯỜNG MỚI ---

        [Display(Name = "Số lần thử lại nếu lỗi")]
        public int MaxRetries { get; set; } = 3;

        [Display(Name = "Delay giữa các lần thử lại (ms)")]
        public int RetryDelayMs { get; set; } = 5000;

        [Display(Name = "Delay giữa các batch (ms)")]
        public int DelayBetweenBatchesMs { get; set; } = 1000;

        [Display(Name = "Temperature (0-2)")]
        [Column(TypeName = "decimal(3, 2)")] // Để lưu số thập phân chính xác
        public decimal Temperature { get; set; } = 0.7m;

        [Display(Name = "Max Output Tokens")]
        public int MaxOutputTokens { get; set; } = 8192;

        [Display(Name = "Bật Thinking Budget (IQ AI)")]
        public bool EnableThinkingBudget { get; set; } = true;

        [Display(Name = "Thinking Budget (IQ AI)")]
        public int ThinkingBudget { get; set; } = 8192;

        // --- PROXY RATE LIMIT SETTINGS ---
        [Display(Name = "Request/Phút/Proxy (RPM)")]
        public int RpmPerProxy { get; set; } = 10;

        // --- KẾT THÚC THÊM CÁC TRƯỜNG MỚI ---
    }
    
    public class VipTranslationSetting
    {
        // Dùng Id cố định là 1 để luôn chỉ có 1 dòng setting
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; } = 1;

        [Display(Name = "Request/Phút (RPM)")]
        public int Rpm { get; set; } = 100;

        [Display(Name = "Số dòng/Request (Batch Size)")]
        public int BatchSize { get; set; } = 40;

        [Display(Name = "Số lần thử lại nếu lỗi")]
        public int MaxRetries { get; set; } = 3;

        [Display(Name = "Delay giữa các lần thử lại (ms)")]
        public int RetryDelayMs { get; set; } = 5000;

        [Display(Name = "Delay giữa các batch (ms)")]
        public int DelayBetweenBatchesMs { get; set; } = 1000;

        [Display(Name = "Temperature (0-2)")]
        [Column(TypeName = "decimal(3, 2)")]
        public decimal Temperature { get; set; } = 0.7m;

        [Display(Name = "Max Output Tokens")]
        public int MaxOutputTokens { get; set; } = 8192;

        [Display(Name = "Bật Thinking Budget (IQ AI)")]
        public bool EnableThinkingBudget { get; set; } = true;

        [Display(Name = "Thinking Budget (IQ AI)")]
        public int ThinkingBudget { get; set; } = 8192;

        [Display(Name = "Request/Phút/Proxy (RPM)")]
        public int RpmPerProxy { get; set; } = 10;

        [Display(Name = "Giới hạn ký tự/dòng (Max Characters per Line)")]
        public int MaxSrtLineLength { get; set; } = 3000;
    }
    
    public class VipApiKey
    {
        public int Id { get; set; }

        [Required]
        public string EncryptedApiKey { get; set; }
        
        [Required]
        public string Iv { get; set; }
        
        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;

        [Display(Name = "Tổng Tokens Đã Dùng")]
        public long TotalTokensUsed { get; set; } = 0;

        [Display(Name = "Số Request Hôm Nay")]
        public int RequestsToday { get; set; } = 0;

        [Display(Name = "Lần cuối reset bộ đếm Request (UTC)")]
        public DateTime LastRequestCountResetUtc { get; set; } = DateTime.UtcNow;

        [Display(Name = "Tạm thời vô hiệu đến (UTC)")]
        public DateTime? TemporaryCooldownUntil { get; set; }

        [Display(Name = "Lý do bị vô hiệu hóa")]
        [StringLength(300)]
        public string? DisabledReason { get; set; }

        [Display(Name = "Số lần gặp lỗi 429 liên tiếp")]
        public int Consecutive429Count { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    
    public class VipAvailableApiModel
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Tên Model")]
        public string ModelName { get; set; }

        [Display(Name = "Đang được kích hoạt")]
        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    
    public class AioTtsServiceAccount
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string ClientEmail { get; set; }

        [Required]
        [StringLength(100)]
        public string ProjectId { get; set; }

        [Required]
        public string EncryptedJsonKey { get; set; } // Sẽ lưu toàn bộ file JSON đã được mã hóa

        [Required]
        public string Iv { get; set; }

        [Display(Name = "Loại Model TTS")]
        public GoogleTtsModelType ModelType { get; set; } = GoogleTtsModelType.Chirp3HD; // Mặc định Chirp3HD để tương thích ngược

        public bool IsEnabled { get; set; } = true;

        // Dùng để theo dõi quota theo tháng
        public long CharactersUsed { get; set; } = 0;

        // Lưu tháng sử dụng dưới dạng "YYYY-MM" để tự động reset
        public string UsageMonth { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    public enum AioTtsJobStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }

    public class AioTtsBatchJob
    {
        [Key]
        public Guid Id { get; set; } // Dùng Guid để đảm bảo ID là duy nhất

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; }

        public AioTtsJobStatus Status { get; set; } = AioTtsJobStatus.Pending;

        // --- Thông số yêu cầu ---
        [Required]
        public string Language { get; set; }
        [Required]
        public string VoiceId { get; set; }
        public double Rate { get; set; }
        [Required]
        public string AudioFormat { get; set; } // "MP3", "WAV", "OGG_OPUS"

        [Display(Name = "Loại Model TTS")]
        public GoogleTtsModelType ModelType { get; set; } = GoogleTtsModelType.Chirp3HD;

        // --- Thông tin xử lý ---
        [Required]
        public string OriginalSrtFilePath { get; set; } // Đường dẫn tới file SRT đã upload
        public string? ResultZipFilePath { get; set; } // Đường dẫn tới file ZIP kết quả
        public string? ErrorMessage { get; set; }
        public int TotalLines { get; set; }
        public int ProcessedLines { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }

    public class GoogleTtsModelConfig
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Loại Model")]
        public GoogleTtsModelType ModelType { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Tên Model trong API")]
        public string ModelIdentifier { get; set; } // Ví dụ: "Standard", "Wavenet", "Neural2", "Chirp3-HD", "Studio"

        [Display(Name = "Giới hạn miễn phí/tháng (ký tự)")]
        public long MonthlyFreeLimit { get; set; }

        [Display(Name = "Giá sau giới hạn ($/1M ký tự)")]
        [Column(TypeName = "decimal(10, 2)")]
        public decimal PricePerMillionChars { get; set; }

        [Display(Name = "Hỗ trợ SSML")]
        public bool SupportsSsml { get; set; } = true;

        [Display(Name = "Hỗ trợ Speaking Rate")]
        public bool SupportsSpeakingRate { get; set; } = true;

        [Display(Name = "Hỗ trợ Pitch")]
        public bool SupportsPitch { get; set; } = true;

        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;

        [Display(Name = "Mô tả")]
        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    public class TranslationLog
    {
        public long Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public int ApiKeyId { get; set; } // ID của key đã được dùng

        [StringLength(100)]
        public string ModelUsed { get; set; }

        public int LineCountRequested { get; set; }
        public int LineCountApproved { get; set; }

        [StringLength(50)]
        public string Provider { get; set; }
        public int TokensUsed { get; set; } = 0;
    }
    public class UpdateInfo
    {
        // Sử dụng Id cố định là 1 để đảm bảo chỉ có một dòng dữ liệu duy nhất
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; } = 1;

        [Required]
        [StringLength(50)]
        [Display(Name = "Phiên bản mới nhất")]
        public string LatestVersion { get; set; }

        [Required]
        [Url]
        [Display(Name = "Đường dẫn tải file")]
        public string DownloadUrl { get; set; }

        [Display(Name = "Ghi chú phiên bản (Release Notes)")]
        public string? ReleaseNotes { get; set; } // Cho phép null nếu không có ghi chú

        [Display(Name = "Lần cuối cập nhật")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public enum ProxyType
    {
        Http = 1,
        Socks4 = 2,
        Socks5 = 3
    }

    public class Proxy
    {
        public int Id { get; set; }

        [Required]
        [StringLength(255)]
        [Display(Name = "Địa chỉ Proxy")]
        public string Host { get; set; }

        [Required]
        [Display(Name = "Cổng")]
        public int Port { get; set; }

        [Display(Name = "Loại Proxy")]
        public ProxyType Type { get; set; } = ProxyType.Socks5;

        [StringLength(100)]
        [Display(Name = "Username (nếu có)")]
        public string? Username { get; set; }

        [StringLength(100)]
        [Display(Name = "Password (nếu có)")]
        public string? Password { get; set; }

        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;

        [Display(Name = "Số lần sử dụng")]
        public int UsageCount { get; set; } = 0;

        [Display(Name = "Số lần lỗi")]
        public int FailureCount { get; set; } = 0;

        [Display(Name = "Lần sử dụng cuối")]
        public DateTime? LastUsedAt { get; set; }

        [Display(Name = "Lần lỗi cuối")]
        public DateTime? LastFailedAt { get; set; }

        [StringLength(500)]
        [Display(Name = "Lý do lỗi cuối")]
        public string? LastFailureReason { get; set; }
        
        [Display(Name = "Request hôm nay (AIO)")]
        public int AioRequestsToday { get; set; } = 0;
        
        [Display(Name = "Lần cuối reset AIO (UTC)")]
        public DateTime LastAioResetUtc { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ==================== External API Key Management Entities ====================

    /// <summary>
    /// Entity for External API Keys - allows third-party clients to access VIP Translation API
    /// </summary>
    public class ExternalApiKey
    {
        public int Id { get; set; }
        
        // Identity & Security
        [Required]
        [StringLength(100)]
        [Display(Name = "Key Hash")]
        public string KeyHash { get; set; } = string.Empty; // SHA-256 hash of API key
        
        [Required]
        [StringLength(10)]
        [Display(Name = "Key Prefix")]
        public string KeyPrefix { get; set; } = "AIO_"; // "AIO_" prefix for identification
        
        [Required]
        [StringLength(10)]
        [Display(Name = "Key Suffix")]
        public string KeySuffix { get; set; } = string.Empty; // Last 4 characters for display
        
        [StringLength(200)]
        [Display(Name = "Tên hiển thị")]
        public string? DisplayName { get; set; }
        
        // Assignment Information
        [StringLength(200)]
        [Display(Name = "Gán cho")]
        public string? AssignedTo { get; set; } // Customer/company name
        
        [StringLength(255)]
        [EmailAddress]
        [Display(Name = "Email")]
        public string? Email { get; set; }
        
        [StringLength(1000)]
        [Display(Name = "Ghi chú")]
        public string? Notes { get; set; }
        
        // Credit Management
        [Display(Name = "Số dư Credit")]
        public long CreditBalance { get; set; } = 0;
        
        [Display(Name = "Tổng Credit đã dùng")]
        public long TotalCreditsUsed { get; set; } = 0;
        
        [Display(Name = "Tổng Credit đã nạp")]
        public long TotalCreditsAdded { get; set; } = 0;
        
        // Rate Limiting
        [Display(Name = "RPM Limit")]
        public int RpmLimit { get; set; } = 100; // Requests per minute
        
        // Status
        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;
        
        [Display(Name = "Ngày tạo")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [Display(Name = "Lần dùng cuối")]
        public DateTime? LastUsedAt { get; set; }
        
        [Display(Name = "Ngày hết hạn")]
        public DateTime? ExpiresAt { get; set; } // Null = never expires
        
        // Navigation properties
        public ICollection<ExternalApiUsageLog> UsageLogs { get; set; } = new List<ExternalApiUsageLog>();
        public ICollection<ExternalApiCreditTransaction> CreditTransactions { get; set; } = new List<ExternalApiCreditTransaction>();
    }

    /// <summary>
    /// Tracks usage/status of each API call
    /// </summary>
    public enum UsageStatus
    {
        Pending = 0,      // Job is running
        Completed = 1,    // Completed successfully, credit charged
        Failed = 2,       // Failed, credit refunded
        Cancelled = 3,    // Cancelled by user, credit refunded
        Refunded = 4      // Manually refunded
    }

    /// <summary>
    /// Entity for External API Usage Logs - tracks each API call
    /// </summary>
    public class ExternalApiUsageLog
    {
        public long Id { get; set; }
        
        public int ApiKeyId { get; set; }
        public ExternalApiKey ApiKey { get; set; } = null!;
        
        // Request Information
        [Required]
        [StringLength(100)]
        [Display(Name = "Session ID")]
        public string SessionId { get; set; } = string.Empty;
        
        [Required]
        [StringLength(200)]
        [Display(Name = "Endpoint")]
        public string Endpoint { get; set; } = string.Empty;
        
        [StringLength(10)]
        [Display(Name = "Ngôn ngữ đích")]
        public string? TargetLanguage { get; set; }
        
        // Statistics
        [Display(Name = "Số dòng input")]
        public int InputLines { get; set; }
        
        [Display(Name = "Số ký tự output")]
        public int OutputCharacters { get; set; }
        
        [Display(Name = "Credit đã tính")]
        public long CreditsCharged { get; set; }
        
        // Status
        [Display(Name = "Trạng thái")]
        public UsageStatus Status { get; set; } = UsageStatus.Pending;
        
        [StringLength(1000)]
        [Display(Name = "Thông báo lỗi")]
        public string? ErrorMessage { get; set; }
        
        [Column(TypeName = "TEXT")]
        [Display(Name = "Lỗi Gemini")]
        public string? GeminiErrors { get; set; } // JSON array of Gemini errors
        
        // Timing
        [Display(Name = "Thời gian bắt đầu")]
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        
        [Display(Name = "Thời gian hoàn thành")]
        public DateTime? CompletedAt { get; set; }
        
        [Display(Name = "Thời gian xử lý (ms)")]
        public int? DurationMs { get; set; }
        
        // Metadata
        [StringLength(50)]
        [Display(Name = "Client IP")]
        public string? ClientIp { get; set; }
        
        [StringLength(500)]
        [Display(Name = "User Agent")]
        public string? UserAgent { get; set; }
    }

    /// <summary>
    /// Types of credit transactions
    /// </summary>
    public enum TransactionType
    {
        Deposit = 1,      // Credit added
        Usage = 2,        // Credit used (deducted)
        Refund = 3,       // Credit refunded due to error
        Adjustment = 4,   // Manual adjustment
        Bonus = 5         // Bonus/gift credit
    }

    /// <summary>
    /// Entity for External API Credit Transactions - tracks all credit movements
    /// </summary>
    public class ExternalApiCreditTransaction
    {
        public long Id { get; set; }
        
        public int ApiKeyId { get; set; }
        public ExternalApiKey ApiKey { get; set; } = null!;
        
        [Display(Name = "Loại giao dịch")]
        public TransactionType Type { get; set; }
        
        [Display(Name = "Số lượng")]
        public long Amount { get; set; } // Positive or negative
        
        [Display(Name = "Số dư sau giao dịch")]
        public long BalanceAfter { get; set; }
        
        [Required]
        [StringLength(500)]
        [Display(Name = "Mô tả")]
        public string Description { get; set; } = string.Empty;
        
        [Display(Name = "Usage Log ID liên quan")]
        public long? RelatedUsageLogId { get; set; }
        
        [StringLength(100)]
        [Display(Name = "Tạo bởi")]
        public string? CreatedBy { get; set; } // Admin username if manual
        
        [Display(Name = "Ngày tạo")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Singleton entity for External API Settings
    /// </summary>
    public class ExternalApiSettings
    {
        public int Id { get; set; } = 1; // Singleton pattern

        // Credit Conversion Rates
        [Display(Name = "Credit/Ký tự")]
        public int CreditsPerCharacter { get; set; } = 5; // 5 credit = 1 character

        [Display(Name = "VND/Credit")]
        [Column(TypeName = "decimal(18,4)")]
        public decimal VndPerCredit { get; set; } = 10; // 10 VND = 1 credit (so 10,000 VND = 1,000 credit)

        // Defaults for new API keys
        [Display(Name = "RPM mặc định")]
        public int DefaultRpm { get; set; } = 100;

        [Display(Name = "Credit khởi tạo mặc định")]
        public long DefaultInitialCredits { get; set; } = 0;

        [Display(Name = "Cập nhật lần cuối")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ==================== Subtitle Translation (Distributed Servers) ====================

    /// <summary>
    /// Cài đặt chung cho hệ thống dịch phụ đề phân tán
    /// </summary>
    public class SubtitleApiSetting
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; } = 1;

        [Display(Name = "Số dòng/Server (Lines per Server)")]
        public int LinesPerServer { get; set; } = 120; // Số dòng gửi cho mỗi server dịch

        [Display(Name = "Số dòng/Batch cho server dịch")]
        public int BatchSizePerServer { get; set; } = 40; // Số dòng/request gửi cho server dịch

        [Display(Name = "Số API Key gửi cho mỗi server")]
        public int ApiKeysPerServer { get; set; } = 5; // Số key gửi cho mỗi server dịch

        [Display(Name = "Ngưỡng gộp batch cuối (dòng)")]
        public int MergeBatchThreshold { get; set; } = 10; // Nếu batch cuối <= X dòng thì gộp vào batch trước

        [Display(Name = "Timeout cho mỗi server (giây)")]
        public int ServerTimeoutSeconds { get; set; } = 300; // 5 phút timeout

        [Display(Name = "Số lần retry khi server lỗi")]
        public int MaxServerRetries { get; set; } = 3;

        [Display(Name = "Delay giữa các server batch (ms)")]
        public int DelayBetweenServerBatchesMs { get; set; } = 500;

        [Display(Name = "Thời gian Cooldown khi key lỗi 429 (phút)")]
        public int ApiKeyCooldownMinutes { get; set; } = 5;

        [Display(Name = "Bật callback về server chính")]
        public bool EnableCallback { get; set; } = true;

        [Display(Name = "Model mặc định")]
        [StringLength(100)]
        public string DefaultModel { get; set; } = "gemini-2.5-flash";

        [Display(Name = "Temperature")]
        [Column(TypeName = "decimal(3, 2)")]
        public decimal Temperature { get; set; } = 0.3m;

        [Display(Name = "Thinking Budget (0 = tắt)")]
        public int ThinkingBudget { get; set; } = 0;
        [Display(Name = "URL Server Chính (Callback)")]
        [StringLength(500)]
        public string? MainServerUrl { get; set; }
        [Display(Name = "Cập nhật lần cuối")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// API Key cho hệ thống dịch phụ đề phân tán
    /// </summary>
    public class SubtitleApiKey
    {
        public int Id { get; set; }

        [Required]
        public string EncryptedApiKey { get; set; }

        [Required]
        public string Iv { get; set; }

        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;

        [Display(Name = "Số Request Hôm Nay")]
        public int RequestsToday { get; set; } = 0;

        [Display(Name = "Tổng Request thành công")]
        public long TotalSuccessRequests { get; set; } = 0;

        [Display(Name = "Tổng Request thất bại")]
        public long TotalFailedRequests { get; set; } = 0;

        [Display(Name = "Lần cuối reset bộ đếm (UTC)")]
        public DateTime LastResetUtc { get; set; } = DateTime.UtcNow;

        [Display(Name = "Tạm thời vô hiệu đến (UTC)")]
        public DateTime? CooldownUntil { get; set; } // Cooldown khi gặp lỗi 429

        [Display(Name = "Số lần lỗi 429 liên tiếp")]
        public int Consecutive429Count { get; set; } = 0;

        [Display(Name = "Lý do bị vô hiệu hóa")]
        [StringLength(300)]
        public string? DisabledReason { get; set; }

        [Display(Name = "Lần cuối sử dụng")]
        public DateTime? LastUsedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Danh sách server dịch phụ đề (deploy trên fly.io)
    /// </summary>
    public class SubtitleTranslationServer
    {
        public int Id { get; set; }

        [Required]
        [StringLength(500)]
        [Display(Name = "URL Server")]
        public string ServerUrl { get; set; } // https://subtitle-server-1.fly.dev

        [StringLength(100)]
        [Display(Name = "Tên hiển thị")]
        public string? DisplayName { get; set; }

        [Display(Name = "Đang hoạt động")]
        public bool IsEnabled { get; set; } = true;

        [Display(Name = "RPM của server")]
        public int RpmLimit { get; set; } = 5; // Request per minute của server này

        [Display(Name = "Đang xử lý job")]
        public bool IsBusy { get; set; } = false;

        [Display(Name = "Session đang xử lý")]
        [StringLength(100)]
        public string? CurrentSessionId { get; set; }

        [Display(Name = "Số lần sử dụng")]
        public long UsageCount { get; set; } = 0;

        [Display(Name = "Số lần thất bại")]
        public long FailureCount { get; set; } = 0;

        [Display(Name = "Thời gian phản hồi trung bình (ms)")]
        public int AvgResponseTimeMs { get; set; } = 0;

        [Display(Name = "Lần cuối sử dụng")]
        public DateTime? LastUsedAt { get; set; }

        [Display(Name = "Lần cuối lỗi")]
        public DateTime? LastFailedAt { get; set; }

        [StringLength(500)]
        [Display(Name = "Lý do lỗi cuối")]
        public string? LastFailureReason { get; set; }

        [Display(Name = "Thứ tự ưu tiên")]
        public int Priority { get; set; } = 0; // Số nhỏ = ưu tiên cao

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Trạng thái job dịch phụ đề phân tán
    /// </summary>
    public enum SubtitleJobStatus
    {
        Pending = 0,      // Chờ xử lý
        Distributing = 1, // Đang phân phối đến các server
        Processing = 2,   // Đang dịch
        Aggregating = 3,  // Đang tổng hợp kết quả
        Completed = 4,    // Hoàn thành
        Failed = 5,       // Thất bại
        PartialCompleted = 6 // Hoàn thành một phần (có lỗi)
    }

    /// <summary>
    /// Job dịch phụ đề - lưu thông tin từ client
    /// </summary>
    public class SubtitleTranslationJob
    {
        [Key]
        [StringLength(100)]
        public string SessionId { get; set; }

        public int? UserId { get; set; } // Nullable nếu là external API call

        [StringLength(100)]
        public string? ExternalApiKeyPrefix { get; set; } // Nếu gọi từ External API

        public SubtitleJobStatus Status { get; set; } = SubtitleJobStatus.Pending;

        [Display(Name = "Tổng số dòng")]
        public int TotalLines { get; set; }

        [Display(Name = "Số dòng đã dịch")]
        public int CompletedLines { get; set; } = 0;

        [Display(Name = "Tiến độ (%)")]
        public float Progress { get; set; } = 0;

        [Column(TypeName = "TEXT")]
        [Display(Name = "System Instruction")]
        public string SystemInstruction { get; set; }

        [Column(TypeName = "TEXT")]
        [Display(Name = "Prompt")]
        public string Prompt { get; set; }

        [StringLength(100)]
        [Display(Name = "Model")]
        public string Model { get; set; } = "gemini-2.5-flash";

        [Display(Name = "Thinking Budget")]
        public int? ThinkingBudget { get; set; }

        [StringLength(500)]
        [Display(Name = "Callback URL")]
        public string? CallbackUrl { get; set; }

        [Column(TypeName = "TEXT")]
        [Display(Name = "Lỗi")]
        public string? ErrorMessage { get; set; }

        [Column(TypeName = "TEXT")]
        [Display(Name = "Dữ liệu gốc (JSON)")]
        public string? OriginalLinesJson { get; set; } // Lưu JSON của lines để retry

        [Column(TypeName = "TEXT")]
        [Display(Name = "Kết quả (JSON)")]
        public string? ResultsJson { get; set; } // Kết quả dịch dạng JSON

        [Column(TypeName = "TEXT")]
        [Display(Name = "Thống kê API Key (JSON)")]
        public string? ApiKeyUsageJson { get; set; } // Thống kê sử dụng key

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        // Navigation
        public ICollection<SubtitleServerTask> ServerTasks { get; set; } = new List<SubtitleServerTask>();
    }

    /// <summary>
    /// Trạng thái task gửi cho từng server
    /// </summary>
    public enum ServerTaskStatus
    {
        Pending = 0,
        Sent = 1,
        Processing = 2,
        Completed = 3,
        Failed = 4,
        Retrying = 5
    }

    /// <summary>
    /// Task con - phần việc gửi cho từng server dịch
    /// </summary>
    public class SubtitleServerTask
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string SessionId { get; set; }

        [ForeignKey("SessionId")]
        public SubtitleTranslationJob Job { get; set; }

        public int ServerId { get; set; }

        [ForeignKey("ServerId")]
        public SubtitleTranslationServer Server { get; set; }

        [Display(Name = "Batch Index")]
        public int BatchIndex { get; set; } // Thứ tự batch (0, 1, 2...)

        [Display(Name = "Dòng bắt đầu")]
        public int StartLineIndex { get; set; }

        [Display(Name = "Số dòng")]
        public int LineCount { get; set; }

        public ServerTaskStatus Status { get; set; } = ServerTaskStatus.Pending;

        [Display(Name = "Số lần retry")]
        public int RetryCount { get; set; } = 0;

        [Column(TypeName = "TEXT")]
        [Display(Name = "Kết quả (JSON)")]
        public string? ResultJson { get; set; }

        [StringLength(500)]
        [Display(Name = "Lỗi")]
        public string? ErrorMessage { get; set; }

        [Display(Name = "Thời gian gửi")]
        public DateTime? SentAt { get; set; }

        [Display(Name = "Thời gian hoàn thành")]
        public DateTime? CompletedAt { get; set; }

        [Display(Name = "Thời gian xử lý (ms)")]
        public int? ProcessingTimeMs { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

}