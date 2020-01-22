using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Serialization;
using Common.DAL.Abstraction.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Smartcontract.App.Filters;
using Smartcontract.Common;
using Smartcontract.Constants.Enums;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;

namespace Smartcontract.App.Managers.QazKom {
	public class QazKomAckquiringService {
		private readonly Provider _provider;
		private readonly QazKomConfig _config;
		private readonly HttpClient _http;
		private QazKomSigner _signer;

		public QazKomAckquiringService(Provider provider, QazKomConfig config, HttpClient http, IHostingEnvironment env) {
			_provider = provider;
			_config = config;
			_http = http;
			_http.BaseAddress = new Uri(config.Host);
			_signer = new QazKomSigner(config, env.ContentRootFileProvider);
			ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
		}

		public async Task<QazKomTransactionResponse> CreateTransaction(string userName, decimal sum, string transactionDescription, string productCode, Uri currentHost) {
			using (var repository = new Repository<TransactionInfo>(_provider)) {
				var now = DateTime.Now;
				var trInfo = new TransactionInfo() {
					TransactionDate = now,
					Sum = sum,
					Employee = userName,
					Description = transactionDescription,
					Status = TransactionStatusEnum.InProcess,
					ProductCode = productCode
				};
				var orderId = repository.Count() + 1;
				trInfo.OrderId = orderId.ToString().PadLeft(14, '0');
				trInfo.Signature = _signer.Build64(trInfo.OrderId, sum);
				repository.Insert(trInfo);
				await repository.CommitAsync();
				return new QazKomTransactionResponse() {
					Host = _config.Host,
					Signature = trInfo.Signature,
					Email = userName,
					PostLink = new Uri(currentHost, $"/api/qazKom/complete?orderId={trInfo.OrderId}").ToString(),
					BackLink = new Uri(currentHost, "/cabinet/packets").ToString(),
					FailurePostLink = new Uri(currentHost, $"/api/qazKom/failed?orderId={trInfo.OrderId}").ToString(),
					FailureBackLink = new Uri(currentHost, "/cabinet/packets").ToString(),
				};
			}
		}

		public async Task CompleteTransaction(TransactionInfo info) {
			var xmlString = _signer.BuildConfirmRequest(info.Reference, info.ApprovalCode, info.OrderId, info.Sum);
			var result = await _http.GetAsync("jsp/remote/control.jsp?" + xmlString);

		}

		public async Task<TransactionInfo> TryUpdateTransactionByOrderIdAsync(string orderId) {
			// Надо добавить сравнение - наш ли мерчант, наш ли заказ на ту ли сумму
			var request = string.Format("<merchant id=\"{0}\"><order id=\"{1}\"/></merchant>", _config.MerchantId, orderId);
			var signature = _signer.Sign64(request);
			request = string.Format("<xml><document>{0}<merchant_sign type=\"RSA\" cert_id=\"{1}\">{2}</merchant_sign></document></xml>", request, _config.CertificateId, signature);
			do {
				var result = await _http.GetAsync("jsp/remote/checkOrdern.jsp?" + HttpUtility.UrlEncode(request));
				var xml = await result.Content.ReadAsStringAsync();
				var kazkomAnswer = Helper.XmlDeserialize<KazkomAnswer>(xml, Encoding.UTF8);
				if (!kazkomAnswer.Bank.Response.Payment && kazkomAnswer.Bank.Response.Status == 0) {
					await Task.Delay(1000);
					continue;
				}
				var rep = new Repository<TransactionInfo>(_provider);
				var transaction = rep.Get(x => x.OrderId == orderId).Single();
				if (transaction.Status != TransactionStatusEnum.InProcess) {
					return transaction;
				}
				transaction.Status = kazkomAnswer.Bank.Response.Payment ? TransactionStatusEnum.Success : TransactionStatusEnum.Error;
				if (transaction.Status == TransactionStatusEnum.Error) {
					transaction.ErrorDescription = GetErrorDescription(kazkomAnswer.Bank.Response);
				}
				else {
					transaction.ApprovalCode = kazkomAnswer.Bank.Response.Approval_code;
					transaction.Reference = kazkomAnswer.Bank.Response.Reference;
				}
				await rep.UpdateAsync(transaction);
				await rep.CommitAsync();
				return transaction;
			} while (true);
		}

