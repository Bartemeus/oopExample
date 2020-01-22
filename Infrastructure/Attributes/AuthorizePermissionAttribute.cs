using System;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Smartcontract.Constants;

namespace Smartcontract.App.Infrastructure.Attributes {
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
	public class AuthorizePermissionAttribute : AuthorizeAttribute {

		public AuthorizePermissionAttribute(params PermissionEnum[] roles) : base() {
			Roles = string.Join(",", roles.Select(r => Enum.GetName(r.GetType(), r)));
		}
	}
}
