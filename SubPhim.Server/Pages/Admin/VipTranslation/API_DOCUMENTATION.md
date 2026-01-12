# API Documentation - VIP Translation Service

## Tổng quan

VIP Translation API cung cấp dịch vụ dịch văn bản tự động sử dụng AI với hệ thống credit và rate limiting. API hỗ trợ xác thực qua API Key và theo dõi chi tiết việc sử dụng.

**Base URL**: `https://your-domain.com/api/v1/external`

---

## Xác thực (Authentication)

### API Key Format
API Key có định dạng: `AIO_xxxxxxxxxxxxxxxxxxxxxxxxx`

### Cách sử dụng
Có 2 cách để truyền API Key vào header:

**Cách 1: Authorization Bearer (Khuyến nghị)**
```
Authorization: Bearer AIO_xxxxxxxxxxxxxxxxxxxxxxxxx
```

**Cách 2: X-API-Key header**
```
X-API-Key: AIO_xxxxxxxxxxxxxxxxxxxxxxxxx
```

### Ví dụ với cURL
```bash
# Sử dụng Authorization Bearer
curl -X GET "https://your-domain.com/api/v1/external/account/info" \
  -H "Authorization: Bearer AIO_xxxxxxxxxxxxxxxxxxxxxxxxx"

# Hoặc sử dụng X-API-Key header
curl -X GET "https://your-domain.com/api/v1/external/account/info" \
  -H "X-API-Key: AIO_xxxxxxxxxxxxxxxxxxxxxxxxx"
```

### Lỗi xác thực
- **401 Unauthorized**: API Key không hợp lệ hoặc đã bị vô hiệu hóa
- **403 Forbidden**: API Key đã hết hạn hoặc không có quyền truy cập

### Bảo mật API Key
- **KHÔNG BAO GIỜ** chia sẻ API Key công khai
- Lưu API Key trong biến môi trường hoặc file cấu hình bảo mật
- Rotate (thay đổi) API Key định kỳ
- Mỗi ứng dụng nên có API Key riêng để dễ quản lý và thu hồi

---

## Endpoints chính

### 1. Lấy thông tin tài khoản

**Endpoint**: `GET /account/info`

**Mô tả**: Lấy thông tin chi tiết về API Key, số dư credit, và giới hạn RPM.

**Response**:
```json
{
  "keyId": "AIO_...abcd",
  "displayName": "My API Key",
  "creditBalance": 50000,
  "rpmLimit": 100,
  "currentRpmUsage": 5,
  "pricing": {
    "creditsPerCharacter": 1,
    "vndPerCredit": 10
  }
}
```

**Ví dụ**:
```bash
curl -X GET "https://your-domain.com/api/v1/external/account/info" \
  -H "Authorization: Bearer YOUR_API_KEY"
```

---

### 2. Ước tính chi phí dịch

**Endpoint**: `POST /estimate`

**Mô tả**: Ước tính số credit cần thiết cho việc dịch văn bản.

**Request Body**:
```json
{
  "characterCount": 1000
}
```

**Response**:
```json
{
  "characterCount": 1000,
  "estimatedCredits": 1000,
  "estimatedCostVnd": 10000,
  "currentBalance": 50000,
  "hasEnoughCredits": true
}
```

**Ví dụ**:
```bash
curl -X POST "https://your-domain.com/api/v1/external/estimate" \
  -H "Authorization: Bearer YOUR_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"characterCount": 1000}'
```

---

### 3. Bắt đầu dịch văn bản

**Endpoint**: `POST /translation/start`

**Mô tả**: Tạo job dịch mới. Job sẽ xử lý bất đồng bộ.

**Request Body**:
```json
{
  "targetLanguage": "Vietnamese",
  "lines": [
    {
      "index": 1,
      "text": "Hello world"
    },
    {
      "index": 2,
      "text": "How are you?"
    }
  ],
  "systemInstruction": "Dịch tự nhiên, phù hợp ngữ cảnh"
}
```

**Parameters**:
- `targetLanguage` (required): Ngôn ngữ đích (ví dụ: "Vietnamese", "English", "Japanese", "Korean", "Thai", "Chinese")
- `lines` (required): Mảng các dòng cần dịch
  - `index`: Số thứ tự dòng (phải là số nguyên dương)
  - `text`: Nội dung cần dịch (tối đa 3000 ký tự/dòng)
