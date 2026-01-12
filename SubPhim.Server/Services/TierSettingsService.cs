using Microsoft.Extensions.Options;
using SubPhim.Server.Data;
using SubPhim.Server.Settings;
using System;
using System.Diagnostics;

namespace SubPhim.Server.Services
{
    public class TierSettingsService : ITierSettingsService
    {
        private readonly IServiceProvider _serviceProvider;

        public TierSettingsService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void ApplyTierSettings(User user, SubscriptionTier tier)
        {
            Debug.WriteLine($"[TierSettingsService] Applying settings for Tier '{tier}' to User '{user.Username}'.");
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var defaultSettings = context.TierDefaultSettings.FirstOrDefault(t => t.Tier == tier);

            if (defaultSettings == null)
            {
                Debug.WriteLine($"[TierSettingsService] WARNING: No default settings found for Tier '{tier}'. Applying safe defaults.");
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
        private void ApplySafeDefaults(User user, SubscriptionTier tier)
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
            }
        }
    }
}