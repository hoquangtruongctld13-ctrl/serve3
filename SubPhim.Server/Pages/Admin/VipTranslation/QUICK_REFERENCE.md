# VIP Translation API - Quick Reference

## Authentication

**Method 1: Authorization Bearer (Recommended)**
```
Authorization: Bearer AIO_xxxxxxxxxxxxxxxxxxxxxxxxx
```

**Method 2: X-API-Key header**
```
X-API-Key: AIO_xxxxxxxxxxxxxxxxxxxxxxxxx
```

## Base URL
```
https://your-domain.com/api/v1/external
```

## Quick Start (Python)
```python
import requests
import time

API_KEY = "AIO_xxx"
BASE_URL = "https://your-domain.com/api/v1/external"
headers = {"Authorization": f"Bearer {API_KEY}"}

# Start job
resp = requests.post(f"{BASE_URL}/translation/start", 
    headers=headers,
    json={
        "targetLanguage": "Vietnamese",
        "lines": [{"index": 1, "text": "Hello world"}]
    })
session_id = resp.json()["sessionId"]

# Poll for results
while True:
    result = requests.get(f"{BASE_URL}/translation/result/{session_id}", 
        headers=headers).json()
    if result["status"] == "Completed":
        print(result["result"]["lines"])
        break
    time.sleep(3)
```

## Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/account/info` | Get balance & limits |
| POST | `/estimate` | Estimate cost |
| POST | `/translation/start` | Start translation |
| GET | `/translation/result/{id}` | Get results |
| POST | `/translation/cancel/{id}` | Cancel job |
| GET | `/account/usage` | Usage history |
| GET | `/account/transactions` | Transaction history |

## Key Limits

- **RPM**: Check `rpmLimit` in account info (default: 100)
- **Max chars/line**: 3000
- **Credit charge**: 1 credit = 1 output character
- **Rate limit headers**: Check `X-RateLimit-Remaining` to monitor usage

## Common Status Codes

| Code | Meaning |
|------|---------|
| 200 | Success |
| 202 | Job accepted |
| 400 | Invalid request |
| 401 | Invalid API key |
| 402 | Insufficient credits |
| 429 | Rate limit exceeded |
| 500 | Server error |

## Translation Job Flow

```
1. POST /translation/start → get sessionId
2. Poll GET /translation/result/{sessionId} every 2-5s
3. Wait for status: "Completed" or "Failed"
4. Retrieve translated lines
```

## Response Status Values

- `"Processing"` - Job in progress
- `"Completed"` - Job finished successfully
- `"Failed"` - Job failed (credits refunded)
- `"Cancelled"` - Job cancelled (credits refunded)

## Error Handling

```python
try:
    response = requests.post(url, headers=headers, json=data)
    response.raise_for_status()
    
    # Monitor rate limits
    remaining = response.headers.get('X-RateLimit-Remaining')
    if remaining and int(remaining) < 10:
        print(f"Warning: Only {remaining} requests remaining")
        
except requests.exceptions.HTTPError as e:
    status_code = e.response.status_code
    if status_code == 402:
        print("Insufficient credits")
    elif status_code == 429:
        retry_after = e.response.headers.get('Retry-After', 60)
        print(f"Rate limit exceeded. Retry after {retry_after}s")
    else:
        print(f"Error: {e.response.json()}")
```

## Best Practices

✅ Check credit balance before large jobs  
✅ Poll results every 2-5 seconds  
✅ Monitor `X-RateLimit-Remaining` header  
✅ Respect `Retry-After` header on 429 errors  
✅ Implement exponential backoff for retries  
✅ Handle all error codes properly  
✅ Monitor credit usage regularly  
✅ Never hardcode API keys (use environment variables)  
✅ Log all API interactions  

## Need Help?

📖 Full documentation: See `API_DOCUMENTATION.md` or `API_DOCUMENTATION_EN.md`  
👨‍💼 Admin support: Contact system administrator  
💰 Add credits: Request through admin  

---
*For complete documentation with detailed examples, see API_DOCUMENTATION.md (Vietnamese) or API_DOCUMENTATION_EN.md (English)*
