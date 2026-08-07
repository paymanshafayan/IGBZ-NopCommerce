namespace Nop.Plugin.Api.Controllers.Admin
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Core.Domain.Catalog;
    using Nop.Services.Catalog;
    using Nop.Services.Stores;

    [ApiController]
    [Route("api/admin/categories")]
    public class AdminCategoriesController : AuthorizedTenantOwnerApiController
    {
        private readonly ICategoryService _categoryService;
        private readonly IStoreMappingService _storeMappingService;

        public AdminCategoriesController(
            IWorkContext workContext,
            IStoreContext storeContext,
            ICategoryService categoryService,
            IStoreMappingService storeMappingService) : base(workContext, storeContext)
        {
            _categoryService = categoryService;
            _storeMappingService = storeMappingService;
        }

        [HttpGet("tree")]
        public async Task<IActionResult> GetCategoryTree()
        {
            var store = await GetAuthorizedStoreAsync();
            
            var allCategories = await _categoryService.GetAllCategoriesAsync(
                storeId: store.Id,
                showHidden: true
            );

            var rootCategories = allCategories
                .Where(c => c.ParentCategoryId == 0)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => MapToCategoryNode(c, allCategories))
                .ToList();

            return Ok(rootCategories);
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveCategory([FromBody] CategorySaveDto dto)
        {
            var store = await GetAuthorizedStoreAsync();

            Category category;
            if (dto.Id > 0)
            {
                category = await _categoryService.GetCategoryByIdAsync(dto.Id);
                if (category == null) return NotFound("دسته‌بندی یافت نشد.");
                
                if (!await _storeMappingService.AuthorizeAsync(category, store.Id))
                    return Forbid();

                category.Name = dto.Name;
                category.ParentCategoryId = dto.ParentCategoryId;
                category.DisplayOrder = dto.DisplayOrder;
                category.Published = dto.Published;
                category.UpdatedOnUtc = System.DateTime.UtcNow;

                await _categoryService.UpdateCategoryAsync(category);
            }
            else
            {
                category = new Category
                {
                    Name = dto.Name,
                    ParentCategoryId = dto.ParentCategoryId,
                    DisplayOrder = dto.DisplayOrder,
                    Published = dto.Published,
                    CreatedOnUtc = System.DateTime.UtcNow,
                    UpdatedOnUtc = System.DateTime.UtcNow,
                    LimitedToStores = true
                };

                await _categoryService.InsertCategoryAsync(category);
                await _storeMappingService.InsertStoreMappingAsync(category, store.Id);
            }

            return Ok(new { success = true, categoryId = category.Id });
        }

        private CategoryTreeNodeDto MapToCategoryNode(Category parent, System.Collections.Generic.IList<Category> allCategories)
        {
            var children = allCategories
                .Where(c => c.ParentCategoryId == parent.Id)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => MapToCategoryNode(c, allCategories))
                .ToList();

            return new CategoryTreeNodeDto
            {
                Id = parent.Id,
                Name = parent.Name,
                ParentCategoryId = parent.ParentCategoryId,
                DisplayOrder = parent.DisplayOrder,
                Published = parent.Published,
                Children = children
            };
        }
    }

    public class CategoryTreeNodeDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int ParentCategoryId { get; set; }
        public int DisplayOrder { get; set; }
        public bool Published { get; set; }
        public System.Collections.Generic.List<CategoryTreeNodeDto> Children { get; set; } = new();
    }

    public class CategorySaveDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int ParentCategoryId { get; set; }
        public int DisplayOrder { get; set; }
        public bool Published { get; set; } = true;
    }
}