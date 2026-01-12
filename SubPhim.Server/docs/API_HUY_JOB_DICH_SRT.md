# Hướng dẫn tích hợp API Hủy Job Dịch SRT (Local API)

## Mục lục
1. [Tổng quan](#tổng-quan)
2. [Các Endpoint mới](#các-endpoint-mới)
3. [Hướng dẫn tích hợp Client](#hướng-dẫn-tích-hợp-client)
4. [Ví dụ Code mẫu](#ví-dụ-code-mẫu)
5. [Xử lý lỗi](#xử-lý-lỗi)
6. [Best Practices](#best-practices)

---

## Tổng quan

API này cho phép client hủy các job dịch SRT đang chạy trên server. Khi user nhấn nút "Hủy" trên ứng dụng client, một request sẽ được gửi đến server để:

1. Dừng ngay lập tức các task dịch đang chạy
2. Giải phóng tài nguyên server (API keys, memory)
3. Hoàn trả lượt dịch cho user (các dòng chưa được dịch thành công)
4. Cập nhật trạng thái job thành "Failed" với lý do "Đã hủy bởi người dùng"

---

## Các Endpoint mới

### 1. Hủy một job cụ thể

**Endpoint:** `POST /api/launcheraio/cancel/{sessionId}`

**Headers:**
```
Authorization: Bearer {token}
Content-Type: application/json
```

**Parameters:**
- `sessionId` (path): ID của job cần hủy (nhận được từ response của `start-translation`)

**Response thành công (200 OK):**
```json
{
  "success": true,
  "message": "Đã hủy job thành công."
}
```

**Response lỗi (400 Bad Request):**
```json
{
  "success": false,
  "message": "Không tìm thấy job."
}
```
hoặc
```json
{
  "success": false,
  "message": "Job đã hoàn thành, không thể hủy."
}
```

**Response lỗi (403 Forbidden):**
```json
{
  "success": false,
  "message": "Bạn không có quyền hủy job này."
}
```

---

### 2. Hủy tất cả job của user

**Endpoint:** `POST /api/launcheraio/cancel-all`

**Headers:**
```
Authorization: Bearer {token}
Content-Type: application/json
```

**Response thành công (200 OK):**
```json
{
  "success": true,
  "cancelledJobsCount": 3,
  "message": "Đã hủy 3 job."
}
```

---

### 3. Lấy danh sách job đang chạy

**Endpoint:** `GET /api/launcheraio/active-jobs`

**Headers:**
```
Authorization: Bearer {token}
```

**Response thành công (200 OK):**
```json
{
  "jobs": [
    {
      "sessionId": "550e8400-e29b-41d4-a716-446655440000",
      "status": "Processing",
      "createdAt": "2025-01-15T10:30:00Z",
      "totalLines": 500
    },
    {
      "sessionId": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
      "status": "Pending",
      "createdAt": "2025-01-15T10:35:00Z",
      "totalLines": 200
    }
  ]
}
```

---

## Hướng dẫn tích hợp Client

### Luồng xử lý khuyến nghị

```
┌─────────────────────┐
│  User nhấn "Hủy"    │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│ Hiển thị dialog xác │
│ nhận "Bạn có chắc   │
│ muốn hủy?"          │
└─────────┬───────────┘
          │ Yes
          ▼
┌─────────────────────┐
│ Gọi API cancel/{id} │
└─────────┬───────────┘
          │
          ▼
    ┌─────┴─────┐
    │ Success?  │
    └─────┬─────┘
    Yes   │   No
          │
    ┌─────┴─────┐        ┌────────────────────┐
    │           │        │                    │
    ▼           ▼        ▼                    │
┌─────────┐  ┌─────────┐ ┌─────────────────┐  │
│ Thông   │  │ Hiển thị│ │ Kiểm tra lỗi    │  │
│ báo đã  │  │ lỗi     │ │ - 403: Không    │◄─┘
│ hủy     │  │         │ │   quyền         │
│ thành   │  │         │ │ - 400: Job đã   │
│ công    │  │         │ │   hoàn thành    │
└─────────┘  └─────────┘ └─────────────────┘
```

### Các điểm cần lưu ý

1. **Lưu `sessionId` sau khi gọi `start-translation`:**
   - Response từ `start-translation` chứa `sessionId`
   - Lưu giá trị này để có thể hủy job sau

2. **Disable nút "Hủy" khi không có job đang chạy:**
   - Sử dụng `GET /api/launcheraio/active-jobs` để kiểm tra

3. **Xử lý trường hợp job hoàn thành nhanh:**
   - Nếu job hoàn thành trước khi user nhấn hủy, API sẽ trả về lỗi 400
   - Hiển thị thông báo phù hợp cho user

4. **Polling kết quả:**
   - Sau khi hủy, dừng việc poll `get-results/{sessionId}`
   - Kết quả cuối cùng sẽ có `isCompleted: true` và `errorMessage: "Job đã bị hủy bởi người dùng."`

---

## Ví dụ Code mẫu

### C# (WPF/WinForms)

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

public class TranslationApiClient
{
    private readonly HttpClient _httpClient;
    private string _currentSessionId;
    
    public TranslationApiClient(string baseUrl, string bearerToken)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", bearerToken);
    }
    
    // Bắt đầu dịch và lưu sessionId
    public async Task<StartTranslationResponse> StartTranslationAsync(
        StartTranslationRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/launcheraio/start-translation", request);
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StartTranslationResponse>();
        
        // Lưu sessionId để có thể hủy sau
        if (result.Status == "Accepted")
        {
            _currentSessionId = result.SessionId;
        }
        
        return result;
    }
    
    // Hủy job hiện tại
    public async Task<CancelJobResponse> CancelCurrentJobAsync()
    {
        if (string.IsNullOrEmpty(_currentSessionId))
        {
            return new CancelJobResponse(false, "Không có job đang chạy.");
        }
        
        var response = await _httpClient.PostAsync(
            $"api/launcheraio/cancel/{_currentSessionId}", null);
        
        var result = await response.Content.ReadFromJsonAsync<CancelJobResponse>();
        
        if (result.Success)
        {
            _currentSessionId = null; // Reset sau khi hủy
        }
        
        return result;
    }
    
    // Hủy tất cả job
    public async Task<CancelAllResponse> CancelAllJobsAsync()
    {
        var response = await _httpClient.PostAsync(
            "api/launcheraio/cancel-all", null);
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CancelAllResponse>();
    }
    
    // Lấy danh sách job đang chạy
    public async Task<List<ActiveJobInfo>> GetActiveJobsAsync()
    {
        var response = await _httpClient.GetAsync("api/launcheraio/active-jobs");
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<ActiveJobsResponse>();
        return result.Jobs;
    }
}

// DTOs
public record StartTranslationResponse(
    string Status, 
    string Message, 
    string SessionId, 
    int RemainingLines);

public record CancelJobResponse(bool Success, string Message);

public record CancelAllResponse(
    bool Success, 
    int CancelledJobsCount, 
    string Message);

public record ActiveJobInfo(
    string SessionId, 
    string Status, 
    DateTime CreatedAt, 
    int TotalLines);

public record ActiveJobsResponse(List<ActiveJobInfo> Jobs);
```

### Ví dụ sử dụng trong WPF:

```csharp
// MainWindow.xaml.cs
public partial class MainWindow : Window
{
    private TranslationApiClient _apiClient;
    private CancellationTokenSource _pollingCts;
    
    // Event handler cho nút Hủy
    private async void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        // Hiển thị dialog xác nhận
        var result = MessageBox.Show(
            "Bạn có chắc chắn muốn hủy quá trình dịch?", 
            "Xác nhận hủy", 
            MessageBoxButton.YesNo, 
            MessageBoxImage.Question);
        
        if (result != MessageBoxResult.Yes)
            return;
        
        // Disable nút trong khi đang xử lý
        BtnCancel.IsEnabled = false;
        StatusText.Text = "Đang hủy job...";
        
        try
        {
            // Dừng polling kết quả
            _pollingCts?.Cancel();
            
            // Gọi API hủy
            var cancelResult = await _apiClient.CancelCurrentJobAsync();
            
            if (cancelResult.Success)
            {
                MessageBox.Show(
                    "Đã hủy job thành công. Lượt dịch chưa sử dụng đã được hoàn trả.", 
                    "Thành công", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
                
                // Reset UI về trạng thái ban đầu
                ResetUI();
            }
            else
            {
                MessageBox.Show(
                    $"Không thể hủy: {cancelResult.Message}", 
                    "Lỗi", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
            }
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(
                $"Lỗi kết nối: {ex.Message}", 
                "Lỗi", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error);
        }
        finally
        {
            BtnCancel.IsEnabled = true;
        }
    }
    
    private void ResetUI()
    {
        StatusText.Text = "Sẵn sàng";
        ProgressBar.Value = 0;
        BtnStart.IsEnabled = true;
        BtnCancel.IsEnabled = false;
    }
}
```

### Python

```python
import requests
from typing import Optional, List
from dataclasses import dataclass
from datetime import datetime

@dataclass
class ActiveJobInfo:
    session_id: str
    status: str
    created_at: datetime
    total_lines: int

class TranslationApiClient:
    def __init__(self, base_url: str, bearer_token: str):
        self.base_url = base_url.rstrip('/')
        self.session = requests.Session()
        self.session.headers.update({
            'Authorization': f'Bearer {bearer_token}',
            'Content-Type': 'application/json'
        })
        self._current_session_id: Optional[str] = None
    
    def start_translation(self, genre: str, target_language: str, 
                         lines: List[dict], system_instruction: str) -> dict:
        """Bắt đầu dịch và lưu session_id."""
        response = self.session.post(
            f'{self.base_url}/api/launcheraio/start-translation',
            json={
                'Genre': genre,
                'TargetLanguage': target_language,
                'Lines': lines,
                'SystemInstruction': system_instruction
            }
        )
        response.raise_for_status()
        result = response.json()
        
        if result.get('status') == 'Accepted':
            self._current_session_id = result.get('sessionId')
        
        return result
    
    def cancel_job(self, session_id: str = None) -> dict:
        """Hủy job theo session_id hoặc job hiện tại."""
        sid = session_id or self._current_session_id
        if not sid:
            return {'success': False, 'message': 'Không có job đang chạy.'}
        
        response = self.session.post(
            f'{self.base_url}/api/launcheraio/cancel/{sid}'
        )
        
        result = response.json()
        if result.get('success'):
            self._current_session_id = None
        
        return result
    
    def cancel_all_jobs(self) -> dict:
        """Hủy tất cả job của user."""
        response = self.session.post(
            f'{self.base_url}/api/launcheraio/cancel-all'
        )
        response.raise_for_status()
        return response.json()
    
    def get_active_jobs(self) -> List[ActiveJobInfo]:
        """Lấy danh sách job đang chạy."""
        response = self.session.get(
            f'{self.base_url}/api/launcheraio/active-jobs'
        )
        response.raise_for_status()
        result = response.json()
        
        return [
            ActiveJobInfo(
                session_id=job['sessionId'],
                status=job['status'],
                created_at=datetime.fromisoformat(job['createdAt'].rstrip('Z')),
                total_lines=job['totalLines']
            )
            for job in result.get('jobs', [])
        ]


# Ví dụ sử dụng
if __name__ == '__main__':
    client = TranslationApiClient(
        base_url='http://localhost:5000',
        bearer_token='your-jwt-token'
    )
    
    # Bắt đầu dịch
    result = client.start_translation(
        genre='Huyền Huyễn',
        target_language='vi',
        lines=[
            {'Index': 1, 'OriginalText': '你好世界'},
            {'Index': 2, 'OriginalText': '这是测试'}
        ],
        system_instruction='Dịch sang tiếng Việt tự nhiên'
    )
    
    print(f"Job started: {result['sessionId']}")
    
    # Giả lập user nhấn hủy
    import time
    time.sleep(2)
    
    cancel_result = client.cancel_job()
    print(f"Cancel result: {cancel_result}")
```

---

## Xử lý lỗi

| HTTP Status | Ý nghĩa | Xử lý khuyến nghị |
|-------------|---------|-------------------|
| 200 | Hủy thành công | Hiển thị thông báo, reset UI |
| 400 | Job không tồn tại hoặc đã hoàn thành | Kiểm tra lại trạng thái job |
| 401 | Token không hợp lệ hoặc hết hạn | Yêu cầu user đăng nhập lại |
| 403 | Không có quyền hủy job này | Thông báo lỗi cho user |
| 500 | Lỗi server | Thử lại sau hoặc liên hệ hỗ trợ |

---

## Best Practices

### 1. Luôn xác nhận trước khi hủy
```
- Hiển thị dialog xác nhận
- Thông báo rõ ràng về hậu quả
```

### 2. Disable UI trong khi xử lý
```
- Disable nút "Hủy" khi đang gửi request
- Hiển thị trạng thái "Đang hủy..."
```

### 3. Xử lý race condition
```
- Job có thể hoàn thành trước khi request hủy đến server
- Luôn kiểm tra response và xử lý trường hợp lỗi
```

### 4. Cleanup sau khi hủy
```
- Dừng polling kết quả
- Reset session_id
- Cập nhật UI về trạng thái ban đầu
```

### 5. Thông báo về việc hoàn trả lượt dịch
```
- Server tự động hoàn trả lượt dịch cho các dòng chưa xử lý
- Hiển thị thông báo để user biết
```

---

## Changelog

| Phiên bản | Ngày | Thay đổi |
|-----------|------|----------|
| 1.0.0 | 2025-01-15 | Thêm tính năng hủy job dịch SRT |

---

## Liên hệ hỗ trợ

Nếu có câu hỏi hoặc gặp vấn đề khi tích hợp, vui lòng liên hệ:
- GitHub Issues: https://github.com/visecal/cpp2/issues
