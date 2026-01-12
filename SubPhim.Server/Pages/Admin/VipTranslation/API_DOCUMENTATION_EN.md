# VIP Translation API Documentation

## Overview

VIP Translation API provides AI-powered text translation with credit-based billing and rate limiting. The API uses API Key authentication and detailed usage tracking.

**Base URL**: `https://your-domain.com/api/v1/external`

---

## Authentication

### API Key Format
API Keys follow the format: `AIO_xxxxxxxxxxxxxxxxxxxxxxxxx`

### Usage
There are 2 ways to pass the API Key in headers:

**Method 1: Authorization Bearer (Recommended)**
```
Authorization: Bearer AIO_xxxxxxxxxxxxxxxxxxxxxxxxx
```

**Method 2: X-API-Key header**
```
X-API-Key: AIO_xxxxxxxxxxxxxxxxxxxxxxxxx
```

### Example with cURL
```bash
# Using Authorization Bearer
curl -X GET "https://your-domain.com/api/v1/external/account/info" \
  -H "Authorization: Bearer AIO_xxxxxxxxxxxxxxxxxxxxxxxxx"

# Or using X-API-Key header
curl -X GET "https://your-domain.com/api/v1/external/account/info" \
  -H "X-API-Key: AIO_xxxxxxxxxxxxxxxxxxxxxxxxx"
```

### Authentication Errors
- **401 Unauthorized**: Invalid or disabled API Key
- **403 Forbidden**: API Key expired or lacks permission

### API Key Security
- **NEVER** share API Keys publicly
- Store API Keys in environment variables or secure config files
- Rotate API Keys periodically
- Use separate API Keys for each application for easier management and revocation

---

## Core Endpoints

### 1. Get Account Information

**Endpoint**: `GET /account/info`

**Description**: Retrieve API Key details, credit balance, and RPM limits.

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

---

### 2. Estimate Translation Cost

**Endpoint**: `POST /estimate`

**Description**: Estimate credits required for translation.

**Request**:
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

---

### 3. Start Translation Job

**Endpoint**: `POST /translation/start`

**Description**: Create a new translation job (processed asynchronously).

**Request**:
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
  "systemInstruction": "Translate naturally, context-appropriate"
}
```

**Parameters**:
- `targetLanguage` (required): Target language (e.g., "Vietnamese", "English", "Japanese", "Korean", "Thai", "Chinese")
- `lines` (required): Array of lines to translate
  - `index`: Line number (must be a positive integer)
  - `text`: Content to translate (max 3000 chars/line)
- `systemInstruction` (optional): Special instructions for AI translation style. Examples:
  - "Translate naturally, keep proper nouns unchanged"
  - "Translate in formal style, suitable for business context"
  - "Translate in casual style, use everyday language"
  - If not provided, defaults to: "Translate naturally, context-appropriate"

**Response (202 Accepted)**:
```json
{
  "status": "Accepted",
  "sessionId": "abc123def456",
  "estimatedCredits": 500,
  "message": "Job started successfully"
}
```

**Error (402 Payment Required)**:
```json
{
  "status": "InsufficientCredits",
  "currentBalance": 100,
  "estimatedRequired": 500,
  "message": "Insufficient credits. Please top up."
}
```

---

### 4. Get Translation Results

**Endpoint**: `GET /translation/result/{sessionId}`

**Description**: Get translation results. Poll this endpoint to check progress.

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

**Best Practice**: Poll every 2-5 seconds until `status` is "Completed" or "Failed".

---

### 5. Cancel Translation Job

**Endpoint**: `POST /translation/cancel/{sessionId}`

**Description**: Cancel a running job and refund unused credits.

**Response**:
```json
{
  "status": "Cancelled",
  "creditsRefunded": 250,
  "message": "Job cancelled. Unused credits refunded."
}
```

---

### 6. Get Usage History

**Endpoint**: `GET /account/usage`

**Description**: View history of completed jobs.

**Query Parameters**:
- `from` (optional): Filter from date (ISO 8601)
- `to` (optional): Filter to date (ISO 8601)
- `page` (default: 1): Current page
- `pageSize` (default: 50, max: 100): Items per page

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
      "durationMs": 45000
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

---

### 7. Get Transaction History

**Endpoint**: `GET /account/transactions`

**Description**: View credit purchase/usage history.

**Query Parameters**:
- `page` (default: 1)
- `pageSize` (default: 50, max: 100)

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
      "description": "Credit top-up #1",
      "createdAt": "2024-12-09T10:00:00Z",
      "createdBy": "Admin"
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
- `Purchase`: Credit top-up
- `Usage`: Credits charged for service
- `Refund`: Credits refunded
- `Adjustment`: Admin adjustment

---

## Limits & Quotas

### Rate Limiting
- **RPM (Requests Per Minute)**: Each API Key has individual limits (default: 100 RPM)
- Exceeding limits returns **429 Too Many Requests**
- `Retry-After` header indicates wait time (seconds)

**Response Headers for rate limit tracking:**
- `X-RateLimit-Limit`: Maximum requests per minute
- `X-RateLimit-Remaining`: Remaining requests in current minute
- `X-RateLimit-Reset`: Reset timestamp (Unix timestamp)

**Example Response Headers:**
```
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 85
X-RateLimit-Reset: 1702123620
```

**Best Practice:** Monitor these headers to avoid rate limiting

### Credit System
- **1 credit = 1 output character translated**
- Credits only charged when job completes successfully
- Credits automatically refunded if job fails or is cancelled

### Technical Limits
- Max **3000 characters/line** in request
- Max **100 items/page** in history queries

---

## Error Codes

| HTTP Status | Status Message | Description |
|-------------|----------------|-------------|
| 400 | InvalidRequest | Missing or invalid request data |
| 401 | Unauthorized | Invalid API Key |
| 402 | InsufficientCredits | Not enough credits |
| 403 | Forbidden | API Key lacks permission |
| 404 | NotFound | Session not found |
| 429 | TooManyRequests | RPM limit exceeded |
| 500 | Error | Server error |

---

## Standard Workflow

### Basic Translation Flow

```
1. Estimate cost (optional)
   POST /estimate

