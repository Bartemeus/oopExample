using System;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Smartcontract.App.Infrastructure.Configuration {
	public class AuthOptions {
		public const string ISSUER = "smartcontract.kz";
		public const string AUDIENCE = "https://smartcontract.kz/";
		const string KEY = "smartcontract the most secret key";
		public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
		public static SymmetricSecurityKey GetSymmetricSecurityKey() {
			return new SymmetricSecurityKey(Encoding.ASCII.GetBytes(KEY));
		}
	}
}
