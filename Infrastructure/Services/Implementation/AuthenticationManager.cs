using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Common.DAL.Abstraction.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using NHibernate.Linq;
using Smartcontract.App.Infrastructure.Configuration;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.App.Managers;
using Smartcontract.DataContracts.Jwt;
using Smartcontract.DataContracts.Registration;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Common.DataContracts.Base;
using Serilog;
using Smartcontract.Common.Certificates;
using Smartcontract.DataContracts;

namespace Smartcontract.App.Infrastructure.Services.Implementation {
	public class AuthenticationManager : IAuthenticationManager {
		private readonly Provider _provider;
		private readonly IPasswordHasher<User> _passwordHasher;

		public AuthenticationManager(Provider provider, IPasswordHasher<User> passwordHasher) {
			this._provider = provider;
			_passwordHasher = passwordHasher;
		}

		public async Task<Tuple<string, List<string>>> AuthenticateAsync(string login, string password) {
			Log.Information("Try login: {login}", login);
			var identity = await GetIdentityAsync(login, password);
			if (identity == null) {
				Log.Warning("Login failed for {login}", login);
				return null;
			}
			var accessToken = GenerateToken(identity, AuthOptions.AccessTokenLifetime);
			var permissions = accessToken.Claims.Where(u => u.Type == ClaimsIdentity.DefaultRoleClaimType).Select(u => u.Value).ToList();
			return new Tuple<string, List<string>>(EncodeTokenToString(accessToken), permissions);
		}

		public async Task<JwtExtendedResponse> AuthenticateWithRefreshAsync(string login, string password) {
			var jwt = await AuthenticateAsync(login, password);
			if (jwt == null) {
				throw new SmartcontractException(@"Пользователь с таким Логин \ Пароль не найден");
			}
			if (string.IsNullOrEmpty(jwt.Item1)) {
				return null;
			}
			Log.Information("Login succeeded for {login}", login);
			var refreshIdentity = GenerateRefreshTokenIdentity(login);
			var refreshToken = EncodeTokenToString(GenerateToken(refreshIdentity, TimeSpan.FromDays(1)));
			await SaveRefreshToken(login, refreshToken);
			return new JwtExtendedResponse(jwt.Item1) { AccessTokenExpiration = DateTime.Now.Add(AuthOptions.AccessTokenLifetime), RefreshToken = refreshToken, Permission = jwt.Item2 };
		}

		public async Task<JwtExtendedResponse> RefreshAccessToken(string refreshToken) {
			var token = ReadToken(refreshToken);
			if (token.ValidFrom >= DateTime.Now || token.ValidTo <= DateTime.Now) {
				throw new SmartcontractException("Срок действия токена истек. Пройдите авторизацию", ApiErrorCode.InvalidToken);
			}
			using (var repository = new Repository<RefreshToken>(_provider)) {
				var tokenEntity = await repository.Get(x => x.Value == refreshToken).SingleOrDefaultAsync();
				if (tokenEntity == null) {
					throw new SmartcontractException("Неизвестный токен", ApiErrorCode.InvalidToken);
				}
				if (tokenEntity.Revoked) {
					throw new SmartcontractException("Токен был отозван", ApiErrorCode.InvalidToken);
				}
				var userRepository = new Repository<User>(repository);
				var user = userRepository.Get(x => x.Id == tokenEntity.User.Id).SingleOrDefault();
				if (user == null) {
					throw new SmartcontractException("Пользователь не найден", ApiErrorCode.InvalidToken);
				}

				var accessTokenIdentity = GenerateAccessTokenIdentity(user, repository);
				var accessToken = GenerateToken(accessTokenIdentity, AuthOptions.AccessTokenLifetime);
				var newRefreshTokenIdentity = GenerateRefreshTokenIdentity(tokenEntity.Value);
				var newRefreshToken = GenerateToken(newRefreshTokenIdentity, TimeSpan.FromDays(1));
                await SaveRefreshToken(user.Email, EncodeTokenToString(newRefreshToken));
				return new JwtExtendedResponse(EncodeTokenToString(accessToken)) {
					RefreshToken = EncodeTokenToString(newRefreshToken),
                    AccessTokenExpiration = DateTime.Now.Add(AuthOptions.AccessTokenLifetime)
                };
			}
		}

