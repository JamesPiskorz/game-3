using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Game.Characters.Dna
{
    /// <summary>
    /// Reads and writes <see cref="CharacterDna"/> as JSON (saves, presets) and as compact share codes.
    /// Writing is deterministic: the same DNA always produces the same JSON text, on every OS.
    /// Bad input throws <see cref="FormatException"/>; DNA from a newer build throws <see cref="NotSupportedException"/>.
    /// </summary>
    public static class DnaSerializer
    {
        const string ShareCodePrefix = "dna1.";

        static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            // camelCase field names, but slider IDs stay exactly as authored.
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
            },
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new HexUInt64Converter() },
            Culture = CultureInfo.InvariantCulture,
        });

        public static string ToJson(CharacterDna dna, bool indented = true)
        {
            var text = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" }; // same bytes on every OS
            using (var writer = new JsonTextWriter(text) { Formatting = indented ? Formatting.Indented : Formatting.None })
                Serializer.Serialize(writer, dna);
            return text.ToString();
        }

        /// <summary>Parses DNA JSON from any supported version, upgrading it to the current one.</summary>
        public static CharacterDna FromJson(string json)
        {
            try
            {
                // DateParseHandling.None: never reinterpret ID or name strings that happen to look like dates.
                var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
                JObject root = JObject.Load(reader);
                DnaMigrations.Upgrade(root);
                return root.ToObject<CharacterDna>(Serializer);
            }
            catch (JsonException e)
            {
                throw new FormatException("Character DNA is not valid: " + e.Message, e);
            }
        }

        /// <summary>
        /// Compact, URL-safe text for sharing a character ("dna1.…"). The exact characters can differ
        /// between runtimes (compressor implementations vary), but every code decodes everywhere.
        /// </summary>
        public static string ToShareCode(CharacterDna dna)
        {
            byte[] json = Encoding.UTF8.GetBytes(ToJson(dna, indented: false));
            using (var buffer = new MemoryStream())
            {
                using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                    deflate.Write(json, 0, json.Length);
                return ShareCodePrefix + ToBase64Url(buffer.ToArray());
            }
        }

        public static CharacterDna FromShareCode(string code)
        {
            code = code?.Trim();
            if (code == null || !code.StartsWith(ShareCodePrefix, StringComparison.Ordinal))
                throw new FormatException("Not a character share code.");

            string json;
            try
            {
                byte[] compressed = FromBase64Url(code.Substring(ShareCodePrefix.Length));
                using (var deflate = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress))
                using (var reader = new StreamReader(deflate, Encoding.UTF8))
                    json = reader.ReadToEnd();
            }
            // .NET reports corrupt deflate data as InvalidDataException; Unity's Mono as IOException.
            catch (Exception e) when (e is FormatException || e is InvalidDataException || e is IOException)
            {
                throw new FormatException("Character share code is corrupted.", e);
            }
            return FromJson(json);
        }

        static string ToBase64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        static byte[] FromBase64Url(string text)
        {
            string base64 = text.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }

        /// <summary>Writes ulongs (seeds) as 16 hex digits: exact in every JSON reader (no 2^53 limit) and easy to copy.</summary>
        sealed class HexUInt64Converter : JsonConverter<ulong>
        {
            public override void WriteJson(JsonWriter writer, ulong value, JsonSerializer serializer) =>
                writer.WriteValue(value.ToString("x16", CultureInfo.InvariantCulture));

            public override ulong ReadJson(JsonReader reader, Type objectType, ulong existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String &&
                    ulong.TryParse((string)reader.Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value))
                    return value;
                throw new JsonSerializationException($"Expected a hex string for a 64-bit value, got {reader.TokenType} '{reader.Value}'.");
            }
        }
    }
}