- `systemInstruction` (optional): Hướng dẫn đặc biệt cho AI về cách dịch. Ví dụ:
  - "Dịch tự nhiên, giữ nguyên tên riêng"
  - "Dịch phong cách trang trọng, phù hợp văn phong công sở"
  - "Dịch phong cách trẻ trung, sử dụng ngôn ngữ đời thường"
  - Nếu không truyền, mặc định sẽ dùng: "Dịch tự nhiên, phù hợp ngữ cảnh"

**Response Success (202 Accepted)**:
```json
{
  "status": "Accepted",
  "sessionId": "abc123def456",
  "estimatedCredits": 500,
  "message": "Job started successfully"
}
```

**Response Error (402 Payment Required)**:
```json
{
  "status": "InsufficientCredits",
  "currentBalance": 100,
  "estimatedRequired": 500,
  "message": "Không đủ credit. Vui lòng nạp thêm."
}
```

**Response Error (400 Bad Request)**:
```json
{
  "status": "InvalidRequest",
  "message": "Lines array is required and must not be empty"
}
```

**Ví dụ**:
```bash
curl -X POST "https://your-domain.com/api/v1/external/translation/start" \
  -H "Authorization: Bearer YOUR_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "targetLanguage": "Vietnamese",
    "lines": [
      {"index": 1, "text": "Hello world"},
      {"index": 2, "text": "How are you?"}
    ]
  }'
```

---

### 4. Lấy kết quả dịch

**Endpoint**: `GET /translation/result/{sessionId}`

**Mô tả**: Lấy kết quả dịch. Poll endpoint này để kiểm tra tiến độ và nhận kết quả.

**Response (Processing)**:
```json
{
  "status": "Processing",
  "progress": {
    "completedLines": 1,
    "totalLines": 2,
    "percentage": 50
  },
  "newLines": [
    {
      "index": 1,
      "translatedText": "Xin chào thế giới"
    }
  ]
}
```

**Response (Completed)**:
```json
{
  "status": "Completed",
  "result": {
    "lines": [
      {
        "index": 1,
        "translatedText": "Xin chào thế giới"
      },
      {
        "index": 2,
        "translatedText": "Bạn khỏe không?"
      }
    ],
    "totalCharacters": 35,
    "creditsCharged": 35,
    "geminiErrors": []
  }
}
```

**Response (Failed)**:
```json
{
  "status": "Failed",
  "error": {
    "code": "TRANSLATION_ERROR",
    "message": "API rate limit exceeded",
    "creditsRefunded": 35
  }
}
```

**Ví dụ**:
```bash
curl -X GET "https://your-domain.com/api/v1/external/translation/result/abc123def456" \
  -H "Authorization: Bearer YOUR_API_KEY"
```

**Best Practice**: Poll mỗi 2-5 giây cho đến khi `status` là "Completed" hoặc "Failed".

---

### 5. Hủy job dịch

**Endpoint**: `POST /translation/cancel/{sessionId}`

**Mô tả**: Hủy job đang chạy và hoàn trả credit chưa sử dụng.

**Response**:
```json
{
  "status": "Cancelled",
  "creditsRefunded": 250,
  "message": "Job đã hủy. Credit chưa sử dụng đã được hoàn trả."
}
```

**Ví dụ**:
```bash
curl -X POST "https://your-domain.com/api/v1/external/translation/cancel/abc123def456" \
  -H "Authorization: Bearer YOUR_API_KEY"
```

---

### 6. Xem lịch sử sử dụng

**Endpoint**: `GET /account/usage`

**Mô tả**: Xem lịch sử các job đã thực hiện.

**Query Parameters**:
- `from` (optional): Lọc từ ngày (ISO 8601 format)
- `to` (optional): Lọc đến ngày (ISO 8601 format)
- `page` (default: 1): Trang hiện tại
- `pageSize` (default: 50, max: 100): Số items mỗi trang

