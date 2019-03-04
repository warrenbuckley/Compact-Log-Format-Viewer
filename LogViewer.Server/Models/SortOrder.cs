using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LogViewer.Server.Models
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum SortOrder
    {
        Ascending = 0,
        Descending = 1
    }
}
