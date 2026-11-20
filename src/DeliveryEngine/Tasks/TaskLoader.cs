using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeliveryEngine.Tasks;

public static class TaskLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // опечатка в имени поля = ошибка
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static DeliveryTask Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DeliveryTask>(json, Options)
                ?? throw new TaskLoadException("Файл задачи пуст (null).");
        }
        catch (JsonException ex)
        {
            var location = ex.Path is null ? "" : $" (поле {ex.Path})";
            throw new TaskLoadException($"Некорректный JSON{location}: {ex.Message}", ex);
        }
    }

    public static DeliveryTask Load(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            throw new TaskLoadException($"Файл задачи не найден: {path}");
        }

        return Parse(System.IO.File.ReadAllText(path));
    }
}

public sealed class TaskLoadException(string message, Exception? inner = null) : Exception(message, inner);