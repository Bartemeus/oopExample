using System;
using System.Text;
using Smartcontract.App.Infrastructure.Services.Abstraction;

namespace Smartcontract.App.Infrastructure.Services.Implementation
{
	public class RandomDataGenerator : INumbersGenerator {
		private readonly Random _rnd;

		public RandomDataGenerator() {
			_rnd = new Random();
		}
		public string GenerateNumbers(int length) {
			var sb = new StringBuilder(length);
			for (int i = 0; i < length; i++) {
				var digit = _rnd.Next(0, 10);
				sb.Append(digit);
			}
			return sb.ToString();
		}
	}
}
