using System.Text.Json;
using System.Text.Json.Serialization;

namespace recontrol_win.Tools
{
    internal class CommandJsonParser
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public CommandJsonParser()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public BaseRequest ParseRequest(string json)
        {
            var req = JsonSerializer.Deserialize<BaseRequest>(json, _jsonOptions);
            if (req == null)
                throw new InvalidOperationException("Invalid request object or missing fields.");
            return req;
        }

        public T DeserializePayload<T>(JsonElement payload)
        {
            var args = payload.Deserialize<T>(_jsonOptions);
            if (args == null)
                throw new InvalidOperationException($"Invalid payload for command. Could not deserialize to {typeof(T).Name}.");
            return args;
        }

        // Return lowercase keys: id, status, result/error
        public string SerializeSuccess(string id, object? result)
        {
            var response = new { id = id, status = "success", result = result };
            return JsonSerializer.Serialize(response, _jsonOptions);
        }

        public string SerializeError(string id, string error)
        {
            var response = new { id = id, status = "error", error = error };
            return JsonSerializer.Serialize(response, _jsonOptions);
        }
    }
}
