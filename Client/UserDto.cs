using System;
using System.Collections.Generic;
using static subphimv1.Services.ApiService;

namespace subphimv1.UserView
{
    [Flags]
    public enum AllowedApis
    {
        None = 0,
        ChutesAI = 1,
        Gemini = 2,
        OpenRouter = 4,
        AIOLauncher = 8,
        Capcutvoice = 16
    }

    public class DeviceDto
    {
        public string Hwid { get; set; }
        public string LastLoginIp { get; set; }
        public DateTime LastLogin { get; set; }
    }

    public class UserDto
    {
        public string Uid { get; set; }
        public int Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string SubscriptionTier { get; set; }
        public DateTime SubscriptionExpiry { get; set; }
        public string Token { get; set; }
        public GrantedFeatures GrantedFeatures { get; set; }
        public int RemainingRequests { get; set; }
        public int VideosProcessedToday { get; set; }
        public int DailyVideoLimit { get; set; }
        public int LocalSrtLinesUsedToday { get; set; }
        public int DailyLocalSrtLineLimit { get; set; }
        public AllowedApis AllowedApiAccess { get; set; }
        public List<DeviceDto> Devices { get; set; } = new List<DeviceDto>();
        public int SrtLinesUsedToday { get; set; }
        public int DailySrtLineLimit { get; set; }
        public long TtsCharactersUsed { get; set; }
        public long TtsCharacterLimit { get; set; }
        public long AioCharactersUsedToday { get; set; }
        public long AioCharacterLimit { get; set; }
        public int VipSrtLinesUsedToday { get; set; }
        public int DailyVipSrtLimit { get; set; }
        public long VbeeCharactersUsed { get; set; }
        public long VbeeCharacterLimit { get; set; }
        public DateTime? VbeeCharacterResetDate { get; set; }
    }
}