		//private async Task<QazKomTransactionResponse> PostFormAsync(string postLink, string backLink, string signature) {
		//	var values = new List<KeyValuePair<string, string>>();
		//	values.Add(new KeyValuePair<string, string>("Signed_Order_B64", signature));
		//	values.Add(new KeyValuePair<string, string>("PostLink", postLink));
		//	values.Add(new KeyValuePair<string, string>("BackLink", backLink));
		//	values.Add(new KeyValuePair<string, string>("FailureBackLink", backLink));
		//	values.Add(new KeyValuePair<string, string>("FailurePostLink", backLink));
		//	values.Add(new KeyValuePair<string, string>("email", "os.varlamov@gmail.com"));
		//	var content = new FormUrlEncodedContent(values);
		//	_http.DefaultRequestHeaders.Referrer = new Uri("https://kkm.webkassa.kz");
		//	_http.DefaultRequestHeaders.Add("Origin", "https://kkm.webkassa.kz");
		//	var result = await _http.PostAsync(new Uri(new Uri(_config.Host), "jsp/process/logon.jsp"), content);
		//	result.EnsureSuccessStatusCode();
		//	return new QazKomTransactionResponse() {
		//		Cookies = result.Headers.GetValues("Set-Cookie"),
		//		Url = result.RequestMessage.RequestUri
		//	};
		//	//var requestForm = "<body onload='document.forms[\"kazkom\"].submit()'>";
		//	//requestForm += $"<form  name='kazkom' action=\"{_config.Host}jsp/process/logon.jsp\" method='post'>";
		//	//requestForm += $"<input type=\"hidden\" value=\"{signature}\" name=\"Signed_Order_B64\" id=\"signed\"/>";
		//	//requestForm += $"<input type=\"hidden\" name=\"PostLink\" value=\"{postLink}\"/>";
		//	//requestForm += $"<input type = \"hidden\" name = \"BackLink\" value = \"{backLink}\"/>";
		//	//requestForm += $"<input type = \"hidden\" name = \"FailureBackLink\" value = \"{backLink}" /> ";
		//	//requestForm += "<input type = \"hidden\" name = \"email\" value = \"smail@kkb.kz\" /> ";
		//	//requestForm += "</form>";
		//	//requestForm += "</body>";
		//}


		private string GetErrorDescription(Response response) {
			if (response.Payment) {
				return string.Empty;
			}
			switch (response.Status) {
				case 2:
					return "Платеж отменен";
				case 0:
					return string.Empty;
				case 7:
					return "Неизвестный номер заказа";
				case 8:
					return "Оплата по данному заказу не произведена";
				case 9:
					return "Системная ошибка на стороне банка";
				default:
					return "Неизвестная ошибка на стороне банка";
			}
		}

		public class CompleteKKb {
			[XmlElement("merchant")]
			public CompleteKkbMerchant Merchant { get; set; }

			[XmlElement("merchant_sign")]
			public CompleteKkbMerchantSign MerchantSign { get; set; }
		}

		public class CompleteKkbMerchantSign {
			[XmlAttribute("type")]
			public string Type { get; set; } = "RSA";

			[XmlAttribute("cert_id")]
			public string CertId { get; set; }

			public string Sign { get; set; }
		}

		public class CompleteKkbMerchant {
			[XmlAttribute("id")]
			public string Id { get; set; }

			[XmlElement("command")]
			public CompleteKkbCommand Command { get; set; }
		}

		public class CompleteKkbCommand {
			[XmlAttribute("type")]
			public string Type { get; set; }

			[XmlElement("payment")]
			public CompleteKkbPayment Payment { get; set; }
		}

		public class CompleteKkbPayment {
			[XmlAttribute("reference")]
			public string Reference { get; set; }

			[XmlAttribute("approval_code")]
			public string ApprovalCode { get; set; }

			[XmlAttribute("orderid")]
			public string OrderId { get; set; }

			[XmlAttribute("amount")]
			public string Amount { get; set; }

			[XmlAttribute("currency_code")]
			public string CurrencyCode { get; set; } = "398";
		}
	}

	public class QazKomTransactionResponse {
		public string Host { get; set; }
		public string Signature { get; set; }
		public string Email { get; set; }
		public string PostLink { get; set; }
		public string BackLink { get; set; }
		public string FailureBackLink { get; set; }
		public string FailurePostLink { get; set; }
	}

