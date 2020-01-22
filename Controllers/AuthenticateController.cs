using System.Threading.Tasks;
using Common.DataContracts.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.DataContracts.Authentication;
using Smartcontract.DataContracts.Jwt;

namespace Smartcontract.App.Controllers {
    [Route("api/[controller]")]
	[AllowAnonymous]
	public class AuthenticateController : Controller {
        [HttpPost]
		[ProducesResponseType(200, Type = typeof(ApiResponse<JwtExtendedResponse>))]
        public async Task<IActionResult> Post([FromBody] AuthenticationRequest request,
	        [FromServices] IAuthenticationManager authentication) {
			var jwt = await authentication.AuthenticateWithRefreshAsync(request.Login, request.Password);
			if (jwt == null) {
				return Json(ApiResponse.Failed(ApiErrorCode.AuthenticationFailed, "Неверный логин\\пароль"));
			}
			return Json(ApiResponse.Success(jwt));
        }

		[HttpPost("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<JwtExtendedResponse>))]
		public async Task<IActionResult> RefreshAccessToken(
			[FromBody] TokenRequest request,
			[FromServices] IAuthenticationManager authentication) {
			var jwt = await authentication.RefreshAccessToken(request.RefreshToken);
			if (jwt == null) {
				return Json(ApiResponse.Failed(ApiErrorCode.AuthenticationFailed, "Неверный логин\\пароль"));
			}

			return Json(ApiResponse.Success(jwt));
		}

		[HttpPost("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<IActionResult> RevokeRefreshToken(
			[FromBody] TokenRequest request,
			[FromServices] IAuthenticationManager authentication) {
			await authentication.RevokeRefreshToken(request.RefreshToken);
			return Json(ApiResponse.Success(true));
		}
	}
}