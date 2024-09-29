using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Bulky.DataAccess.Repository.IRepository
{
    public interface IRepository<T> where T : class
    {
        // T - 泛型類型參數，可以是Category/User/Product
        IEnumerable<T> GetAll(string? includeProperties = null);  // 獲取所有實體
        T Get(Expression<Func<T, bool>> filter, string? includeProperties = null);  // 根據條件獲取單個實體
        void Add(T entity);  // 添加實體
        void Remove(T entity);  // 移除單個實體
        void RemoveRange(IEnumerable<T> entity);  // 移除多個實體
    }

}