	public class QazKomSigner {
		private readonly QazKomConfig _config;
		private readonly IFileProvider _fileProvider;
		public string currency = "398";                                      //Код валюты 398 - тенге, 840 - доллары 
		public Encoding _encoding;
		public QazKomSigner(QazKomConfig config, IFileProvider fileProvider) {
			_config = config;
			_fileProvider = fileProvider;
			_encoding = Encoding.UTF8;
		}
		//Эти параметры необходимо настроить под магазин
		//private  string _certsDirectory = HttpContext.Current.Server.MapPath(ConfigurationElements.KkbCertsPath);
		//public  string KKBpfxFile = Path.Combine(_certsDirectory, "cert.pfx");        //Полный путь к pfx-файлу с ключами магазина (файл дает банк)
		//public  string KKBCaFile = Path.Combine(_certsDirectory, ConfigHelper.GetValue<string>("KkbCaCert"));        //Полный путь к файлу с публичным ключом банка (файл дает банк)
		//public  string KKBpfxPass = ConfigHelper.GetValue<string>("KkbPfxPass");                                 //Пароль к pfx-файлу (дает банк)
		//public  string Cert_Id = ConfigHelper.GetValue<string>("KkbCertId");                                //Номер сертификата  (дает банк)
		//public  string ShopName = ConfigHelper.GetValue<string>("KkbName");                                //Имя магазина (дает банк)
		//public  string merchant_id = ConfigurationElements.KkbMerchantId;                              //номер терминала магазина, он же MerchantID  (дает банк)




		//Эти параметры как правило не требуют изменения
		public string KKBRequestStr {
			get {
				return
					$"<merchant cert_id=\"{_config.CertificateId}\" name=\"{_config.MerchantName}\"><order order_id=\"%ORDER%\" amount=\"%AMOUNT%\" currency=\"{currency}\"><department merchant_id=\"{_config.MerchantId}\" amount=\"%AMOUNT%\"/></order></merchant>";
			}
		}


		public byte[] ConvertStringToByteArray(string s) {
			return _encoding.GetBytes(s);
		}

		// Функция Build64 генерирует запрос который отправляется на https://testpay.kkb.kz/jsp/process/logon.jsp
		// В качестве входящих параметров ожидает idOrder (номер заказа в магазине) и Amount (сумма к оплате)
		// Возвращает строку в Base64

