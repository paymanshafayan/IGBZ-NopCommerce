namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using System;
    using Nop.Core;

    /// <summary>
    /// درس/سرفصل یک دوره. طبق راهنمای راه‌اندازی سرویس آموزش: Product = دوره، این جدول = سرفصل‌ها.
    /// </summary>
    public class CourseLesson : BaseEntity
    {
        public int ProductId { get; set; }
        public string Title { get; set; }
        public int DisplayOrder { get; set; }
        public int DurationMinutes { get; set; }

        /// <summary>مسیر/شناسهٔ ویدیو نزد سرویس VOD (نه لینک مستقیم و ثابت)</summary>
        public string VodVideoPath { get; set; }

        /// <summary>لینک پیوست (PDF/تمرین) در صورت وجود</summary>
        public string AttachmentUrl { get; set; }

        public bool IsFreePreview { get; set; }
        public DateTime CreatedOnUtc { get; set; }
    }

    public class CourseQuizQuestion : BaseEntity
    {
        public int ProductId { get; set; }
        public int DisplayOrder { get; set; }
        public string QuestionText { get; set; }
    }

    public class CourseQuizOption : BaseEntity
    {
        public int QuestionId { get; set; }
        public string OptionText { get; set; }
        public bool IsCorrect { get; set; }
    }

    /// <summary>پیشرفت مشتری در دوره — برای نمایش درصد تکمیل و شرط صدور گواهی</summary>
    public class CourseEnrollmentProgress : BaseEntity
    {
        public int CustomerId { get; set; }
        public int ProductId { get; set; }
        public int LessonId { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedOnUtc { get; set; }
    }

    /// <summary>گواهی دیجیتال پس از قبولی در آزمون پایانی دوره</summary>
    public class CourseCertificate : BaseEntity
    {
        public int CustomerId { get; set; }
        public int ProductId { get; set; }
        public string CertificateCode { get; set; }
        public int QuizScorePercent { get; set; }
        public DateTime IssuedOnUtc { get; set; }
    }
}
