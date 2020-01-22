using System.Net.Http;
using System.Threading.Tasks;
using Serilog;
using Smartcontract.App.Infrastructure.Configuration;
using Smartcontract.App.Infrastructure.Services.Abstraction;

namespace Smartcontract.App.Infrastructure.Services.Implementation {
	public class RemoteKalkanServiceProvider : RemoteHttpServiceBase, ICryptoProviderService {
		public const string EventId = "KALKAN-CLIENT";
		public RemoteKalkanServiceProvider(KalkanServiceConfig config, HttpClient client) : base(client) {
			Client.BaseAddress = new System.Uri(config.Url);
		}

		public async Task<string> SignXml(string xml) {
			Log.Information("{EventId} sign xml request", EventId);
			var response = await Send<SignedXmlResponse>("/api/kalkan/signXml", new XmlRequest() {Xml = xml}, HttpMethod.Post);
			Log.Information("{EventId} xml signed", EventId);
			return response.Xml;
		}

		public async Task<bool> VerifyXml(string xml) {
			Log.Information("{EventId} verify xml request", EventId);
			var response = await Send<XmlValidationResponse>("/api/kalkan/verifyXml", new XmlRequest() {Xml = xml}, HttpMethod.Post);
			Log.Information("{EventId} verify xml result: {IsValid}", EventId, response.IsValid);
			return response.IsValid;
		}

		public async Task<bool> VerifyCMSAsync(string data, string cms) {
			Log.Information("{EventId} verify CMS request", EventId);
			var response = await Send<XmlValidationResponse>("/api/kalkan/verifyCms", new  {Data = data, CMS = cms}, HttpMethod.Post);
			Log.Information("{EventId} verify CMS result: {IsValid}", EventId, response.IsValid);
			return response.IsValid;
		}

        public async Task<bool> VerifyExpiredCert(string cms)
        {
            Log.Information("{EventId} verify Expired request", EventId);
            var response = await Send<XmlValidationResponse>("/api/kalkan/VerifyExpiredCert", new XmlRequest() { Xml = cms }, HttpMethod.Post);
            Log.Information("{EventId} verify Expired result: {IsValid}", EventId, response.IsValid);
            return response.IsValid;
        }

        class XmlRequest {
			public string Xml { get; set; }
		}
		class SignedXmlResponse {
			public string Xml { get; set; }
		}
		class XmlValidationResponse {
			public bool IsValid { get; set; }

		}
	}
}