		private JwtSecurityToken ReadToken(string jwt) {
			var handler = new JwtSecurityTokenHandler();
			try {
				SecurityToken token;
				var principal = handler.ValidateToken(jwt, new TokenValidationParameters {
					ValidateIssuer = true,
					ValidIssuer = AuthOptions.ISSUER,
					ValidateAudience = true,
					ValidAudience = AuthOptions.AUDIENCE,
					ValidateLifetime = true,
					IssuerSigningKey = AuthOptions.GetSymmetricSecurityKey(),
					ValidateIssuerSigningKey = true,
				}, out token);
			}
			catch (SecurityTokenException e) {
				throw new SmartcontractException("Принятый токен невалиден: " + e.Message, ApiErrorCode.InvalidToken);
			}

			return handler.ReadJwtToken(jwt);
		}

		public async Task RevokeRefreshToken(string refreshToken) {
			var token = ReadToken(refreshToken);
			if (token.ValidFrom >= DateTime.UtcNow || token.ValidTo <= DateTime.UtcNow) {
				throw new SmartcontractException("Срок действия токена истек. Пройдите авторизацию", ApiErrorCode.InvalidToken);
			}

			using (var refreshTokenRepository = new Repository<RefreshToken>(_provider)) {
				var refreshTokenEntity = await refreshTokenRepository.Get(u => u.Value == refreshToken && u.Revoked == false).FirstOrDefaultAsync();
				if (refreshTokenEntity == null) {
					throw new SmartcontractException("Не найден токен обновления");
				}
				refreshTokenEntity.Revoked = true;
				refreshTokenRepository.Update(refreshTokenEntity);
				refreshTokenRepository.Commit();
			}
		}

		private async Task<ClaimsIdentity> GetIdentityAsync(string email, string password) {
			using (var userRepository = new Repository<User>(_provider)) {
				var user = await userRepository.Get(x => x.UserName == email).SingleOrDefaultAsync();
				if (user == null) {
					return null;
				}

				if (!user.Confirmed.HasValue) {
					throw new SmartcontractException($"Учетная запись: {user.Email} не подтверждена. Для подтверждения регистрации пройдите по ссылке отправленной на e-mail.");
				}

				var verification = _passwordHasher.VerifyHashedPassword(user, user.Password, password);
				if (verification != PasswordVerificationResult.Success) {
					return null;
				}
				return GenerateAccessTokenIdentity(user, userRepository);
			}
		}

		private ClaimsIdentity GenerateAccessTokenIdentity(User user, Repository baseRepository) {
			var claims = new List<Claim> { new Claim(ClaimsIdentity.DefaultNameClaimType, user.UserName) };
			foreach (var x in user.Permissions) {
				claims.Add(new Claim(ClaimsIdentity.DefaultRoleClaimType, x.Permission.ToString()));
			}
			var claimsIdentity = new ClaimsIdentity(claims, "Token", ClaimsIdentity.DefaultNameClaimType, ClaimsIdentity.DefaultRoleClaimType);
			return claimsIdentity;
		}

		private ClaimsIdentity GenerateRefreshTokenIdentity(string userName) {
			var claims = new List<Claim> { new Claim(ClaimsIdentity.DefaultNameClaimType, userName) };
			var claimsIdentity = new ClaimsIdentity(claims, "RefreshToken", ClaimsIdentity.DefaultNameClaimType, ClaimsIdentity.DefaultRoleClaimType);
			return claimsIdentity;
		}

		private JwtSecurityToken GenerateToken(ClaimsIdentity identity, TimeSpan duration) {
			var now = DateTime.Now;
			var jwt = new JwtSecurityToken(
				issuer: AuthOptions.ISSUER,
				audience: AuthOptions.AUDIENCE,
				notBefore: now,
				claims: identity.Claims,
				expires: now.Add(duration),
				signingCredentials: new SigningCredentials(AuthOptions.GetSymmetricSecurityKey(), SecurityAlgorithms.HmacSha256));
			return jwt;
		}

