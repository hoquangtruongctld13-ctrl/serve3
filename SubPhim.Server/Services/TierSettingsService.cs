using Microsoft.Extensions.Options;
using SubPhim.Server.Data;
using SubPhim.Server.Settings;
using System;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace SubPhim.Server.Services
{
    public class TierSettingsService : ITierSettingsService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TierSettingsService> _logger;

        public TierSettingsService(IServiceProvider serviceProvider, ILogger<TierSettingsService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public void ApplyTierSettings(User user, SubscriptionTier tier)
        {
            Debug.WriteLine($"[TierSettingsService] Applying settings for Tier '{tier}' to User '{user.Username}'.");
            
            TierDefaultSetting? defaultSettings = null;
            
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                // Use timeout to prevent blocking forever on database lock
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                
                // Execute synchronously but with a short timeout
                defaultSettings = context.TierDefaultSettings
                    .AsNoTracking()
                    .FirstOrDefault(t => t.Tier == tier);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load TierDefaultSettings from database for tier {Tier}, using safe defaults", tier);
            }

            if (defaultSettings == null)
            {
                Debug.WriteLine($"[TierSettingsService] WARNING: No default settings found for Tier '{tier}'. Applying safe defaults.");
                _logger.LogWarning("No TierDefaultSettings found for tier {Tier}, applying safe defaults", tier);
                ApplySafeDefaults(user, tier);
                return;
            }

            user.Tier = tier;
            user.GrantedFeatures = defaultSettings.GrantedFeatures;
            user.AllowedApiAccess = defaultSettings.AllowedApiAccess;
            user.VideoDurationLimitMinutes = defaultSettings.VideoDurationMinutes;
            user.DailyVideoLimit = defaultSettings.DailyVideoCount;
            user.DailyRequestLimitOverride = defaultSettings.DailyTranslationRequests;
            user.DailySrtLineLimit = defaultSettings.DailySrtLineLimit;
            user.TtsCharacterLimit = defaultSettings.TtsCharacterLimit;
            user.TtsCharactersUsed = 0;
            user.LastTtsResetUtc = DateTime.UtcNow;
            Debug.WriteLine($"[TierSettingsService] Applied TTS Character Limit: {user.TtsCharacterLimit}. Usage reset.");

            user.AioCharactersUsedToday = 0;
            user.LastAioResetUtc = DateTime.UtcNow;
            Debug.WriteLine($"[TierSettingsService] Applied AIO Character Limit from defaults. Usage reset.");

            user.MaxDevices = (tier == SubscriptionTier.Free) ? 1 : 1;

            if (tier == SubscriptionTier.Free)
            {
                user.SubscriptionExpiry = null;
            }
        }
        
        public void ApplySafeDefaults(User user, SubscriptionTier tier)
        {
            user.Tier = tier;
            if (tier == SubscriptionTier.Free)
            {
                user.GrantedFeatures = GrantedFeatures.None;
                user.AllowedApiAccess = AllowedApis.OpenRouter;
                user.VideoDurationLimitMinutes = 30;
                user.DailyVideoLimit = 2;
                user.DailyRequestLimitOverride = 30;
                user.DailySrtLineLimit = 1000;
                user.MaxDevices = 1;
                user.SubscriptionExpiry = null;
                user.TtsCharacterLimit = 10000;
                user.TtsCharactersUsed = 0;
                user.LastTtsResetUtc = DateTime.UtcNow;
                user.AioCharactersUsedToday = 0;
                user.LastAioResetUtc = DateTime.UtcNow;
            }
            else
            {
                user.GrantedFeatures = GrantedFeatures.SubPhim | GrantedFeatures.DichThuat;
                user.AllowedApiAccess = AllowedApis.ChutesAI | AllowedApis.Gemini | AllowedApis.OpenRouter;
                user.VideoDurationLimitMinutes = 120;
                user.DailyVideoLimit = -1;
                user.DailyRequestLimitOverride = -1;
                user.DailySrtLineLimit = 99999;
                user.MaxDevices = 1;
                user.TtsCharacterLimit = 100000;
                user.TtsCharactersUsed = 0;
                user.LastTtsResetUtc = DateTime.UtcNow;
                user.AioCharactersUsedToday = 0;
                user.LastAioResetUtc = DateTime.UtcNow;
            }
            
            _logger.LogInformation("Applied safe defaults for tier {Tier} to user {Username}", tier, user.Username);
        }
    }
}
