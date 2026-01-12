# Hướng dẫn API Dịch Phụ Đề VIP (VIP Subtitle Translation API)

## Tổng quan

API Dịch Phụ Đề VIP cho phép dịch phụ đề SRT sử dụng Gemini API với quota quản lý theo từng user. Hệ thống tự động reset quota vào 12:00 sáng mỗi ngày theo giờ Việt Nam.

### Đặc điểm chính

- **Quota theo user**: 
  - Free: 0 dòng/ngày (mặc định)
  - Monthly: 3000 dòng/ngày
  - Yearly: 15000 dòng/ngày
- **Validation**: Từ chối dịch nếu bất kỳ dòng SRT nào > 3000 ký tự
- **Proxy**: Sử dụng chung proxy infrastructure với LocalApi
- **Session-based**: Dịch bất đồng bộ với sessionID để lấy kết quả

---

## Base URL

```
http://your-server:5000/api/viptranslation
```

---

## Authentication

Tất cả các endpoint yêu cầu JWT authentication trong header:

```
Authorization: Bearer {your-jwt-token}
```

---

## Endpoints

### 1. Bắt đầu dịch (Start Translation)

**Endpoint:** `POST /api/viptranslation/start`

**Headers:**
```
Authorization: Bearer {token}
Content-Type: application/json
```

**Request Body:**
```json
{
  "TargetLanguage": "Vietnamese",
  "SystemInstruction": "Dịch phụ đề phim hành động sang tiếng Việt tự nhiên",
  "Lines": [
    {
      "Index": 1,
      "OriginalText": "Hello, how are you?"
    },
    {
      "Index": 2,
      "OriginalText": "I'm fine, thank you."
    }
  ]
}
```

**Response Success (200 OK):**
```json
{
  "status": "Accepted",
  "sessionId": "550e8400-e29b-41d4-a716-446655440000",
  "message": null
}
```

**Response Error - Quota Exceeded (429 Too Many Requests):**
```json
{
  "status": "Error",
  "message": "Bạn đã hết lượt dịch VIP hôm nay. Giới hạn: 3000 dòng/ngày.",
  "sessionId": null
}
```

**Response Error - Line Too Long (429 Too Many Requests):**
```json
{
  "status": "Error",
  "message": "Dòng 5 vượt quá giới hạn 3000 ký tự. Vui lòng kiểm tra lại file SRT.",
  "sessionId": null
}
```

**Response Error - Not Enough Lines (429 Too Many Requests):**
```json
{
  "status": "Error",
  "message": "Không đủ lượt dịch. Yêu cầu: 500 dòng, còn lại: 200 dòng.",
  "sessionId": null
}
```

---

### 2. Lấy kết quả dịch (Get Results)

**Endpoint:** `GET /api/viptranslation/result/{sessionId}`

**Headers:**
```
Authorization: Bearer {token}
```

**Parameters:**
- `sessionId` (path): Session ID nhận được từ endpoint start

**Response (200 OK):**
```json
{
  "newLines": [
    {
      "index": 1,
      "translatedText": "Xin chào, bạn khỏe không?",
      "success": true
    },
    {
      "index": 2,
      "translatedText": "Tôi khỏe, cảm ơn bạn.",
      "success": true
    }
  ],
  "isCompleted": false,
  "errorMessage": null
}
```

**Khi hoàn thành:**
```json
{
  "newLines": [...],
  "isCompleted": true,
  "errorMessage": null
}
```

**Khi có lỗi:**
```json
{
  "newLines": [...],
  "isCompleted": true,
  "errorMessage": "Một số lỗi đã xảy ra trong quá trình dịch"
}
```

**Response Not Found (404):**
```json
"Session không hợp lệ hoặc đã hết hạn."
```

---

### 3. Hủy job dịch (Cancel Job)

**Endpoint:** `POST /api/viptranslation/cancel/{sessionId}`

**Headers:**
```
Authorization: Bearer {token}
```

**Parameters:**
- `sessionId` (path): Session ID của job cần hủy

**Response Success (200 OK):**
```json
{
  "success": true,
  "message": "Đã hủy job thành công. Lượt dịch chưa sử dụng đã được hoàn trả."
}
```

**Response Error (400 Bad Request):**
```json
{
  "success": false,
  "message": "Không thể hủy job. Job không tồn tại hoặc đã hoàn thành."
}
```

---

## Luồng sử dụng API

### 1. Polling kết quả