**Response**:
```json
{
  "summary": {
    "totalJobs": 150,
    "totalCreditsUsed": 45000,
    "totalCharactersTranslated": 45000,
    "estimatedCostVnd": 450000
  },
  "items": [
    {
      "sessionId": "abc123def456",
      "startedAt": "2024-12-09T10:30:00Z",
      "completedAt": "2024-12-09T10:30:45Z",
      "status": "Completed",
      "inputLines": 10,
      "outputCharacters": 500,
      "creditsCharged": 500,
      "targetLanguage": "Vietnamese",
      "durationMs": 45000,
      "geminiErrors": []
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalPages": 3,
    "totalItems": 150
  }
}
```

**Ví dụ**:
```bash
curl -X GET "https://your-domain.com/api/v1/external/account/usage?page=1&pageSize=20" \
  -H "Authorization: Bearer YOUR_API_KEY"
```

---

### 7. Xem lịch sử giao dịch credit

**Endpoint**: `GET /account/transactions`

**Mô tả**: Xem lịch sử nạp/trừ credit.

**Query Parameters**:
- `page` (default: 1): Trang hiện tại
- `pageSize` (default: 50, max: 100): Số items mỗi trang

**Response**:
```json
{
  "currentBalance": 50000,
  "items": [
    {
      "id": 123,
      "type": "Purchase",
      "amount": 10000,
      "balanceAfter": 50000,
      "description": "Nạp credit lần 1",
      "createdAt": "2024-12-09T10:00:00Z",
      "createdBy": "Admin"
    },
    {
      "id": 124,
      "type": "Usage",
      "amount": -500,
      "balanceAfter": 49500,
      "description": "Job abc123def456",
      "createdAt": "2024-12-09T10:30:45Z",
      "createdBy": "System"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalPages": 5,
    "totalItems": 225
  }
}
```

**Transaction Types**:
- `Purchase`: Nạp credit
- `Usage`: Trừ credit khi dùng dịch vụ
- `Refund`: Hoàn trả credit
- `Adjustment`: Điều chỉnh bởi admin

**Ví dụ**:
```bash
curl -X GET "https://your-domain.com/api/v1/external/account/transactions?page=1" \
  -H "Authorization: Bearer YOUR_API_KEY"
```

---

## Giới hạn và Quota

### Rate Limiting
- **RPM (Requests Per Minute)**: Mỗi API Key có giới hạn số request/phút riêng (mặc định: 100 RPM)
- Khi vượt giới hạn, API trả về **429 Too Many Requests**
- Header `Retry-After` chỉ định thời gian chờ (giây)

**Response Headers theo dõi rate limit:**
- `X-RateLimit-Limit`: Số request tối đa mỗi phút
- `X-RateLimit-Remaining`: Số request còn lại trong phút hiện tại
- `X-RateLimit-Reset`: Thời điểm reset (Unix timestamp)

**Ví dụ Response Headers:**
```
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 85
X-RateLimit-Reset: 1702123620
```

**Best Practice:** Kiểm tra các headers này để tránh bị rate limited

### Credit System
- **1 credit = 1 ký tự đầu ra đã dịch**
- Credit chỉ bị trừ khi job hoàn thành thành công
- Credit tự động hoàn trả nếu job thất bại hoặc bị hủy

### Giới hạn kỹ thuật
- Tối đa **3000 ký tự/dòng** trong request
- Tối đa **100 items/page** khi query lịch sử

---

## Mã lỗi (Error Codes)

| HTTP Status | Status Message | Mô tả |
|-------------|----------------|-------|
| 400 | InvalidRequest | Request thiếu thông tin hoặc sai format |
| 401 | Unauthorized | API Key không hợp lệ |
| 402 | InsufficientCredits | Không đủ credit để thực hiện |
| 403 | Forbidden | API Key không có quyền truy cập |
| 404 | NotFound | Session không tồn tại |
| 429 | TooManyRequests | Vượt quá giới hạn RPM |
| 500 | Error | Lỗi server |

---

## Workflow Tiêu chuẩn

### Quy trình dịch văn bản cơ bản

```
1. Ước tính chi phí (optional)
   POST /estimate

2. Bắt đầu dịch
   POST /translation/start
   → Nhận sessionId

3. Poll kết quả (mỗi 2-5s)
   GET /translation/result/{sessionId}
   → Kiểm tra status
   
4. Nhận kết quả khi status = "Completed"
```

### Code Example (Python)

