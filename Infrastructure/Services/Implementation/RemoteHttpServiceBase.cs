using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Common.DataContracts.Base;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog;
using Smartcontract.Common;
using Smartcontract.DataContracts;

namespace Smartcontract.App.Infrastructure.Services.Implementation {
	public class RemoteHttpServiceBase {
		private JsonSerializerSettings _serializationSettings;
		protected HttpClient Client { get; private set; }

		public RemoteHttpServiceBase(HttpClient client) {
			this.Client = client;
			_serializationSettings = new JsonSerializerSettings() {
				DateFormatString = "dd.MM.yyyy HH:mm:ss"
			};
		}


		protected async Task<TResponse> Send<TResponse>(string url, object request, HttpMethod method) {
			var idempotencyKey = Guid.NewGuid().ToString();
			Log.Debug("{url} - {ikey} - {@request}", url, idempotencyKey, request);
			var message = new HttpRequestMessage(method, url) {
				Content = CreateHttpContentFromRequest(request),
			};
			message.Headers.Add("Idempotency-Key", idempotencyKey);
			var responseMessage = await Client.SendAsync(message);
			responseMessage.EnsureSuccessStatusCode();
			var response = await ReadResponseFromHttpContent<TResponse>(responseMessage.Content);
			Log.Debug("{url} - {@request}", url,  request);
			return response;
		}

		private async Task<TResponse> ReadResponseFromHttpContent<TResponse>(HttpContent httpContent) {
			var json = await httpContent.ReadAsStringAsync();
			return JsonConvert.DeserializeObject<TResponse>(json, _serializationSettings);
		}

		private HttpContent CreateHttpContentFromRequest(object request) {
			var json = JsonConvert.SerializeObject(request, _serializationSettings);
			return new StringContent(json, Encoding.UTF8, "application/json");
		}

		protected void EnsureSuccess(ApiResponse response) {
			if (response.HasError()) {
				throw new SmartcontractException(response.Error.Text, response.Error.Code);
			}
		}
	}
}