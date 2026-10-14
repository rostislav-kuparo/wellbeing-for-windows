using System.Text.Json;

namespace Wellbeing.Core.Policies;

public static class PolicyJson
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(Policy policy) => JsonSerializer.Serialize(policy, Options);

    public static Policy Deserialize(string json) =>
        JsonSerializer.Deserialize<Policy>(json, Options) ?? throw new InvalidDataException("Пустая политика.");
}