```python
import requests
import time

API_KEY = "AIO_xxxxxxxxxxxxxxxxxxxxxxxxx"
BASE_URL = "https://your-domain.com/api/v1/external"
HEADERS = {"Authorization": f"Bearer {API_KEY}"}

# 1. Ước tính chi phí
estimate_resp = requests.post(
    f"{BASE_URL}/estimate",
    headers=HEADERS,
    json={"characterCount": 1000}
)
print(f"Estimated credits: {estimate_resp.json()['estimatedCredits']}")

# 2. Bắt đầu dịch
start_resp = requests.post(
    f"{BASE_URL}/translation/start",
    headers=HEADERS,
    json={
        "targetLanguage": "Vietnamese",
        "lines": [
            {"index": 1, "text": "Hello world"},
            {"index": 2, "text": "How are you?"}
        ]
    }
)
session_id = start_resp.json()["sessionId"]
print(f"Job started: {session_id}")

# 3. Poll kết quả
while True:
    result_resp = requests.get(
        f"{BASE_URL}/translation/result/{session_id}",
        headers=HEADERS
    )
    data = result_resp.json()
    
    if data["status"] == "Completed":
        print("Translation completed!")
        for line in data["result"]["lines"]:
            print(f"{line['index']}: {line['translatedText']}")
        break
    elif data["status"] == "Failed":
        print(f"Translation failed: {data['error']['message']}")
        break
    else:
        print(f"Progress: {data['progress']['percentage']}%")
        time.sleep(3)
```

### Code Example (JavaScript/Node.js)

```javascript
const axios = require('axios');

const API_KEY = 'AIO_xxxxxxxxxxxxxxxxxxxxxxxxx';
const BASE_URL = 'https://your-domain.com/api/v1/external';
const headers = { 'Authorization': `Bearer ${API_KEY}` };

async function translateText() {
  try {
    // 1. Bắt đầu dịch
    const startResp = await axios.post(
      `${BASE_URL}/translation/start`,
      {
        targetLanguage: 'Vietnamese',
        lines: [
          { index: 1, text: 'Hello world' },
          { index: 2, text: 'How are you?' }
        ]
      },
      { headers }
    );
    
    const sessionId = startResp.data.sessionId;
    console.log(`Job started: ${sessionId}`);
    
    // 2. Poll kết quả
    while (true) {
      await new Promise(resolve => setTimeout(resolve, 3000));
      
      const resultResp = await axios.get(
        `${BASE_URL}/translation/result/${sessionId}`,
        { headers }
      );
      
      const data = resultResp.data;
      
      if (data.status === 'Completed') {
        console.log('Translation completed!');
        data.result.lines.forEach(line => {
          console.log(`${line.index}: ${line.translatedText}`);
        });
        break;
      } else if (data.status === 'Failed') {
        console.error(`Translation failed: ${data.error.message}`);
        break;
      } else {
        console.log(`Progress: ${data.progress.percentage}%`);
      }
    }
  } catch (error) {
    console.error('Error:', error.response?.data || error.message);
  }
}

translateText();
```

---

## Quản lý Credit & Thanh toán

### Cách thức hoạt động
1. **Nạp credit**: Liên hệ admin để nạp credit vào tài khoản
2. **Sử dụng**: Credit tự động trừ khi job dịch hoàn thành
3. **Hoàn trả**: Credit được hoàn lại nếu job thất bại hoặc bị hủy

### Tính toán chi phí
```
Credit cần = Số ký tự output đã dịch
Ví dụ: 1000 ký tự đã dịch = 1000 credits
```

### Xem pricing
```bash
curl -X GET "https://your-domain.com/api/v1/external/account/info" \
  -H "Authorization: Bearer YOUR_API_KEY"
```

Response sẽ có field `pricing`:
```json
{
  "pricing": {
    "creditsPerCharacter": 1,
    "vndPerCredit": 10
  }
}
```

### Theo dõi chi tiêu
- Xem tổng credit đã dùng: `GET /account/usage`
- Xem chi tiết từng giao dịch: `GET /account/transactions`
- Xem số dư hiện tại: `GET /account/info`

---

## Best Practices

