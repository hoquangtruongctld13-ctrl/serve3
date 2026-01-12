# Admin - VIP Translation Management

## Overview
This folder contains the admin interface for managing VIP Translation service and External API Keys.

## Admin Pages

### 1. Index (`Index.cshtml`)
Main dashboard for VIP Translation settings management.

### 2. External API Keys (`ExternalApiKeys.cshtml`)
Manage API Keys for external customers:
- Create new API Keys
- View all keys with usage statistics
- Enable/disable keys
- Add credits to keys
- Configure pricing and default settings

### 3. External API Key Detail (`ExternalApiKeyDetail.cshtml`)
Detailed view of individual API Key:
- View usage logs
- View credit transaction history
- Update key settings (RPM limit, name, etc.)
- Add credits

## Customer API Documentation

Complete API documentation for customers is available in:

- **📄 API_DOCUMENTATION.md** - Vietnamese version (comprehensive)
- **📄 API_DOCUMENTATION_EN.md** - English version (comprehensive)
- **📄 QUICK_REFERENCE.md** - Quick reference guide (bilingual)

### What's included in the API docs:
- **Authentication guide** - Bearer token and X-API-Key header methods
- **All API endpoints** with request/response examples
- **Rate limiting** - Monitor via X-RateLimit-* headers
- **Error handling** - Complete error codes and retry strategies
- **Code examples** - Python & JavaScript with best practices
- **Security best practices** - API key management and protection
- **systemInstruction** parameter - Customization examples
- **Credit & billing** - Transparent pricing and usage tracking
- **FAQ section** - Common questions answered

### Recent Documentation Updates
✅ Added X-API-Key authentication method  
✅ Documented rate limit response headers (X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset)  
✅ Enhanced error handling examples with rate limit retry logic  
✅ Added API key security best practices  
✅ Improved systemInstruction parameter documentation with examples

### Sharing with Customers
Provide customers with either documentation file depending on their language preference. The docs contain all information needed to integrate with the VIP Translation API.

## Related Endpoints

### Admin Endpoints
- `/Admin/VipTranslation/Index` - Main settings
- `/Admin/VipTranslation/ExternalApiKeys` - Key management
- `/Admin/VipTranslation/ExternalApiKeyDetail/{id}` - Key details
- `/Admin/LocalApi/Proxy` - Shared proxy management

### Customer API Endpoints
- `/api/v1/external/translation/start` - Start translation job
- `/api/v1/external/translation/result/{sessionId}` - Get results
- `/api/v1/external/translation/cancel/{sessionId}` - Cancel job
- `/api/v1/external/account/info` - Get account info
- `/api/v1/external/account/usage` - Get usage history
- `/api/v1/external/account/transactions` - Get transaction history
- `/api/v1/external/estimate` - Estimate cost

## Database Models

### ExternalApiKey
Stores API Key information:
- Hashed key (SHA-256)
- Display name, assignment info
- Credit balance
- RPM limit
- Status (enabled/disabled)

### ExternalApiUsageLog
Tracks each API call:
- Session ID
- Input/output statistics
- Credits charged
- Status (Pending/Completed/Failed/Cancelled)
- Error messages

### ExternalApiCreditTransaction
Records all credit movements:
- Type (Purchase/Usage/Refund/Adjustment)
- Amount
- Balance after transaction
- Description

### ExternalApiSettings
Global pricing settings:
- Credits per character
- VND per credit
- Default RPM
- Default initial credits

## Services

### IExternalApiKeyService
- Create, update, delete API Keys
- Enable/disable keys
- Validate keys

### IExternalApiCreditService
- Add credits
- Charge credits
- Refund credits
- Estimate costs
- Get balance and pricing

### VipTranslationService
- Shared translation service
- Handles actual translation jobs
- Manages batch processing
- Interacts with Gemini API

## Security Notes

⚠️ **Important**:
- API Keys are hashed (SHA-256) in database
- Plain text keys shown only once at creation
- Keys support both Bearer token and X-API-Key header authentication
- Rate limiting per key (RPM) with transparent headers
- Credit system prevents abuse
- All API calls are logged with detailed metrics
- Automatic credit refund on job failure or cancellation

## Support Workflow

When customers need help:

1. **Credit Issues**: Use "Add Credits" in key detail page
2. **Rate Limiting**: Adjust RPM limit in key settings
3. **Usage Questions**: Check usage logs and transaction history
4. **Integration Help**: Share API_DOCUMENTATION.md or API_DOCUMENTATION_EN.md
5. **Technical Issues**: Check usage logs for error messages

## Common Admin Tasks

### Creating a New API Key
1. Go to `/Admin/VipTranslation/ExternalApiKeys`
2. Click "Create New Key"
3. Fill in customer details (name, email, RPM limit, initial credits)
4. Copy the generated key immediately (shown only once)
5. Share key with customer along with API documentation

### Adding Credits
1. Go to key detail page
2. Enter credit amount and description
3. Credits are immediately available
4. Transaction is logged

### Monitoring Usage
1. Check usage summary in ExternalApiKeys list
2. View detailed logs in key detail page
3. Export transaction history if needed

### Handling Issues
1. Check usage logs for errors
2. Verify credit balance
3. Check RPM usage
4. Review Gemini API errors if present

## Related Files
- `ExternalTranslationController.cs` - API controller
- `Models.cs` - Database models (ExternalApiKey, ExternalApiUsageLog, etc.)
- Services in `Services/` folder
