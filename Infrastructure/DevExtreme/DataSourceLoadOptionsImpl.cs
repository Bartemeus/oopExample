using DevExtreme.AspNet.Data;
using Microsoft.AspNetCore.Mvc;

namespace Smartcontract.App.Infrastructure.DevExtreme {
	[ModelBinder(BinderType = typeof(DataSourceLoadOptionsBinder))]

	public class DataSourceLoadOptionsImpl : DataSourceLoadOptionsBase {
		static DataSourceLoadOptionsImpl() {
			StringToLowerDefault = true;
		}
	}
}