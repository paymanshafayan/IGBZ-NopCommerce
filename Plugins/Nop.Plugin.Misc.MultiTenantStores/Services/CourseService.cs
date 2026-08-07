namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
    using Nop.Core.Domain.Orders;
    using Nop.Data;
    using Nop.Services.Orders;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    public interface ICourseService
    {
        Task<IList<CourseLesson>> GetLessonsByProductIdAsync(int productId);
        Task<bool> HasAccessToCourseAsync(int customerId, int productId);
        Task<SecureCourseVideoResult> GetSecureLessonVideoAsync(int customerId, int lessonId, string userIpAddress, string userPhone);
        Task MarkLessonCompletedAsync(int customerId, int lessonId);
        Task<int> GetCompletionPercentAsync(int customerId, int productId);
        Task<IList<CourseQuizQuestion>> GetQuizQuestionsAsync(int productId);
        Task<IList<CourseQuizOption>> GetQuizOptionsAsync(int questionId);
        Task<QuizGradeResult> GradeQuizAsync(int customerId, int productId, IDictionary<int, int> selectedOptionIdByQuestionId);
        Task<CourseCertificate> GetCertificateAsync(int customerId, int productId);
    }

    public class CourseService : ICourseService
    {
        private const int PassingScorePercent = 70;

        private readonly IRepository<CourseLesson> _lessonRepository;
        private readonly IRepository<CourseQuizQuestion> _questionRepository;
        private readonly IRepository<CourseQuizOption> _optionRepository;
        private readonly IRepository<CourseEnrollmentProgress> _progressRepository;
        private readonly IRepository<CourseCertificate> _certificateRepository;
        private readonly IOrderService _orderService;
        private readonly ILmsAndVideoSecurityService _videoSecurityService;

        public CourseService(
            IRepository<CourseLesson> lessonRepository,
            IRepository<CourseQuizQuestion> questionRepository,
            IRepository<CourseQuizOption> optionRepository,
            IRepository<CourseEnrollmentProgress> progressRepository,
            IRepository<CourseCertificate> certificateRepository,
            IOrderService orderService,
            ILmsAndVideoSecurityService videoSecurityService)
        {
            _lessonRepository = lessonRepository;
            _questionRepository = questionRepository;
            _optionRepository = optionRepository;
            _progressRepository = progressRepository;
            _certificateRepository = certificateRepository;
            _orderService = orderService;
            _videoSecurityService = videoSecurityService;
        }

        public async Task<IList<CourseLesson>> GetLessonsByProductIdAsync(int productId)
        {
            return await _lessonRepository.GetAllAsync(q =>
                q.Where(l => l.ProductId == productId).OrderBy(l => l.DisplayOrder));
        }

        /// <summary>
        /// کنترل دسترسی واقعی: بررسی وجود سفارش واقعاً پرداخت‌شده (نه یک Flag ساختگی) برای این
        /// مشتری روی این محصول، دقیقاً طبق بند «مدیریت دسترسی» راهنمای شما.
        /// </summary>
        public async Task<bool> HasAccessToCourseAsync(int customerId, int productId)
        {
            if (customerId <= 0) return false;

            var paidOrdersCount = (await _orderService.SearchOrdersAsync(
                customerId: customerId,
                productId: productId,
                psIds: new List<int> { (int)PaymentStatus.Paid },
                getOnlyTotalCount: true)).TotalCount;

            return paidOrdersCount > 0;
        }

        /// <summary>
        /// تولید لینک امن پخش — با استفاده از سرویس امضای HMAC-SHA256 واقعی موجود (نه الگوریتم
        /// MD5 ضعیف‌تری که در متن راهنما فقط برای توضیح مفهومی آمده بود).
        /// </summary>
        public async Task<SecureCourseVideoResult> GetSecureLessonVideoAsync(int customerId, int lessonId, string userIpAddress, string userPhone)
        {
            var lesson = await _lessonRepository.GetByIdAsync(lessonId);
            if (lesson == null)
                return new SecureCourseVideoResult { IsSuccess = false, Message = "درس یافت نشد." };

            if (!lesson.IsFreePreview && !await HasAccessToCourseAsync(customerId, lesson.ProductId))
                return new SecureCourseVideoResult { IsSuccess = false, Message = "شما این دوره را خریداری نکرده‌اید." };

            return await _videoSecurityService.GetWatermarkedCourseVideoUrlAsync(
                courseId: lesson.ProductId, lessonId: lesson.Id, customerId: customerId,
                userPhoneNumber: userPhone, userIpAddress: userIpAddress, validFor: TimeSpan.FromHours(2));
        }

        public async Task MarkLessonCompletedAsync(int customerId, int lessonId)
        {
            var lesson = await _lessonRepository.GetByIdAsync(lessonId);
            if (lesson == null) return;

            var existing = (await _progressRepository.GetAllAsync(q =>
                q.Where(p => p.CustomerId == customerId && p.LessonId == lessonId))).FirstOrDefault();

            if (existing != null)
            {
                if (!existing.IsCompleted)
                {
                    existing.IsCompleted = true;
                    existing.CompletedOnUtc = DateTime.UtcNow;
                    await _progressRepository.UpdateAsync(existing);
                }
                return;
            }

            await _progressRepository.InsertAsync(new CourseEnrollmentProgress
            {
                CustomerId = customerId,
                ProductId = lesson.ProductId,
                LessonId = lessonId,
                IsCompleted = true,
                CompletedOnUtc = DateTime.UtcNow
            });
        }

        public async Task<int> GetCompletionPercentAsync(int customerId, int productId)
        {
            var allLessons = await GetLessonsByProductIdAsync(productId);
            if (allLessons.Count == 0) return 0;

            var completed = (await _progressRepository.GetAllAsync(q =>
                q.Where(p => p.CustomerId == customerId && p.ProductId == productId && p.IsCompleted))).Count;

            return (int)Math.Round(100.0 * completed / allLessons.Count);
        }

        public async Task<IList<CourseQuizQuestion>> GetQuizQuestionsAsync(int productId)
        {
            return await _questionRepository.GetAllAsync(q =>
                q.Where(x => x.ProductId == productId).OrderBy(x => x.DisplayOrder));
        }

        public async Task<IList<CourseQuizOption>> GetQuizOptionsAsync(int questionId)
        {
            return await _optionRepository.GetAllAsync(q => q.Where(x => x.QuestionId == questionId));
        }

        /// <summary>
        /// نمره‌دهی واقعی آزمون و صدور گواهی دیجیتال در صورت قبولی (طبق «ایدهٔ تکمیلی» راهنما).
        /// </summary>
        public async Task<QuizGradeResult> GradeQuizAsync(int customerId, int productId, IDictionary<int, int> selectedOptionIdByQuestionId)
        {
            var questions = await GetQuizQuestionsAsync(productId);
            if (questions.Count == 0)
                return new QuizGradeResult { ScorePercent = 0, Passed = false, Message = "این دوره آزمونی ندارد." };

            var correctCount = 0;
            foreach (var question in questions)
            {
                if (!selectedOptionIdByQuestionId.TryGetValue(question.Id, out var selectedOptionId))
                    continue;

                var options = await GetQuizOptionsAsync(question.Id);
                var correctOption = options.FirstOrDefault(o => o.IsCorrect);
                if (correctOption != null && correctOption.Id == selectedOptionId)
                    correctCount++;
            }

            var scorePercent = (int)Math.Round(100.0 * correctCount / questions.Count);
            var passed = scorePercent >= PassingScorePercent;

            CourseCertificate certificate = null;
            if (passed)
                certificate = await IssueCertificateAsync(customerId, productId, scorePercent);

            return new QuizGradeResult
            {
                ScorePercent = scorePercent,
                Passed = passed,
                CertificateCode = certificate?.CertificateCode,
                Message = passed
                    ? $"تبریک! شما با نمرهٔ {scorePercent}٪ قبول شدید و گواهی شما صادر شد."
                    : $"نمرهٔ شما {scorePercent}٪ بود. برای قبولی حداقل {PassingScorePercent}٪ لازم است."
            };
        }

        public async Task<CourseCertificate> GetCertificateAsync(int customerId, int productId)
        {
            return (await _certificateRepository.GetAllAsync(q =>
                q.Where(c => c.CustomerId == customerId && c.ProductId == productId))).FirstOrDefault();
        }

        private async Task<CourseCertificate> IssueCertificateAsync(int customerId, int productId, int scorePercent)
        {
            var existing = await GetCertificateAsync(customerId, productId);
            if (existing != null)
                return existing;

            var certificate = new CourseCertificate
            {
                CustomerId = customerId,
                ProductId = productId,
                CertificateCode = $"CERT-{productId}-{customerId}-{RandomNumberGenerator.GetInt32(100000, 999999)}",
                QuizScorePercent = scorePercent,
                IssuedOnUtc = DateTime.UtcNow
            };

            await _certificateRepository.InsertAsync(certificate);
            return certificate;
        }
    }

    public class QuizGradeResult
    {
        public int ScorePercent { get; set; }
        public bool Passed { get; set; }
        public string CertificateCode { get; set; }
        public string Message { get; set; }
    }
}
