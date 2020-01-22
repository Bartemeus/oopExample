using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Smartcontract.DataContracts.Registration;

namespace Smartcontract.App.Infrastructure.Services.Abstraction
{
    public interface IInviteService {
	     Task<bool> RegisterContrager(InviteRequest request,string url,string emailSender);
	      string GenerateCode(string xin);
	      string DecodeCode(string code);
    }
}
