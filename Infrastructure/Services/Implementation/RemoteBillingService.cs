using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Billing.DataContracts.Responses;
using Common.DataContracts.Base;
using DevExtreme.AspNet.Data.ResponseModel;
using Serilog;
using Smartcontract.App.Infrastructure.DevExtreme;

namespace Smartcontract.App.Infrastructure.Services.Implementation {
	public class RemoteBillingService : RemoteHttpServiceBase {
		public const string SmartContractServiceCode = "smart-contract";
		public const string EventId = "BILLING-CLIENT";
		public RemoteBillingService(BillingServiceConfig config, HttpClient client) : base(client) {
			Client.BaseAddress = new System.Uri(config.Url);
		}

		public async Task RegisterUserAsync(string login, string fullName) {
			Log.Information("{EventId} register user request: {login}, {fullName}", EventId, login, fullName);
			var response = await Send<ApiResponse<bool>>("/api/owners", new { Name = fullName, Code = login }, HttpMethod.Post);
			Log.Information("{EventId} register user succeeded");
		}

		public async Task RegisterServiceAsync() {
			Log.Information("{EventId} register service request", EventId);
			var response = await Send<ApiResponse<bool>>("/api/services", new { Name = "Smart Contract", Code = SmartContractServiceCode }, HttpMethod.Post);
			Log.Information("{EventId} register service succeeded", EventId);
			base.EnsureSuccess(response);
		}

		public async Task<string> AddPacketAsync(string packetType, string login) {
			Log.Information("{EventId} add packet: {packetType}, {login}", EventId, packetType, login);
			var response = await Send<ApiResponse<string>>("/api/billing/addPacket", new { PacketTypeCode = packetType, OwnerCode = login, ServiceCode = SmartContractServiceCode }, HttpMethod.Post);
			Log.Information("{EventId} add packet response: {packetId}", EventId, response.Data);
			return response.Data;
		}

		public async Task<bool> IsAvailableAsync(string login) {
			Log.Information("{EventId} is available for: {login}", EventId, login);
			var response = await Send<ApiResponse<ServiceAvailabilityResponse>>($"/api/billing/isAvailable?ownerCode={login}&serviceCode={SmartContractServiceCode}", null, HttpMethod.Get);
			Log.Information("{EventId} is available for {login}: {IsAvailable}", EventId, login, response.Data.Available);
			return response.Data.Available;
		}

		public async Task<LoadResult> GetPacketsAsync(string login, DataSourceLoadOptionsImpl options) {
			Log.Information("{EventId} get packets for: {login}", EventId, login);
			var response = await Send<ApiResponse<LoadResultImpl<ActivationPacketItemResponse>>>(
				$"/api/billing/getPackets?ownerCode={login}&serviceCode={SmartContractServiceCode}", options, HttpMethod.Post);
			Log.Information("{EventId} get packets for {login} succeeded", EventId, login);
			return response.Data;
		}
		public async Task<LoadResult> GetActivePacketAsync(string login) {
			Log.Information("{EventId} is Active packets for: {login}", EventId, login);
			var response = await Send<ApiResponse<LoadResult>>($"/api/billing/getActivePacket?ownerCode={login}&serviceCode={SmartContractServiceCode}", null, HttpMethod.Post);
			Log.Information("{EventId} is Active packets for {login}: {IsAvailable}", EventId, login, response.Data);
			return response.Data;
        }

		public async Task WriteOffBalanceAsync(string login) {
			Log.Information("{EventId} write off balance for: {login}", EventId, login);
			var response = await Send<ApiResponse<bool>>($"/api/billing/countServiceUsage?ownerCode={login}&serviceCode={SmartContractServiceCode}", null, HttpMethod.Post);
			Log.Information("{EventId} write off balance for {login} succeeded", EventId, login);
		}

		public async Task<LoadResult> GetPacketsHistoryAsync(string login, DataSourceLoadOptionsImpl options) {
			Log.Information("{EventId} get packets history for: {login}", EventId, login);
			var response = await Send<ApiResponse<LoadResultImpl<PacketHistoryItemResponse>>>(
				$"/api/billing/getHistory?ownerCode={login}&serviceCode={SmartContractServiceCode}", options, HttpMethod.Post);
			Log.Information("{EventId} get packets history for {login} succeeded", EventId, login);
			return response.Data;
		}

		public Task AddFreePacketAsync(string userName) {
			return AddPacketAsync("F10", userName);
		}
	}
}