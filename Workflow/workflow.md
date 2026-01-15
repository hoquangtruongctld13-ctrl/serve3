
## Mục tiêu
Tích hợp **Antigravity Tools API** (OpenAI-compatible endpoint) vào hệ thống dịch phụ đề của SubPhim.Server, chạy **song song** với hệ thống API keys hiện tại để tăng throughput. Trong Admin/LocalApi: xoá trên admin panel hướng dẫn proxy: Mặc định: 10 request/phút/proxy, thêm vào đó các trường cài đặt: ONE OFF chức năng gọi tới Antigravity Tools API, nếu bật mới sử dụng,có cài đặt rpm tới Antigravity Tools API, số dòng srt/request Antigravity API, mặc định 200,  Delay mỗi batch: mặc định 5000ms, ô nhập link api nếu có thay đổi, hiển thị các model hõ trợ, tôi có thêm hoặc xoá, mặc định là:
    ("Gemini 3 Flash", "gemini-3-flash"),
    ("Gemini 3 Pro High", "gemini-3-pro-high"),
    ("Gemini 3 Pro Low", "gemini-3-pro-low"),
    ("Gemini 3 Pro (Image)", "gemini-3-pro-image"),
    ("Gemini 2.5 Flash", "gemini-2.5-flash"),
    ("Gemini 2.5 Flash Lite", "gemini-2.5-flash-lite"),
    ("Gemini 2.5 Pro", "gemini-2.5-pro"),
    ("Gemini 2.5 Flash (Thinking)", "gemini-2.5-flash-thinking"),
    ("Claude 4.5 Sonnet", "claude-sonnet-4-5"),
Cố gắng chia sẻ đều request giữa những người dùng, nếu có nhiều người cùng lúc gọi endpoint server này, nhưng vẫn phải đảm bảo rpm chính xác như logic proxy và Antigravity Tools API sau khi tích hợp

---

## 1. Kiến trúc hiện tại

### TranslationOrchestratorService (LocalAPI)
- **Vị trí**: `Services/TranslationOrchestratorService.cs`
- **Chức năng**: Dịch phụ đề sử dụng Google Generative AI API
- **Cách hoạt động**:
  - Quản lý pool `ManagedApiKey` trong database
  - Round-robin rotation giữa các API keys
  - Rate limiting per-key với `SemaphoreSlim`
  - Proxy support qua `ProxyService`
  - Gọi trực tiếp: `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`

### Quota System
- **User quota**: `User.LocalSrtLinesUsedToday` (reset hàng ngày theo timezone VN)
- **Tier limits**: Cấu hình trong `TierSettingsService`
- **Refund logic**: Hoàn trả lines khi job fail

---

## 2. Antigravity API

### Thông tin kết nối
```
URL: http://host.docker.internal:8045/v1  (từ Docker container)
     http://127.0.0.1:8045/v1             (từ native app)
API Key: sk-antigravity
Protocol: OpenAI-compatible
```


### Request format: Do đây là một hướng khác API chính thức nên nếu gọi API này sẽ gộp systemInstruction và prompt của user gửi từ client lên thành 1 kèm nội dung dịch
ví dụ python hãy port qua C#:
from openai import OpenAI
 
 client = OpenAI(
     base_url="http://127.0.0.1:8045/v1",
     api_key="sk-adb0c54dbc3245088dd33e607e9481ce"
 )
 
 response = client.chat.completions.create(
     model="gemini-2.5-flash",
     messages=[{"role": "user", "content": "Hello"}]
 )
 
 print(response.choices[0].message.content)
```

---

## 3. Yêu cầu tích hợp

### 3.1. Tạo AntigravityTranslationService
```csharp
// Services/AntigravityTranslationService.cs
public class AntigravityTranslationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AntigravityTranslationService> _logger;
    private readonly string _baseUrl = "http://host.docker.internal:8045/v1";
    private readonly string _apiKey = "sk-antigravity";
    
    // Health check endpoint
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default);
    
    // Translate a batch of lines
    public async Task<(Dictionary<int, string> translations, int tokensUsed)> 
        TranslateBatchAsync(
            List<OriginalSrtLineDb> batch,
            string targetLanguage,
            string systemInstruction,
            string modelName,
            CancellationToken ct = default);
}
```

### 3.2. Cập nhật TranslationOrchestratorService

Thêm logic **hybrid** để sử dụng cả 2 nguồn:

```csharp
// Trong ProcessJob hoặc TranslateBatchAsync
private async Task<BatchResult> TranslateBatchHybridAsync(...)
{
    // 1. Check Antigravity availability
    bool antigravityAvailable = await _antigravityService.IsAvailableAsync(ct);
    
    // 2. Quyết định nguồn dựa trên load và availability
    if (antigravityAvailable && ShouldUseAntigravity())
    {
        return await TranslateViaAntigravityAsync(batch, ...);
    }
    else
    {
        return await TranslateViaDirectApiAsync(batch, ...); // Logic hiện tại
    }
}

