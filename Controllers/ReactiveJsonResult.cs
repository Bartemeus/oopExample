using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;

namespace Smartcontract.App.Controllers {
	public class ReactiveJsonResult : JsonResult {
		private readonly string _jsonValue;
		
		public ReactiveJsonResult(object value, JsonSerializerSettings serializerSettings) : base(value, serializerSettings) {
			_jsonValue = Newtonsoft.Json.JsonConvert.SerializeObject(value, serializerSettings);
		}
		
		public override void ExecuteResult(ActionContext context) {
			
		}

		public override Task ExecuteResultAsync(ActionContext context) {
			MediaTypeHeaderValue contentType1 = MediaTypeHeaderValue.Parse((StringSegment) "application/json");
			contentType1.Encoding = Encoding.UTF8;
			var content = new ContentResult {
				Content = _jsonValue,
				ContentType = contentType1?.ToString(),
				StatusCode = 200
			};
			return content.ExecuteResultAsync(context);
		}

	}
}