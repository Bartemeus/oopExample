using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Smartcontract.App.Infrastructure.Converters {
	public class RussianDateFormatConverter : JsonConverter {
		private Type _dateTimeType = typeof(DateTime);
		public static readonly string[] RussianDateTimeFormat = new string[] {
			"dd.MM.yyyy HH:mm:ss.FFFFFFF","dd.MM.yyyy HH:mm","dd.MM.yyyy","o"
		};

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
			if (value == null) {
				return;
			}
			var dateTime = (DateTime)value;
			writer.WriteValue(dateTime.ToString(RussianDateTimeFormat[0]));
		}


		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
			if (reader.Value == null) {
				return null;
			}
			var text = reader.Value.ToString();
			DateTime date;
			if (DateTime.TryParseExact(text, RussianDateTimeFormat, new CultureInfo("RU-ru"), DateTimeStyles.None, out date)) {
				return date;
			}

			throw new FormatException($"Date have incorrect format: {text}");
		}

		public override bool CanConvert(Type objectType) {
			return objectType == _dateTimeType;
		}
	}
}