### 1. Xử lý lỗi đầy đủ
```python
import requests

try:
    response = requests.post(url, headers=headers, json=data)
    response.raise_for_status()
except requests.exceptions.HTTPError as e:
    if e.response.status_code == 402:
        print("Insufficient credits!")
    elif e.response.status_code == 429:
        # Rate limit exceeded - check Retry-After header
        retry_after = int(e.response.headers.get('Retry-After', 60))
        print(f"Rate limit exceeded! Retry after {retry_after} seconds")
    else:
        print(f"Error: {e.response.json()}")
```

### 2. Theo dõi Rate Limit trong code
```python
def make_api_request(url, headers, data=None):
    """Make API request and monitor rate limit"""
    response = requests.post(url, headers=headers, json=data) if data else requests.get(url, headers=headers)
    
    # Check rate limit headers
    limit = response.headers.get('X-RateLimit-Limit')
    remaining = response.headers.get('X-RateLimit-Remaining')
    reset = response.headers.get('X-RateLimit-Reset')
    
    if remaining and int(remaining) < 10:
        print(f"Warning: Only {remaining} requests remaining in current window")
    
    return response

# Sử dụng
response = make_api_request(f"{BASE_URL}/account/info", headers)
```

### 3. Implement retry logic với exponential backoff
```python
import time

def call_api_with_retry(func, max_retries=3):
    for i in range(max_retries):
        try:
            return func()
        except requests.exceptions.HTTPError as e:
            if e.response.status_code == 429:
                # Rate limit - respect Retry-After header
                retry_after = int(e.response.headers.get('Retry-After', 60))
                print(f"Rate limited. Waiting {retry_after} seconds...")
                time.sleep(retry_after)
            elif i == max_retries - 1:
                raise
            else:
                wait_time = 2 ** i  # 1s, 2s, 4s
                time.sleep(wait_time)
```

### 4. Kiểm tra credit trước khi gửi job lớn
```python
# Ước tính trước
estimate = api.estimate(char_count)
if not estimate['hasEnoughCredits']:
    print(f"Need {estimate['estimatedCredits']} credits, only have {estimate['currentBalance']}")
    return
    
# Tiếp tục với job
```

### 5. Cache API Key
- Không hardcode API Key trong code
- Sử dụng environment variables hoặc config files
- Bảo mật API Key như password

**Ví dụ Python với environment variable:**
```python
import os

API_KEY = os.environ.get('VIP_TRANSLATION_API_KEY')
if not API_KEY:
    raise ValueError("API_KEY not found in environment variables")
```

### 6. Log và Monitor
- Log tất cả các API calls và responses
- Monitor credit balance
- Set up alerts khi credit thấp

---

## Câu hỏi thường gặp (FAQ)

### Q: Làm sao để lấy API Key?
**A**: Liên hệ admin để được cấp API Key. Admin sẽ tạo key và cung cấp cho bạn.

### Q: API Key có thời hạn sử dụng không?
**A**: Tùy vào cấu hình của admin. Một số key có `expiresAt`, một số không giới hạn thời gian.

### Q: Tôi có thể dùng bao nhiêu request cùng lúc?
**A**: Giới hạn bởi RPM của API Key. Kiểm tra `rpmLimit` trong `GET /account/info`.

### Q: Credit có hết hạn không?
**A**: Không, credit không hết hạn và có thể sử dụng bất cứ lúc nào.

### Q: Nếu job thất bại, tôi có bị mất credit?
**A**: Không, credit chỉ bị trừ khi job hoàn thành thành công. Nếu thất bại, credit sẽ được hoàn trả.

### Q: Làm sao để biết job đã hoàn thành?
**A**: Poll endpoint `GET /translation/result/{sessionId}` cho đến khi `status` là "Completed" hoặc "Failed".

### Q: Tôi có thể hủy job đang chạy không?
**A**: Có, dùng endpoint `POST /translation/cancel/{sessionId}`.

### Q: Giới hạn ký tự tối đa cho mỗi dòng?
**A**: 3000 ký tự/dòng. Request sẽ bị reject nếu vượt quá.

---

## Liên hệ & Hỗ trợ

Để được hỗ trợ về:
- Tạo API Key mới
- Nạp credit
- Thắc mắc về pricing
- Vấn đề kỹ thuật

Vui lòng liên hệ admin của hệ thống.

---

**Version**: 1.0  
**Last Updated**: 2024-12-09