		public string Build64(string idOrder, decimal Amount) {

			var forSign = KKBRequestStr
				.Replace("%ORDER%", idOrder)
				.Replace("%AMOUNT%", string.Format("{0:f}", (double)Amount)
					.Replace(",", "."));
			var merchantCertificate = GetMerchantCertificate();
			var rsaCSP = merchantCertificate.GetRSAPrivateKey();
			var SignData = rsaCSP.SignData(ConvertStringToByteArray(forSign), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
			Array.Reverse(SignData);
			var ResultStr = "<document>" + forSign + "<merchant_sign type=\"RSA\">" + Convert.ToBase64String(SignData, Base64FormattingOptions.None) + "</merchant_sign></document>";
			return Convert.ToBase64String(ConvertStringToByteArray(ResultStr), Base64FormattingOptions.None);
		}

		private X509Certificate2 GetMerchantCertificate() {
			var file = _fileProvider.GetFileInfo(Path.Combine(_config.CertificatesPath, _config.CertificateName));
			return new X509Certificate2(file.PhysicalPath, _config.CertificatePassword);
		}

		private X509Certificate2 GetCACertificate() {
			var file = _fileProvider.GetFileInfo(Path.Combine(_config.CertificatesPath, _config.CACertificateName));
			return new X509Certificate2(file.PhysicalPath);
		}

		// Функция  Verify проверяет корректность подписи, полученной от банка
		// В качестве входящих параметров ожидает StrForVerify (строка, которую получили от банка) и Sign (ЭЦП к данной строке)

		public bool Verify(string StrForVerify, string Sign) {
			var KKbCert = GetCACertificate();
			var rsaCSP = KKbCert.GetRSAPrivateKey();
			var bStrForVerify = ConvertStringToByteArray(StrForVerify);
			var bSign = Convert.FromBase64String(Sign);
			Array.Reverse(bSign);
			bool result;
			try {
				result = rsaCSP.VerifyData(bStrForVerify, bSign, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
			}
			catch {
				result = false;
			}
			return result;
		}

		public string BuildConfirmRequest(string reference, string approvalCode, string orderId, decimal amount) {
			var requestString = string.Format("<merchant id=\"{0}\">", _config.MerchantId);
			var amountText = string.Format("{0:f}", (double)amount).Replace(",", ".");
			requestString +=
				$"<command type=\"complete\"/><payment reference=\"{reference}\" approval_code=\"{approvalCode}\" orderid=\"{orderId}\"" +
				$" amount=\"{amountText}\" currency_code=\"{currency}\"/></merchant>";
			var signedString = Sign64(requestString);
			requestString = $"<document>{requestString}<merchant_sign type=\"RSA\" cert_id=\"{_config.CertificateId}\">{signedString}</merchant_sign></document>";
			var encodedRequestString = HttpUtility.UrlEncode(requestString);
			return encodedRequestString;
		}
		// Функция Sign64 подписывает произвольную строку
		// В качестве входящих параметров ожидает StrForSign (подписываемая строка)
		// Возвращает ЭЦП кодированный в Base64

		public string Sign64(string StrForSign) {
			var merchantCertificate = GetMerchantCertificate();
			var rsaCSP = merchantCertificate.GetRSAPrivateKey();
			var SignData = rsaCSP.SignData(ConvertStringToByteArray(StrForSign), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
			Array.Reverse(SignData);
			return Convert.ToBase64String(SignData, Base64FormattingOptions.None);

		}


	}
	[XmlRoot(ElementName = "department")]
	public class Department {
		[XmlAttribute(AttributeName = "merchant_id")]
		public string Merchant_id { get; set; }
		[XmlAttribute(AttributeName = "amount")]
		public string Amount { get; set; }
		[XmlAttribute(AttributeName = "rl")]
		public string Rl { get; set; }
	}

	[XmlRoot(ElementName = "order")]
	public class KkbOrder {
		[XmlElement(ElementName = "department")]
		public Department Department { get; set; }
		[XmlAttribute(AttributeName = "order_id")]
		public string Order_id { get; set; }
		[XmlAttribute(AttributeName = "amount")]
		public string Amount { get; set; }
		[XmlAttribute(AttributeName = "currency")]
		public string Currency { get; set; }
	}

	[XmlRoot(ElementName = "merchant")]
	public class KkbMerchant {
		[XmlElement(ElementName = "order")]
		public KkbOrder Order { get; set; }
		[XmlAttribute(AttributeName = "cert_id")]
		public string Cert_id { get; set; }
		[XmlAttribute(AttributeName = "name")]
		public string Name { get; set; }
	}

	[XmlRoot(ElementName = "merchant_sign")]
	public class KkbMerchant_sign {
		[XmlAttribute(AttributeName = "type")]
		public string Type { get; set; }
	}

	[XmlRoot(ElementName = "customer")]
	public class KkbCustomer {
		[XmlElement(ElementName = "merchant")]
		public KkbMerchant Merchant { get; set; }
		[XmlElement(ElementName = "merchant_sign")]
		public KkbMerchant_sign Merchant_sign { get; set; }
		[XmlAttribute(AttributeName = "name")]
		public string Name { get; set; }
		[XmlAttribute(AttributeName = "mail")]
		public string Mail { get; set; }
		[XmlAttribute(AttributeName = "phone")]
		public string Phone { get; set; }
	}

	[XmlRoot(ElementName = "customer_sign")]
	public class KkbCustomer_sign {
		[XmlAttribute(AttributeName = "type")]
		public string Type { get; set; }
		[XmlText]
		public string Text { get; set; }
	}

	[XmlRoot(ElementName = "payment")]
	public class KkbPayment {
		[XmlAttribute(AttributeName = "merchant_id")]
		public string Merchant_id { get; set; }
		[XmlAttribute(AttributeName = "amount")]
		public string Amount { get; set; }
		[XmlAttribute(AttributeName = "reference")]
		public string Reference { get; set; }
		[XmlAttribute(AttributeName = "approval_code")]
		public string Approval_code { get; set; }
		[XmlAttribute(AttributeName = "response_code")]
		public string Response_code { get; set; }
		[XmlAttribute(AttributeName = "Secure")]
		public string Secure { get; set; }
		[XmlAttribute(AttributeName = "card_bin")]
		public string Card_bin { get; set; }
		[XmlAttribute(AttributeName = "c_hash")]
		public string C_hash { get; set; }
	}

	[XmlRoot(ElementName = "results")]
	public class KkbResults {
		[XmlElement(ElementName = "payment")]
		public KkbPayment Payment { get; set; }
		[XmlAttribute(AttributeName = "timestamp")]
		public string Timestamp { get; set; }
	}

	[XmlRoot(ElementName = "bank")]
	public class KkbBank {
		[XmlElement(ElementName = "customer")]
		public KkbCustomer Customer { get; set; }
		[XmlElement(ElementName = "customer_sign")]
		public KkbCustomer_sign Customer_sign { get; set; }
		[XmlElement(ElementName = "results")]
		public KkbResults Results { get; set; }
		[XmlAttribute(AttributeName = "name")]
		public string Name { get; set; }
		[XmlElement(ElementName = "merchant")]
		public Merchant Merchant { get; set; }
		[XmlElement(ElementName = "merchant_sign")]
		public Merchant_sign Merchant_sign { get; set; }
		[XmlElement(ElementName = "response")]
		public Response Response { get; set; }
	}

	[XmlRoot(ElementName = "merchant_sign")]
	public class Merchant_sign {
		[XmlAttribute(AttributeName = "type")]
		public string Type { get; set; }
		[XmlAttribute(AttributeName = "cert_id")]
		public string Cert_id { get; set; }
		[XmlText]
		public string Text { get; set; }
	}


	[XmlRoot(ElementName = "response")]
	public class Response {
		[XmlAttribute(AttributeName = "payment")]
		public bool Payment { get; set; }
		[XmlAttribute(AttributeName = "status")]
		public int Status { get; set; }
		[XmlAttribute(AttributeName = "result")]
		public int Result { get; set; }
		[XmlAttribute(AttributeName = "amount")]
		public string Amount { get; set; }
		[XmlAttribute(AttributeName = "currencycode")]
		public string Currencycode { get; set; }
		[XmlAttribute(AttributeName = "timestamp")]
		public string Timestamp { get; set; }
		[XmlAttribute(AttributeName = "reference")]
		public string Reference { get; set; }
		[XmlAttribute(AttributeName = "cardhash")]
		public string Cardhash { get; set; }
		[XmlAttribute(AttributeName = "card_to")]
		public string Card_to { get; set; }
		[XmlAttribute(AttributeName = "approval_code")]
		public string Approval_code { get; set; }
		[XmlAttribute(AttributeName = "msg")]
		public string Msg { get; set; }
		[XmlAttribute(AttributeName = "secure")]
		public string Secure { get; set; }
		[XmlAttribute(AttributeName = "card_bin")]
		public string Card_bin { get; set; }
		[XmlAttribute(AttributeName = "payername")]
		public string Payername { get; set; }
		[XmlAttribute(AttributeName = "payermail")]
		public string Payermail { get; set; }
		[XmlAttribute(AttributeName = "payerphone")]
		public string Payerphone { get; set; }
		[XmlAttribute(AttributeName = "c_hash")]
		public string C_hash { get; set; }
		[XmlAttribute(AttributeName = "recur")]
		public string Recur { get; set; }
		[XmlAttribute(AttributeName = "OrderID")]
		public string OrderID { get; set; }
		[XmlAttribute(AttributeName = "SessionID")]
		public string SessionID { get; set; }
	}


	[XmlRoot(ElementName = "bank_sign")]
	public class KkbBank_sign {
		[XmlAttribute(AttributeName = "cert_id")]
		public string Cert_id { get; set; }
		[XmlAttribute(AttributeName = "type")]
		public string Type { get; set; }
		[XmlText]
		public string Text { get; set; }
	}

	[XmlRoot(ElementName = "document")]
	public class KazkomAnswer {
		[XmlElement(ElementName = "bank")]
		public KkbBank Bank { get; set; }
		[XmlElement(ElementName = "bank_sign")]
		public KkbBank_sign Bank_sign { get; set; }
	}

	[XmlRoot(ElementName = "merchant")]
	public class Merchant {
		[XmlElement(ElementName = "order")]
		public Order Order { get; set; }
		[XmlAttribute(AttributeName = "id")]
		public string Id { get; set; }
	}

	[XmlRoot(ElementName = "order")]
	public class Order {
		[XmlAttribute(AttributeName = "id")]
		public string Id { get; set; }
	}
}