```javascript
// Bước 1: Bắt đầu dịch
const startResponse = await fetch('http://server:5000/api/viptranslation/start', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${token}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    TargetLanguage: 'Vietnamese',
    SystemInstruction: 'Dịch tự nhiên',
    Lines: lines
  })
});

const { sessionId } = await startResponse.json();

// Bước 2: Poll kết quả mỗi 2 giây
const pollInterval = setInterval(async () => {
  const resultResponse = await fetch(
    `http://server:5000/api/viptranslation/result/${sessionId}`,
    {
      headers: { 'Authorization': `Bearer ${token}` }
    }
  );
  
  const result = await resultResponse.json();
  
  // Hiển thị tiến độ
  console.log(`Đã dịch: ${result.newLines.length} dòng`);
  
  if (result.isCompleted) {
    clearInterval(pollInterval);
    if (result.errorMessage) {
      console.error('Lỗi:', result.errorMessage);
    } else {
      console.log('Hoàn thành!', result.newLines);
    }
  }
}, 2000);
```

### 2. Hủy job khi cần

```javascript
const cancelResponse = await fetch(
  `http://server:5000/api/viptranslation/cancel/${sessionId}`,
  {
    method: 'POST',
    headers: { 'Authorization': `Bearer ${token}` }
  }
);

const { success, message } = await cancelResponse.json();
console.log(message);
```

---

## C# Example (WPF/WinForms)

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

public class VipTranslationApiClient
{
    private readonly HttpClient _httpClient;
    private string _currentSessionId;
    
    public VipTranslationApiClient(string baseUrl, string bearerToken)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", bearerToken);
    }
    
    // Bắt đầu dịch
    public async Task<StartVipTranslationResponse> StartTranslationAsync(
        string targetLanguage, 
        List<SrtLine> lines, 
        string systemInstruction)
    {
        var request = new
        {
            TargetLanguage = targetLanguage,
            Lines = lines,
            SystemInstruction = systemInstruction
        };
        
        var response = await _httpClient.PostAsJsonAsync(
            "api/viptranslation/start", request);
        
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var error = await response.Content.ReadFromJsonAsync<StartVipTranslationResponse>();
            throw new Exception(error.Message);
        }
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StartVipTranslationResponse>();
        
        _currentSessionId = result.SessionId;
        return result;
    }
    
    // Lấy kết quả
    public async Task<GetVipResultsResponse> GetResultsAsync(string sessionId = null)
    {
        var sid = sessionId ?? _currentSessionId;
        var response = await _httpClient.GetAsync($"api/viptranslation/result/{sid}");
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<GetVipResultsResponse>();
    }
    
    // Hủy job
    public async Task<bool> CancelJobAsync(string sessionId = null)
    {
        var sid = sessionId ?? _currentSessionId;
        var response = await _httpClient.PostAsync(
            $"api/viptranslation/cancel/{sid}", null);
        
        var result = await response.Content.ReadFromJsonAsync<CancelVipJobResponse>();
        return result.Success;
    }
}

// DTOs
public record StartVipTranslationResponse(
    string Status, 
    string SessionId, 
    string Message);

public record GetVipResultsResponse(
    List<TranslatedSrtLine> NewLines, 
    bool IsCompleted, 
    string ErrorMessage);

public record CancelVipJobResponse(bool Success, string Message);

public class SrtLine
{
    public int Index { get; set; }
    public string OriginalText { get; set; }
}

public class TranslatedSrtLine
{
    public int Index { get; set; }
    public string TranslatedText { get; set; }
    public bool Success { get; set; }
}
```

---

## Error Codes

| HTTP Status | Ý nghĩa | Xử lý |
|-------------|---------|-------|
| 200 | Thành công | Xử lý kết quả |
| 401 | Token không hợp lệ | Đăng nhập lại |
| 404 | Session không tồn tại | Kiểm tra sessionId |
| 429 | Hết quota hoặc validation failed | Hiển thị thông báo lỗi |
| 500 | Lỗi server | Thử lại sau |

---

## Limitations

- **Max line length**: 3000 ký tự/dòng
- **Quota reset**: 12:00 AM giờ Việt Nam mỗi ngày
- **Default quota**: 
  - Free tier: 0 dòng/ngày
  - Monthly tier: 3000 dòng/ngày
  - Yearly tier: 15000 dòng/ngày
- **Session timeout**: Sessions có thể hết hạn sau một khoảng thời gian không hoạt động

---

## Best Practices

1. **Kiểm tra quota trước khi dịch**: Tính toán số dòng trước khi gọi API
2. **Validate độ dài dòng**: Đảm bảo không có dòng nào > 3000 ký tự
3. **Polling frequency**: Poll kết quả mỗi 2-3 giây, không nên quá thường xuyên
4. **Handle errors gracefully**: Hiển thị thông báo lỗi rõ ràng cho user
5. **Cancel when needed**: Hủy job khi user dừng để hoàn trả quota
6. **Session cleanup**: Không lưu sessionId lâu dài, chúng có thể hết hạn

---

## Changelog

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2025-12-07 | Initial release của VIP Translation API |

---

## Support

Nếu có câu hỏi hoặc gặp vấn đề, vui lòng liên hệ qua GitHub Issues: https://github.com/visecal/cpp2/issues
