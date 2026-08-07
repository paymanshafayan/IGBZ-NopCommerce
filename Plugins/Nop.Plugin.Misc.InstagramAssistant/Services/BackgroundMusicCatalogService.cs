namespace Nop.Plugin.Misc.InstagramAssistant.Services
{
    using System.Collections.Generic;

    public class BackgroundMusicTrack
    {
        public string TrackId { get; set; }
        public string DisplayName { get; set; }
        public string Mood { get; set; }
    }

    /// <summary>
    /// فهرست موسیقی‌های پس‌زمینهٔ Royalty-Free قابل‌انتخاب برای ویدیوی استوری محصول (نیازمندی #۱:
    /// «AI ساخت پست تصویر+آهنگ»). این فهرست فعلاً ثابت (Hardcode) است؛ در نسخهٔ کامل باید از یک
    /// کتابخانهٔ صوتی واقعی (مثلاً Epidemic Sound API یا فایل‌های آپلودی خودِ پلتفرم) خوانده شود.
    /// شناسهٔ انتخاب‌شده صرفاً به AiMultimediaStudioService پاس داده می‌شود — تلفیق واقعی صدا با
    /// ویدیو وظیفهٔ سرویس AI بیرونی (آتنا) است، نه این کد؛ اگر آن سرویس چنین قابلیتی نداشته باشد
    /// (که با مستندات فعلاً نامشخص است)، این فیلد صرفاً نادیده گرفته می‌شود.
    /// </summary>
    public interface IBackgroundMusicCatalogService
    {
        IList<BackgroundMusicTrack> GetAvailableTracks();
    }

    public class BackgroundMusicCatalogService : IBackgroundMusicCatalogService
    {
        private static readonly IList<BackgroundMusicTrack> Tracks = new List<BackgroundMusicTrack>
        {
            new() { TrackId = "upbeat_retail", DisplayName = "پرانرژی/فروشگاهی", Mood = "Upbeat" },
            new() { TrackId = "calm_luxury", DisplayName = "آرام/لوکس", Mood = "Calm" },
            new() { TrackId = "energetic_sale", DisplayName = "پرشور/حراج", Mood = "Energetic" },
            new() { TrackId = "warm_acoustic", DisplayName = "گرم/آکوستیک", Mood = "Warm" },
            new() { TrackId = "none", DisplayName = "بدون موسیقی", Mood = "None" }
        };

        public IList<BackgroundMusicTrack> GetAvailableTracks() => Tracks;
    }
}
