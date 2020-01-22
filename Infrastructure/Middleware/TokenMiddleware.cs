using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Smartcontract.App.Infrastructure.Middleware
{
    public class TokenMiddleware
    {
        private readonly RequestDelegate _next;

        public TokenMiddleware(RequestDelegate next)
        {
            this._next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                var header = context.Request.Headers["Authorization"];

                if (context.Request.Cookies.TryGetValue("auth-jwt", out string jwt))
                {
                    if (header.Count == 0)
                    {
                        jwt = jwt.Replace("\"", string.Empty);
                        context.Request.Headers["Authorization"] = "Bearer " + jwt;
                    }
                }
            }
            catch (Exception)
            {
            }
            await _next.Invoke(context);
        }
    }
}