2. Start translation
   POST /translation/start
   → Receive sessionId

3. Poll for results (every 2-5s)
   GET /translation/result/{sessionId}
   → Check status
   
4. Receive results when status = "Completed"
```

### Python Example

```python
import requests
import time

API_KEY = "AIO_xxxxxxxxxxxxxxxxxxxxxxxxx"
BASE_URL = "https://your-domain.com/api/v1/external"
HEADERS = {"Authorization": f"Bearer {API_KEY}"}

# Start translation
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

# Poll results
while True:
    result_resp = requests.get(
        f"{BASE_URL}/translation/result/{session_id}",
        headers=HEADERS
    )
    data = result_resp.json()
    
    if data["status"] == "Completed":
        for line in data["result"]["lines"]:
            print(f"{line['index']}: {line['translatedText']}")
        break
    elif data["status"] == "Failed":
        print(f"Failed: {data['error']['message']}")
        break
    
    time.sleep(3)
```

---

## Credit & Billing

### How It Works
1. **Top-up**: Contact admin to add credits
2. **Usage**: Credits automatically deducted when jobs complete
3. **Refunds**: Credits refunded if jobs fail or are cancelled

### Cost Calculation
```
Credits needed = Output characters translated
Example: 1000 characters = 1000 credits
```

### Tracking Spending
- Total credits used: `GET /account/usage`
- Transaction details: `GET /account/transactions`
- Current balance: `GET /account/info`

---

## Best Practices

### 1. Handle Errors Properly
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

### 2. Monitor Rate Limits in Code
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

# Usage
response = make_api_request(f"{BASE_URL}/account/info", headers)
```

### 3. Implement Exponential Backoff
```python
def call_with_retry(func, max_retries=3):
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
                time.sleep(2 ** i)  # 1s, 2s, 4s
```

### 4. Check Credits Before Large Jobs
```python
estimate = api.estimate(char_count)
if not estimate['hasEnoughCredits']:
    print(f"Need {estimate['estimatedCredits']} credits")
    return
```

### 5. Secure API Keys
- Never hardcode API Keys
- Use environment variables
- Treat keys like passwords

**Python example with environment variable:**
```python
import os

API_KEY = os.environ.get('VIP_TRANSLATION_API_KEY')
if not API_KEY:
    raise ValueError("API_KEY not found in environment variables")
```

### 6. Monitor Usage
- Log all API calls
- Monitor credit balance
- Set up low-credit alerts

---

## FAQ

**Q: How do I get an API Key?**  
A: Contact the admin to receive an API Key.

**Q: Do API Keys expire?**  
A: Depends on configuration. Some keys have `expiresAt`, others don't expire.

**Q: How many concurrent requests can I make?**  
A: Limited by your API Key's RPM. Check `rpmLimit` in `GET /account/info`.

**Q: Do credits expire?**  
A: No, credits never expire.

**Q: If a job fails, do I lose credits?**  
A: No, credits are only charged on successful completion and refunded on failure.

**Q: How do I know when a job is done?**  
A: Poll `GET /translation/result/{sessionId}` until `status` is "Completed" or "Failed".

**Q: Can I cancel a running job?**  
A: Yes, use `POST /translation/cancel/{sessionId}`.

**Q: What's the maximum characters per line?**  
A: 3000 characters per line. Requests exceeding this will be rejected.

---

## Support

For assistance with:
- Creating API Keys
- Adding credits
- Pricing questions
- Technical issues

Please contact the system administrator.

---

**Version**: 1.0  
**Last Updated**: 2024-12-09