		private string EncodeTokenToString(JwtSecurityToken jwt) {
			var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);
			return encodedJwt;
		}

		public async Task RegisterAsync(RegistrationRequest request, RemoteBillingService billingService) {
			using (var repository = new Repository<User>(_provider)) {
				var user = await repository.Get(x => x.UserName == request.UserAccount.Email).SingleOrDefaultAsync();
				if (user != null && user.Confirmed.HasValue) {
					throw new SmartcontractException($"Пользователь с e-mail {request.UserAccount.Email} уже зарегистрирован в системе", ApiErrorCode.ValidationError);
				}
				else if (user != null && !user.Confirmed.HasValue) {
					throw new SmartcontractException($"На эту почту уже отправлена ссылка для подтверждения почты", ApiErrorCode.ValidationError);
				}
				
				user = new User {
					Email = request.UserAccount.Email,
					UserName = request.UserAccount.Email,
					Phone = request.UserAccount.Phone
				};
				if (request.InvitedUser) {
					user.Confirmed=DateTime.Now;
				}
				var signedCms = CertificatesHelper.DecodeCmsFromString(request.SignedCms);
                foreach (var certificate in signedCms.Certificates)
                {
                    var information = CertificatesHelper.ReadX509CertificateCommonData(certificate);
                    if (information.SerialNumber != request.Profile.PersonIin)
                    {
                        throw new SmartcontractException("ИИН в подписи не соответствует ИИН профиля", ApiErrorCode.ValidationError);
                    }
                    if (!string.IsNullOrEmpty(request.Profile.OrganizationXin))
                    {
                        if (information.Bin != request.Profile.OrganizationXin && request.Profile.OrganizationXin != request.Profile.PersonIin)
                        {
                            throw new SmartcontractException("БИН в подписи не соответствует БИН организации", ApiErrorCode.ValidationError);
                        }
                    }
                }

				await billingService.RegisterUserAsync(user.UserName, user.UserName);
				await billingService.AddFreePacketAsync(user.UserName);
				repository.Insert(user);
				user.Password = _passwordHasher.HashPassword(user, request.UserAccount.Password);
				repository.Update(user);
				var profileManager = new ProfileManager(_provider);
				await profileManager.CreateOrAttachToProfile(request.Profile, request.InvitedUser,user.UserName, repository);
				repository.Commit();
			}
		}

        public async Task<bool> PasswordsMatched(User user,string newPassword) {
            var verification = _passwordHasher.VerifyHashedPassword(user, user.Password, newPassword);
            return verification == PasswordVerificationResult.Success;
        }
		public async Task EditPasswordAsync(UserAccountRequest request) {
			using (var userRepository = new Repository<User>(_provider)) {               
				var user = await userRepository.Get(x => x.UserName == request.Email).SingleAsync();                      
				user.Password = _passwordHasher.HashPassword(user, request.Password);     
				userRepository.Update(user);
				userRepository.Commit();
			}            
		}
        public async Task EditPhoneAsync(UserAccountRequest request) {
            using (var userRepository = new Repository<User>(_provider)) {               
                var user = await userRepository.Get(x => x.UserName == request.Email).SingleAsync();
                user.Phone = request.Phone;     
                userRepository.Update(user);
                userRepository.Commit();
            }
        }
		private async Task SaveRefreshToken(string login, string token) {
			using (var repository = new Repository<RefreshToken>(_provider)) {
				var userRepository = new Repository<User>(repository);
				var user = userRepository.Get(x => x.Email == login).Single();
				var refreshToken = new RefreshToken {
					Created = DateTime.Now,
					Value = token,
					User = user
				};
				await repository.InsertAsync(refreshToken);
				await repository.CommitAsync();
			}
		}
	}
	internal static class CustomClaims {
		public const string CurrentUser = "CurrentUser";
	}
}