private bool ShouldUseAntigravity()
{
    // Load balancing logic:
    // - Ưu tiên Antigravity nếu direct API keys đang bị rate limit
    // - Hoặc round-robin 50/50
    // - Hoặc dựa trên queue depth
}
```

### 3.3. Chia sẻ Quota

**QUAN TRỌNG**: Cả hai nguồn phải dùng chung quota của user.

```csharp
// Trước khi dịch batch:
int linesToTranslate = batch.Count;
if (user.LocalSrtLinesUsedToday + linesToTranslate > user.DailyLimit)
{
    throw new QuotaExceededException();
}

// Sau khi dịch thành công (dù qua Antigravity hay Direct API):
user.LocalSrtLinesUsedToday += successfulLines;
await _context.SaveChangesAsync();
```

### 3.4. Error Handling & Fallback

```csharp
try
{
    // Thử Antigravity trước
    result = await _antigravityService.TranslateBatchAsync(...);
}
catch (Exception ex) when (IsAntigravityError(ex))
{
    _logger.LogWarning("Antigravity failed, falling back to direct API: {Error}", ex.Message);
    // Fallback sang direct API
    result = await TranslateViaDirectApiAsync(...);
}
```

---

## 4. Cấu hình trong appsettings.json

```json
{
  "AntigravitySettings": {
    "Enabled": true,
    "BaseUrl": "http://host.docker.internal:8045/v1",
    "ApiKey": "sk-antigravity",
    "TimeoutSeconds": 120,
    "PreferredModels": ["gemini-3-flash", "gemini-2.5-flash"],
    "LoadBalanceRatio": 0.5,  // 50% traffic to Antigravity
    "FallbackOnError": true
  }
}
```

---

## 5. Docker Compose Update

```yaml
services:
  subphim-server:
    build: .
    container_name: subphim_app
    restart: always
    ports:
      - "80:8080"
    volumes:
      - ./app_data:/data
    environment:
      - TZ=Asia/Ho_Chi_Minh
      - AntigravitySettings__Enabled=true
      - AntigravitySettings__BaseUrl=http://host.docker.internal:8045/v1
    extra_hosts:
      - "host.docker.internal:host-gateway"  # Cho phép container gọi host
```

---

## 6. Flow xử lý request

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Translation Request                                   │
│                    (User submit SRT file)                               │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                 Check User Quota (LocalSrtLinesUsedToday)               │
│                 if exceeded → return error                              │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      Split into Batches                                  │
│                      (BatchSize from settings)                          │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                  For Each Batch (Parallel)                              │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │   Check Antigravity Available?                                     │ │
│  │        │                                                           │ │
│  │        ├── YES & LoadBalance → Antigravity API                    │ │
│  │        │       POST http://host.docker.internal:8045/v1/chat/...  │ │
│  │        │                                                           │ │
│  │        └── NO or Error → Direct API (existing logic)             │ │
│  │                POST https://generativelanguage.googleapis.com/... │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    Parse Response                                        │
│                    Extract translations by index                        │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    Update User Quota                                     │
│                    user.LocalSrtLinesUsedToday += successCount          │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    Save Results to DB                                    │
│                    Return to Client                                      │
└─────────────────────────────────────────────────────────────────────────┘
```


## 9. Checklist tích hợp

- [ ] Tạo `AntigravitySettings` class và đăng ký trong DI
- [ ] Tạo `AntigravityTranslationService` với health check và translate methods
- [ ] Cập nhật `TranslationOrchestratorService` để sử dụng hybrid logic
- [ ] Đảm bảo quota được tính chung cho cả 2 nguồn
- [ ] Thêm fallback khi Antigravity không available
- [ ] Cập nhật Docker Compose với `extra_hosts`
- [ ] Test với load cao để đảm bảo không conflict
- [ ] Monitor logs để theo dõi ratio giữa 2 nguồn

---

## 10. Lưu ý quan trọng

1. **Quota**: PHẢI dùng chung `User.LocalSrtLinesUsedToday` - không tạo quota riêng
2. **Thread Safety**: Antigravity đã xử lý concurrency, SubPhim.Server chỉ cần gọi
3. **Timeout**: Set timeout 120s cho requests dài
4. **429 Handling**: Antigravity tự xử lý 429 và rotate accounts, không cần retry logic, chỉ kiểm tra xem thiếu dòng nào hay không để retry
5. **Model Mapping**: Dùng model ID như trong bảng trên, không cần prefix

---

*Document created: 2026-01-15*
*Antigravity Server: http://34.56.197.96:8045/v1 (external) | http://host.docker.internal:8045/v1 (Docker)*
