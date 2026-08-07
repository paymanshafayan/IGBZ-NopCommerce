namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Services.Catalog;
    using Nop.Services.Security;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    /// <summary>
    /// مدیریت سوالات آزمون پایانی دوره از پنل ادمین (Product = دوره). قبل از این کنترلر، سوالات و
    /// گزینه‌های آزمون فقط از طریق CourseService خوانده و نمره‌دهی می‌شدند (GetQuizQuestionsAsync/
    /// GradeQuizAsync)، ولی هیچ راهی برای ادمین جهت *ساختن* آن‌ها وجود نداشت — این کنترلر آن خلأ را
    /// می‌بندد. دقیقاً هم‌معماری با CourseLessonsController (همان Area/Filter/الگوی مسیر با productId).
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [ServiceFilter(typeof(Infrastructure.Filters.TenantAdminScopeFilter))]
    public class CourseQuizQuestionsController : BasePluginController
    {
        private readonly IRepository<CourseQuizQuestion> _questionRepository;
        private readonly IRepository<CourseQuizOption> _optionRepository;
        private readonly IProductService _productService;
        private readonly IPermissionService _permissionService;

        public CourseQuizQuestionsController(
            IRepository<CourseQuizQuestion> questionRepository,
            IRepository<CourseQuizOption> optionRepository,
            IProductService productService,
            IPermissionService permissionService)
        {
            _questionRepository = questionRepository;
            _optionRepository = optionRepository;
            _productService = productService;
            _permissionService = permissionService;
        }

        public async Task<IActionResult> Index(int productId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null)
                return RedirectToAction("List", "Product", new { area = AreaNames.ADMIN });

            var questions = await _questionRepository.GetAllAsync(q =>
                q.Where(x => x.ProductId == productId).OrderBy(x => x.DisplayOrder));

            // برای جلوگیری از N+1 واقعی در Razor، همهٔ گزینه‌های همهٔ سوالات این دوره یک‌جا خوانده
            // و بر اساس QuestionId گروه‌بندی می‌شود.
            var questionIds = questions.Select(x => x.Id).ToList();
            var allOptions = await _optionRepository.GetAllAsync(q => q.Where(o => questionIds.Contains(o.QuestionId)));
            var optionsByQuestionId = allOptions.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.ToList());

            ViewBag.ProductId = productId;
            ViewBag.ProductName = product.Name;
            ViewBag.OptionsByQuestionId = optionsByQuestionId;

            return View("~/Plugins/Misc.MultiTenantStores/Views/CourseQuizQuestions/Index.cshtml", questions);
        }

        [HttpPost]
        public async Task<IActionResult> CreateQuestion(int productId, string questionText)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            if (!string.IsNullOrWhiteSpace(questionText))
            {
                var existingCount = (await _questionRepository.GetAllAsync(q => q.Where(x => x.ProductId == productId))).Count;

                await _questionRepository.InsertAsync(new CourseQuizQuestion
                {
                    ProductId = productId,
                    DisplayOrder = existingCount + 1,
                    QuestionText = questionText.Trim()
                });
            }

            return RedirectToAction("Index", new { productId });
        }

        /// <summary>حذف سوال به‌همراه حذف آبشاری همهٔ گزینه‌های آن — وگرنه رکورد یتیم در جدول گزینه‌ها می‌ماند.</summary>
        [HttpPost]
        public async Task<IActionResult> DeleteQuestion(int id, int productId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            var question = await _questionRepository.GetByIdAsync(id);
            if (question != null)
            {
                var relatedOptions = await _optionRepository.GetAllAsync(q => q.Where(o => o.QuestionId == id));
                foreach (var option in relatedOptions)
                    await _optionRepository.DeleteAsync(option);

                await _questionRepository.DeleteAsync(question);
            }

            return RedirectToAction("Index", new { productId });
        }

        [HttpPost]
        public async Task<IActionResult> AddOption(int questionId, int productId, string optionText, bool isCorrect)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            if (!string.IsNullOrWhiteSpace(optionText))
            {
                // طبق منطق نمره‌دهی در CourseService.GradeQuizAsync، هر سوال دقیقاً یک گزینهٔ صحیح
                // دارد؛ اگر گزینهٔ جدید صحیح علامت بخورد، بقیهٔ گزینه‌های همان سوال غلط می‌شوند.
                if (isCorrect)
                    await ClearOtherCorrectOptionsAsync(questionId, exceptOptionId: null);

                await _optionRepository.InsertAsync(new CourseQuizOption
                {
                    QuestionId = questionId,
                    OptionText = optionText.Trim(),
                    IsCorrect = isCorrect
                });
            }

            return RedirectToAction("Index", new { productId });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteOption(int id, int questionId, int productId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            var option = await _optionRepository.GetByIdAsync(id);
            if (option != null)
                await _optionRepository.DeleteAsync(option);

            return RedirectToAction("Index", new { productId });
        }

        /// <summary>گزینهٔ صحیح یک سوال را تعیین می‌کند (Radio-button-like) — بقیهٔ گزینه‌های همان سوال غلط می‌شوند.</summary>
        [HttpPost]
        public async Task<IActionResult> SetCorrectOption(int optionId, int questionId, int productId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            await ClearOtherCorrectOptionsAsync(questionId, exceptOptionId: null);

            var selected = await _optionRepository.GetByIdAsync(optionId);
            if (selected != null && selected.QuestionId == questionId)
            {
                selected.IsCorrect = true;
                await _optionRepository.UpdateAsync(selected);
            }

            return RedirectToAction("Index", new { productId });
        }

        private async Task ClearOtherCorrectOptionsAsync(int questionId, int? exceptOptionId)
        {
            var options = await _optionRepository.GetAllAsync(q => q.Where(o => o.QuestionId == questionId && o.IsCorrect));
            foreach (var option in options)
            {
                if (exceptOptionId.HasValue && option.Id == exceptOptionId.Value)
                    continue;

                option.IsCorrect = false;
                await _optionRepository.UpdateAsync(option);
            }
        }
    }
}
