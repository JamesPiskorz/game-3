using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Game.Characters.Content
{
    /// <summary>Reads and writes <see cref="ContentCatalog"/> JSON (camelCase fields, keys of tag and value maps verbatim).</summary>
    public static class CatalogSerializer
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
            },
            // Lists and maps start empty in the classes; replace them rather than appending to them.
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Error,
            Culture = CultureInfo.InvariantCulture,
            Formatting = Formatting.Indented,
        };

        /// <summary>Parses a catalog. Unknown fields are errors, so typos in hand-edited content fail loudly.</summary>
        public static ContentCatalog FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<ContentCatalog>(json, Settings)
                    ?? throw new FormatException("Content catalog JSON is empty.");
            }
            catch (JsonException e)
            {
                throw new FormatException("Content catalog is not valid: " + e.Message, e);
            }
        }

        public static string ToJson(ContentCatalog catalog) => JsonConvert.SerializeObject(catalog, Settings);
    }
}
