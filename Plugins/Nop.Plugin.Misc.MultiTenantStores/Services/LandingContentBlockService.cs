namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    /// <summary>
    /// مدیریت بلوک‌های محتوایی سایت مادر (فروشگاه/اپلیکیشن/دستیار اینستاگرام) — کاملاً از پنل
    /// مدیریت قابل درج/ویرایش/حذف، طبق درخواست صریح کاربر (نه Hardcode در فرانت‌اند Next.js).
    /// </summary>
    public interface ILandingContentBlockService
    {
        Task<IList<LandingContentBlock>> GetAllBlocksAsync();
        Task<IList<LandingContentBlock>> GetActiveBlocksAsync();
        Task<LandingContentBlock> GetByIdAsync(int id);
        Task<LandingContentBlock> GetByPageKeyAsync(string pageKey);
        Task InsertAsync(LandingContentBlock block);
        Task UpdateAsync(LandingContentBlock block);
        Task DeleteAsync(int id);
    }

    public class LandingContentBlockService : ILandingContentBlockService
    {
        private readonly IRepository<LandingContentBlock> _repository;

        public LandingContentBlockService(IRepository<LandingContentBlock> repository)
        {
            _repository = repository;
        }

        public async Task<IList<LandingContentBlock>> GetAllBlocksAsync()
        {
            return await _repository.GetAllAsync(q => q.OrderBy(b => b.DisplayOrder));
        }

        public async Task<IList<LandingContentBlock>> GetActiveBlocksAsync()
        {
            return await _repository.GetAllAsync(q => q.Where(b => b.IsActive).OrderBy(b => b.DisplayOrder));
        }

        public async Task<LandingContentBlock> GetByIdAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<LandingContentBlock> GetByPageKeyAsync(string pageKey)
        {
            var all = await _repository.GetAllAsync(q => q.Where(b => b.PageKey == pageKey));
            return all.FirstOrDefault();
        }

        public async Task InsertAsync(LandingContentBlock block)
        {
            await _repository.InsertAsync(block);
        }

        public async Task UpdateAsync(LandingContentBlock block)
        {
            await _repository.UpdateAsync(block);
        }

        public async Task DeleteAsync(int id)
        {
            var block = await _repository.GetByIdAsync(id);
            if (block != null)
                await _repository.DeleteAsync(block);
        }
    }
}
