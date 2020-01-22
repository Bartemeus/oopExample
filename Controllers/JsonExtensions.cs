using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Smartcontract.App.Controllers {
	public static class JsonExtensions {
		public static IActionResult JsonEx(this Controller controller, object value) {
			var options = (IOptions<MvcJsonOptions>)controller.HttpContext.RequestServices.GetService(typeof(IOptions<MvcJsonOptions>));
			return new ReactiveJsonResult(value, options.Value.SerializerSettings);
		}

	}
}