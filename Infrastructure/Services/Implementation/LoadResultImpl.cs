using DevExtreme.AspNet.Data.ResponseModel;

namespace Smartcontract.App.Infrastructure.Services.Implementation {
	public class LoadResultImpl<T> : LoadResult {
		// хак чтобы можно было десериализовать данные
		public new T[] data { get; set; }
	}
}