namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Public
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    [ApiController]
    [Route("api/courses")]
    public class CourseController : ControllerBase
    {
        private readonly IWorkContext _workContext;
        private readonly ICourseService _courseService;

        public CourseController(IWorkContext workContext, ICourseService courseService)
        {
            _workContext = workContext;
            _courseService = courseService;
        }

        /// <summary>لیست سرفصل‌های دوره + وضعیت دسترسی + درصد پیشرفت</summary>
        [HttpGet("{productId}/lessons")]
        public async Task<IActionResult> GetLessons(int productId)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var hasAccess = await _courseService.HasAccessToCourseAsync(customer.Id, productId);
            var lessons = await _courseService.GetLessonsByProductIdAsync(productId);
            var completionPercent = hasAccess ? await _courseService.GetCompletionPercentAsync(customer.Id, productId) : 0;

            return Ok(new
            {
                hasAccess,
                completionPercent,
                lessons = lessons.Select(l => new
                {
                    l.Id,
                    l.Title,
                    l.DisplayOrder,
                    l.DurationMinutes,
                    l.IsFreePreview,
                    // لینک ویدیو عمداً اینجا برگردانده نمی‌شود — فقط از طریق GetLessonVideo با
                    // بررسی دسترسی و امضای زمان‌دار صادر می‌شود
                    hasAttachment = !string.IsNullOrEmpty(l.AttachmentUrl)
                })
            });
        }

        /// <summary>لینک امن پخش ویدیوی درس — فقط برای خریداران واقعی یا درس‌های پیش‌نمایش رایگان</summary>
        [HttpGet("lessons/{lessonId}/video")]
        public async Task<IActionResult> GetLessonVideo(int lessonId)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var userIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var result = await _courseService.GetSecureLessonVideoAsync(customer.Id, lessonId, userIp, customer.Phone);
            if (!result.IsSuccess)
                return StatusCode(403, new { success = false, message = result.Message });

            return Ok(new { success = true, embedUrl = result.EmbedPlayerUrl, expiresOnUtc = result.ExpiresOnUtc });
        }

        [HttpPost("lessons/{lessonId}/complete")]
        public async Task<IActionResult> MarkLessonCompleted(int lessonId)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            await _courseService.MarkLessonCompletedAsync(customer.Id, lessonId);
            return Ok(new { success = true });
        }

        [HttpGet("{productId}/quiz")]
        public async Task<IActionResult> GetQuiz(int productId)
        {
            var questions = await _courseService.GetQuizQuestionsAsync(productId);
            var result = new List<object>();
            foreach (var q in questions)
            {
                var options = await _courseService.GetQuizOptionsAsync(q.Id);
                result.Add(new
                {
                    q.Id,
                    q.QuestionText,
                    options = options.Select(o => new { o.Id, o.OptionText }) // IsCorrect عمداً به کلاینت فرستاده نمی‌شود
                });
            }
            return Ok(result);
        }

        [HttpPost("{productId}/quiz/submit")]
        public async Task<IActionResult> SubmitQuiz(int productId, [FromBody] Dictionary<int, int> answers)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();

            if (!await _courseService.HasAccessToCourseAsync(customer.Id, productId))
                return StatusCode(403, new { success = false, message = "شما این دوره را خریداری نکرده‌اید." });

            var result = await _courseService.GradeQuizAsync(customer.Id, productId, answers);
            return Ok(result);
        }

        [HttpGet("{productId}/certificate")]
        public async Task<IActionResult> GetCertificate(int productId)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var certificate = await _courseService.GetCertificateAsync(customer.Id, productId);

            if (certificate == null)
                return NotFound(new { success = false, message = "هنوز گواهی برای این دوره صادر نشده است." });

            return Ok(new
            {
                success = true,
                certificate.CertificateCode,
                certificate.QuizScorePercent,
                certificate.IssuedOnUtc
            });
        }
    }
}
